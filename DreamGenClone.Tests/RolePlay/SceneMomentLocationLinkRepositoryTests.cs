using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentLocationLinkRepositoryTests
{
    [Fact]
    public async Task Upsert_IsIdempotentPerMoment()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"moment-location-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var repository = new SceneMomentLocationLinkRepository(options);

        try
        {
            var first = new SceneMomentLocationLink
            {
                MomentId = "moment-1",
                LocationAssetId = "loc-a",
                ScenarioLocationId = "scenario-loc-1",
                Origin = "operator-bound",
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            await repository.UpsertAsync(first);

            var second = new SceneMomentLocationLink
            {
                MomentId = "moment-1",
                LocationAssetId = "loc-b",
                ScenarioLocationId = null,
                Origin = "operator-bound",
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            await repository.UpsertAsync(second);

            var loaded = await repository.GetAsync("moment-1");
            Assert.NotNull(loaded);
            Assert.Equal("loc-b", loaded!.LocationAssetId);
            Assert.Null(loaded.ScenarioLocationId);
        }
        finally
        {
            DeleteDatabaseFiles(dbPath);
        }
    }

    [Fact]
    public async Task Upsert_MissingRequiredValue_FailsFast()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"moment-location-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var repository = new SceneMomentLocationLinkRepository(options);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpsertAsync(new SceneMomentLocationLink
            {
                MomentId = "moment-1",
                LocationAssetId = string.Empty,
                Origin = "operator-bound"
            }));
        }
        finally
        {
            DeleteDatabaseFiles(dbPath);
        }
    }

    private static void DeleteDatabaseFiles(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(dbPath + suffix); } catch { }
        }
    }
}
