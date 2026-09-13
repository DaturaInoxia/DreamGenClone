using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// The ONE media edit-session store. Behaviour (status transitions, provenance checks, SHA
/// normalization) is the union of the two stores it replaces, so both edit surfaces now get every
/// fix and every future column exactly once.
/// </summary>
public sealed class MediaEditRepository : IMediaEditRepository
{
    private const string SessionSelect = """
        SELECT Id, SubjectKind, SubjectId, SubjectScopeId, SourceImageId, SourceImageSha256, Status,
               DescriptionText, CreatedUtc, UpdatedUtc, CompletedUtc
        FROM MediaEditSessions
        """;

    private const string AttemptSelect = """
        SELECT Id, EditSessionId, Ordinal, RawIntent, ClarificationContextJson, SourceImageSha256, Status,
               ResolvedModelSnapshotJson, CompilerSchemaVersion, SystemPromptVersion, RawModelResponse,
               ParsedResultJson, Error, CreatedUtc, StartedUtc, CompletedUtc
        FROM MediaEditCompilationAttempts
        """;

    private const string RevisionSelect = """
        SELECT Id, CompilationAttemptId, Ordinal, Prompt, RevisionKind, PromptSha256, CreatedUtc
        FROM MediaEditPromptRevisions
        """;

    private readonly string _connectionString;

    public MediaEditRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    /// <summary>Creates the store and runs the one-time legacy backfill, reporting what was copied.</summary>
    public async Task<MediaEditSchemaReport> EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenRawAsync(cancellationToken);
        return await MediaEditSchema.EnsureAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Copies any legacy edit rows that appeared after the marker was written. This is the cutover
    /// step that lets the two legacy stores be deleted without losing sessions created meantime.
    /// </summary>
    public async Task<MediaEditSchemaReport> ReBackfillLegacyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenRawAsync(cancellationToken);
        return await MediaEditSchema.ReBackfillAsync(connection, cancellationToken);
    }

    public async Task CreateSessionAsync(MediaEditSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        Require(session.Id, "Media edit session id");
        Require(session.SubjectId, "Media edit subject id");
        Require(session.SourceImageId, "Source image id");
        RequireSha256(session.SourceImageSha256, "Source image checksum");
        if (session.SubjectKind == MediaEditSubjectKind.Unknown)
            throw new InvalidOperationException("Media edit subject kind must be explicit.");
        if (session.Status == MediaEditSessionStatus.Unknown)
            throw new InvalidOperationException("Media edit session status must be explicit.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MediaEditSessions
                (Id, SubjectKind, SubjectId, SubjectScopeId, SourceImageId, SourceImageSha256, Status,
                 DescriptionText, CreatedUtc, UpdatedUtc, CompletedUtc)
            VALUES ($id, $kind, $subjectId, $scopeId, $sourceImageId, $sourceSha, $status,
                    $description, $createdUtc, $updatedUtc, $completedUtc);
            """;
        command.Parameters.AddWithValue("$id", session.Id.Trim());
        command.Parameters.AddWithValue("$kind", session.SubjectKind.ToString());
        command.Parameters.AddWithValue("$subjectId", session.SubjectId.Trim());
        command.Parameters.AddWithValue("$scopeId", (object?)session.SubjectScopeId?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceImageId", session.SourceImageId.Trim());
        command.Parameters.AddWithValue("$sourceSha", NormalizeSha256(session.SourceImageSha256));
        command.Parameters.AddWithValue("$status", session.Status.ToString());
        command.Parameters.AddWithValue("$description", (object?)session.DescriptionText?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", session.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", session.UpdatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedUtc", session.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MediaEditSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Media edit session id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetSessionAsync(connection, sessionId.Trim(), cancellationToken);
    }

    public async Task<MediaEditSession?> GetLatestSessionAsync(
        MediaEditSubjectKind subjectKind, string subjectId, string sourceImageId, CancellationToken cancellationToken = default)
    {
        if (subjectKind == MediaEditSubjectKind.Unknown)
            throw new InvalidOperationException("Media edit subject kind must be explicit.");
        Require(subjectId, "Media edit subject id");
        Require(sourceImageId, "Source image id");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SessionSelect} WHERE SubjectKind = $kind AND SubjectId = $subjectId AND SourceImageId = $sourceImageId ORDER BY UpdatedUtc DESC LIMIT 1;";
        command.Parameters.AddWithValue("$kind", subjectKind.ToString());
        command.Parameters.AddWithValue("$subjectId", subjectId.Trim());
        command.Parameters.AddWithValue("$sourceImageId", sourceImageId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
    }

    public async Task UpdateSessionStatusAsync(
        string sessionId,
        MediaEditSessionStatus status,
        DateTime updatedUtc,
        DateTime? completedUtc = null,
        CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Media edit session id");
        if (status == MediaEditSessionStatus.Unknown)
            throw new InvalidOperationException("Media edit session status must be explicit.");
        if (status == MediaEditSessionStatus.Completed && completedUtc is null)
            throw new InvalidOperationException("A completed media edit session requires a completion timestamp.");
        if (status != MediaEditSessionStatus.Completed && completedUtc is not null)
            throw new InvalidOperationException("Only a completed media edit session can have a completion timestamp.");

        await using var connection = await OpenAsync(cancellationToken);
        var existing = await GetSessionAsync(connection, sessionId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Media edit session '{sessionId}' was not found.");
        if (!IsAllowedSessionTransition(existing.Status, status))
            throw new InvalidOperationException($"Media edit session cannot transition from {existing.Status} to {status}.");

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE MediaEditSessions SET Status = $status, UpdatedUtc = $updatedUtc, CompletedUtc = $completedUtc WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", sessionId.Trim());
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$updatedUtc", updatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedUtc", completedUtc?.ToString("O") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetDescriptionAsync(
        string sessionId, string description, DateTime updatedUtc, CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Media edit session id");
        Require(description, "Media edit session description");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE MediaEditSessions SET DescriptionText = $description, UpdatedUtc = $updatedUtc WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", sessionId.Trim());
        command.Parameters.AddWithValue("$description", description.Trim());
        command.Parameters.AddWithValue("$updatedUtc", updatedUtc.ToString("O"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Media edit session '{sessionId}' was not found.");
    }

    public async Task CreateAttemptAsync(MediaEditCompilationAttempt attempt, CancellationToken cancellationToken = default)
    {
        ValidateAttempt(attempt);
        if (attempt.Status != SceneImageEditCompilationAttemptStatus.Pending)
            throw new InvalidOperationException("A new media compilation attempt must be Pending.");

        await using var connection = await OpenAsync(cancellationToken);
        if (await GetSessionAsync(connection, attempt.EditSessionId, cancellationToken) is null)
            throw new InvalidOperationException($"Media edit session '{attempt.EditSessionId}' was not found.");

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MediaEditCompilationAttempts
                (Id, EditSessionId, Ordinal, RawIntent, ClarificationContextJson, SourceImageSha256, Status,
                 ResolvedModelSnapshotJson, CompilerSchemaVersion, SystemPromptVersion, RawModelResponse,
                 ParsedResultJson, Error, CreatedUtc, StartedUtc, CompletedUtc)
            VALUES ($id, $sessionId, $ordinal, $rawIntent, $clarification, $sourceSha, $status,
                    $modelSnapshot, $schemaVersion, $systemPromptVersion, $rawResponse,
                    $parsedResult, $error, $createdUtc, $startedUtc, $completedUtc);
            """;
        command.Parameters.AddWithValue("$id", attempt.Id.Trim());
        command.Parameters.AddWithValue("$sessionId", attempt.EditSessionId.Trim());
        command.Parameters.AddWithValue("$ordinal", attempt.Ordinal);
        command.Parameters.AddWithValue("$rawIntent", attempt.RawIntent.Trim());
        command.Parameters.AddWithValue("$clarification", (object?)attempt.ClarificationContextJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceSha", NormalizeSha256(attempt.SourceImageSha256));
        command.Parameters.AddWithValue("$status", attempt.Status.ToString());
        command.Parameters.AddWithValue("$modelSnapshot", attempt.ResolvedModelSnapshotJson);
        command.Parameters.AddWithValue("$schemaVersion", attempt.CompilerSchemaVersion);
        command.Parameters.AddWithValue("$systemPromptVersion", attempt.SystemPromptVersion);
        command.Parameters.AddWithValue("$rawResponse", (object?)attempt.RawModelResponse ?? DBNull.Value);
        command.Parameters.AddWithValue("$parsedResult", (object?)attempt.ParsedResultJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)attempt.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", attempt.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$startedUtc", attempt.StartedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", attempt.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAttemptAsync(MediaEditCompilationAttempt attempt, CancellationToken cancellationToken = default)
    {
        ValidateAttempt(attempt);
        await using var connection = await OpenAsync(cancellationToken);
        var existing = await GetAttemptAsync(connection, attempt.Id.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Media compilation attempt '{attempt.Id}' was not found.");
        if (!IsAllowedAttemptTransition(existing.Status, attempt.Status))
            throw new InvalidOperationException($"Media compilation attempt cannot transition from {existing.Status} to {attempt.Status}.");

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE MediaEditCompilationAttempts SET
                RawIntent = $rawIntent, ClarificationContextJson = $clarification, SourceImageSha256 = $sourceSha,
                Status = $status, ResolvedModelSnapshotJson = $modelSnapshot, CompilerSchemaVersion = $schemaVersion,
                SystemPromptVersion = $systemPromptVersion, RawModelResponse = $rawResponse,
                ParsedResultJson = $parsedResult, Error = $error, StartedUtc = $startedUtc, CompletedUtc = $completedUtc
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", attempt.Id.Trim());
        command.Parameters.AddWithValue("$rawIntent", attempt.RawIntent.Trim());
        command.Parameters.AddWithValue("$clarification", (object?)attempt.ClarificationContextJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceSha", NormalizeSha256(attempt.SourceImageSha256));
        command.Parameters.AddWithValue("$status", attempt.Status.ToString());
        command.Parameters.AddWithValue("$modelSnapshot", attempt.ResolvedModelSnapshotJson);
        command.Parameters.AddWithValue("$schemaVersion", attempt.CompilerSchemaVersion);
        command.Parameters.AddWithValue("$systemPromptVersion", attempt.SystemPromptVersion);
        command.Parameters.AddWithValue("$rawResponse", (object?)attempt.RawModelResponse ?? DBNull.Value);
        command.Parameters.AddWithValue("$parsedResult", (object?)attempt.ParsedResultJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)attempt.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$startedUtc", attempt.StartedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", attempt.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MediaEditCompilationAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default)
    {
        Require(attemptId, "Media compilation attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetAttemptAsync(connection, attemptId.Trim(), cancellationToken);
    }

    public async Task<MediaEditCompilationAttempt?> GetLatestAttemptAsync(
        string editSessionId, CancellationToken cancellationToken = default)
    {
        Require(editSessionId, "Media edit session id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{AttemptSelect} WHERE EditSessionId = $id ORDER BY Ordinal DESC LIMIT 1;";
        command.Parameters.AddWithValue("$id", editSessionId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
    }

    public async Task CreateRevisionAsync(MediaEditPromptRevision revision, CancellationToken cancellationToken = default)
    {
        ValidateRevision(revision);
        await using var connection = await OpenAsync(cancellationToken);
        if (await GetAttemptAsync(connection, revision.CompilationAttemptId.Trim(), cancellationToken) is null)
            throw new InvalidOperationException($"Media compilation attempt '{revision.CompilationAttemptId}' was not found.");

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MediaEditPromptRevisions
                (Id, CompilationAttemptId, Ordinal, Prompt, RevisionKind, PromptSha256, CreatedUtc)
            VALUES ($id, $attemptId, $ordinal, $prompt, $kind, $sha, $createdUtc);
            """;
        command.Parameters.AddWithValue("$id", revision.Id.Trim());
        command.Parameters.AddWithValue("$attemptId", revision.CompilationAttemptId.Trim());
        command.Parameters.AddWithValue("$ordinal", revision.Ordinal);
        command.Parameters.AddWithValue("$prompt", revision.Prompt);
        command.Parameters.AddWithValue("$kind", revision.RevisionKind.ToString());
        command.Parameters.AddWithValue("$sha", NormalizeSha256(revision.PromptSha256));
        command.Parameters.AddWithValue("$createdUtc", revision.CreatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MediaEditPromptRevision?> GetRevisionAsync(string revisionId, CancellationToken cancellationToken = default)
    {
        Require(revisionId, "Media prompt revision id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetRevisionAsync(connection, revisionId.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<MediaEditPromptRevision>> ListRevisionsAsync(
        string attemptId, CancellationToken cancellationToken = default)
    {
        Require(attemptId, "Media compilation attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{RevisionSelect} WHERE CompilationAttemptId = $id ORDER BY Ordinal;";
        command.Parameters.AddWithValue("$id", attemptId.Trim());
        var revisions = new List<MediaEditPromptRevision>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            revisions.Add(ReadRevision(reader));
        return revisions;
    }

    public async Task<MediaEditPromptRevision> GetExecutableRevisionAsync(
        string editSessionId,
        string attemptId,
        string revisionId,
        string sourceImageSha256,
        string promptSha256,
        CancellationToken cancellationToken = default)
    {
        Require(editSessionId, "Media edit session id");
        Require(attemptId, "Media compilation attempt id");
        Require(revisionId, "Media prompt revision id");
        RequireSha256(sourceImageSha256, "Source image checksum");
        RequireSha256(promptSha256, "Prompt checksum");

        var session = await GetSessionAsync(editSessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Media edit session '{editSessionId}' was not found.");
        var attempt = await GetAttemptAsync(attemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Media compilation attempt '{attemptId}' was not found.");
        var revision = await GetRevisionAsync(revisionId, cancellationToken)
            ?? throw new InvalidOperationException($"Media prompt revision '{revisionId}' was not found.");

        if (attempt.EditSessionId != session.Id
            || revision.CompilationAttemptId != attempt.Id
            || attempt.Status != SceneImageEditCompilationAttemptStatus.Ready
            || !ShaEquals(session.SourceImageSha256, sourceImageSha256)
            || !ShaEquals(attempt.SourceImageSha256, sourceImageSha256)
            || !ShaEquals(revision.PromptSha256, promptSha256))
        {
            throw new InvalidOperationException("Media edit execution provenance is stale or invalid.");
        }

        var latest = await GetLatestAttemptAsync(session.Id, cancellationToken);
        var revisions = await ListRevisionsAsync(attempt.Id, cancellationToken);
        if (latest?.Id != attempt.Id || revisions.LastOrDefault()?.Id != revision.Id)
            throw new InvalidOperationException("The selected compilation attempt or prompt revision is stale.");

        return revision;
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Media edit session id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await DeleteChildrenAsync(connection, "MediaEditCompilationAttempts", "EditSessionId", sessionId.Trim(), cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MediaEditSessions WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", sessionId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAttemptAsync(string attemptId, CancellationToken cancellationToken = default)
    {
        Require(attemptId, "Media compilation attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await DeleteChildrenAsync(connection, "MediaEditPromptRevisions", "CompilationAttemptId", attemptId.Trim(), cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MediaEditCompilationAttempts WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", attemptId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Attempts are owned by a session; revisions are owned by an attempt.</summary>
    private static async Task DeleteChildrenAsync(
        SqliteConnection connection, string table, string foreignKey, string parentId, CancellationToken cancellationToken)
    {
        if (string.Equals(table, "MediaEditCompilationAttempts", StringComparison.Ordinal))
        {
            var attemptIds = new List<string>();
            await using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT Id FROM MediaEditCompilationAttempts WHERE EditSessionId = $parentId;";
                select.Parameters.AddWithValue("$parentId", parentId);
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    attemptIds.Add(reader.GetString(0));
            }

            foreach (var id in attemptIds)
                await DeleteChildrenAsync(connection, "MediaEditPromptRevisions", "CompilationAttemptId", id, cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"DELETE FROM {table} WHERE {foreignKey} = $parentId;";
            command.Parameters.AddWithValue("$parentId", parentId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = await OpenRawAsync(cancellationToken);
        await MediaEditSchema.EnsureAsync(connection, cancellationToken);
        return connection;
    }

    private async Task<SqliteConnection> OpenRawAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
        await foreignKeys.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task<MediaEditSession?> GetSessionAsync(
        SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SessionSelect} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
    }

    private static async Task<MediaEditCompilationAttempt?> GetAttemptAsync(
        SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"{AttemptSelect} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
    }

    private static async Task<MediaEditPromptRevision?> GetRevisionAsync(
        SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"{RevisionSelect} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRevision(reader) : null;
    }

    private static MediaEditSession ReadSession(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        SubjectKind = ParseSubjectKind(reader.GetString(1), reader.GetString(0)),
        SubjectId = reader.GetString(2),
        SubjectScopeId = reader.IsDBNull(3) ? null : reader.GetString(3),
        SourceImageId = reader.GetString(4),
        SourceImageSha256 = reader.GetString(5),
        Status = ParseSessionStatus(reader.GetString(6), reader.GetString(0)),
        DescriptionText = reader.IsDBNull(7) ? null : reader.GetString(7),
        CreatedUtc = ParseUtc(reader.GetString(8), reader.GetString(0)),
        UpdatedUtc = ParseUtc(reader.GetString(9), reader.GetString(0)),
        CompletedUtc = reader.IsDBNull(10) ? null : ParseUtc(reader.GetString(10), reader.GetString(0))
    };

    private static MediaEditCompilationAttempt ReadAttempt(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        EditSessionId = reader.GetString(1),
        Ordinal = reader.GetInt32(2),
        RawIntent = reader.GetString(3),
        ClarificationContextJson = reader.IsDBNull(4) ? null : reader.GetString(4),
        SourceImageSha256 = reader.GetString(5),
        Status = ParseAttemptStatus(reader.GetString(6), reader.GetString(0)),
        ResolvedModelSnapshotJson = reader.GetString(7),
        CompilerSchemaVersion = reader.GetString(8),
        SystemPromptVersion = reader.GetString(9),
        RawModelResponse = reader.IsDBNull(10) ? null : reader.GetString(10),
        ParsedResultJson = reader.IsDBNull(11) ? null : reader.GetString(11),
        Error = reader.IsDBNull(12) ? null : reader.GetString(12),
        CreatedUtc = ParseUtc(reader.GetString(13), reader.GetString(0)),
        StartedUtc = reader.IsDBNull(14) ? null : ParseUtc(reader.GetString(14), reader.GetString(0)),
        CompletedUtc = reader.IsDBNull(15) ? null : ParseUtc(reader.GetString(15), reader.GetString(0))
    };

    private static MediaEditPromptRevision ReadRevision(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        CompilationAttemptId = reader.GetString(1),
        Ordinal = reader.GetInt32(2),
        Prompt = reader.GetString(3),
        RevisionKind = ParseRevisionKind(reader.GetString(4), reader.GetString(0)),
        PromptSha256 = reader.GetString(5),
        CreatedUtc = ParseUtc(reader.GetString(6), reader.GetString(0))
    };

    private static void ValidateAttempt(MediaEditCompilationAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        Require(attempt.Id, "Media compilation attempt id");
        Require(attempt.EditSessionId, "Media edit session id");
        Require(attempt.RawIntent, "Raw intent");
        RequireSha256(attempt.SourceImageSha256, "Source image checksum");
        Require(attempt.ResolvedModelSnapshotJson, "Resolved model snapshot");
        Require(attempt.CompilerSchemaVersion, "Compiler schema version");
        Require(attempt.SystemPromptVersion, "System prompt version");
        if (attempt.Ordinal < 0 || attempt.Status == SceneImageEditCompilationAttemptStatus.Unknown)
            throw new InvalidOperationException("Media compilation attempt ordinal and status must be explicit.");
    }

    private static void ValidateRevision(MediaEditPromptRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        Require(revision.Id, "Media prompt revision id");
        Require(revision.CompilationAttemptId, "Media compilation attempt id");
        Require(revision.Prompt, "Prompt");
        RequireSha256(revision.PromptSha256, "Prompt checksum");
        if (revision.Ordinal < 0 || revision.RevisionKind == SceneImageEditPromptRevisionKind.Unknown)
            throw new InvalidOperationException("Media prompt revision ordinal and kind must be explicit.");
    }

    private static bool IsAllowedSessionTransition(MediaEditSessionStatus from, MediaEditSessionStatus to) =>
        from == to || from switch
        {
            MediaEditSessionStatus.Active => to is MediaEditSessionStatus.Ready
                or MediaEditSessionStatus.ClarificationRequired
                or MediaEditSessionStatus.Invalid
                or MediaEditSessionStatus.Failed,
            MediaEditSessionStatus.Ready => to is MediaEditSessionStatus.Active
                or MediaEditSessionStatus.Failed
                or MediaEditSessionStatus.Completed,
            MediaEditSessionStatus.ClarificationRequired => to == MediaEditSessionStatus.Active,
            MediaEditSessionStatus.Invalid => to == MediaEditSessionStatus.Active,
            MediaEditSessionStatus.Failed => to == MediaEditSessionStatus.Active,
            _ => false
        };

    private static bool IsAllowedAttemptTransition(
        SceneImageEditCompilationAttemptStatus from, SceneImageEditCompilationAttemptStatus to) =>
        from == to || (from, to) switch
        {
            (SceneImageEditCompilationAttemptStatus.Pending, SceneImageEditCompilationAttemptStatus.Compiling) => true,
            (SceneImageEditCompilationAttemptStatus.Pending, SceneImageEditCompilationAttemptStatus.Failed) => true,
            (SceneImageEditCompilationAttemptStatus.Compiling, SceneImageEditCompilationAttemptStatus.Ready
                or SceneImageEditCompilationAttemptStatus.ClarificationRequired
                or SceneImageEditCompilationAttemptStatus.Invalid
                or SceneImageEditCompilationAttemptStatus.Failed) => true,
            _ => false
        };

    private static MediaEditSubjectKind ParseSubjectKind(string value, string id) =>
        Enum.TryParse<MediaEditSubjectKind>(value, out var kind) && kind != MediaEditSubjectKind.Unknown
            ? kind
            : throw new InvalidOperationException($"Stored media edit session '{id}' has invalid subject kind '{value}'.");

    private static MediaEditSessionStatus ParseSessionStatus(string value, string id) =>
        Enum.TryParse<MediaEditSessionStatus>(value, out var status) && status != MediaEditSessionStatus.Unknown
            ? status
            : throw new InvalidOperationException($"Stored media edit session '{id}' has invalid status '{value}'.");

    private static SceneImageEditCompilationAttemptStatus ParseAttemptStatus(string value, string id) =>
        Enum.TryParse<SceneImageEditCompilationAttemptStatus>(value, out var status)
            && status != SceneImageEditCompilationAttemptStatus.Unknown
                ? status
                : throw new InvalidOperationException($"Stored media compilation attempt '{id}' has invalid status '{value}'.");

    private static SceneImageEditPromptRevisionKind ParseRevisionKind(string value, string id) =>
        Enum.TryParse<SceneImageEditPromptRevisionKind>(value, out var kind)
            && kind != SceneImageEditPromptRevisionKind.Unknown
                ? kind
                : throw new InvalidOperationException($"Stored media prompt revision '{id}' has invalid kind '{value}'.");

    private static DateTime ParseUtc(string value, string id) =>
        DateTime.TryParse(value, null, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Stored media edit row '{id}' has invalid UTC timestamp '{value}'.");

    private static bool ShaEquals(string left, string right) =>
        string.Equals(NormalizeSha256(left), NormalizeSha256(right), StringComparison.Ordinal);

    private static void Require(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{label} is required.");
    }

    private static void RequireSha256(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{label} is required.");

        var normalized = NormalizeSha256(value);
        if (normalized.Length != 64 || !normalized.All(char.IsAsciiHexDigit))
            throw new InvalidOperationException($"{label} must be a 64-character SHA-256 hex string.");
    }

    private static string NormalizeSha256(string value) => value.Trim().ToUpperInvariant();
}
