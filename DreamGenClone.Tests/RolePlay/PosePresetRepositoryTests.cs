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

    [Fact]
    public async Task Library_UpsertListAndGet()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            await repo.UpsertLibraryAsync(new PoseLibrary
            {
                Id = PoseLibraryIds.BundledPackFolder,
                Name = "OpenPose NSFW pack",
                Description = "Imported",
                IsSystem = true
            });
            await repo.UpsertLibraryAsync(new PoseLibrary { Id = "handshakes", Name = "Handshakes" });

            var libraries = await repo.ListLibrariesAsync();
            Assert.Equal(2, libraries.Count);
            Assert.Equal(PoseLibraryIds.BundledPackFolder, libraries[0].Id); // system libraries sort first
            Assert.True(libraries[0].IsSystem);

            var loaded = await repo.GetLibraryAsync("handshakes");
            Assert.NotNull(loaded);
            Assert.Equal("Handshakes", loaded!.Name);
            Assert.False(loaded.IsSystem);

            Assert.Null(await repo.GetLibraryAsync("no-such-library"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Search_ByKeyword_MatchesTheKeywordsColumn()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            await repo.UpsertAsync(new PosePreset
            {
                Name = "standing 028",
                Category = "standing",
                Keywords = "upright neutral arms down",
                KeypointsJson = "[]"
            });
            await repo.UpsertAsync(new PosePreset
            {
                Name = "kneeling 017",
                Category = "kneeling",
                Keywords = "kneel knees down",
                KeypointsJson = "[]"
            });

            var byKeyword = await repo.SearchAsync("arms down");
            Assert.Single(byKeyword);
            Assert.Equal("standing 028", byKeyword[0].Name);

            var byCategoryText = await repo.SearchAsync("kneel");
            Assert.Single(byCategoryText);
            Assert.Equal("kneeling 017", byCategoryText[0].Name);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Search_ByLibrary_FiltersToThatLibrary()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            await repo.UpsertAsync(new PosePreset
            {
                Name = "pack pose",
                Category = "standing",
                LibraryId = PoseLibraryIds.BundledPackFolder,
                KeypointsJson = "[]"
            });
            await repo.UpsertAsync(new PosePreset
            {
                Name = "authored pose",
                Category = "standing",
                LibraryId = PoseLibraryIds.Authored,
                KeypointsJson = "[]"
            });

            var result = await repo.SearchAsync(null, null, PoseLibraryIds.Authored);

            Assert.Single(result);
            Assert.Equal("authored pose", result[0].Name);
            Assert.Equal(PoseLibraryIds.Authored, result[0].LibraryId);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task EnsureSchema_UpgradesADatabaseThatPredatesTheLibraryModel()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"pose-presets-legacy-{Guid.NewGuid():N}.db");
        try
        {
            // The pre-library schema, exactly as the first version of this table created it.
            await using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE PosePresets (
                        Id TEXT PRIMARY KEY,
                        Name TEXT NOT NULL,
                        Category TEXT NOT NULL DEFAULT '',
                        KeypointsJson TEXT NOT NULL DEFAULT '[]',
                        SkeletonPngPath TEXT NULL,
                        ThumbnailPath TEXT NULL,
                        KnownGood INTEGER NOT NULL DEFAULT 0,
                        ProvenanceJson TEXT NULL,
                        CreatedUtc TEXT NOT NULL
                    );
                    INSERT INTO PosePresets (Id, Name, Category, KeypointsJson, KnownGood, CreatedUtc)
                    VALUES ('legacy-1', 'Legacy pose', 'standing', '[]', 0, '2026-01-01T00:00:00.0000000Z');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
            var repo = new PosePresetRepository(options);

            var presets = await repo.ListAsync();

            var legacy = Assert.Single(presets);
            Assert.Equal("Legacy pose", legacy.Name);
            Assert.Equal(string.Empty, legacy.LibraryId);
            Assert.Equal(string.Empty, legacy.Keywords);
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
