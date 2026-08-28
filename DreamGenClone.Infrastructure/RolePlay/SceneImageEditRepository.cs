using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class SceneImageEditRepository : ISceneImageEditRepository
{
    private readonly string _connectionString;

    public SceneImageEditRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task CreateSessionAsync(SceneImageEditSession session, CancellationToken cancellationToken = default)
    {
        Require(session.Id, "Edit session id");
        Require(session.SourceImageId, "Source image id");
        RequireSha256(session.SourceImageSha256, "Source image checksum");
        Require(session.SessionId, "Role-play session id");
        Require(session.InteractionId, "Interaction id");
        if (session.Status == SceneImageEditSessionStatus.Unknown)
            throw new InvalidOperationException("Edit session status must be explicit.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SceneImageEditSessions
                (Id, SourceImageId, SourceImageSha256, SessionId, InteractionId, Status, CreatedUtc, UpdatedUtc, CompletedUtc)
            VALUES
                ($id, $sourceImageId, $sourceSha, $sessionId, $interactionId, $status, $createdUtc, $updatedUtc, $completedUtc);
            """;
        command.Parameters.AddWithValue("$id", session.Id.Trim());
        command.Parameters.AddWithValue("$sourceImageId", session.SourceImageId.Trim());
        command.Parameters.AddWithValue("$sourceSha", NormalizeSha256(session.SourceImageSha256));
        command.Parameters.AddWithValue("$sessionId", session.SessionId.Trim());
        command.Parameters.AddWithValue("$interactionId", session.InteractionId.Trim());
        command.Parameters.AddWithValue("$status", session.Status.ToString());
        command.Parameters.AddWithValue("$createdUtc", session.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", session.UpdatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedUtc", session.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SceneImageEditSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Edit session id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetSessionAsync(connection, sessionId.Trim(), cancellationToken);
    }

    public async Task UpdateSessionStatusAsync(
        string sessionId,
        SceneImageEditSessionStatus status,
        DateTime updatedUtc,
        DateTime? completedUtc = null,
        CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Edit session id");
        if (status == SceneImageEditSessionStatus.Unknown)
            throw new InvalidOperationException("Edit session status must be explicit.");
        if (status == SceneImageEditSessionStatus.Completed && completedUtc is null)
            throw new InvalidOperationException("A completed edit session requires a completion timestamp.");
        if (status != SceneImageEditSessionStatus.Completed && completedUtc is not null)
            throw new InvalidOperationException("Only a completed edit session can have a completion timestamp.");

        await using var connection = await OpenAsync(cancellationToken);
        var existing = await GetSessionAsync(connection, sessionId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Edit session '{sessionId}' was not found.");
        if (!IsAllowedSessionTransition(existing.Status, status))
            throw new InvalidOperationException($"Edit session cannot transition from {existing.Status} to {status}.");

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SceneImageEditSessions
            SET Status = $status, UpdatedUtc = $updatedUtc, CompletedUtc = $completedUtc
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", sessionId.Trim());
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$updatedUtc", updatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedUtc", completedUtc?.ToString("O") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetDescriptionAsync(string sessionId, string description, DateTime updatedUtc, CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Edit session id");
        if (string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException("Edit session description must be non-empty.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SceneImageEditSessions
            SET DescriptionText = $description, UpdatedUtc = $updatedUtc
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$description", description.Trim());
        command.Parameters.AddWithValue("$updatedUtc", updatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$id", sessionId.Trim());
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rows == 0)
            throw new InvalidOperationException($"Edit session '{sessionId}' was not found.");
    }

    public async Task CreateAttemptAsync(SceneImageEditCompilationAttempt attempt, CancellationToken cancellationToken = default)
    {
        ValidateAttempt(attempt);
        if (attempt.Status != SceneImageEditCompilationAttemptStatus.Pending)
            throw new InvalidOperationException("A new compilation attempt must have Pending status.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var session = await GetSessionAsync(connection, attempt.EditSessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Edit session '{attempt.EditSessionId}' was not found.");
        if (!ShaEquals(session.SourceImageSha256, attempt.SourceImageSha256))
            throw new InvalidOperationException("Compilation attempt source checksum does not match its edit session.");

        await using var ordinalCommand = connection.CreateCommand();
        ordinalCommand.Transaction = (SqliteTransaction)transaction;
        ordinalCommand.CommandText = "SELECT COALESCE(MAX(Ordinal) + 1, 0) FROM SceneImageEditCompilationAttempts WHERE EditSessionId = $sessionId;";
        ordinalCommand.Parameters.AddWithValue("$sessionId", attempt.EditSessionId.Trim());
        var expectedOrdinal = Convert.ToInt32(await ordinalCommand.ExecuteScalarAsync(cancellationToken));
        if (attempt.Ordinal != expectedOrdinal)
            throw new InvalidOperationException($"Compilation attempt ordinal must be {expectedOrdinal}, not {attempt.Ordinal}.");

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO SceneImageEditCompilationAttempts
                (Id, EditSessionId, Ordinal, RawIntent, ClarificationContextJson, SourceImageSha256, Status,
                 ResolvedModelSnapshotJson, CompilerSchemaVersion, SystemPromptVersion, RawModelResponse,
                 ParsedResultJson, Error, CreatedUtc, StartedUtc, CompletedUtc)
            VALUES
                ($id, $sessionId, $ordinal, $rawIntent, $clarification, $sourceSha, $status,
                 $modelSnapshot, $schemaVersion, $promptVersion, $rawResponse,
                 $parsedResult, $error, $createdUtc, $startedUtc, $completedUtc);
            """;
        AddAttemptParameters(command, attempt);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAttemptAsync(SceneImageEditCompilationAttempt attempt, CancellationToken cancellationToken = default)
    {
        ValidateAttempt(attempt);
        await using var connection = await OpenAsync(cancellationToken);
        var existing = await GetAttemptAsync(connection, attempt.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Compilation attempt '{attempt.Id}' was not found.");

        if (existing.EditSessionId != attempt.EditSessionId
            || existing.Ordinal != attempt.Ordinal
            || existing.RawIntent != attempt.RawIntent
            || existing.ClarificationContextJson != attempt.ClarificationContextJson
            || !ShaEquals(existing.SourceImageSha256, attempt.SourceImageSha256)
            || existing.ResolvedModelSnapshotJson != attempt.ResolvedModelSnapshotJson
            || existing.CompilerSchemaVersion != attempt.CompilerSchemaVersion
            || existing.SystemPromptVersion != attempt.SystemPromptVersion)
        {
            throw new InvalidOperationException("Compilation attempt immutable input fields cannot be changed.");
        }

        if (!IsAllowedTransition(existing.Status, attempt.Status))
            throw new InvalidOperationException($"Compilation attempt cannot transition from {existing.Status} to {attempt.Status}.");

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SceneImageEditCompilationAttempts
            SET Status = $status, RawModelResponse = $rawResponse, ParsedResultJson = $parsedResult,
                Error = $error, StartedUtc = $startedUtc, CompletedUtc = $completedUtc
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", attempt.Id.Trim());
        command.Parameters.AddWithValue("$status", attempt.Status.ToString());
        command.Parameters.AddWithValue("$rawResponse", (object?)attempt.RawModelResponse ?? DBNull.Value);
        command.Parameters.AddWithValue("$parsedResult", (object?)attempt.ParsedResultJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)attempt.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$startedUtc", attempt.StartedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", attempt.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SceneImageEditCompilationAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default)
    {
        Require(attemptId, "Compilation attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetAttemptAsync(connection, attemptId.Trim(), cancellationToken);
    }

    public async Task<SceneImageEditCompilationAttempt?> GetLatestAttemptAsync(string editSessionId, CancellationToken cancellationToken = default)
    {
        Require(editSessionId, "Edit session id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{AttemptSelect} WHERE EditSessionId = $sessionId ORDER BY Ordinal DESC LIMIT 1;";
        command.Parameters.AddWithValue("$sessionId", editSessionId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
    }

    public async Task CreateRevisionAsync(SceneImageEditPromptRevision revision, CancellationToken cancellationToken = default)
    {
        Require(revision.Id, "Prompt revision id");
        Require(revision.CompilationAttemptId, "Compilation attempt id");
        Require(revision.Prompt, "Prompt");
        RequireSha256(revision.PromptSha256, "Prompt checksum");

        var calculatedSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revision.Prompt)));
        if (!ShaEquals(calculatedSha, revision.PromptSha256))
            throw new InvalidOperationException("Prompt revision checksum does not match its prompt.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var attempt = await GetAttemptAsync(connection, revision.CompilationAttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Compilation attempt '{revision.CompilationAttemptId}' was not found.");
        if (attempt.Status != SceneImageEditCompilationAttemptStatus.Ready)
            throw new InvalidOperationException("Prompt revisions require a Ready compilation attempt.");

        await using var latestAttemptCommand = connection.CreateCommand();
        latestAttemptCommand.Transaction = (SqliteTransaction)transaction;
        latestAttemptCommand.CommandText = "SELECT MAX(Ordinal) FROM SceneImageEditCompilationAttempts WHERE EditSessionId = $sessionId;";
        latestAttemptCommand.Parameters.AddWithValue("$sessionId", attempt.EditSessionId);
        if (Convert.ToInt32(await latestAttemptCommand.ExecuteScalarAsync(cancellationToken)) != attempt.Ordinal)
            throw new InvalidOperationException("Prompt revisions cannot be added to a stale compilation attempt.");

        await using var ordinalCommand = connection.CreateCommand();
        ordinalCommand.Transaction = (SqliteTransaction)transaction;
        ordinalCommand.CommandText = "SELECT COALESCE(MAX(Ordinal) + 1, 0) FROM SceneImageEditPromptRevisions WHERE CompilationAttemptId = $attemptId;";
        ordinalCommand.Parameters.AddWithValue("$attemptId", revision.CompilationAttemptId.Trim());
        var expectedOrdinal = Convert.ToInt32(await ordinalCommand.ExecuteScalarAsync(cancellationToken));
        if (revision.Ordinal != expectedOrdinal)
            throw new InvalidOperationException($"Prompt revision ordinal must be {expectedOrdinal}, not {revision.Ordinal}.");
        var expectedKind = expectedOrdinal == 0
            ? SceneImageEditPromptRevisionKind.CompilerOutput
            : SceneImageEditPromptRevisionKind.UserEdited;
        if (revision.RevisionKind != expectedKind)
            throw new InvalidOperationException($"Prompt revision {expectedOrdinal} must have kind {expectedKind}.");

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO SceneImageEditPromptRevisions
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
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SceneImageEditPromptRevision?> GetRevisionAsync(string revisionId, CancellationToken cancellationToken = default)
    {
        Require(revisionId, "Prompt revision id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetRevisionAsync(connection, revisionId.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<SceneImageEditPromptRevision>> ListRevisionsAsync(string attemptId, CancellationToken cancellationToken = default)
    {
        Require(attemptId, "Compilation attempt id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{RevisionSelect} WHERE CompilationAttemptId = $attemptId ORDER BY Ordinal;";
        command.Parameters.AddWithValue("$attemptId", attemptId.Trim());
        var results = new List<SceneImageEditPromptRevision>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadRevision(reader));
        return results;
    }

    public async Task<SceneImageEditPromptRevision> GetExecutableRevisionAsync(
        string editSessionId,
        string attemptId,
        string revisionId,
        string sourceImageSha256,
        string promptSha256,
        CancellationToken cancellationToken = default)
    {
        Require(editSessionId, "Edit session id");
        Require(attemptId, "Compilation attempt id");
        Require(revisionId, "Prompt revision id");
        RequireSha256(sourceImageSha256, "Source image checksum");
        RequireSha256(promptSha256, "Prompt checksum");

        await using var connection = await OpenAsync(cancellationToken);
        var session = await GetSessionAsync(connection, editSessionId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Edit session '{editSessionId}' was not found.");
        var attempt = await GetAttemptAsync(connection, attemptId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Compilation attempt '{attemptId}' was not found.");
        var revision = await GetRevisionAsync(connection, revisionId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Prompt revision '{revisionId}' was not found.");

        if (attempt.EditSessionId != session.Id || revision.CompilationAttemptId != attempt.Id)
            throw new InvalidOperationException("Edit session, compilation attempt, and prompt revision lineage do not match.");
        if (attempt.Status != SceneImageEditCompilationAttemptStatus.Ready)
            throw new InvalidOperationException("Only a Ready compilation attempt can execute.");
        if (!ShaEquals(session.SourceImageSha256, sourceImageSha256) || !ShaEquals(attempt.SourceImageSha256, sourceImageSha256))
            throw new InvalidOperationException("Source image checksum is stale.");
        if (!ShaEquals(revision.PromptSha256, promptSha256))
            throw new InvalidOperationException("Prompt revision checksum is stale.");

        await using var latestCommand = connection.CreateCommand();
        latestCommand.CommandText = """
            SELECT
                (SELECT MAX(Ordinal) FROM SceneImageEditCompilationAttempts WHERE EditSessionId = $sessionId),
                (SELECT MAX(Ordinal) FROM SceneImageEditPromptRevisions WHERE CompilationAttemptId = $attemptId);
            """;
        latestCommand.Parameters.AddWithValue("$sessionId", session.Id);
        latestCommand.Parameters.AddWithValue("$attemptId", attempt.Id);
        await using var reader = await latestCommand.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        if (reader.GetInt32(0) != attempt.Ordinal || reader.GetInt32(1) != revision.Ordinal)
            throw new InvalidOperationException("The selected compilation attempt or prompt revision is stale.");

        return revision;
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        DeleteGuardedAsync("SceneImageEditSessions", "Id", sessionId, "EditSessionId", "edit session", cancellationToken);

    public Task DeleteAttemptAsync(string attemptId, CancellationToken cancellationToken = default) =>
        DeleteGuardedAsync("SceneImageEditCompilationAttempts", "Id", attemptId, "EditCompilationAttemptId", "compilation attempt", cancellationToken);

    public Task DeleteRevisionAsync(string revisionId, CancellationToken cancellationToken = default) =>
        DeleteGuardedAsync("SceneImageEditPromptRevisions", "Id", revisionId, "EditPromptRevisionId", "prompt revision", cancellationToken);

    private async Task DeleteGuardedAsync(
        string table,
        string idColumn,
        string id,
        string sceneImageReferenceColumn,
        string entityName,
        CancellationToken cancellationToken)
    {
        Require(id, $"{entityName} id");
        await using var connection = await OpenAsync(cancellationToken);
        if (await TableExistsAsync(connection, "SceneImages", cancellationToken))
        {
            await using var referenceCommand = connection.CreateCommand();
            referenceCommand.CommandText = $"SELECT COUNT(*) FROM SceneImages WHERE {sceneImageReferenceColumn} = $id;";
            referenceCommand.Parameters.AddWithValue("$id", id.Trim());
            if (Convert.ToInt64(await referenceCommand.ExecuteScalarAsync(cancellationToken)) > 0)
                throw new InvalidOperationException($"Cannot delete {entityName} '{id}' because a scene image references it.");
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {table} WHERE {idColumn} = $id;";
            command.Parameters.AddWithValue("$id", id.Trim());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException($"Cannot delete {entityName} '{id}' because dependent edit records exist.", exception);
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
        await foreignKeys.ExecuteNonQueryAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SceneImageEditSessions (
                Id TEXT PRIMARY KEY, SourceImageId TEXT NOT NULL, SourceImageSha256 TEXT NOT NULL,
                SessionId TEXT NOT NULL, InteractionId TEXT NOT NULL, Status TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL, CompletedUtc TEXT NULL,
                DescriptionText TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_SceneImageEditSessions_Source ON SceneImageEditSessions (SourceImageId);
            CREATE INDEX IF NOT EXISTS IX_SceneImageEditSessions_Session ON SceneImageEditSessions (SessionId, UpdatedUtc DESC);
            CREATE TABLE IF NOT EXISTS SceneImageEditCompilationAttempts (
                Id TEXT PRIMARY KEY, EditSessionId TEXT NOT NULL, Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
                RawIntent TEXT NOT NULL, ClarificationContextJson TEXT NULL, SourceImageSha256 TEXT NOT NULL,
                Status TEXT NOT NULL, ResolvedModelSnapshotJson TEXT NOT NULL, CompilerSchemaVersion TEXT NOT NULL,
                SystemPromptVersion TEXT NOT NULL, RawModelResponse TEXT NULL, ParsedResultJson TEXT NULL,
                Error TEXT NULL, CreatedUtc TEXT NOT NULL, StartedUtc TEXT NULL, CompletedUtc TEXT NULL,
                FOREIGN KEY (EditSessionId) REFERENCES SceneImageEditSessions(Id) ON DELETE RESTRICT,
                UNIQUE (EditSessionId, Ordinal)
            );
            CREATE INDEX IF NOT EXISTS IX_SceneImageEditCompilationAttempts_SessionStatus
                ON SceneImageEditCompilationAttempts (EditSessionId, Status, Ordinal DESC);
            CREATE TABLE IF NOT EXISTS SceneImageEditPromptRevisions (
                Id TEXT PRIMARY KEY, CompilationAttemptId TEXT NOT NULL, Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
                Prompt TEXT NOT NULL, RevisionKind TEXT NOT NULL, PromptSha256 TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
                FOREIGN KEY (CompilationAttemptId) REFERENCES SceneImageEditCompilationAttempts(Id) ON DELETE RESTRICT,
                UNIQUE (CompilationAttemptId, Ordinal), UNIQUE (CompilationAttemptId, PromptSha256)
            );
            CREATE INDEX IF NOT EXISTS IX_SceneImageEditPromptRevisions_Attempt
                ON SceneImageEditPromptRevisions (CompilationAttemptId, Ordinal DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SceneImageEditSession?> GetSessionAsync(SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SourceImageId, SourceImageSha256, SessionId, InteractionId, Status, CreatedUtc, UpdatedUtc, CompletedUtc, DescriptionText
            FROM SceneImageEditSessions WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new SceneImageEditSession
        {
            Id = reader.GetString(0), SourceImageId = reader.GetString(1), SourceImageSha256 = reader.GetString(2),
            SessionId = reader.GetString(3), InteractionId = reader.GetString(4),
            Status = ParseEnum<SceneImageEditSessionStatus>(reader.GetString(5), "edit session", id),
            CreatedUtc = ParseUtc(reader.GetString(6), "edit session", id),
            UpdatedUtc = ParseUtc(reader.GetString(7), "edit session", id),
            CompletedUtc = reader.IsDBNull(8) ? null : ParseUtc(reader.GetString(8), "edit session", id),
            DescriptionText = reader.IsDBNull(9) ? null : reader.GetString(9)
        };
    }

    private static async Task<SceneImageEditCompilationAttempt?> GetAttemptAsync(SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"{AttemptSelect} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
    }

    private static async Task<SceneImageEditPromptRevision?> GetRevisionAsync(SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"{RevisionSelect} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRevision(reader) : null;
    }

    private const string AttemptSelect = """
        SELECT Id, EditSessionId, Ordinal, RawIntent, ClarificationContextJson, SourceImageSha256, Status,
               ResolvedModelSnapshotJson, CompilerSchemaVersion, SystemPromptVersion, RawModelResponse,
               ParsedResultJson, Error, CreatedUtc, StartedUtc, CompletedUtc
        FROM SceneImageEditCompilationAttempts
        """;

    private const string RevisionSelect = """
        SELECT Id, CompilationAttemptId, Ordinal, Prompt, RevisionKind, PromptSha256, CreatedUtc
        FROM SceneImageEditPromptRevisions
        """;

    private static SceneImageEditCompilationAttempt ReadAttempt(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        return new SceneImageEditCompilationAttempt
        {
            Id = id, EditSessionId = reader.GetString(1), Ordinal = reader.GetInt32(2), RawIntent = reader.GetString(3),
            ClarificationContextJson = reader.IsDBNull(4) ? null : reader.GetString(4), SourceImageSha256 = reader.GetString(5),
            Status = ParseEnum<SceneImageEditCompilationAttemptStatus>(reader.GetString(6), "compilation attempt", id),
            ResolvedModelSnapshotJson = reader.GetString(7), CompilerSchemaVersion = reader.GetString(8),
            SystemPromptVersion = reader.GetString(9), RawModelResponse = reader.IsDBNull(10) ? null : reader.GetString(10),
            ParsedResultJson = reader.IsDBNull(11) ? null : reader.GetString(11), Error = reader.IsDBNull(12) ? null : reader.GetString(12),
            CreatedUtc = ParseUtc(reader.GetString(13), "compilation attempt", id),
            StartedUtc = reader.IsDBNull(14) ? null : ParseUtc(reader.GetString(14), "compilation attempt", id),
            CompletedUtc = reader.IsDBNull(15) ? null : ParseUtc(reader.GetString(15), "compilation attempt", id)
        };
    }

    private static SceneImageEditPromptRevision ReadRevision(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        return new SceneImageEditPromptRevision
        {
            Id = id, CompilationAttemptId = reader.GetString(1), Ordinal = reader.GetInt32(2), Prompt = reader.GetString(3),
            RevisionKind = ParseEnum<SceneImageEditPromptRevisionKind>(reader.GetString(4), "prompt revision", id),
            PromptSha256 = reader.GetString(5), CreatedUtc = ParseUtc(reader.GetString(6), "prompt revision", id)
        };
    }

    private static void AddAttemptParameters(SqliteCommand command, SceneImageEditCompilationAttempt attempt)
    {
        command.Parameters.AddWithValue("$id", attempt.Id.Trim());
        command.Parameters.AddWithValue("$sessionId", attempt.EditSessionId.Trim());
        command.Parameters.AddWithValue("$ordinal", attempt.Ordinal);
        command.Parameters.AddWithValue("$rawIntent", attempt.RawIntent);
        command.Parameters.AddWithValue("$clarification", (object?)attempt.ClarificationContextJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceSha", NormalizeSha256(attempt.SourceImageSha256));
        command.Parameters.AddWithValue("$status", attempt.Status.ToString());
        command.Parameters.AddWithValue("$modelSnapshot", attempt.ResolvedModelSnapshotJson);
        command.Parameters.AddWithValue("$schemaVersion", attempt.CompilerSchemaVersion);
        command.Parameters.AddWithValue("$promptVersion", attempt.SystemPromptVersion);
        command.Parameters.AddWithValue("$rawResponse", (object?)attempt.RawModelResponse ?? DBNull.Value);
        command.Parameters.AddWithValue("$parsedResult", (object?)attempt.ParsedResultJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)attempt.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", attempt.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$startedUtc", attempt.StartedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", attempt.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
    }

    private static void ValidateAttempt(SceneImageEditCompilationAttempt attempt)
    {
        Require(attempt.Id, "Compilation attempt id");
        Require(attempt.EditSessionId, "Edit session id");
        Require(attempt.RawIntent, "Raw intent");
        RequireSha256(attempt.SourceImageSha256, "Source image checksum");
        Require(attempt.ResolvedModelSnapshotJson, "Resolved model snapshot");
        Require(attempt.CompilerSchemaVersion, "Compiler schema version");
        Require(attempt.SystemPromptVersion, "System prompt version");
        if (attempt.Ordinal < 0) throw new InvalidOperationException("Compilation attempt ordinal cannot be negative.");
        if (attempt.Status == SceneImageEditCompilationAttemptStatus.Unknown)
            throw new InvalidOperationException("Compilation attempt status must be explicit.");
    }

    private static bool IsAllowedTransition(SceneImageEditCompilationAttemptStatus from, SceneImageEditCompilationAttemptStatus to) =>
        from == to || (from, to) switch
        {
            (SceneImageEditCompilationAttemptStatus.Pending, SceneImageEditCompilationAttemptStatus.Compiling) => true,
            (SceneImageEditCompilationAttemptStatus.Pending, SceneImageEditCompilationAttemptStatus.Failed) => true,
            (SceneImageEditCompilationAttemptStatus.Compiling, SceneImageEditCompilationAttemptStatus.Ready) => true,
            (SceneImageEditCompilationAttemptStatus.Compiling, SceneImageEditCompilationAttemptStatus.ClarificationRequired) => true,
            (SceneImageEditCompilationAttemptStatus.Compiling, SceneImageEditCompilationAttemptStatus.Invalid) => true,
            (SceneImageEditCompilationAttemptStatus.Compiling, SceneImageEditCompilationAttemptStatus.Failed) => true,
            _ => false
        };

    private static bool IsAllowedSessionTransition(SceneImageEditSessionStatus from, SceneImageEditSessionStatus to) =>
        from == to || from switch
        {
            SceneImageEditSessionStatus.Active => to is SceneImageEditSessionStatus.Ready
                or SceneImageEditSessionStatus.ClarificationRequired
                or SceneImageEditSessionStatus.Invalid
                or SceneImageEditSessionStatus.Failed,
            SceneImageEditSessionStatus.Ready => to is SceneImageEditSessionStatus.Active
                or SceneImageEditSessionStatus.Failed
                or SceneImageEditSessionStatus.Completed,
            SceneImageEditSessionStatus.ClarificationRequired => to == SceneImageEditSessionStatus.Active,
            SceneImageEditSessionStatus.Invalid => to == SceneImageEditSessionStatus.Active,
            SceneImageEditSessionStatus.Failed => to == SceneImageEditSessionStatus.Active,
            _ => false
        };

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $table;";
        command.Parameters.AddWithValue("$table", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static string NormalizeSha256(string value) => value.Trim().ToUpperInvariant();
    private static bool ShaEquals(string left, string right) =>
        string.Equals(NormalizeSha256(left), NormalizeSha256(right), StringComparison.Ordinal);

    private static void RequireSha256(string value, string label)
    {
        Require(value, label);
        var normalized = NormalizeSha256(value);
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"{label} must be a 64-character SHA-256 value.");
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{label} is required.");
    }

    private static T ParseEnum<T>(string value, string entity, string id) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var parsed) && Convert.ToInt32(parsed) != 0
            ? parsed
            : throw new InvalidOperationException($"Stored {entity} '{id}' has invalid {typeof(T).Name} value '{value}'.");

    private static DateTime ParseUtc(string value, string entity, string id) =>
        DateTime.TryParse(value, null, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Stored {entity} '{id}' has invalid UTC timestamp '{value}'.");
}