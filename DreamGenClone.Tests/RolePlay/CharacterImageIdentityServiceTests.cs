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
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
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
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
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
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
            await using var input = new MemoryStream(MinimalPng(64, 64));
            var asset = await service.UploadAssetAsync(pack.Id, SceneImageReferenceAssetKind.Face, "face.png", input, SceneImageReferenceFaceView.Front);

            foreach (var view in new[]
            {
                SceneImageReferenceFaceView.ThreeQuarterLeft,
                SceneImageReferenceFaceView.ThreeQuarterRight,
                SceneImageReferenceFaceView.ProfileLeft,
                SceneImageReferenceFaceView.ProfileRight
            })
            {
                await using var extra = new MemoryStream(MinimalPng(64, 64));
                var other = await service.UploadAssetAsync(pack.Id, SceneImageReferenceAssetKind.Face, $"{view}.png", extra, view);
                await service.SetAssetProvenanceAsync(other.Id, "curated reference", SceneImageReferenceConsentState.Confirmed);
                await service.SetAssetApprovalAsync(other.Id, true);
            }

            await service.SetAssetProvenanceAsync(asset.Id, "curated reference", SceneImageReferenceConsentState.Confirmed);
            await service.SetAssetApprovalAsync(asset.Id, true);
            await service.ApprovePackAsync(pack.Id, "{\"descriptor\":\"dark hair\"}", asset.Id);

            var next = await service.SupersedePackAsync(pack.Id);
            var copied = (await service.ListAssetsAsync(next.Id)).Single(a => a.FaceView == SceneImageReferenceFaceView.Front);
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
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
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
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
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
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
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
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
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
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
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

    [Fact]
    public async Task CreateDraft_CarriesTheExplicitScope_AndPersistsIt()
    {
        var (service, repo, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);

            Assert.Equal(CharacterImageIdentityPackScope.FaceOnly, pack.PackScope);
            Assert.Equal(
                CharacterImageIdentityPackScope.FaceOnly,
                (await repo.GetPackAsync(pack.Id))!.PackScope);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task CreateDraft_RefusesToRaiseAnExistingDraft_AndNamesTheWideningCall()
    {
        var (service, _, root, dbPath) = CreateFixture();
        try
        {
            var draft = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.BodyComplete));

            Assert.Contains(draft.Id, error.Message, StringComparison.Ordinal);
            Assert.Contains("FaceOnly", error.Message, StringComparison.Ordinal);
            Assert.Contains("SetDraftPackScopeAsync", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task SetDraftPackScope_RaisesADraft_AndRecordsTheCanonicalUnclothedFront()
    {
        var (service, repo, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
            var body = await UploadBodyAsync(service, pack.Id, "unclothed-front.png",
                SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.Front);

            var raised = await service.SetDraftPackScopeAsync(
                pack.Id, CharacterImageIdentityPackScope.BodyComplete, body.Id);

            Assert.Equal(CharacterImageIdentityPackScope.BodyComplete, raised.PackScope);
            Assert.Equal(body.Id, raised.CanonicalFullBodyAssetId);
            var stored = await repo.GetPackAsync(pack.Id);
            Assert.Equal(CharacterImageIdentityPackScope.BodyComplete, stored!.PackScope);
            Assert.Equal(body.Id, stored.CanonicalFullBodyAssetId);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task SetDraftPackScope_RefusesAnApprovedPack_AndNamesSupersede()
    {
        var (service, _, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
            var front = await service.UploadAssetAsync(
                pack.Id, SceneImageReferenceAssetKind.Face, "front.png",
                new MemoryStream(MinimalPng(64, 64)), SceneImageReferenceFaceView.Front);
            foreach (var view in new[]
                     {
                         SceneImageReferenceFaceView.ThreeQuarterLeft,
                         SceneImageReferenceFaceView.ThreeQuarterRight,
                         SceneImageReferenceFaceView.ProfileLeft,
                         SceneImageReferenceFaceView.ProfileRight
                     })
            {
                await using var extra = new MemoryStream(MinimalPng(64, 64));
                var other = await service.UploadAssetAsync(
                    pack.Id, SceneImageReferenceAssetKind.Face, $"{view}.png", extra, view);
                await service.SetAssetApprovalAsync(other.Id, true);
            }

            await service.SetAssetApprovalAsync(front.Id, true);
            await service.ApprovePackAsync(pack.Id, "{\"descriptor\":\"dark hair\"}", front.Id);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SetDraftPackScopeAsync(pack.Id, CharacterImageIdentityPackScope.BodyComplete, "anything"));

            Assert.Contains("Approved", error.Message, StringComparison.Ordinal);
            Assert.Contains("Supersede", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task SetDraftPackScope_RefusesACanonicalAssetThatIsNotTheUnclothedFront()
    {
        var (service, _, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
            var clothed = await UploadBodyAsync(service, pack.Id, "clothed-front.png",
                SceneImageReferenceBodyState.Clothed, SceneImageReferenceBodyView.Front);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SetDraftPackScopeAsync(pack.Id, CharacterImageIdentityPackScope.BodyComplete, clothed.Id));

            Assert.Contains("unclothed Front", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task SetDraftPackScope_RefusesToNarrowABodyCompletePack()
    {
        var (service, _, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
            var body = await UploadBodyAsync(service, pack.Id, "unclothed-front.png",
                SceneImageReferenceBodyState.Unclothed, SceneImageReferenceBodyView.Front);
            await service.SetDraftPackScopeAsync(pack.Id, CharacterImageIdentityPackScope.BodyComplete, body.Id);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SetDraftPackScopeAsync(pack.Id, CharacterImageIdentityPackScope.FaceOnly, null));

            Assert.Contains("cannot be narrowed", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    /// <summary>
    /// Operator report, 2026-09-24: "Promoted the five accepted views to draft identity pack … but it did not replace
    /// the images in the pack, it is still the older images." The promotion appended a second asset per view, so the
    /// pack kept supplying the pre-existing (approved) one. A slot write REPLACES the slot, and the promoted Front
    /// SEEDS the canonical face when the pack has none.
    /// </summary>
    [Fact]
    public async Task ReplaceSlotAsset_ReplacesTheSlot_AndSeedsTheCanonicalFace()
    {
        var (service, repo, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
            await using (var first = new MemoryStream(MinimalPng(64, 64)))
            {
                var seeded = await service.ReplaceSlotAssetAsync(
                    pack.Id, SceneImageReferenceAssetKind.Face, "front.png", first, SceneImageReferenceFaceView.Front);
                Assert.Equal(0, seeded.ReplacedAssets);
                Assert.Equal(seeded.Asset.Id, (await repo.GetPackAsync(pack.Id))!.CanonicalFaceAssetId);
            }

            await using var second = new MemoryStream(MinimalPng(128, 128));
            var replaced = await service.ReplaceSlotAssetAsync(
                pack.Id, SceneImageReferenceAssetKind.Face, "front.png", second, SceneImageReferenceFaceView.Front);

            Assert.Equal(1, replaced.ReplacedAssets);
            var faces = (await repo.ListAssetsAsync(pack.Id))
                .Where(asset => asset.FaceView == SceneImageReferenceFaceView.Front)
                .ToList();
            var only = Assert.Single(faces);
            Assert.Equal(replaced.Asset.Id, only.Id);
            Assert.Equal(128, only.Width);

            // The pointer followed the replacement instead of dangling on a deleted asset, and the replaced asset's
            // file is gone because nothing references it any more.
            Assert.Equal(replaced.Asset.Id, (await repo.GetPackAsync(pack.Id))!.CanonicalFaceAssetId);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    /// <summary>Pressing promote again must not churn the store when the slot already holds exactly these bytes.</summary>
    [Fact]
    public async Task ReplaceSlotAsset_IsANoOpWhenTheSlotAlreadyHoldsTheseBytes()
    {
        var (service, repo, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
            var png = MinimalPng(64, 64);
            await using (var first = new MemoryStream(png))
            {
                await service.ReplaceSlotAssetAsync(
                    pack.Id, SceneImageReferenceAssetKind.Face, "front.png", first, SceneImageReferenceFaceView.Front);
            }

            await using var same = new MemoryStream(png);
            var write = await service.ReplaceSlotAssetAsync(
                pack.Id, SceneImageReferenceAssetKind.Face, "front.png", same, SceneImageReferenceFaceView.Front);

            Assert.Equal(0, write.ReplacedAssets);
            Assert.Single(await repo.ListAssetsAsync(pack.Id));
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    [Fact]
    public async Task ReplaceSlotAsset_RefusesAnApprovedPack_AndASlotItCannotName()
    {
        var (service, repo, root, dbPath) = CreateFixture();
        try
        {
            var pack = await service.CreateDraftPackAsync("char-1", CharacterImageIdentityPackScope.FaceOnly);
            await using var input = new MemoryStream(MinimalPng(64, 64));
            var unnamed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReplaceSlotAssetAsync(pack.Id, SceneImageReferenceAssetKind.Face, "front.png", input));
            Assert.Contains("requires the face view it occupies", unnamed.Message, StringComparison.Ordinal);

            var front = await service.ReplaceSlotAssetAsync(
                pack.Id, SceneImageReferenceAssetKind.Face, "front.png", new MemoryStream(MinimalPng(64, 64)),
                SceneImageReferenceFaceView.Front);
            await service.SetAssetProvenanceAsync(front.Asset.Id, "test", SceneImageReferenceConsentState.NotApplicable);
            await service.SetAssetApprovalAsync(front.Asset.Id, true);
            foreach (var view in new[]
                     {
                         SceneImageReferenceFaceView.ThreeQuarterLeft, SceneImageReferenceFaceView.ThreeQuarterRight,
                         SceneImageReferenceFaceView.ProfileLeft, SceneImageReferenceFaceView.ProfileRight
                     })
            {
                var uploaded = await service.ReplaceSlotAssetAsync(
                    pack.Id, SceneImageReferenceAssetKind.Face, "view.png", new MemoryStream(MinimalPng(64, 64)), view);
                await service.SetAssetProvenanceAsync(uploaded.Asset.Id, "test", SceneImageReferenceConsentState.NotApplicable);
                await service.SetAssetApprovalAsync(uploaded.Asset.Id, true);
            }

            await service.ApprovePackAsync(pack.Id, "{\"descriptor\":\"frozen\"}", front.Asset.Id);

            var approvedError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReplaceSlotAssetAsync(
                    pack.Id, SceneImageReferenceAssetKind.Face, "front.png", new MemoryStream(MinimalPng(64, 64)),
                    SceneImageReferenceFaceView.Front));
            Assert.Contains("only a draft pack can be written", approvedError.Message, StringComparison.Ordinal);
            Assert.Equal(CharacterImageIdentityPackStatus.Approved, (await repo.GetPackAsync(pack.Id))!.Status);
        }
        finally
        {
            Cleanup(dbPath, root);
        }
    }

    private static async Task<SceneImageReferenceAsset> UploadBodyAsync(
        CharacterImageIdentityService service,
        string packId,
        string fileName,
        SceneImageReferenceBodyState state,
        SceneImageReferenceBodyView view)
    {
        await using var input = new MemoryStream(MinimalPng(64, 64));
        return await service.UploadAssetAsync(
            packId,
            SceneImageReferenceAssetKind.FullBody,
            fileName,
            input,
            faceView: null,
            bodyView: view,
            bodyState: state);
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
