using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.ModelManager;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.Processing;

public sealed class RegisteredModelStructuredOutputCapabilityTests
{
    [Fact]
    public async Task Initialize_MigratesStructuredOutputCapabilitiesOntoExistingTable()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"registered-model-capability-migration-{Guid.NewGuid():N}.db");
        var options = new PersistenceOptions { ConnectionString = $"Data Source={databasePath}" };
        try
        {
            var persistence = CreatePersistence(options);
            await persistence.InitializeAsync();
            await using (var connection = new SqliteConnection(options.ConnectionString))
            {
                await connection.OpenAsync();
                foreach (var column in new[] { "SupportsStructuredJsonSchema", "StructuredOutputMode", "MaximumContextTokens", "MaximumOutputTokens" })
                {
                    await using var dropCommand = connection.CreateCommand();
                    dropCommand.CommandText = $"ALTER TABLE RegisteredModels DROP COLUMN {column}";
                    await dropCommand.ExecuteNonQueryAsync();
                }
            }

            await persistence.InitializeAsync();

            await using var verificationConnection = new SqliteConnection(options.ConnectionString);
            await verificationConnection.OpenAsync();
            await using var verificationCommand = verificationConnection.CreateCommand();
            verificationCommand.CommandText = "SELECT name FROM pragma_table_info('RegisteredModels')";
            await using var reader = await verificationCommand.ExecuteReaderAsync();
            var columns = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(0));

            Assert.Contains("SupportsStructuredJsonSchema", columns);
            Assert.Contains("StructuredOutputMode", columns);
            Assert.Contains("MaximumContextTokens", columns);
            Assert.Contains("MaximumOutputTokens", columns);
        }
        finally
        {
            Cleanup(databasePath);
        }
    }

    [Fact]
    public async Task Save_RoundTripsStructuredOutputCapabilities()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var model = new RegisteredModel
            {
                Id = "model-structured",
                ProviderId = "provider-1",
                ModelIdentifier = "structured-model",
                DisplayName = "Structured Model",
                StructuredOutputMode = StructuredOutputMode.JsonObject,
                MaximumContextTokens = 131072,
                MaximumOutputTokens = 8192
            };

            await fixture.Repository.SaveAsync(model);
            var actual = await fixture.Repository.GetByIdAsync(model.Id);

            Assert.NotNull(actual);
            Assert.Equal(StructuredOutputMode.JsonObject, actual!.StructuredOutputMode);
            Assert.Equal(131072, actual.MaximumContextTokens);
            Assert.Equal(8192, actual.MaximumOutputTokens);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"registered-model-capability-{Guid.NewGuid():N}.db");
        var options = new PersistenceOptions { ConnectionString = $"Data Source={databasePath}" };
        var persistence = CreatePersistence(options);
        await persistence.InitializeAsync();
        await using (var connection = new SqliteConnection(options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Providers (Id, Name, ProviderType, BaseUrl, CreatedUtc, UpdatedUtc)
                VALUES ('provider-1', 'Capability Provider', 0, 'http://localhost', $utc, $utc);
                """;
            command.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
        return new TestFixture(
            new RegisteredModelRepository(Options.Create(options), NullLogger<RegisteredModelRepository>.Instance),
            databasePath);
    }

    private static SqlitePersistence CreatePersistence(PersistenceOptions options)
    {
        return new SqlitePersistence(
            Options.Create(options),
            Options.Create(new LmStudioOptions()),
            Options.Create(new StoryAnalysisOptions()),
            Options.Create(new ScenarioAdaptationOptions()),
            NullLogger<SqlitePersistence>.Instance);
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

    private sealed record TestFixture(RegisteredModelRepository Repository, string DatabasePath);
}