using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentLocationLinkRepositoryTests
{
    [Fact]
    public async Task Upsert_IsIdempotentPerMomentAndPov()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"moment-location-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var repository = new SceneMomentLocationLinkRepository(options);

        try
        {
            var first = new SceneMomentLocationLink
            {
                MomentId = "moment-1",
                Pov = "Omniscient",
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
                Pov = "Omniscient",
                LocationAssetId = "loc-b",
                ScenarioLocationId = null,
                Origin = "operator-bound",
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            await repository.UpsertAsync(second);

            var loaded = await repository.GetAsync("moment-1", "Omniscient");
            Assert.NotNull(loaded);
            Assert.Equal("loc-b", loaded!.LocationAssetId);
            Assert.Null(loaded.ScenarioLocationId);
            Assert.Equal("Omniscient", loaded.Pov);
        }
        finally
        {
            DeleteDatabaseFiles(dbPath);
        }
    }

    /// <summary>
    /// The point of the per-POV key: one POV binding its place must not overwrite another POV's, which a single row per
    /// moment could not express.
    /// </summary>
    [Fact]
    public async Task Upsert_KeepsEachPovsOwnBindingForTheSameMoment()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"moment-location-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var repository = new SceneMomentLocationLinkRepository(options);

        try
        {
            await repository.UpsertAsync(Link("Omniscient", "loc-wide"));
            await repository.UpsertAsync(Link("Dean", "loc-dean"));
            await repository.UpsertAsync(Link("Becky", "loc-becky"));

            Assert.Equal("loc-wide", (await repository.GetAsync("moment-1", "Omniscient"))!.LocationAssetId);
            Assert.Equal("loc-dean", (await repository.GetAsync("moment-1", "Dean"))!.LocationAssetId);
            Assert.Equal("loc-becky", (await repository.GetAsync("moment-1", "Becky"))!.LocationAssetId);
            // A POV that never bound one has none — never another POV's, and never a default.
            Assert.Null(await repository.GetAsync("moment-1", "Marcus"));
        }
        finally
        {
            DeleteDatabaseFiles(dbPath);
        }
    }

    /// <summary>
    /// The pre-per-POV schema held one row per moment (MomentId as the primary key) and cannot hold a second POV's row.
    /// The migration must rebuild it, carry the existing row across, and leave that legacy row unable to be read as any
    /// POV's binding.
    /// </summary>
    [Fact]
    public async Task ExistingDatabase_WithThePrePovTable_IsMigratedAndItsRowPreserved()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"moment-location-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });

        try
        {
            await using (var legacy = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await legacy.OpenAsync();
                await using var create = legacy.CreateCommand();
                create.CommandText = """
                    CREATE TABLE SceneMomentLocationLinks (
                        MomentId TEXT PRIMARY KEY,
                        LocationAssetId TEXT NOT NULL CHECK (length(trim(LocationAssetId)) > 0),
                        ScenarioLocationId TEXT NULL,
                        Origin TEXT NOT NULL CHECK (length(trim(Origin)) > 0),
                        CreatedUtc TEXT NOT NULL,
                        UpdatedUtc TEXT NOT NULL
                    );
                    INSERT INTO SceneMomentLocationLinks
                        (MomentId, LocationAssetId, ScenarioLocationId, Origin, CreatedUtc, UpdatedUtc)
                    VALUES
                        ('moment-legacy', 'loc-legacy', 'scenario-loc-1', 'operator-bound',
                         '2026-01-01T00:00:00.0000000Z', '2026-01-01T00:00:00.0000000Z');
                    """;
                await create.ExecuteNonQueryAsync();
            }

            var repository = new SceneMomentLocationLinkRepository(options);

            // Opening the repository migrates; the second POV can now be recorded where the primary key forbade it.
            await repository.UpsertAsync(Link("Dean", "loc-dean"));

            Assert.Equal("loc-dean", (await repository.GetAsync("moment-1", "Dean"))!.LocationAssetId);
            // A legacy row (POV NULL) neither collides with nor answers for a real POV on the SAME moment.
            await repository.UpsertAsync(new SceneMomentLocationLink
            {
                MomentId = "moment-legacy",
                Pov = "Dean",
                LocationAssetId = "loc-dean",
                Origin = "operator-bound",
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            Assert.Equal("loc-dean", (await repository.GetAsync("moment-legacy", "Dean"))!.LocationAssetId);
            // The legacy row named no POV, so no POV may be handed it.
            Assert.Null(await repository.GetAsync("moment-legacy", "Omniscient"));

            await using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            await connection.OpenAsync();
            await using var preserved = connection.CreateCommand();
            preserved.CommandText = "SELECT LocationAssetId, Pov FROM SceneMomentLocationLinks WHERE MomentId = 'moment-legacy';";
            await using var reader = await preserved.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("loc-legacy", reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
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
                Pov = "Omniscient",
                LocationAssetId = string.Empty,
                Origin = "operator-bound"
            }));

            // A binding with no POV is refused rather than stored against every POV at once.
            var missingPov = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpsertAsync(new SceneMomentLocationLink
            {
                MomentId = "moment-1",
                LocationAssetId = "loc-a",
                Origin = "operator-bound"
            }));
            Assert.Contains("POV", missingPov.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDatabaseFiles(dbPath);
        }
    }

    private static SceneMomentLocationLink Link(string pov, string locationAssetId) => new()
    {
        MomentId = "moment-1",
        Pov = pov,
        LocationAssetId = locationAssetId,
        ScenarioLocationId = null,
        Origin = "operator-bound",
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow
    };

    private static void DeleteDatabaseFiles(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(dbPath + suffix); } catch { }
        }
    }
}
