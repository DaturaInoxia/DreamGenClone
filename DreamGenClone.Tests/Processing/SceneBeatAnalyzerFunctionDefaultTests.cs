using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.ModelManager;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.Processing;

public sealed class SceneBeatAnalyzerFunctionDefaultTests
{
    [Fact]
    public async Task Save_RoundTripsCompleteAnalyzerExecutionPolicy()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var expected = CreateValidDefault();
            await fixture.Repository.SaveAsync(expected);

            var actual = await fixture.Repository.GetByFunctionAsync(AppFunction.RolePlaySceneBeatAnalyzer);

            Assert.NotNull(actual);
            Assert.Equal(3, actual!.MaxConcurrentJobs);
            Assert.Equal(120, actual.DurableJobLeaseSeconds);
            Assert.Equal(250, actual.DurableJobPollIntervalMilliseconds);
            Assert.Equal(2, actual.TransientRetryCount);
            Assert.Equal("[5,30]", actual.TransientRetryDelaysSecondsJson);
            Assert.Equal(30, actual.DiagnosticsRetentionDays);
            Assert.Equal(8, actual.MaximumCatalogueEntries);
            Assert.Null(actual.ValidateSceneBeatAnalyzerConfiguration());
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Save_RejectsMissingAnalyzerExecutionPolicy()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var incomplete = CreateValidDefault();
            incomplete.DurableJobLeaseSeconds = null;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.Repository.SaveAsync(incomplete));

            Assert.Contains("Lease Seconds", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Save_RejectsMissingMaximumCatalogueEntries()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var incomplete = CreateValidDefault();
            incomplete.MaximumCatalogueEntries = null;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.Repository.SaveAsync(incomplete));

            Assert.Contains("Maximum Catalogue Entries", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public void Validation_RejectsRetryScheduleThatDoesNotMatchRetryCount()
    {
        var functionDefault = CreateValidDefault();
        functionDefault.TransientRetryDelaysSecondsJson = "[5]";

        Assert.Contains("one positive whole-second value per retry", functionDefault.ValidateSceneBeatAnalyzerConfiguration());
    }

    [Fact]
    public async Task Initialize_MigratesAllAnalyzerPolicyColumnsIntoExistingFunctionDefaultsTable()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scene-beat-analyzer-migration-{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await using var create = connection.CreateCommand();
                create.CommandText = """
                    CREATE TABLE FunctionModelDefaults (
                        Id TEXT PRIMARY KEY NOT NULL, FunctionName TEXT NOT NULL UNIQUE,
                        ModelId TEXT NOT NULL, Temperature REAL NOT NULL, TopP REAL NOT NULL,
                        MaxTokens INTEGER NOT NULL, ThinkingMode INTEGER NOT NULL DEFAULT 0,
                        MaxConcurrentJobs INTEGER NULL, UpdatedUtc TEXT NOT NULL);
                    """;
                await create.ExecuteNonQueryAsync();
            }

            await CreatePersistence(databasePath).InitializeAsync();

            await using var verifyConnection = new SqliteConnection($"Data Source={databasePath}");
            await verifyConnection.OpenAsync();
            await using var verify = verifyConnection.CreateCommand();
            verify.CommandText = "SELECT name FROM pragma_table_info('FunctionModelDefaults')";
            await using var reader = await verify.ExecuteReaderAsync();
            var columns = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
            Assert.Contains("DurableJobLeaseSeconds", columns);
            Assert.Contains("DurableJobPollIntervalMilliseconds", columns);
            Assert.Contains("TransientRetryCount", columns);
            Assert.Contains("TransientRetryDelaysSecondsJson", columns);
            Assert.Contains("DiagnosticsRetentionDays", columns);
            Assert.Contains("MaximumCatalogueEntries", columns);
        }
        finally
        {
            Cleanup(databasePath);
        }
    }

    private static FunctionModelDefault CreateValidDefault() => new()
    {
        Id = "scene-beat-analyzer-default",
        FunctionName = AppFunction.RolePlaySceneBeatAnalyzer.ToString(),
        ModelId = "model-1",
        Temperature = 0.2,
        TopP = 0.9,
        MaxTokens = 4000,
        ThinkingMode = ThinkingMode.Disabled,
        MaxConcurrentJobs = 3,
        DurableJobLeaseSeconds = 120,
        DurableJobPollIntervalMilliseconds = 250,
        TransientRetryCount = 2,
        TransientRetryDelaysSecondsJson = "[5,30]",
        DiagnosticsRetentionDays = 30,
        MaximumCatalogueEntries = 8
    };

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scene-beat-analyzer-default-{Guid.NewGuid():N}.db");
        var options = new PersistenceOptions { ConnectionString = $"Data Source={databasePath}" };
        var persistence = CreatePersistence(databasePath);
        await persistence.InitializeAsync();
        await SeedModelAsync(options.ConnectionString);
        return new TestFixture(
            new FunctionDefaultRepository(Options.Create(options), NullLogger<FunctionDefaultRepository>.Instance),
            databasePath);
    }

    private static SqlitePersistence CreatePersistence(string databasePath)
    {
        var options = new PersistenceOptions { ConnectionString = $"Data Source={databasePath}" };
        return new SqlitePersistence(
            Options.Create(options),
            Options.Create(new LmStudioOptions()),
            Options.Create(new StoryAnalysisOptions()),
            Options.Create(new ScenarioAdaptationOptions()),
            NullLogger<SqlitePersistence>.Instance);
    }

    private static async Task SeedModelAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Providers (Id, Name, ProviderType, BaseUrl, CreatedUtc, UpdatedUtc)
            VALUES ('provider-1', 'Test Provider', 0, 'http://localhost', $utc, $utc);
            INSERT INTO RegisteredModels (Id, ProviderId, ModelIdentifier, DisplayName, CreatedUtc)
            VALUES ('model-1', 'provider-1', 'model-1', 'Test Model', $utc);
            """;
        command.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
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

    private sealed record TestFixture(FunctionDefaultRepository Repository, string DatabasePath);
}