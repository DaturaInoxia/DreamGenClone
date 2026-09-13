using Microsoft.Data.Sqlite;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>Rows copied out of the two legacy edit stores by the one-time backfill.</summary>
public sealed record MediaEditSchemaReport(int Sessions, int Attempts, int Revisions)
{
    public static readonly MediaEditSchemaReport None = new(0, 0, 0);

    public int Total => Sessions + Attempts + Revisions;
}

/// <summary>
/// Schema for the single media edit store, plus the one-time backfill from the two legacy stores
/// (<c>SceneImageEdit*</c> and <c>SceneAssetImageEdit*</c>). The backfill preserves ids because the
/// attempt and revision rows reference sessions by id.
/// </summary>
public static class MediaEditSchema
{
    private const string MigrationMarker = "media-edit-store-v1";

    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS MediaEditSessions (
            Id TEXT PRIMARY KEY, SubjectKind TEXT NOT NULL, SubjectId TEXT NOT NULL, SubjectScopeId TEXT NULL,
            SourceImageId TEXT NOT NULL, SourceImageSha256 TEXT NOT NULL, Status TEXT NOT NULL,
            DescriptionText TEXT NULL, CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL, CompletedUtc TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_MediaEditSessions_Subject
            ON MediaEditSessions (SubjectKind, SubjectId, UpdatedUtc DESC);
        CREATE INDEX IF NOT EXISTS IX_MediaEditSessions_Source
            ON MediaEditSessions (SourceImageId, UpdatedUtc DESC);

        CREATE TABLE IF NOT EXISTS MediaEditCompilationAttempts (
            Id TEXT PRIMARY KEY, EditSessionId TEXT NOT NULL, Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
            RawIntent TEXT NOT NULL, ClarificationContextJson TEXT NULL, SourceImageSha256 TEXT NOT NULL,
            Status TEXT NOT NULL, ResolvedModelSnapshotJson TEXT NOT NULL, CompilerSchemaVersion TEXT NOT NULL,
            SystemPromptVersion TEXT NOT NULL, RawModelResponse TEXT NULL, ParsedResultJson TEXT NULL,
            Error TEXT NULL, CreatedUtc TEXT NOT NULL, StartedUtc TEXT NULL, CompletedUtc TEXT NULL,
            FOREIGN KEY (EditSessionId) REFERENCES MediaEditSessions(Id) ON DELETE RESTRICT,
            UNIQUE (EditSessionId, Ordinal)
        );
        CREATE INDEX IF NOT EXISTS IX_MediaEditCompilationAttempts_SessionStatus
            ON MediaEditCompilationAttempts (EditSessionId, Status, Ordinal DESC);

        CREATE TABLE IF NOT EXISTS MediaEditPromptRevisions (
            Id TEXT PRIMARY KEY, CompilationAttemptId TEXT NOT NULL, Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
            Prompt TEXT NOT NULL, RevisionKind TEXT NOT NULL, PromptSha256 TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
            FOREIGN KEY (CompilationAttemptId) REFERENCES MediaEditCompilationAttempts(Id) ON DELETE RESTRICT,
            UNIQUE (CompilationAttemptId, Ordinal), UNIQUE (CompilationAttemptId, PromptSha256)
        );
        CREATE INDEX IF NOT EXISTS IX_MediaEditPromptRevisions_Attempt
            ON MediaEditPromptRevisions (CompilationAttemptId, Ordinal DESC);

        CREATE TABLE IF NOT EXISTS MediaEditStoreMigration (
            Key TEXT PRIMARY KEY, AppliedUtc TEXT NOT NULL
        );
        """;

    /// <summary>
    /// Creates the store if needed and backfills the legacy rows exactly once per database.
    /// </summary>
    public static async Task<MediaEditSchemaReport> EnsureAsync(
        SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        await using (var ddl = connection.CreateCommand())
        {
            ddl.CommandText = Ddl;
            await ddl.ExecuteNonQueryAsync(cancellationToken);
        }

        if (await HasMarkerAsync(connection, cancellationToken))
            return MediaEditSchemaReport.None;

        var report = await BackfillAsync(connection, cancellationToken);
        await MarkAsync(connection, cancellationToken);
        return report;
    }

    /// <summary>
    /// Runs the legacy backfill again regardless of the marker. Used by the cutover that stops the
    /// legacy stores and by repair. Idempotent: rows that already exist in the new store are kept.
    /// </summary>
    public static Task<MediaEditSchemaReport> ReBackfillAsync(
        SqliteConnection connection, CancellationToken cancellationToken = default)
        => BackfillAsync(connection, cancellationToken);

    private static async Task<bool> HasMarkerAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM MediaEditStoreMigration WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", MigrationMarker);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task MarkAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO MediaEditStoreMigration (Key, AppliedUtc) VALUES ($key, $applied);";
        command.Parameters.AddWithValue("$key", MigrationMarker);
        command.Parameters.AddWithValue("$applied", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<MediaEditSchemaReport> BackfillAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        var tables = await ListLegacyTablesAsync(connection, cancellationToken);
        if (tables.Count == 0)
            return MediaEditSchemaReport.None;

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var sessions = 0;
        if (tables.Contains("SceneImageEditSessions"))
        {
            sessions += await CopyAsync(connection, """
                INSERT OR IGNORE INTO MediaEditSessions
                    (Id, SubjectKind, SubjectId, SubjectScopeId, SourceImageId, SourceImageSha256, Status,
                     DescriptionText, CreatedUtc, UpdatedUtc, CompletedUtc)
                SELECT Id, 'SceneImage', InteractionId, SessionId, SourceImageId, SourceImageSha256, Status,
                       DescriptionText, CreatedUtc, UpdatedUtc, CompletedUtc
                FROM SceneImageEditSessions;
                """, cancellationToken);
        }

        if (tables.Contains("SceneAssetImageEditSessions"))
        {
            sessions += await CopyAsync(connection, """
                INSERT OR IGNORE INTO MediaEditSessions
                    (Id, SubjectKind, SubjectId, SubjectScopeId, SourceImageId, SourceImageSha256, Status,
                     DescriptionText, CreatedUtc, UpdatedUtc, CompletedUtc)
                SELECT Id, 'AssetImage', AssetId, NULL, SourceImageId, SourceImageSha256, Status,
                       DescriptionText, CreatedUtc, UpdatedUtc, CompletedUtc
                FROM SceneAssetImageEditSessions;
                """, cancellationToken);
        }

        var attempts = 0;
        foreach (var table in new[] { "SceneImageEditCompilationAttempts", "SceneAssetImageEditCompilationAttempts" })
        {
            if (!tables.Contains(table))
                continue;

            attempts += await CopyAsync(connection, $"""
                INSERT OR IGNORE INTO MediaEditCompilationAttempts
                    (Id, EditSessionId, Ordinal, RawIntent, ClarificationContextJson, SourceImageSha256, Status,
                     ResolvedModelSnapshotJson, CompilerSchemaVersion, SystemPromptVersion, RawModelResponse,
                     ParsedResultJson, Error, CreatedUtc, StartedUtc, CompletedUtc)
                SELECT Id, EditSessionId, Ordinal, RawIntent, ClarificationContextJson, SourceImageSha256, Status,
                       ResolvedModelSnapshotJson, CompilerSchemaVersion, SystemPromptVersion, RawModelResponse,
                       ParsedResultJson, Error, CreatedUtc, StartedUtc, CompletedUtc
                FROM {table};
                """, cancellationToken);
        }

        var revisions = 0;
        foreach (var table in new[] { "SceneImageEditPromptRevisions", "SceneAssetImageEditPromptRevisions" })
        {
            if (!tables.Contains(table))
                continue;

            revisions += await CopyAsync(connection, $"""
                INSERT OR IGNORE INTO MediaEditPromptRevisions
                    (Id, CompilationAttemptId, Ordinal, Prompt, RevisionKind, PromptSha256, CreatedUtc)
                SELECT Id, CompilationAttemptId, Ordinal, Prompt, RevisionKind, PromptSha256, CreatedUtc
                FROM {table};
                """, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new MediaEditSchemaReport(sessions, attempts, revisions);
    }

    private static async Task<int> CopyAsync(
        SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> ListLegacyTablesAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name FROM sqlite_master
            WHERE type = 'table' AND name IN (
                'SceneImageEditSessions', 'SceneImageEditCompilationAttempts', 'SceneImageEditPromptRevisions',
                'SceneAssetImageEditSessions', 'SceneAssetImageEditCompilationAttempts', 'SceneAssetImageEditPromptRevisions');
            """;
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tables.Add(reader.GetString(0));
        return tables;
    }
}
