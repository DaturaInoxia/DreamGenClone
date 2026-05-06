using DreamGenClone.Application.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RPFinishHisControlLevelSeedServiceTests : IDisposable
{
    private readonly string _databasePath;

    public RPFinishHisControlLevelSeedServiceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"dreamgen-finishhiscontrol-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_InsertsExactly3Entries()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishHisControlLevelSeedService(service, NullLogger<RPFinishHisControlLevelSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishHisControlLevelsAsync(includeDisabled: true);
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_NamesAreAsksLeadsCommands()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishHisControlLevelSeedService(service, NullLogger<RPFinishHisControlLevelSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishHisControlLevelsAsync(includeDisabled: true);
        var names = entries.Select(e => e.Name).ToHashSet();
        Assert.Contains("Asks", names);
        Assert.Contains("Leads", names);
        Assert.Contains("Commands", names);
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_BandsMatchDominanceTiers()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishHisControlLevelSeedService(service, NullLogger<RPFinishHisControlLevelSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishHisControlLevelsAsync(includeDisabled: true);
        var asks = Assert.Single(entries, e => e.Name == "Asks");
        var leads = Assert.Single(entries, e => e.Name == "Leads");
        var commands = Assert.Single(entries, e => e.Name == "Commands");
        Assert.Equal("0-29", asks.EligibleOtherManDominanceBands);
        Assert.Equal("30-59", leads.EligibleOtherManDominanceBands);
        Assert.Equal("60-100", commands.EligibleOtherManDominanceBands);
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_AllHaveNonEmptyExampleDialogue()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishHisControlLevelSeedService(service, NullLogger<RPFinishHisControlLevelSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishHisControlLevelsAsync(includeDisabled: true);
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.ExampleDialogue), $"ExampleDialogue empty for '{e.Name}'."));
    }

    [Fact]
    public async Task SeedDefaultsAsync_Twice_IsIdempotent()
    {
        var service = await CreateServiceAsync();
        var seeder = new RPFinishHisControlLevelSeedService(service, NullLogger<RPFinishHisControlLevelSeedService>.Instance);

        await seeder.SeedDefaultsAsync();
        await seeder.SeedDefaultsAsync();

        var entries = await service.ListFinishHisControlLevelsAsync(includeDisabled: true);
        Assert.Equal(3, entries.Count);
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
