using DreamGenClone.Application.Administration;
using DreamGenClone.Application.Sessions;
using DreamGenClone.Domain.Administration;
using DreamGenClone.Infrastructure.Administration;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Web.Application.Administration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.Administration;

public sealed class DatabaseBackupTests
{
    [Fact]
    public async Task CreateDatabaseBackupAsync_CreatesBackupFile_AndPersistsMetadata()
    {
        using var fixture = await CreateFixtureAsync();

        var backup = await fixture.Facade.CreateDatabaseBackupAsync("pre-release");
        var persisted = await fixture.Repository.GetByIdAsync(backup.Id);
        var download = await fixture.Facade.GetBackupDownloadAsync(backup.Id);

        Assert.NotNull(persisted);
        Assert.NotNull(download);
        Assert.Equal("pre-release", persisted!.DisplayName);
        Assert.Equal(backup.FileName, persisted.FileName);
        Assert.True(File.Exists(download!.Value.FilePath));
        Assert.True(new FileInfo(download.Value.FilePath).Length > 0);
    }

    [Fact]
    public async Task GetDatabaseBackupsAsync_ReturnsNewestBackupFirst()
    {
        using var fixture = await CreateFixtureAsync();

        var first = await fixture.Facade.CreateDatabaseBackupAsync("first-backup");
        await Task.Delay(20);
        var second = await fixture.Facade.CreateDatabaseBackupAsync("second-backup");

        var backups = await fixture.Facade.GetDatabaseBackupsAsync();

        Assert.True(backups.Count >= 2);
        Assert.Equal(second.Id, backups[0].Id);
        Assert.Equal(first.Id, backups[1].Id);
    }

    [Fact]
    public async Task GetBackupDownloadAsync_ReturnsNull_ForUnknownBackup()
    {
        using var fixture = await CreateFixtureAsync();

        var download = await fixture.Facade.GetBackupDownloadAsync("missing-backup");

        Assert.Null(download);
    }

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"dreamgenclone-admin-tests-{Guid.NewGuid():N}");
        var dataPath = Path.Combine(rootPath, "data");
        Directory.CreateDirectory(dataPath);

        var databasePath = Path.Combine(dataPath, "dreamgenclone.test.db");
        var persistenceOptions = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={databasePath}"
        });

        var sqlitePersistence = new SqlitePersistence(
            persistenceOptions,
            Options.Create(new LmStudioOptions()),
            Options.Create(new StoryAnalysisOptions()),
            Options.Create(new ScenarioAdaptationOptions()),
            NullLogger<SqlitePersistence>.Instance);
        await sqlitePersistence.InitializeAsync();

        var repository = new DatabaseBackupRepository(persistenceOptions);
        IAutoSaveCoordinator autoSaveCoordinator = new AutoSaveCoordinator(NullLogger<AutoSaveCoordinator>.Instance);
        var facade = new AdministrationFacade(
            repository,
            autoSaveCoordinator,
            persistenceOptions,
            sqlitePersistence,
            NullLogger<AdministrationFacade>.Instance);

        return new TestFixture(rootPath, repository, facade);
    }

    private sealed class TestFixture : IDisposable
    {
        public TestFixture(string rootPath, IDatabaseBackupRepository repository, AdministrationFacade facade)
        {
            RootPath = rootPath;
            Repository = repository;
            Facade = facade;
        }

        public string RootPath { get; }

        public IDatabaseBackupRepository Repository { get; }

        public AdministrationFacade Facade { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
                // Test cleanup should not mask assertion failures.
            }
        }
    }
}