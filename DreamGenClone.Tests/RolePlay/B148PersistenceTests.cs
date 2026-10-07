using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class B148PersistenceTests
{
    [Fact]
    public async Task SceneAsset_HierarchyAndScenarioLinkColumns_RoundTrip()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"scene-asset-b148-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var repository = new SceneAssetRepository(options);

        try
        {
            var asset = new SceneAsset
            {
                Id = "loc-container",
                Name = "Husband and Wife Trailer",
                Type = SceneAssetType.Location,
                IsContainerOnly = true,
                Kind = SceneAssetKind.Uploaded,
                Status = SceneAssetStatus.Pending,
                ParentAssetId = "world-1",
                ScenarioLocationId = "scenario-location-1",
                ScenarioId = "scenario-1"
            };
            await repository.UpsertAsync(asset);

            var loaded = await repository.GetAsync("loc-container");
            Assert.NotNull(loaded);
            Assert.Equal("world-1", loaded!.ParentAssetId);
            Assert.Equal("scenario-location-1", loaded.ScenarioLocationId);
            Assert.Equal("scenario-1", loaded.ScenarioId);
        }
        finally
        {
            DeleteDatabaseFiles(dbPath);
        }
    }

    [Fact]
    public async Task ProductionGroup_LocationBackdropColumn_RoundTripsAndSurvivesReopen()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"production-group-b148-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var repository = new SceneImageProductionGroupRepository(options);

        try
        {
            // Trigger schema creation + the guarded ALTER through a read that only opens the store.
            _ = await repository.GetRetentionPolicyAsync();

            await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await connection.OpenAsync();

                // The group table carries a foreign key to the enrichment table, which another repository owns, so the
                // row it references has to exist for the insert to be accepted at all.
                await using (var enrichment = connection.CreateCommand())
                {
                    enrichment.CommandText =
                        "CREATE TABLE IF NOT EXISTS SceneMomentEnrichments (Id TEXT PRIMARY KEY);"
                        + "INSERT OR IGNORE INTO SceneMomentEnrichments (Id) VALUES ('enrichment');";
                    await enrichment.ExecuteNonQueryAsync();
                }

                await using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO SceneImageProductionGroups
                        (Id, SessionId, InteractionId, CatalogueId, BeatId, BeatProductionPlanId, BeatProductionPlanVersion,
                         MomentSetId, MomentSetVersion, MomentId, MomentEnrichmentId, MomentEnrichmentRevision, Pov,
                         Status, IdentityPolicy, CreatedUtc, UpdatedUtc)
                    VALUES
                        ('group-1', 'session', 'interaction', 'catalogue', 'beat', 'plan', 1,
                         'moment-set', 1, 'moment-1', 'enrichment', 1, 'pov',
                         'Draft', 'Required', '2026-10-04T00:00:00Z', '2026-10-04T00:00:00Z');
                    """;
                await insert.ExecuteNonQueryAsync();
            }

            var backdropJson = "{\"AssetId\":\"loc-a\",\"ImageId\":\"img-a\",\"Sha256\":\"ABC\",\"ProductionVersion\":2,\"Label\":\"Front\"}";
            var updated = await repository.SetLocationBackdropAsync("group-1", backdropJson, DateTime.UtcNow);
            Assert.Equal(backdropJson, updated.LocationBackdropJson);

            // Reopen through a fresh repository: the column persists.
            var reopened = await new SceneImageProductionGroupRepository(options).GetAsync("group-1");
            Assert.Equal(backdropJson, reopened!.LocationBackdropJson);

            var cleared = await repository.SetLocationBackdropAsync("group-1", null, DateTime.UtcNow);
            Assert.Null(cleared.LocationBackdropJson);
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
