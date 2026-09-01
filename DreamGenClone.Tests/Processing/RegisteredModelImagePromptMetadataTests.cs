using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.ModelManager;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.Processing;

public sealed class RegisteredModelImagePromptMetadataTests
{
    [Fact]
    public async Task Save_RoundTripsExplicitImagePromptMetadata()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var model = CreateModel(
                "roundtrip-model",
                "opaque-checkpoint.safetensors",
                SceneImageModelFamily.Pony,
                SceneImagePromptDialect.PonyV6Tags);

            await fixture.Repository.SaveAsync(model);
            var actual = await fixture.Repository.GetByIdAsync(model.Id);

            Assert.NotNull(actual);
            Assert.Equal(SceneImageModelFamily.Pony, actual!.SceneImageModelFamily);
            Assert.Equal(SceneImagePromptDialect.PonyV6Tags, actual.PromptDialect);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Save_IncompatibleImagePromptMetadata_FailsFast()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var model = CreateModel(
                "mismatched-model",
                "opaque-checkpoint.safetensors",
                SceneImageModelFamily.Pony,
                SceneImagePromptDialect.SdxlNaturalLanguage);

            var exception = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Repository.SaveAsync(model));

            Assert.Contains("incompatible", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Theory]
    [InlineData("e2ea5b23-a182-4cd9-a853-6b6632b839ee", "juggernautXL_ragnarok.safetensors", SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage)]
    [InlineData("dbb08226-fe7d-4514-b247-6b208a525b7b", "ponyDiffusionV6XL_v6.safetensors", SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags)]
    [InlineData("different-id", "juggernautXL_ragnarok.safetensors", SceneImageModelFamily.Unknown, SceneImagePromptDialect.Unknown)]
    [InlineData("e2ea5b23-a182-4cd9-a853-6b6632b839ee", "different-checkpoint.safetensors", SceneImageModelFamily.Unknown, SceneImagePromptDialect.Unknown)]
    [InlineData("qwen-editor", "qwen_image_edit_2511_fp8mixed.safetensors", SceneImageModelFamily.Unknown, SceneImagePromptDialect.Unknown)]
    public async Task Initialize_MigratesOnlyExactReviewedRows(
        string modelId,
        string modelIdentifier,
        SceneImageModelFamily expectedFamily,
        SceneImagePromptDialect expectedDialect)
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await fixture.Repository.SaveAsync(CreateModel(
                modelId,
                modelIdentifier,
                SceneImageModelFamily.Unknown,
                SceneImagePromptDialect.Unknown));

            await fixture.Persistence.InitializeAsync();
            var actual = await fixture.Repository.GetByIdAsync(modelId);

            Assert.NotNull(actual);
            Assert.Equal(expectedFamily, actual!.SceneImageModelFamily);
            Assert.Equal(expectedDialect, actual.PromptDialect);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    private static RegisteredModel CreateModel(
        string id,
        string identifier,
        SceneImageModelFamily family,
        SceneImagePromptDialect dialect) => new()
    {
        Id = id,
        ProviderId = "provider-1",
        ModelIdentifier = identifier,
        DisplayName = identifier,
        ModelKind = ModelKind.Image,
        SceneImageModelFamily = family,
        PromptDialect = dialect
    };

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"registered-model-image-metadata-{Guid.NewGuid():N}.db");
        var options = new PersistenceOptions { ConnectionString = $"Data Source={databasePath}" };
        var persistence = CreatePersistence(options);
        await persistence.InitializeAsync();
        await using (var connection = new SqliteConnection(options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Providers (Id, Name, ProviderType, BaseUrl, CreatedUtc, UpdatedUtc)
                VALUES ('provider-1', 'Image Provider', 0, 'http://localhost', $utc, $utc);
                """;
            command.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        return new TestFixture(
            persistence,
            new RegisteredModelRepository(Options.Create(options), NullLogger<RegisteredModelRepository>.Instance),
            databasePath);
    }

    private static SqlitePersistence CreatePersistence(PersistenceOptions options) => new(
        Options.Create(options),
        Options.Create(new LmStudioOptions()),
        Options.Create(new StoryAnalysisOptions()),
        Options.Create(new ScenarioAdaptationOptions()),
        NullLogger<SqlitePersistence>.Instance);

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
        SqlitePersistence Persistence,
        RegisteredModelRepository Repository,
        string DatabasePath);
}