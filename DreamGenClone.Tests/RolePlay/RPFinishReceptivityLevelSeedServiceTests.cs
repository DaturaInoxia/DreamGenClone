using DreamGenClone.Application.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RPFinishReceptivityLevelSeedServiceTests : IDisposable
{
    private readonly string _databasePath;

    private static readonly string[] CanonicalNames =
    [
        "Begging", "Enthusiastic", "Eager", "Accepting",
        "Tolerating", "Reluctant", "CumDodging", "Enduring"
    ];

    public RPFinishReceptivityLevelSeedServiceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"dreamgen-finishreceptivity-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_InsertsExactly8Entries()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishReceptivityLevelSeedService(service, NullLogger<RPFinishReceptivityLevelSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishReceptivityLevelsAsync(includeDisabled: true);
        Assert.Equal(8, entries.Count);
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_AllCanonicalNamesPresent()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishReceptivityLevelSeedService(service, NullLogger<RPFinishReceptivityLevelSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishReceptivityLevelsAsync(includeDisabled: true);
        var names = entries.Select(e => e.Name).ToHashSet();
        foreach (var canonical in CanonicalNames)
            Assert.Contains(canonical, names);
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_SortOrdersAre0Through7()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishReceptivityLevelSeedService(service, NullLogger<RPFinishReceptivityLevelSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishReceptivityLevelsAsync(includeDisabled: true);
        var sortOrders = entries.Select(e => e.SortOrder).OrderBy(x => x).ToList();
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], sortOrders);
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_AllHaveNonEmptyPhysicalCuesAndNarrativeCue()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishReceptivityLevelSeedService(service, NullLogger<RPFinishReceptivityLevelSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishReceptivityLevelsAsync(includeDisabled: true);
        Assert.All(entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.PhysicalCues), $"PhysicalCues empty for '{e.Name}'.");
            Assert.False(string.IsNullOrWhiteSpace(e.NarrativeCue), $"NarrativeCue empty for '{e.Name}'.");
        });
    }

    [Fact]
    public async Task SeedDefaultsAsync_Twice_IsIdempotent()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishReceptivityLevelSeedService(service, NullLogger<RPFinishReceptivityLevelSeedService>.Instance);

        await seeder.SeedDefaultsAsync();
        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishReceptivityLevelsAsync(includeDisabled: true);
        Assert.Equal(8, entries.Count);
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
