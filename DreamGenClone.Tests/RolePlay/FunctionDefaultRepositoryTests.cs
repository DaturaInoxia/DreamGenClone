using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.ModelManager;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class FunctionDefaultRepositoryTests
{
    private static async Task<(SqlitePersistence persistence, PersistenceOptions options)> CreateTempDbAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dreamgenclone-funcdefault-{Guid.NewGuid():N}.db");
        var options = new PersistenceOptions { ConnectionString = $"Data Source={dbPath}" };
        var persistence = new SqlitePersistence(
            Options.Create(options),
            Options.Create(new LmStudioOptions()),
            Options.Create(new StoryAnalysisOptions()),
            Options.Create(new ScenarioAdaptationOptions()),
            NullLogger<SqlitePersistence>.Instance);
        await persistence.InitializeAsync();
        return (persistence, options);
    }

    [Fact]
    public async Task SaveAndLoad_MaxConcurrentJobs_RoundTrip()
    {
        var (_, options) = await CreateTempDbAsync();
        var repo = new FunctionDefaultRepository(Options.Create(options), NullLogger<FunctionDefaultRepository>.Instance);

        var fd = new FunctionModelDefault
        {
            Id = Guid.NewGuid().ToString(),
            FunctionName = AppFunction.RolePlaySemanticAnalysis.ToString(),
            ModelId = "model-abc",
            Temperature = 0.8,
            TopP = 0.95,
            MaxTokens = 1000,
            MaxConcurrentJobs = 4
        };

        await repo.SaveAsync(fd);

        var loaded = await repo.GetByFunctionAsync(AppFunction.RolePlaySemanticAnalysis);

        Assert.NotNull(loaded);
        Assert.Equal(4, loaded.MaxConcurrentJobs);
        Assert.Equal("model-abc", loaded.ModelId);
    }

    [Fact]
    public async Task SaveAndLoad_MaxConcurrentJobs_NullPreserved()
    {
        var (_, options) = await CreateTempDbAsync();
        var repo = new FunctionDefaultRepository(Options.Create(options), NullLogger<FunctionDefaultRepository>.Instance);

        var fd = new FunctionModelDefault
        {
            Id = Guid.NewGuid().ToString(),
            FunctionName = AppFunction.RolePlaySemanticAnalysis.ToString(),
            ModelId = "model-xyz",
            MaxConcurrentJobs = null
        };

        await repo.SaveAsync(fd);

        var loaded = await repo.GetByFunctionAsync(AppFunction.RolePlaySemanticAnalysis);

        Assert.NotNull(loaded);
        Assert.Null(loaded.MaxConcurrentJobs);
    }

    [Fact]
    public async Task GetAllAsync_IncludesMaxConcurrentJobs()
    {
        var (_, options) = await CreateTempDbAsync();
        var repo = new FunctionDefaultRepository(Options.Create(options), NullLogger<FunctionDefaultRepository>.Instance);

        await repo.SaveAsync(new FunctionModelDefault
        {
            Id = Guid.NewGuid().ToString(),
            FunctionName = AppFunction.RolePlaySemanticAnalysis.ToString(),
            ModelId = "m1",
            MaxConcurrentJobs = 6
        });

        var all = await repo.GetAllAsync();
        var row = all.First(x => x.FunctionName == AppFunction.RolePlaySemanticAnalysis.ToString());

        Assert.Equal(6, row.MaxConcurrentJobs);
    }
}
