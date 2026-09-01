using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class SceneBeatDiagnosticsRepository : ISceneBeatDiagnosticsRepository
{
    private static readonly SceneBeatPipelineStage[] Stages =
    [
        SceneBeatPipelineStage.Catalogue,
        SceneBeatPipelineStage.BeatProduction,
        SceneBeatPipelineStage.MomentDiscovery,
        SceneBeatPipelineStage.MomentEnrichment
    ];

    private readonly string _connectionString;

    public SceneBeatDiagnosticsRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task<SceneBeatStageMetrics> GetMetricsAsync(
        SceneBeatPipelineStage stage,
        CancellationToken cancellationToken = default)
    {
        var table = GetTable(stage);
        await using var connection = await OpenAsync(cancellationToken);
        if (!await TableExistsAsync(connection, table.AttemptTable, cancellationToken))
            return EmptyMetrics(stage);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*),
                   SUM(CASE WHEN Status = 'Queued' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN Status = 'Processing' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN Status = 'Complete' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN Status = 'Superseded' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN Status = 'Cancelled' THEN 1 ELSE 0 END),
                   AVG(DurationMs), MAX(DurationMs),
                   COALESCE(SUM(InputCharacters), 0), COALESCE(SUM(OutputCharacters), 0),
                   MIN(CreatedUtc), MAX(CreatedUtc),
                   SUM(CASE WHEN RawModelResponse IS NOT NULL THEN 1 ELSE 0 END),
                   SUM(CASE WHEN ReasoningContent IS NOT NULL THEN 1 ELSE 0 END)
            FROM {table.AttemptTable};
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var attemptCount = reader.GetInt32(0);
        if (attemptCount == 0) return EmptyMetrics(stage);

        return new SceneBeatStageMetrics(
            stage,
            attemptCount,
            new SceneBeatStageStatusCounts(
                reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6)),
            reader.IsDBNull(7) ? null : reader.GetDouble(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.GetInt64(9),
            reader.GetInt64(10),
            ParseNullableUtc(reader, 11),
            ParseNullableUtc(reader, 12),
            reader.GetInt32(13),
            reader.GetInt32(14));
    }

    public async Task<IReadOnlyList<SceneBeatDiagnosticAttemptSummary>> GetRecentDiagnosticsAsync(
        SceneBeatPipelineStage stage,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit), "Diagnostic limit must be positive.");
        var table = GetTable(stage);
        await using var connection = await OpenAsync(cancellationToken);
        if (!await TableExistsAsync(connection, table.AttemptTable, cancellationToken)
            || !await TableExistsAsync(connection, table.OwnerTable, cancellationToken))
            return [];

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT a.OwnerRecordId, a.Id, a.JobId, a.AttemptNumber, a.Status,
                   o.ModelIdentifier, o.ProviderName, a.FinishReason, a.ValidationCode,
                   a.DurationMs, a.InputCharacters, a.OutputCharacters, a.CreatedUtc,
                   a.StartedUtc, a.CompletedUtc, a.UpdatedUtc,
                   CASE WHEN a.RawModelResponse IS NULL THEN 0 ELSE 1 END,
                   CASE WHEN a.ReasoningContent IS NULL THEN 0 ELSE 1 END
            FROM {table.AttemptTable} a
            JOIN {table.OwnerTable} o ON o.Id = a.OwnerRecordId
            ORDER BY a.CreatedUtc DESC, a.Id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var summaries = new List<SceneBeatDiagnosticAttemptSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            summaries.Add(new SceneBeatDiagnosticAttemptSummary(
                stage,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                Enum.Parse<SceneBeatAnalysisAttemptStatus>(reader.GetString(4)),
                GetNullableString(reader, 5),
                GetNullableString(reader, 6),
                GetNullableString(reader, 7),
                GetNullableString(reader, 8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9),
                reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                ParseUtc(reader.GetString(12)),
                ParseNullableUtc(reader, 13),
                ParseNullableUtc(reader, 14),
                ParseUtc(reader.GetString(15)),
                reader.GetInt32(16) == 1,
                reader.GetInt32(17) == 1));
        }
        return summaries;
    }

    public async Task<SceneBeatDiagnosticsPruneRun> PruneRawDiagnosticsAsync(
        string functionDefaultId,
        int retentionDays,
        DateTime cutoffUtc,
        DateTime prunedUtc,
        string actor,
        CancellationToken cancellationToken = default)
    {
        Require(functionDefaultId, nameof(functionDefaultId));
        Require(actor, nameof(actor));
        if (retentionDays < 1) throw new ArgumentOutOfRangeException(nameof(retentionDays));
        RequireUtc(cutoffUtc, nameof(cutoffUtc));
        RequireUtc(prunedUtc, nameof(prunedUtc));

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var counts = new Dictionary<SceneBeatPipelineStage, int>();
        foreach (var stage in Stages)
        {
            var table = GetTable(stage);
            counts[stage] = await TableExistsAsync(connection, table.AttemptTable, cancellationToken, transaction)
                ? await PruneTableAsync(connection, transaction, table.AttemptTable, cutoffUtc, cancellationToken)
                : 0;
        }

        var run = new SceneBeatDiagnosticsPruneRun(
            Guid.NewGuid().ToString(), functionDefaultId.Trim(), retentionDays, cutoffUtc, prunedUtc, actor.Trim(),
            counts[SceneBeatPipelineStage.Catalogue],
            counts[SceneBeatPipelineStage.BeatProduction],
            counts[SceneBeatPipelineStage.MomentDiscovery],
            counts[SceneBeatPipelineStage.MomentEnrichment]);
        await InsertPruneRunAsync(connection, transaction, run, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return run;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SceneBeatDiagnosticsPruneRuns (
                Id TEXT PRIMARY KEY, FunctionDefaultId TEXT NOT NULL, RetentionDays INTEGER NOT NULL,
                CutoffUtc TEXT NOT NULL, PrunedUtc TEXT NOT NULL, Actor TEXT NOT NULL,
                CataloguePrunedCount INTEGER NOT NULL, BeatProductionPrunedCount INTEGER NOT NULL,
                MomentDiscoveryPrunedCount INTEGER NOT NULL, MomentEnrichmentPrunedCount INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_SceneBeatDiagnosticsPruneRuns_PrunedUtc
                ON SceneBeatDiagnosticsPruneRuns (PrunedUtc DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task<int> PruneTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string attemptTable,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {attemptTable}
            SET RawModelResponse = NULL, ReasoningContent = NULL
            WHERE Status IN ('Complete', 'Failed', 'Superseded', 'Cancelled')
              AND COALESCE(CompletedUtc, UpdatedUtc) < $cutoffUtc
              AND (RawModelResponse IS NOT NULL OR ReasoningContent IS NOT NULL);
            """;
        command.Parameters.AddWithValue("$cutoffUtc", FormatUtc(cutoffUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPruneRunAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SceneBeatDiagnosticsPruneRun run,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SceneBeatDiagnosticsPruneRuns (
                Id, FunctionDefaultId, RetentionDays, CutoffUtc, PrunedUtc, Actor,
                CataloguePrunedCount, BeatProductionPrunedCount,
                MomentDiscoveryPrunedCount, MomentEnrichmentPrunedCount)
            VALUES ($id, $functionDefaultId, $retentionDays, $cutoffUtc, $prunedUtc, $actor,
                $catalogue, $production, $discovery, $enrichment);
            """;
        command.Parameters.AddWithValue("$id", run.Id);
        command.Parameters.AddWithValue("$functionDefaultId", run.FunctionDefaultId);
        command.Parameters.AddWithValue("$retentionDays", run.RetentionDays);
        command.Parameters.AddWithValue("$cutoffUtc", FormatUtc(run.CutoffUtc));
        command.Parameters.AddWithValue("$prunedUtc", FormatUtc(run.PrunedUtc));
        command.Parameters.AddWithValue("$actor", run.Actor);
        command.Parameters.AddWithValue("$catalogue", run.CataloguePrunedCount);
        command.Parameters.AddWithValue("$production", run.BeatProductionPrunedCount);
        command.Parameters.AddWithValue("$discovery", run.MomentDiscoveryPrunedCount);
        command.Parameters.AddWithValue("$enrichment", run.MomentEnrichmentPrunedCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static StageTable GetTable(SceneBeatPipelineStage stage) => stage switch
    {
        SceneBeatPipelineStage.Catalogue => new("SceneBeatAnalysisAttempts", "SceneBeatCatalogues"),
        SceneBeatPipelineStage.BeatProduction => new("SceneBeatProductionAttempts", "SceneBeatProductionPlans"),
        SceneBeatPipelineStage.MomentDiscovery => new("SceneMomentDiscoveryAttempts", "SceneMomentSets"),
        SceneBeatPipelineStage.MomentEnrichment => new("SceneMomentEnrichmentAttempts", "SceneMomentEnrichments"),
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown scene beat pipeline stage.")
    };

    private static SceneBeatStageMetrics EmptyMetrics(SceneBeatPipelineStage stage)
        => new(stage, 0, new(0, 0, 0, 0, 0, 0), null, null, 0, 0, null, null, 0, 0);

    private static string? GetNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTime? ParseNullableUtc(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : ParseUtc(reader.GetString(ordinal));

    private static DateTime ParseUtc(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string FormatUtc(DateTime value)
        => value.ToString("O", CultureInfo.InvariantCulture);

    private static void RequireUtc(DateTime value, string name)
    {
        if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.", name);
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", name);
    }

    private sealed record StageTable(string AttemptTable, string OwnerTable);
}