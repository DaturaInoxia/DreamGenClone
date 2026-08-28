using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneAssetRepositoryTests
{
    [Fact]
    public async Task Upsert_Get_RoundTripsMetadata()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            var asset = new SceneAsset
            {
                Id = "a1",
                Name = "Forest clearing",
                Kind = SceneAssetKind.PromptGenerated,
                Status = SceneAssetStatus.Complete,
                Prompt = "a misty forest clearing",
                FileRelativePath = "assets/a1.png",
                MediaType = "image/png",
                Width = 1024,
                Height = 1024,
                ByteLength = 123,
                Sha256 = "ABCD",
                FaceView = SceneImageReferenceFaceView.Front,
                IdentityPackId = "pack-1",
                CharacterProfileId = "char-1",
                CompletedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            await repo.UpsertAsync(asset);

            var loaded = await repo.GetAsync("a1");
            Assert.NotNull(loaded);
            Assert.Equal("Forest clearing", loaded!.Name);
            Assert.Equal(SceneAssetKind.PromptGenerated, loaded.Kind);
            Assert.Equal(SceneAssetStatus.Complete, loaded.Status);
            Assert.Equal("assets/a1.png", loaded.FileRelativePath);
            Assert.Equal(1024, loaded.Width);
            Assert.Equal(SceneImageReferenceFaceView.Front, loaded.FaceView);
            Assert.Equal("pack-1", loaded.IdentityPackId);
            Assert.Equal("char-1", loaded.CharacterProfileId);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task List_ReturnsNewestFirst()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertAsync(new SceneAsset { Id = "old", Name = "Old", Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete, CreatedUtc = DateTime.UtcNow.AddHours(-2) });
            await repo.UpsertAsync(new SceneAsset { Id = "new", Name = "New", Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete, CreatedUtc = DateTime.UtcNow });

            var all = await repo.ListAsync();
            Assert.Equal(2, all.Count);
            Assert.Equal("new", all[0].Id);
            Assert.Equal("old", all[1].Id);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task ListByPack_FiltersToPack()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertAsync(new SceneAsset { Id = "p1f", Name = "Front", Kind = SceneAssetKind.ProfilePackFront, Status = SceneAssetStatus.Complete, IdentityPackId = "pack-1", CharacterProfileId = "char-1" });
            await repo.UpsertAsync(new SceneAsset { Id = "p1e", Name = "3/4", Kind = SceneAssetKind.ProfilePackFace, Status = SceneAssetStatus.Complete, IdentityPackId = "pack-1", CharacterProfileId = "char-1" });
            await repo.UpsertAsync(new SceneAsset { Id = "other", Name = "Other", Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete, IdentityPackId = "pack-2" });

            var byPack = await repo.ListByPackAsync("pack-1");
            Assert.Equal(2, byPack.Count);
            Assert.DoesNotContain(byPack, a => a.Id == "other");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Upsert_Update_PersistsNewStatus()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            var asset = new SceneAsset { Id = "a1", Name = "X", Kind = SceneAssetKind.PromptGenerated, Status = SceneAssetStatus.Pending };
            await repo.UpsertAsync(asset);

            asset.Status = SceneAssetStatus.Complete;
            asset.FileRelativePath = "assets/a1.png";
            asset.UpdatedUtc = DateTime.UtcNow;
            await repo.UpsertAsync(asset);

            var loaded = await repo.GetAsync("a1");
            Assert.Equal(SceneAssetStatus.Complete, loaded!.Status);
            Assert.Equal("assets/a1.png", loaded.FileRelativePath);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Delete_RemovesRow()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertAsync(new SceneAsset { Id = "a1", Name = "X", Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete });
            await repo.DeleteAsync("a1");
            Assert.Null(await repo.GetAsync("a1"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task CountByFilePath_CountsSharedFiles()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertAsync(new SceneAsset { Id = "a1", Name = "A", Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete, FileRelativePath = "assets/shared.png" });
            await repo.UpsertAsync(new SceneAsset { Id = "a2", Name = "B", Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete, FileRelativePath = "assets/shared.png" });

            Assert.Equal(2, await repo.CountByFilePathAsync("assets/shared.png"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static SceneAssetRepository CreateRepoAsync(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"scene-asset-repo-{Guid.NewGuid():N}.db");
        var repo = new SceneAssetRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False"
        }));
        // Touch the schema by running a read against a missing row (no row is inserted).
        repo.GetAsync("__schema_probe__").GetAwaiter().GetResult();
        return repo;
    }

    private static void Cleanup(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(dbPath + suffix); } catch { /* best effort */ }
        }
    }
}
