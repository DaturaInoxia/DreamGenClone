using DreamGenClone.Application.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class FinishingMoveMatrixSeedServiceTests : IDisposable
{
    private readonly string _databasePath;

    public FinishingMoveMatrixSeedServiceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"dreamgen-finish-seed-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task SeedDefaultsAsync_WhenEmpty_InsertsExampleMatrixRows()
    {
        var rpThemeService = await CreateServiceAsync();
        var seeder = new FinishingMoveMatrixSeedService(rpThemeService, NullLogger<FinishingMoveMatrixSeedService>.Instance);

        await seeder.SeedDefaultsAsync();

        var rows = await rpThemeService.ListFinishingMoveMatrixRowsAsync();
        Assert.Equal(27, rows.Count);

        Assert.Contains(rows, r => r.DesireBand == "60-100" && r.SelfRespectBand == "60-100" && r.OtherManDominanceBand == "0-29");
        Assert.Contains(rows, r => r.DesireBand == "0-29" && r.SelfRespectBand == "0-29" && r.OtherManDominanceBand == "60-100");
    }

    [Fact]
    public async Task SeedDefaultsAsync_Twice_IsIdempotent()
    {
        var rpThemeService = await CreateServiceAsync();
        var seeder = new FinishingMoveMatrixSeedService(rpThemeService, NullLogger<FinishingMoveMatrixSeedService>.Instance);

        await seeder.SeedDefaultsAsync();
        await seeder.SeedDefaultsAsync();

        var rows = await rpThemeService.ListFinishingMoveMatrixRowsAsync();
        Assert.Equal(27, rows.Count);
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
