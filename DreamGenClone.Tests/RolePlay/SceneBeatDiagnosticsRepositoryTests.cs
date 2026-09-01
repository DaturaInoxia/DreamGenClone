using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatDiagnosticsRepositoryTests
{
    private static readonly (SceneBeatPipelineStage Stage, string OwnerTable, string AttemptTable)[] StageTables =
    [
        (SceneBeatPipelineStage.Catalogue, "SceneBeatCatalogues", "SceneBeatAnalysisAttempts"),
        (SceneBeatPipelineStage.BeatProduction, "SceneBeatProductionPlans", "SceneBeatProductionAttempts"),
        (SceneBeatPipelineStage.MomentDiscovery, "SceneMomentSets", "SceneMomentDiscoveryAttempts"),
        (SceneBeatPipelineStage.MomentEnrichment, "SceneMomentEnrichments", "SceneMomentEnrichmentAttempts")
    ];

    [Fact]
    public async Task CatalogueMetricsAndRecentDiagnostics_ExposeCountsAndProvenanceWithoutRawContent()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await SeedStageAsync(fixture.ConnectionString, "SceneBeatCatalogues", "SceneBeatAnalysisAttempts");

            var metrics = await fixture.Repository.GetMetricsAsync(SceneBeatPipelineStage.Catalogue);
            var recent = Assert.Single(await fixture.Repository.GetRecentDiagnosticsAsync(
                SceneBeatPipelineStage.Catalogue, 1));

            Assert.Equal(2, metrics.AttemptCount);
            Assert.Equal(1, metrics.StatusCounts.Complete);
            Assert.Equal(1, metrics.StatusCounts.Failed);
            Assert.Equal(150, metrics.AverageDurationMs);
            Assert.Equal(200, metrics.MaximumDurationMs);
            Assert.Equal(30, metrics.TotalInputCharacters);
            Assert.Equal(50, metrics.TotalOutputCharacters);
            Assert.Equal(2, metrics.RawResponseRetainedCount);
            Assert.Equal(1, metrics.ReasoningRetainedCount);
            Assert.Equal("owner-2", recent.OwnerRecordId);
            Assert.Equal("attempt-2", recent.AttemptId);
            Assert.Equal("job-2", recent.JobId);
            Assert.Equal("model-2", recent.ModelIdentifier);
            Assert.Equal("provider-2", recent.ProviderName);
            Assert.Equal("length", recent.FinishReason);
            Assert.Equal("schema", recent.ValidationCode);
            Assert.Equal(200, recent.DurationMs);
            Assert.Equal(20, recent.InputCharacters);
            Assert.Equal(30, recent.OutputCharacters);
            Assert.Equal(new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc), recent.CreatedUtc);
            Assert.Equal(new DateTime(2026, 8, 2, 0, 0, 1, DateTimeKind.Utc), recent.StartedUtc);
            Assert.Equal(new DateTime(2026, 8, 2, 0, 0, 3, DateTimeKind.Utc), recent.CompletedUtc);
            Assert.Equal(new DateTime(2026, 8, 2, 0, 0, 3, DateTimeKind.Utc), recent.UpdatedUtc);
            Assert.True(recent.RawResponseRetained);
            Assert.False(recent.ReasoningRetained);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task AbsentCanonicalStageTables_AreValidZeroAttempts()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            foreach (var table in StageTables)
            {
                var metrics = await fixture.Repository.GetMetricsAsync(table.Stage);
                Assert.Equal(0, metrics.AttemptCount);
                Assert.Null(metrics.OldestAttemptUtc);
                Assert.Null(metrics.NewestAttemptUtc);
                Assert.Empty(await fixture.Repository.GetRecentDiagnosticsAsync(table.Stage, 5));
            }
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task MetricsAndRecentDiagnostics_MapEachStageToItsCanonicalAttemptTable()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            foreach (var table in StageTables)
                await SeedStageAsync(fixture.ConnectionString, table.OwnerTable, table.AttemptTable);

            foreach (var table in StageTables)
            {
                var metrics = await fixture.Repository.GetMetricsAsync(table.Stage);
                var recent = Assert.Single(await fixture.Repository.GetRecentDiagnosticsAsync(table.Stage, 1));
                Assert.Equal(table.Stage, metrics.Stage);
                Assert.Equal(2, metrics.AttemptCount);
                Assert.Equal(1, metrics.StatusCounts.Complete);
                Assert.Equal(1, metrics.StatusCounts.Failed);
                Assert.Equal(150, metrics.AverageDurationMs);
                Assert.Equal(200, metrics.MaximumDurationMs);
                Assert.Equal(30, metrics.TotalInputCharacters);
                Assert.Equal(50, metrics.TotalOutputCharacters);
                Assert.Equal(table.Stage, recent.Stage);
                Assert.Equal("attempt-2", recent.AttemptId);
                Assert.Equal(2, recent.AttemptNumber);
                Assert.Equal(SceneBeatAnalysisAttemptStatus.Failed, recent.Status);
            }
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Prune_ClearsOnlyExpiredTerminalRawDiagnosticsAcrossAllStagesAndAuditsEveryRun()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            foreach (var table in StageTables)
            {
                await SeedStageAsync(fixture.ConnectionString, table.OwnerTable, table.AttemptTable);
                await PreparePruneCasesAsync(fixture.ConnectionString, table.AttemptTable);
            }
            await SeedLegacyAnalysisAsync(fixture.ConnectionString);

            var cutoffUtc = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
            var prunedUtc = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
            var run = await fixture.Repository.PruneRawDiagnosticsAsync(
                "function-default-9", 16, cutoffUtc, prunedUtc, "operator@example");

            Assert.Equal(1, run.CataloguePrunedCount);
            Assert.Equal(1, run.BeatProductionPrunedCount);
            Assert.Equal(1, run.MomentDiscoveryPrunedCount);
            Assert.Equal(1, run.MomentEnrichmentPrunedCount);
            Assert.Equal(4, run.TotalPrunedCount);
            foreach (var table in StageTables)
                await AssertPruneCasesAsync(fixture.ConnectionString, table.AttemptTable);
            await AssertLegacyUnchangedAsync(fixture.ConnectionString);
            await AssertAuditAsync(fixture.ConnectionString, expectedRuns: 1, expectedLatestTotal: 4);

            var zeroRun = await fixture.Repository.PruneRawDiagnosticsAsync(
                "function-default-9", 16, cutoffUtc, prunedUtc.AddMinutes(1), "operator@example");

            Assert.Equal(0, zeroRun.TotalPrunedCount);
            await AssertAuditAsync(fixture.ConnectionString, expectedRuns: 2, expectedLatestTotal: 0);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scene-beat-diagnostics-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        var repository = new SceneBeatDiagnosticsRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = connectionString
        }));
        _ = await repository.GetMetricsAsync(SceneBeatPipelineStage.Catalogue);
        return new(repository, connectionString, databasePath);
    }

    private static async Task SeedStageAsync(string connectionString, string ownerTable, string attemptTable)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $$"""
            CREATE TABLE {{ownerTable}} (Id TEXT PRIMARY KEY, ModelIdentifier TEXT NULL, ProviderName TEXT NULL);
            CREATE TABLE {{attemptTable}} (
                Id TEXT PRIMARY KEY, OwnerRecordId TEXT NOT NULL, AttemptNumber INTEGER NOT NULL,
                JobId TEXT NOT NULL, Status TEXT NOT NULL, SystemPrompt TEXT NOT NULL, UserPrompt TEXT NOT NULL,
                RawModelResponse TEXT NULL, ReasoningContent TEXT NULL, FinishReason TEXT NULL,
                ValidationCode TEXT NULL, ValidationDetailsJson TEXT NOT NULL, DurationMs INTEGER NULL,
                InputCharacters INTEGER NOT NULL, OutputCharacters INTEGER NULL, CreatedUtc TEXT NOT NULL,
                StartedUtc TEXT NULL, CompletedUtc TEXT NULL, UpdatedUtc TEXT NOT NULL);
              INSERT INTO {{ownerTable}} VALUES ('owner-1', 'model-1', 'provider-1'), ('owner-2', 'model-2', 'provider-2');
              INSERT INTO {{attemptTable}} VALUES
                ('attempt-1', 'owner-1', 1, 'job-1', 'Complete', 'system-1', 'user-1', 'raw-1', 'reasoning-1',
                  'stop', NULL, '{}', 100, 10, 20, '2026-08-01T00:00:00.0000000Z',
                 '2026-08-01T00:00:01.0000000Z', '2026-08-01T00:00:02.0000000Z', '2026-08-01T00:00:02.0000000Z'),
                ('attempt-2', 'owner-2', 2, 'job-2', 'Failed', 'system-2', 'user-2', 'raw-2', NULL,
                  'length', 'schema', '{"path":"$.beats"}', 200, 20, 30, '2026-08-02T00:00:00.0000000Z',
                 '2026-08-02T00:00:01.0000000Z', '2026-08-02T00:00:03.0000000Z', '2026-08-02T00:00:03.0000000Z');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PreparePruneCasesAsync(string connectionString, string attemptTable)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $$"""
            UPDATE {{attemptTable}}
            SET CreatedUtc = '2026-08-20T00:00:00.0000000Z', StartedUtc = '2026-08-20T00:00:01.0000000Z',
                CompletedUtc = '2026-08-20T00:00:03.0000000Z', UpdatedUtc = '2026-08-20T00:00:03.0000000Z'
            WHERE Id = 'attempt-2';
              INSERT INTO {{attemptTable}} VALUES
                ('attempt-3', 'owner-1', 3, 'job-3', 'Processing', 'system-3', 'user-3', 'raw-3', 'reasoning-3',
                  NULL, NULL, '{}', NULL, 40, NULL, '2026-07-01T00:00:00.0000000Z',
                 '2026-07-01T00:00:01.0000000Z', NULL, '2026-07-01T00:00:01.0000000Z');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertPruneCasesAsync(string connectionString, string attemptTable)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id, SystemPrompt, UserPrompt, RawModelResponse, ReasoningContent, FinishReason,
                   ValidationCode, ValidationDetailsJson, DurationMs, InputCharacters, OutputCharacters,
                   JobId, OwnerRecordId, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc
            FROM {attemptTable} ORDER BY Id;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("attempt-1", reader.GetString(0));
        Assert.Equal("system-1", reader.GetString(1));
        Assert.Equal("user-1", reader.GetString(2));
        Assert.True(reader.IsDBNull(3));
        Assert.True(reader.IsDBNull(4));
        Assert.Equal("stop", reader.GetString(5));
        Assert.True(reader.IsDBNull(6));
        Assert.Equal("{}", reader.GetString(7));
        Assert.Equal(100, reader.GetInt64(8));
        Assert.Equal(10, reader.GetInt32(9));
        Assert.Equal(20, reader.GetInt32(10));
        Assert.Equal("job-1", reader.GetString(11));
        Assert.Equal("owner-1", reader.GetString(12));
        Assert.Equal("2026-08-01T00:00:00.0000000Z", reader.GetString(13));
        Assert.Equal("2026-08-01T00:00:01.0000000Z", reader.GetString(14));
        Assert.Equal("2026-08-01T00:00:02.0000000Z", reader.GetString(15));
        Assert.Equal("2026-08-01T00:00:02.0000000Z", reader.GetString(16));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("attempt-2", reader.GetString(0));
        Assert.Equal("raw-2", reader.GetString(3));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("attempt-3", reader.GetString(0));
        Assert.Equal("raw-3", reader.GetString(3));
        Assert.Equal("reasoning-3", reader.GetString(4));
    }

    private static async Task SeedLegacyAnalysisAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE SceneImageBeatAnalyses (Id TEXT PRIMARY KEY, RawModelResponse TEXT, ReasoningContent TEXT);
            INSERT INTO SceneImageBeatAnalyses VALUES ('legacy-1', 'legacy-raw', 'legacy-reasoning');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertLegacyUnchangedAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RawModelResponse, ReasoningContent FROM SceneImageBeatAnalyses WHERE Id = 'legacy-1';";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("legacy-raw", reader.GetString(0));
        Assert.Equal("legacy-reasoning", reader.GetString(1));
    }

    private static async Task AssertAuditAsync(string connectionString, int expectedRuns, int expectedLatestTotal)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FunctionDefaultId, RetentionDays, CutoffUtc, Actor,
                   CataloguePrunedCount + BeatProductionPrunedCount
                     + MomentDiscoveryPrunedCount + MomentEnrichmentPrunedCount,
                   (SELECT COUNT(*) FROM SceneBeatDiagnosticsPruneRuns)
            FROM SceneBeatDiagnosticsPruneRuns ORDER BY PrunedUtc DESC LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("function-default-9", reader.GetString(0));
        Assert.Equal(16, reader.GetInt32(1));
        Assert.Equal("2026-08-15T00:00:00.0000000Z", reader.GetString(2));
        Assert.Equal("operator@example", reader.GetString(3));
        Assert.Equal(expectedLatestTotal, reader.GetInt32(4));
        Assert.Equal(expectedRuns, reader.GetInt32(5));
    }

    private static void Cleanup(string databasePath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                if (File.Exists(databasePath + suffix)) File.Delete(databasePath + suffix);
            }
            catch
            {
            }
        }
    }

    private sealed record TestFixture(
        SceneBeatDiagnosticsRepository Repository,
        string ConnectionString,
        string DatabasePath);
}