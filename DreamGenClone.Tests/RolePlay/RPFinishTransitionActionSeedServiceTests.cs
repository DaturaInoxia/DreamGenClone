using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RPFinishTransitionActionSeedServiceTests : IDisposable
{
    private readonly string _databasePath;

    public RPFinishTransitionActionSeedServiceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"dreamgen-finishtransition-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_InsertsAtLeast6Entries()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishTransitionActionSeedService(service, NullLogger<RPFinishTransitionActionSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishTransitionActionsAsync(includeDisabled: true);
        Assert.True(entries.Count >= 6, $"Expected ≥6 transition action entries but got {entries.Count}.");
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_AllHaveNonEmptyTransitionText()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishTransitionActionSeedService(service, NullLogger<RPFinishTransitionActionSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishTransitionActionsAsync(includeDisabled: true);
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.TransitionText), $"TransitionText empty for '{e.Name}'."));
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_AllEnabledByDefault()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishTransitionActionSeedService(service, NullLogger<RPFinishTransitionActionSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishTransitionActionsAsync(includeDisabled: true);
        Assert.All(entries, e => Assert.True(e.IsEnabled));
    }

    [Fact]
    public async Task SeedDefaultsAsync_Twice_IsIdempotent()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishTransitionActionSeedService(service, NullLogger<RPFinishTransitionActionSeedService>.Instance);

        await seeder.SeedDefaultsAsync();
        var countAfterFirst = (await service.ListFinishTransitionActionsAsync(includeDisabled: true)).Count;

        await seeder.SeedDefaultsAsync();
        var countAfterSecond = (await service.ListFinishTransitionActionsAsync(includeDisabled: true)).Count;

        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    private async Task<RPThemeService> CreateServiceAsync()
    {
        var opts = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={_databasePath}" });
        var persistence = new SqlitePersistence(opts, Options.Create(new LmStudioOptions()), Options.Create(new StoryAnalysisOptions()), Options.Create(new ScenarioAdaptationOptions()), NullLogger<SqlitePersistence>.Instance);
        await persistence.InitializeAsync();
        return new RPThemeService(opts, NullLogger<RPThemeService>.Instance);
    }

    public void Dispose()
    {
        if (!File.Exists(_databasePath))
        {
            return;
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // Provider cleanup can hold a transient handle after test completion.
        }
    }
}
