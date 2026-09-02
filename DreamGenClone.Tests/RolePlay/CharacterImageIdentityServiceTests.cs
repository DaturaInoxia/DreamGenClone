using System.Buffers.Binary;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Infrastructure.Storage;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class CharacterImageIdentityServiceTests
{
    [Fact]
    public async Task UploadAsset_StoresBytesAndMetadata()
    {
        var (service, repo, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1");
            var png = MinimalPng(320, 240);
            await using var input = new MemoryStream(png);

            var asset = await service.UploadAssetAsync(pack.Id, SceneImageReferenceAssetKind.Face, "face.png", input, SceneImageReferenceFaceView.Front);

            Assert.Equal(SceneImageReferenceAssetKind.Face, asset.AssetKind);
            Assert.Equal("image/png", asset.MediaType);
            Assert.Equal(320, asset.Width);
            Assert.Equal(240, asset.Height);
            Assert.Equal(png.Length, asset.ByteLength);
            Assert.False(asset.IsApproved);

            var loaded = await repo.GetAssetAsync(asset.Id);
            Assert.NotNull(loaded);
            Assert.True(File.Exists(Path.Combine(root, asset.FileRelativePath)));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task DeleteAsset_RemovesFileWhenUnreferenced()
    {
        var (service, _, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1");
            await using var input = new MemoryStream(MinimalPng(64, 64));
            var asset = await service.UploadAssetAsync(pack.Id, SceneImageReferenceAssetKind.Face, "face.png", input, SceneImageReferenceFaceView.Front);
            var fullPath = Path.Combine(root, asset.FileRelativePath);
            Assert.True(File.Exists(fullPath));

            await service.DeleteAssetAsync(asset.Id);

            Assert.False(File.Exists(fullPath));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task SupersedeCopy_DeleteAsset_KeepsSharedFile()
    {
        var (service, repo, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1");
            await using var input = new MemoryStream(MinimalPng(64, 64));
            var asset = await service.UploadAssetAsync(pack.Id, SceneImageReferenceAssetKind.Face, "face.png", input, SceneImageReferenceFaceView.Front);

            await service.SetAssetProvenanceAsync(asset.Id, "curated reference", SceneImageReferenceConsentState.Confirmed);
            await service.SetAssetApprovalAsync(asset.Id, true);
            await service.ApprovePackAsync(pack.Id, "{\"descriptor\":\"dark hair\"}", asset.Id);

            var next = await service.SupersedePackAsync(pack.Id);
            var copied = (await service.ListAssetsAsync(next.Id)).Single();
            Assert.Equal(asset.FileRelativePath, copied.FileRelativePath);

            var fullPath = Path.Combine(root, asset.FileRelativePath);
            await service.DeleteAssetAsync(copied.Id);

            // The superseded pack's asset still references the file, so it must survive.
            Assert.True(File.Exists(fullPath));
            Assert.NotNull(await repo.GetAssetAsync(asset.Id));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task UploadFace_WithoutFaceView_Throws()
    {
        var (service, _, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1");
            await using var input = new MemoryStream(MinimalPng(64, 64));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UploadAssetAsync(pack.Id, SceneImageReferenceAssetKind.Face, "face.png", input, faceView: null));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task UploadNonFace_WithFaceView_Throws()
    {
        var (service, _, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1");
            await using var input = new MemoryStream(MinimalPng(64, 64));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UploadAssetAsync(
                    pack.Id, SceneImageReferenceAssetKind.Wardrobe, "outfit.png", input,
                    SceneImageReferenceFaceView.Front));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task UploadFace_WithFaceView_PersistsView()
    {
        var (service, repo, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1");
            await using var input = new MemoryStream(MinimalPng(64, 64));

            var asset = await service.UploadAssetAsync(
                pack.Id, SceneImageReferenceAssetKind.Face, "profile.png", input,
                SceneImageReferenceFaceView.ThreeQuarterLeft);

            Assert.Equal(SceneImageReferenceFaceView.ThreeQuarterLeft, asset.FaceView);
            var loaded = await repo.GetAssetAsync(asset.Id);
            Assert.Equal(SceneImageReferenceFaceView.ThreeQuarterLeft, loaded!.FaceView);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task SetAssetQuality_PersistsRatingAndNotes()
    {
        var (service, repo, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1");
            await using var input = new MemoryStream(MinimalPng(64, 64));
            var asset = await service.UploadAssetAsync(pack.Id, SceneImageReferenceAssetKind.Face, "face.png", input, SceneImageReferenceFaceView.Front);

            await service.SetAssetQualityAsync(asset.Id, SceneImageReferenceQuality.Ok, "Moderate resolution.");

            var loaded = await repo.GetAssetAsync(asset.Id);
            Assert.Equal(SceneImageReferenceQuality.Ok, loaded!.QualityRating);
            Assert.Equal("Moderate resolution.", loaded.QualityNotes);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task UploadFace_AutoAnalyzesQuality()
    {
        var (service, _, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1");
            await using var input = new MemoryStream(MinimalPng(64, 64));

            var asset = await service.UploadAssetAsync(pack.Id, SceneImageReferenceAssetKind.Face, "tiny.png", input, SceneImageReferenceFaceView.Front);

            Assert.NotEqual(SceneImageReferenceQuality.NotRated, asset.QualityRating);
            Assert.False(string.IsNullOrWhiteSpace(asset.QualityNotes));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    private static (CharacterImageIdentityService Service, CharacterImageIdentityRepository Repo, string Root, string DbPath) CreateFixture()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"identity-service-{Guid.NewGuid():N}.db");
        var root = Path.Combine(Path.GetTempPath(), $"identity-service-files-{Guid.NewGuid():N}");
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False",
            SceneImageRoot = root
        });

        var repo = new CharacterImageIdentityRepository(options);
        var storage = new CharacterImageAssetStorageService(options, NullLogger<CharacterImageAssetStorageService>.Instance);
        var service = new CharacterImageIdentityService(repo, storage, new ReferenceImageQualityAnalyzer(), NullLogger<CharacterImageIdentityService>.Instance);
        return (service, repo, root, dbPath);
    }

    private static void Cleanup(string dbPath, string root)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); } catch (IOException) { }
        }

        try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
    }

    private static byte[] MinimalPng(int width, int height)
    {
        var bytes = new byte[29];
        bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47;
        bytes[4] = 0x0D; bytes[5] = 0x0A; bytes[6] = 0x1A; bytes[7] = 0x0A;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        bytes[12] = 0x49; bytes[13] = 0x48; bytes[14] = 0x44; bytes[15] = 0x52;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        bytes[24] = 8; bytes[25] = 6; bytes[26] = 0; bytes[27] = 0; bytes[28] = 0;
        return bytes;
    }
}
