using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class SceneAssetImageEditRepository : ISceneAssetImageEditRepository
{
    private readonly string _connectionString;

    public SceneAssetImageEditRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task CreateSessionAsync(SceneAssetImageEditSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        Require(session.Id, "Asset edit session id");
        Require(session.AssetId, "Asset id");
        Require(session.SourceImageId, "Source asset image id");
        RequireSha256(session.SourceImageSha256, "Source asset image checksum");
        if (session.Status == SceneAssetImageEditSessionStatus.Unknown)
            throw new InvalidOperationException("Asset edit session status must be explicit.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SceneAssetImageEditSessions
                (Id, AssetId, SourceImageId, SourceImageSha256, Status, DescriptionText, CreatedUtc, UpdatedUtc, CompletedUtc)
            VALUES
                ($id, $assetId, $sourceImageId, $sourceSha, $status, $description, $createdUtc, $updatedUtc, $completedUtc);
            """;
        command.Parameters.AddWithValue("$id", session.Id.Trim());
        command.Parameters.AddWithValue("$assetId", session.AssetId.Trim());
        command.Parameters.AddWithValue("$sourceImageId", session.SourceImageId.Trim());
        command.Parameters.AddWithValue("$sourceSha", NormalizeSha256(session.SourceImageSha256));
        command.Parameters.AddWithValue("$status", session.Status.ToString());
        command.Parameters.AddWithValue("$description", (object?)session.DescriptionText?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", session.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", session.UpdatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedUtc", session.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SceneAssetImageEditSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Asset edit session id");
        await using var connection = await OpenAsync(cancellationToken);
        return await GetSessionAsync(connection, sessionId.Trim(), cancellationToken);
    }

    public async Task UpdateSessionStatusAsync(string sessionId, SceneAssetImageEditSessionStatus status, DateTime updatedUtc, DateTime? completedUtc = null, CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Asset edit session id");
        if (status == SceneAssetImageEditSessionStatus.Unknown)
            throw new InvalidOperationException("Asset edit session status must be explicit.");
        if (status == SceneAssetImageEditSessionStatus.Completed && completedUtc is null)
            throw new InvalidOperationException("A completed asset edit session requires a completion timestamp.");
        if (status != SceneAssetImageEditSessionStatus.Completed && completedUtc is not null)
            throw new InvalidOperationException("Only a completed asset edit session can have a completion timestamp.");

        await using var connection = await OpenAsync(cancellationToken);
        var existing = await GetSessionAsync(connection, sessionId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Asset edit session '{sessionId}' was not found.");
        if (!IsAllowedTransition(existing.Status, status))
            throw new InvalidOperationException($"Asset edit session cannot transition from {existing.Status} to {status}.");

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE SceneAssetImageEditSessions SET Status = $status, UpdatedUtc = $updatedUtc, CompletedUtc = $completedUtc WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", sessionId.Trim());
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$updatedUtc", updatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedUtc", completedUtc?.ToString("O") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetDescriptionAsync(string sessionId, string description, DateTime updatedUtc, CancellationToken cancellationToken = default)
    {
        Require(sessionId, "Asset edit session id");
        Require(description, "Asset edit session description");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE SceneAssetImageEditSessions SET DescriptionText = $description, UpdatedUtc = $updatedUtc WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", sessionId.Trim());
        command.Parameters.AddWithValue("$description", description.Trim());
        command.Parameters.AddWithValue("$updatedUtc", updatedUtc.ToString("O"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Asset edit session '{sessionId}' was not found.");
    }

    public async Task CreateAttemptAsync(SceneAssetImageEditCompilationAttempt attempt, CancellationToken cancellationToken = default)
    {
        ValidateAttempt(attempt);
        if (attempt.Status != SceneImageEditCompilationAttemptStatus.Pending) throw new InvalidOperationException("A new asset compilation attempt must be Pending.");
        await using var connection = await OpenAsync(cancellationToken);
        var session = await GetSessionAsync(connection, attempt.EditSessionId, cancellationToken) ?? throw new InvalidOperationException($"Asset edit session '{attempt.EditSessionId}' was not found.");
        if (!string.Equals(NormalizeSha256(session.SourceImageSha256), NormalizeSha256(attempt.SourceImageSha256), StringComparison.Ordinal)) throw new InvalidOperationException("Asset compilation attempt source checksum does not match its edit session.");
        await using var ordinal = connection.CreateCommand();
        ordinal.CommandText = "SELECT COALESCE(MAX(Ordinal) + 1, 0) FROM SceneAssetImageEditCompilationAttempts WHERE EditSessionId = $id;";
        ordinal.Parameters.AddWithValue("$id", attempt.EditSessionId);
        if (Convert.ToInt32(await ordinal.ExecuteScalarAsync(cancellationToken)) != attempt.Ordinal) throw new InvalidOperationException("Asset compilation attempt ordinal is stale.");
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO SceneAssetImageEditCompilationAttempts (Id, EditSessionId, Ordinal, RawIntent, ClarificationContextJson, SourceImageSha256, Status, ResolvedModelSnapshotJson, CompilerSchemaVersion, SystemPromptVersion, RawModelResponse, ParsedResultJson, Error, CreatedUtc, StartedUtc, CompletedUtc) VALUES ($id,$session,$ordinal,$intent,$clarification,$sha,$status,$model,$schema,$promptVersion,$response,$result,$error,$created,$started,$completed);";
        AddAttemptParameters(command, attempt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAttemptAsync(SceneAssetImageEditCompilationAttempt attempt, CancellationToken cancellationToken = default)
    {
        ValidateAttempt(attempt);
        var existing = await GetAttemptAsync(attempt.Id, cancellationToken) ?? throw new InvalidOperationException($"Asset compilation attempt '{attempt.Id}' was not found.");
        if (existing.EditSessionId != attempt.EditSessionId || existing.Ordinal != attempt.Ordinal || existing.RawIntent != attempt.RawIntent || existing.ClarificationContextJson != attempt.ClarificationContextJson || !string.Equals(NormalizeSha256(existing.SourceImageSha256), NormalizeSha256(attempt.SourceImageSha256), StringComparison.Ordinal) || existing.ResolvedModelSnapshotJson != attempt.ResolvedModelSnapshotJson || existing.CompilerSchemaVersion != attempt.CompilerSchemaVersion || existing.SystemPromptVersion != attempt.SystemPromptVersion) throw new InvalidOperationException("Asset compilation attempt immutable input fields cannot be changed.");
        if (!IsAllowedAttemptTransition(existing.Status, attempt.Status)) throw new InvalidOperationException($"Asset compilation attempt cannot transition from {existing.Status} to {attempt.Status}.");
        await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE SceneAssetImageEditCompilationAttempts SET Status=$status, RawModelResponse=$response, ParsedResultJson=$result, Error=$error, StartedUtc=$started, CompletedUtc=$completed WHERE Id=$id;";
        AddAttemptParameters(command, attempt); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SceneAssetImageEditCompilationAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default)
    {
        Require(attemptId, "Asset compilation attempt id"); await using var connection = await OpenAsync(cancellationToken); return await GetAttemptAsync(connection, attemptId.Trim(), cancellationToken);
    }

    public async Task<SceneAssetImageEditCompilationAttempt?> GetLatestAttemptAsync(string editSessionId, CancellationToken cancellationToken = default)
    {
        Require(editSessionId, "Asset edit session id"); await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = $"{AttemptSelect} WHERE EditSessionId=$id ORDER BY Ordinal DESC LIMIT 1;"; command.Parameters.AddWithValue("$id", editSessionId.Trim()); await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
    }

    public async Task CreateRevisionAsync(SceneAssetImageEditPromptRevision revision, CancellationToken cancellationToken = default)
    {
        Require(revision.Id, "Asset prompt revision id"); Require(revision.CompilationAttemptId, "Asset compilation attempt id"); Require(revision.Prompt, "Prompt"); RequireSha256(revision.PromptSha256, "Prompt checksum");
        if (!string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(revision.Prompt))), NormalizeSha256(revision.PromptSha256), StringComparison.Ordinal)) throw new InvalidOperationException("Asset prompt revision checksum does not match its prompt.");
        var attempt = await GetAttemptAsync(revision.CompilationAttemptId, cancellationToken) ?? throw new InvalidOperationException($"Asset compilation attempt '{revision.CompilationAttemptId}' was not found.");
        if (attempt.Status != SceneImageEditCompilationAttemptStatus.Ready) throw new InvalidOperationException("Asset prompt revisions require a Ready compilation attempt.");
        var revisions = await ListRevisionsAsync(attempt.Id, cancellationToken); if (revision.Ordinal != revisions.Count || revision.RevisionKind != (revisions.Count == 0 ? SceneImageEditPromptRevisionKind.CompilerOutput : SceneImageEditPromptRevisionKind.UserEdited)) throw new InvalidOperationException("Asset prompt revision ordinal or kind is invalid.");
        await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO SceneAssetImageEditPromptRevisions (Id, CompilationAttemptId, Ordinal, Prompt, RevisionKind, PromptSha256, CreatedUtc) VALUES ($id,$attempt,$ordinal,$prompt,$kind,$sha,$created);"; command.Parameters.AddWithValue("$id", revision.Id); command.Parameters.AddWithValue("$attempt", revision.CompilationAttemptId); command.Parameters.AddWithValue("$ordinal", revision.Ordinal); command.Parameters.AddWithValue("$prompt", revision.Prompt); command.Parameters.AddWithValue("$kind", revision.RevisionKind.ToString()); command.Parameters.AddWithValue("$sha", NormalizeSha256(revision.PromptSha256)); command.Parameters.AddWithValue("$created", revision.CreatedUtc.ToString("O")); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SceneAssetImageEditPromptRevision?> GetRevisionAsync(string revisionId, CancellationToken cancellationToken = default)
    {
        Require(revisionId, "Asset prompt revision id"); await using var connection = await OpenAsync(cancellationToken); return await GetRevisionAsync(connection, revisionId.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<SceneAssetImageEditPromptRevision>> ListRevisionsAsync(string attemptId, CancellationToken cancellationToken = default)
    {
        Require(attemptId, "Asset compilation attempt id"); await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = $"{RevisionSelect} WHERE CompilationAttemptId=$id ORDER BY Ordinal;"; command.Parameters.AddWithValue("$id", attemptId.Trim()); var results = new List<SceneAssetImageEditPromptRevision>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) results.Add(ReadRevision(reader)); return results;
    }

    public async Task<SceneAssetImageEditPromptRevision> GetExecutableRevisionAsync(string editSessionId, string attemptId, string revisionId, string sourceImageSha256, string promptSha256, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(editSessionId, cancellationToken) ?? throw new InvalidOperationException($"Asset edit session '{editSessionId}' was not found."); var attempt = await GetAttemptAsync(attemptId, cancellationToken) ?? throw new InvalidOperationException($"Asset compilation attempt '{attemptId}' was not found."); var revision = await GetRevisionAsync(revisionId, cancellationToken) ?? throw new InvalidOperationException($"Asset prompt revision '{revisionId}' was not found.");
        if (attempt.EditSessionId != session.Id || revision.CompilationAttemptId != attempt.Id || attempt.Status != SceneImageEditCompilationAttemptStatus.Ready || !string.Equals(NormalizeSha256(session.SourceImageSha256), NormalizeSha256(sourceImageSha256), StringComparison.Ordinal) || !string.Equals(NormalizeSha256(attempt.SourceImageSha256), NormalizeSha256(sourceImageSha256), StringComparison.Ordinal) || !string.Equals(NormalizeSha256(revision.PromptSha256), NormalizeSha256(promptSha256), StringComparison.Ordinal)) throw new InvalidOperationException("Asset edit execution provenance is stale or invalid.");
        var latest = await GetLatestAttemptAsync(session.Id, cancellationToken); var revisions = await ListRevisionsAsync(attempt.Id, cancellationToken); if (latest?.Id != attempt.Id || revisions.LastOrDefault()?.Id != revision.Id) throw new InvalidOperationException("The selected asset compilation attempt or prompt revision is stale."); return revision;
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
            CREATE TABLE IF NOT EXISTS SceneAssetImageEditSessions (
                Id TEXT PRIMARY KEY, AssetId TEXT NOT NULL, SourceImageId TEXT NOT NULL,
                SourceImageSha256 TEXT NOT NULL, Status TEXT NOT NULL, DescriptionText TEXT NULL,
                CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL, CompletedUtc TEXT NULL,
                FOREIGN KEY (AssetId) REFERENCES SceneAssets(Id) ON DELETE RESTRICT,
                FOREIGN KEY (SourceImageId) REFERENCES SceneAssetImages(Id) ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS IX_SceneAssetImageEditSessions_Source
                ON SceneAssetImageEditSessions (SourceImageId, UpdatedUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_SceneAssetImageEditSessions_Asset
                ON SceneAssetImageEditSessions (AssetId, UpdatedUtc DESC);
            CREATE TABLE IF NOT EXISTS SceneAssetImageEditCompilationAttempts (
                Id TEXT PRIMARY KEY, EditSessionId TEXT NOT NULL, Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
                RawIntent TEXT NOT NULL, ClarificationContextJson TEXT NULL, SourceImageSha256 TEXT NOT NULL,
                Status TEXT NOT NULL, ResolvedModelSnapshotJson TEXT NOT NULL, CompilerSchemaVersion TEXT NOT NULL,
                SystemPromptVersion TEXT NOT NULL, RawModelResponse TEXT NULL, ParsedResultJson TEXT NULL,
                Error TEXT NULL, CreatedUtc TEXT NOT NULL, StartedUtc TEXT NULL, CompletedUtc TEXT NULL,
                FOREIGN KEY (EditSessionId) REFERENCES SceneAssetImageEditSessions(Id) ON DELETE RESTRICT,
                UNIQUE (EditSessionId, Ordinal)
            );
            CREATE INDEX IF NOT EXISTS IX_SceneAssetImageEditCompilationAttempts_SessionStatus
                ON SceneAssetImageEditCompilationAttempts (EditSessionId, Status, Ordinal DESC);
            CREATE TABLE IF NOT EXISTS SceneAssetImageEditPromptRevisions (
                Id TEXT PRIMARY KEY, CompilationAttemptId TEXT NOT NULL, Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
                Prompt TEXT NOT NULL, RevisionKind TEXT NOT NULL, PromptSha256 TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
                FOREIGN KEY (CompilationAttemptId) REFERENCES SceneAssetImageEditCompilationAttempts(Id) ON DELETE RESTRICT,
                UNIQUE (CompilationAttemptId, Ordinal), UNIQUE (CompilationAttemptId, PromptSha256)
            );
            CREATE INDEX IF NOT EXISTS IX_SceneAssetImageEditPromptRevisions_Attempt
                ON SceneAssetImageEditPromptRevisions (CompilationAttemptId, Ordinal DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SceneAssetImageEditSession?> GetSessionAsync(SqliteConnection connection, string sessionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, AssetId, SourceImageId, SourceImageSha256, Status, DescriptionText, CreatedUtc, UpdatedUtc, CompletedUtc FROM SceneAssetImageEditSessions WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new SceneAssetImageEditSession
        {
            Id = reader.GetString(0), AssetId = reader.GetString(1), SourceImageId = reader.GetString(2), SourceImageSha256 = reader.GetString(3),
            Status = ParseStatus(reader.GetString(4), sessionId), DescriptionText = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedUtc = ParseUtc(reader.GetString(6), sessionId), UpdatedUtc = ParseUtc(reader.GetString(7), sessionId),
            CompletedUtc = reader.IsDBNull(8) ? null : ParseUtc(reader.GetString(8), sessionId)
        };
    }

    private static async Task<SceneAssetImageEditCompilationAttempt?> GetAttemptAsync(SqliteConnection connection, string attemptId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandText = $"{AttemptSelect} WHERE Id=$id;"; command.Parameters.AddWithValue("$id", attemptId); await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? ReadAttempt(reader) : null;
    }

    private static async Task<SceneAssetImageEditPromptRevision?> GetRevisionAsync(SqliteConnection connection, string revisionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandText = $"{RevisionSelect} WHERE Id=$id;"; command.Parameters.AddWithValue("$id", revisionId); await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? ReadRevision(reader) : null;
    }

    private const string AttemptSelect = "SELECT Id, EditSessionId, Ordinal, RawIntent, ClarificationContextJson, SourceImageSha256, Status, ResolvedModelSnapshotJson, CompilerSchemaVersion, SystemPromptVersion, RawModelResponse, ParsedResultJson, Error, CreatedUtc, StartedUtc, CompletedUtc FROM SceneAssetImageEditCompilationAttempts";
    private const string RevisionSelect = "SELECT Id, CompilationAttemptId, Ordinal, Prompt, RevisionKind, PromptSha256, CreatedUtc FROM SceneAssetImageEditPromptRevisions";

    private static SceneAssetImageEditCompilationAttempt ReadAttempt(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0), EditSessionId = reader.GetString(1), Ordinal = reader.GetInt32(2), RawIntent = reader.GetString(3), ClarificationContextJson = reader.IsDBNull(4) ? null : reader.GetString(4), SourceImageSha256 = reader.GetString(5), Status = ParseAttemptStatus(reader.GetString(6), reader.GetString(0)), ResolvedModelSnapshotJson = reader.GetString(7), CompilerSchemaVersion = reader.GetString(8), SystemPromptVersion = reader.GetString(9), RawModelResponse = reader.IsDBNull(10) ? null : reader.GetString(10), ParsedResultJson = reader.IsDBNull(11) ? null : reader.GetString(11), Error = reader.IsDBNull(12) ? null : reader.GetString(12), CreatedUtc = ParseUtc(reader.GetString(13), reader.GetString(0)), StartedUtc = reader.IsDBNull(14) ? null : ParseUtc(reader.GetString(14), reader.GetString(0)), CompletedUtc = reader.IsDBNull(15) ? null : ParseUtc(reader.GetString(15), reader.GetString(0))
    };

    private static SceneAssetImageEditPromptRevision ReadRevision(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0), CompilationAttemptId = reader.GetString(1), Ordinal = reader.GetInt32(2), Prompt = reader.GetString(3), RevisionKind = ParseRevisionKind(reader.GetString(4), reader.GetString(0)), PromptSha256 = reader.GetString(5), CreatedUtc = ParseUtc(reader.GetString(6), reader.GetString(0))
    };

    private static void AddAttemptParameters(SqliteCommand command, SceneAssetImageEditCompilationAttempt attempt)
    {
        command.Parameters.AddWithValue("$id", attempt.Id); command.Parameters.AddWithValue("$session", attempt.EditSessionId); command.Parameters.AddWithValue("$ordinal", attempt.Ordinal); command.Parameters.AddWithValue("$intent", attempt.RawIntent); command.Parameters.AddWithValue("$clarification", (object?)attempt.ClarificationContextJson ?? DBNull.Value); command.Parameters.AddWithValue("$sha", NormalizeSha256(attempt.SourceImageSha256)); command.Parameters.AddWithValue("$status", attempt.Status.ToString()); command.Parameters.AddWithValue("$model", attempt.ResolvedModelSnapshotJson); command.Parameters.AddWithValue("$schema", attempt.CompilerSchemaVersion); command.Parameters.AddWithValue("$promptVersion", attempt.SystemPromptVersion); command.Parameters.AddWithValue("$response", (object?)attempt.RawModelResponse ?? DBNull.Value); command.Parameters.AddWithValue("$result", (object?)attempt.ParsedResultJson ?? DBNull.Value); command.Parameters.AddWithValue("$error", (object?)attempt.Error ?? DBNull.Value); command.Parameters.AddWithValue("$created", attempt.CreatedUtc.ToString("O")); command.Parameters.AddWithValue("$started", attempt.StartedUtc?.ToString("O") ?? (object)DBNull.Value); command.Parameters.AddWithValue("$completed", attempt.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
    }

    private static void ValidateAttempt(SceneAssetImageEditCompilationAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt); Require(attempt.Id, "Asset compilation attempt id"); Require(attempt.EditSessionId, "Asset edit session id"); Require(attempt.RawIntent, "Raw intent"); RequireSha256(attempt.SourceImageSha256, "Source asset image checksum"); Require(attempt.ResolvedModelSnapshotJson, "Resolved model snapshot"); Require(attempt.CompilerSchemaVersion, "Compiler schema version"); Require(attempt.SystemPromptVersion, "System prompt version"); if (attempt.Ordinal < 0 || attempt.Status == SceneImageEditCompilationAttemptStatus.Unknown) throw new InvalidOperationException("Asset compilation attempt ordinal and status must be explicit.");
    }

    private static bool IsAllowedAttemptTransition(SceneImageEditCompilationAttemptStatus from, SceneImageEditCompilationAttemptStatus to) => from == to || (from, to) switch { (SceneImageEditCompilationAttemptStatus.Pending, SceneImageEditCompilationAttemptStatus.Compiling) => true, (SceneImageEditCompilationAttemptStatus.Pending, SceneImageEditCompilationAttemptStatus.Failed) => true, (SceneImageEditCompilationAttemptStatus.Compiling, SceneImageEditCompilationAttemptStatus.Ready or SceneImageEditCompilationAttemptStatus.ClarificationRequired or SceneImageEditCompilationAttemptStatus.Invalid or SceneImageEditCompilationAttemptStatus.Failed) => true, _ => false };

    private static SceneImageEditCompilationAttemptStatus ParseAttemptStatus(string value, string id) => Enum.TryParse<SceneImageEditCompilationAttemptStatus>(value, out var status) && status != SceneImageEditCompilationAttemptStatus.Unknown ? status : throw new InvalidOperationException($"Stored asset compilation attempt '{id}' has invalid status '{value}'.");
    private static SceneImageEditPromptRevisionKind ParseRevisionKind(string value, string id) => Enum.TryParse<SceneImageEditPromptRevisionKind>(value, out var kind) && kind != SceneImageEditPromptRevisionKind.Unknown ? kind : throw new InvalidOperationException($"Stored asset prompt revision '{id}' has invalid kind '{value}'.");

    private static bool IsAllowedTransition(SceneAssetImageEditSessionStatus from, SceneAssetImageEditSessionStatus to) =>
        from == to || from switch
        {
            SceneAssetImageEditSessionStatus.Active => to is SceneAssetImageEditSessionStatus.Ready or SceneAssetImageEditSessionStatus.ClarificationRequired or SceneAssetImageEditSessionStatus.Invalid or SceneAssetImageEditSessionStatus.Failed,
            SceneAssetImageEditSessionStatus.Ready => to is SceneAssetImageEditSessionStatus.Active or SceneAssetImageEditSessionStatus.Failed or SceneAssetImageEditSessionStatus.Completed,
            SceneAssetImageEditSessionStatus.ClarificationRequired or SceneAssetImageEditSessionStatus.Invalid or SceneAssetImageEditSessionStatus.Failed => to == SceneAssetImageEditSessionStatus.Active,
            _ => false
        };

    private static SceneAssetImageEditSessionStatus ParseStatus(string value, string id) =>
        Enum.TryParse<SceneAssetImageEditSessionStatus>(value, out var status) && status != SceneAssetImageEditSessionStatus.Unknown
            ? status : throw new InvalidOperationException($"Stored asset edit session '{id}' has invalid status '{value}'.");

    private static DateTime ParseUtc(string value, string id) =>
        DateTime.TryParse(value, null, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed : throw new InvalidOperationException($"Stored asset edit session '{id}' has invalid UTC timestamp '{value}'.");

    private static string NormalizeSha256(string value) => value.Trim().ToUpperInvariant();

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
}