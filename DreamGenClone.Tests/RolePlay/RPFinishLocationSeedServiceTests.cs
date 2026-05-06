using DreamGenClone.Application.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RPFinishLocationSeedServiceTests : IDisposable
{
    private readonly string _databasePath;

    public RPFinishLocationSeedServiceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"dreamgen-finishlocation-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_InsertsAtLeast15Entries()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishLocationSeedService(service, NullLogger<RPFinishLocationSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishLocationsAsync(includeDisabled: true);
        Assert.True(entries.Count >= 15, $"Expected ≥15 location entries but got {entries.Count}.");
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_AllFiveCategoriesPresent()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishLocationSeedService(service, NullLogger<RPFinishLocationSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishLocationsAsync(includeDisabled: true);
        var categories = entries.Select(e => e.Category).Distinct().ToHashSet();
        Assert.Contains("Internal", categories);
        Assert.Contains("External", categories);
        Assert.Contains("Facial", categories);
        Assert.Contains("OnBody", categories);
        Assert.Contains("Withdrawal", categories);
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_NoDuplicateNames()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishLocationSeedService(service, NullLogger<RPFinishLocationSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishLocationsAsync(includeDisabled: true);
        var names = entries.Select(e => e.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_AllEnabledByDefault()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishLocationSeedService(service, NullLogger<RPFinishLocationSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishLocationsAsync(includeDisabled: true);
        Assert.All(entries, e => Assert.True(e.IsEnabled));
    }

    [Fact]
    public async Task SeedDefaultsAsync_Twice_IsIdempotent()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishLocationSeedService(service, NullLogger<RPFinishLocationSeedService>.Instance);

        await seeder.SeedDefaultsAsync();
        var countAfterFirst = (await service.ListFinishLocationsAsync(includeDisabled: true)).Count;

        await seeder.SeedDefaultsAsync();
        var countAfterSecond = (await service.ListFinishLocationsAsync(includeDisabled: true)).Count;

        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    private async Task<RPThemeService> CreateServiceAsync()
    {
        var opts = Options.Create(new SqliteConfiguration { DatabasePath = _databasePath });
        var persistence = new SqlitePersistence(opts, NullLogger<SqlitePersistence>.Instance);
        await persistence.InitializeAsync();
        return new RPThemeService(opts, NullLogger<RPThemeService>.Instance);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }
}
