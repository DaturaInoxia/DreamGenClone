using DreamGenClone.Application.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SteerPositionMatrixSeedServiceTests : IDisposable
{
    private readonly string _databasePath;

    public SteerPositionMatrixSeedServiceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"dreamgen-steer-seed-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_InsertsExampleMatrixRows()
    {
        var rpThemeService = await CreateServiceAsync();
        var seeder = new SteerPositionMatrixSeedService(rpThemeService, NullLogger<SteerPositionMatrixSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var rows = await rpThemeService.ListSteerPositionMatrixRowsAsync();
        Assert.Equal(54, rows.Count);

        Assert.Contains(rows, r => r.DesireBand == "60-100" && r.SelfRespectBand == "60-100" && r.WifeDominanceBand == "Low" && r.OtherManDominanceBand == "Any");
        Assert.Contains(rows, r => r.DesireBand == "0-29" && r.SelfRespectBand == "0-29" && r.WifeDominanceBand == "Any" && r.OtherManDominanceBand == "High");
    }

    [Fact]
    public async Task SeedDefaultsAsync_Twice_IsIdempotent()
    {
        var rpThemeService = await CreateServiceAsync();
        var seeder = new SteerPositionMatrixSeedService(rpThemeService, NullLogger<SteerPositionMatrixSeedService>.Instance);

        await seeder.SeedDefaultsAsync();
        await seeder.SeedDefaultsAsync();

        var rows = await rpThemeService.ListSteerPositionMatrixRowsAsync();
        Assert.Equal(54, rows.Count);
    }

    private async Task<RPThemeService> CreateServiceAsync()
    {
        var sqlite = CreatePersistence();
        await sqlite.InitializeAsync();
        return new RPThemeService(CreatePersistenceOptions(), NullLogger<RPThemeService>.Instance);
    }

    private SqlitePersistence CreatePersistence()
    {
        return new SqlitePersistence(
            CreatePersistenceOptions(),
            Options.Create(new LmStudioOptions()),
            Options.Create(new StoryAnalysisOptions()),
            Options.Create(new ScenarioAdaptationOptions()),
            NullLogger<SqlitePersistence>.Instance);
    }

    private IOptions<PersistenceOptions> CreatePersistenceOptions()
        => Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={_databasePath}"
        });

    public void Dispose()
    {
        if (!File.Exists(_databasePath))
        {
            return;
        }

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
