using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class PosePresetRepositoryTests
{
    [Fact]
    public async Task Upsert_AndRoundTrip()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var preset = new PosePreset { Name = "Standing A", Category = "standing", KeypointsJson = "[{\"x\":0.5}]", KnownGood = true };
            await repo.UpsertAsync(preset);

            var loaded = await repo.GetAsync(preset.Id);
            Assert.NotNull(loaded);
            Assert.Equal("Standing A", loaded!.Name);
            Assert.True(loaded.KnownGood);
            Assert.Equal("[{\"x\":0.5}]", loaded.KeypointsJson);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Search_FiltersByNameAndCategory()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            await repo.UpsertAsync(new PosePreset { Name = "Standing A", Category = "standing", KeypointsJson = "[]" });
            await repo.UpsertAsync(new PosePreset { Name = "Kneeling B", Category = "kneeling", KeypointsJson = "[]" });

            var standing = await repo.SearchAsync(null, "standing");
            Assert.Single(standing);
            Assert.Equal("Standing A", standing[0].Name);

            var byName = await repo.SearchAsync("kneel", null);
            Assert.Single(byName);
            Assert.Equal("Kneeling B", byName[0].Name);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Upsert_UpdatesExisting_AndDeleteRemoves()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var preset = new PosePreset { Name = "Standing A", Category = "standing", KeypointsJson = "[]" };
            await repo.UpsertAsync(preset);
            preset.Name = "Standing A v2";
            preset.KnownGood = true;
            await repo.UpsertAsync(preset);

            Assert.Single(await repo.ListAsync());
            Assert.Equal("Standing A v2", (await repo.GetAsync(preset.Id))!.Name);

            await repo.DeleteAsync(preset.Id);
            Assert.Empty(await repo.ListAsync());
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Upsert_MissingNameOrKeypoints_Throws()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repo.UpsertAsync(new PosePreset { Name = "", KeypointsJson = "[]" }));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repo.UpsertAsync(new PosePreset { Name = "NoKeypoints", KeypointsJson = "  " }));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static (PosePresetRepository repo, string dbPath) CreateRepo()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"pose-presets-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        return (new PosePresetRepository(options), dbPath);
    }

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
