using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class CharacterImageIdentityRepositoryTests
{
    private const string FaceSha = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string BodySha = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public async Task DraftPack_RoundTrips()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft,
                DescriptorSnapshotJson = "{\"hair\":\"dark\"}"
            };
            var inserted = await repo.UpsertDraftAsync(pack);

            Assert.Equal(pack.Id, inserted.Id);
            Assert.Equal(1, inserted.Version);

            var loaded = await repo.GetPackAsync(pack.Id);
            Assert.NotNull(loaded);
            Assert.Equal("char-1", loaded!.CharacterProfileId);
            Assert.Equal(CharacterImageIdentityPackStatus.Draft, loaded.Status);
            Assert.Equal("{\"hair\":\"dark\"}", loaded.DescriptorSnapshotJson);

            var list = await repo.ListPacksAsync("char-1");
            Assert.Single(list);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task DuplicateVersion_Fails()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });

            await Assert.ThrowsAsync<SqliteException>(() => repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            }));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Approve_RejectsUnknownConsent()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            var face = FaceAsset(pack.Id, isApproved: true, consentState: SceneImageReferenceConsentState.Confirmed);
            var body = FullBodyAsset(pack.Id);
            body.ConsentState = SceneImageReferenceConsentState.Unknown;
            await repo.AddAssetAsync(face);
            await repo.AddAssetAsync(body);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repo.ApproveAsync(pack.Id, "{\"hair\":\"dark\"}", face.Id));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task FaceAsset_FaceView_RoundTrips()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            var face = FaceAsset(pack.Id, isApproved: false, consentState: SceneImageReferenceConsentState.Confirmed);
            face.FaceView = SceneImageReferenceFaceView.ProfileLeft;

            await repo.AddAssetAsync(face);
            var assets = await repo.ListAssetsAsync(pack.Id);

            var loaded = Assert.Single(assets);
            Assert.Equal(SceneImageReferenceFaceView.ProfileLeft, loaded.FaceView);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task FaceAsset_MissingFaceView_Throws()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            var face = FaceAsset(pack.Id, isApproved: false, consentState: SceneImageReferenceConsentState.Confirmed);
            face.FaceView = null;

            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AddAssetAsync(face));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Asset_QualityRating_RoundTrips()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            var face = FaceAsset(pack.Id, isApproved: false, consentState: SceneImageReferenceConsentState.Confirmed);

            await repo.AddAssetAsync(face);
            var loaded = Assert.Single(await repo.ListAssetsAsync(pack.Id));
            Assert.Equal(SceneImageReferenceQuality.NotRated, loaded.QualityRating);

            await repo.UpdateAssetQualityAsync(face.Id, SceneImageReferenceQuality.NotGood, "Low resolution 403×496.");
            var updated = Assert.Single(await repo.ListAssetsAsync(pack.Id));
            Assert.Equal(SceneImageReferenceQuality.NotGood, updated.QualityRating);
            Assert.Equal("Low resolution 403×496.", updated.QualityNotes);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Approve_RejectsMissingProvenance()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            var asset = FaceAsset(pack.Id, isApproved: true, consentState: SceneImageReferenceConsentState.Confirmed);
            asset.SourceLabel = string.Empty;
            await repo.AddAssetAsync(asset);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repo.ApproveAsync(pack.Id, "{\"hair\":\"dark\"}", asset.Id));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Approve_RejectsUnapprovedCanonicalFace()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            var asset = FaceAsset(pack.Id, isApproved: false, consentState: SceneImageReferenceConsentState.Confirmed);
            await repo.AddAssetAsync(asset);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repo.ApproveAsync(pack.Id, "{\"hair\":\"dark\"}", asset.Id));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Approve_Succeeds_AndFreezesPack()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            var face = FaceAsset(pack.Id, isApproved: true, consentState: SceneImageReferenceConsentState.Confirmed);
            await repo.AddAssetAsync(face);

            var approved = await repo.ApproveAsync(pack.Id, "{\"hair\":\"dark\"}", face.Id);

            Assert.Equal(CharacterImageIdentityPackStatus.Approved, approved.Status);
            Assert.Equal(face.Id, approved.CanonicalFaceAssetId);
            Assert.NotNull(approved.ApprovedUtc);

            // Frozen: a second upsert on the approved pack must fail.
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                Id = pack.Id,
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            }));

            var latest = await repo.GetLatestApprovedPackAsync("char-1");
            Assert.Equal(pack.Id, latest!.Id);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Supersede_CreatesNewDraftVersion_WithCopiedAssets()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            var face = FaceAsset(pack.Id, isApproved: true, consentState: SceneImageReferenceConsentState.Confirmed);
            await repo.AddAssetAsync(face);
            await repo.ApproveAsync(pack.Id, "{\"hair\":\"dark\"}", face.Id);

            var next = await repo.SupersedeAsync(pack.Id);

            Assert.Equal(2, next.Version);
            Assert.Equal(CharacterImageIdentityPackStatus.Draft, next.Status);
            Assert.Equal(pack.Id, next.SupersedesId);
            Assert.NotEqual(pack.Id, next.Id);

            var old = await repo.GetPackAsync(pack.Id);
            Assert.Equal(CharacterImageIdentityPackStatus.Superseded, old!.Status);

            var copiedAssets = await repo.ListAssetsAsync(next.Id);
            Assert.Single(copiedAssets);
            Assert.Equal(face.Sha256, copiedAssets[0].Sha256);
            Assert.Equal(face.FileRelativePath, copiedAssets[0].FileRelativePath);
            Assert.NotEqual(face.Id, copiedAssets[0].Id);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Supersede_CopiesFaceViewAndQualityRating()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            var face = FaceAsset(pack.Id, isApproved: true, consentState: SceneImageReferenceConsentState.Confirmed);
            face.FaceView = SceneImageReferenceFaceView.ThreeQuarterRight;
            face.QualityRating = SceneImageReferenceQuality.NotGood;
            await repo.AddAssetAsync(face);
            await repo.ApproveAsync(pack.Id, "{\"hair\":\"dark\"}", face.Id);

            var next = await repo.SupersedeAsync(pack.Id);

            var copied = Assert.Single(await repo.ListAssetsAsync(next.Id));
            Assert.Equal(SceneImageReferenceFaceView.ThreeQuarterRight, copied.FaceView);
            Assert.Equal(SceneImageReferenceQuality.NotGood, copied.QualityRating);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Delete_ApprovedPackBlocked()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            var face = FaceAsset(pack.Id, isApproved: true, consentState: SceneImageReferenceConsentState.Confirmed);
            await repo.AddAssetAsync(face);
            await repo.ApproveAsync(pack.Id, "{\"hair\":\"dark\"}", face.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeletePackAsync(pack.Id));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Delete_DraftPackSucceeds()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            await repo.AddAssetAsync(FaceAsset(pack.Id, isApproved: false, consentState: SceneImageReferenceConsentState.Unknown));

            await repo.DeletePackAsync(pack.Id);

            Assert.Null(await repo.GetPackAsync(pack.Id));
            Assert.Empty(await repo.ListAssetsAsync(pack.Id));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Delete_DraftThatSupersededApproved_RestoresParent()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var approved = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            var face = FaceAsset(approved.Id, isApproved: true, consentState: SceneImageReferenceConsentState.Confirmed);
            await repo.AddAssetAsync(face);
            await repo.ApproveAsync(approved.Id, "{\"hair\":\"dark\"}", face.Id);

            var draft = await repo.SupersedeAsync(approved.Id);
            Assert.Equal(CharacterImageIdentityPackStatus.Superseded, (await repo.GetPackAsync(approved.Id))!.Status);

            await repo.DeletePackAsync(draft.Id);

            // The draft is gone and its parent is Approved again, so the character still has a
            // usable pack and the studio's identity dropdown keeps showing it.
            Assert.Null(await repo.GetPackAsync(draft.Id));
            Assert.Empty(await repo.ListAssetsAsync(draft.Id));

            var parent = await repo.GetPackAsync(approved.Id);
            Assert.NotNull(parent);
            Assert.Equal(CharacterImageIdentityPackStatus.Approved, parent!.Status);

            var latest = await repo.GetLatestApprovedPackAsync("char-1");
            Assert.Equal(approved.Id, latest!.Id);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task SetAssetApproval_RequiresProvenanceAndConsent()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            var asset = FaceAsset(pack.Id, isApproved: false, consentState: SceneImageReferenceConsentState.Unknown);
            await repo.AddAssetAsync(asset);

            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.SetAssetApprovalAsync(asset.Id, true));

            await repo.UpdateAssetProvenanceAsync(asset.Id, "reference photo", SceneImageReferenceConsentState.Confirmed);
            await repo.SetAssetApprovalAsync(asset.Id, true);

            var loaded = await repo.GetAssetAsync(asset.Id);
            Assert.True(loaded!.IsApproved);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task DeleteAsset_OfApprovedPackBlocked()
    {
        var repo = await CreateRepoAsync(out var dbPath);
        try
        {
            var pack = await repo.UpsertDraftAsync(new CharacterImageIdentityPack
            {
                CharacterProfileId = "char-1",
                Version = 1,
                Status = CharacterImageIdentityPackStatus.Draft
            });
            var face = FaceAsset(pack.Id, isApproved: true, consentState: SceneImageReferenceConsentState.Confirmed);
            var body = FullBodyAsset(pack.Id);
            await repo.AddAssetAsync(face);
            await repo.AddAssetAsync(body);
            await repo.ApproveAsync(pack.Id, "{\"hair\":\"dark\"}", face.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeleteAssetAsync(face.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeleteAssetAsync(body.Id));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static SceneImageReferenceAsset FaceAsset(
        string packId,
        bool isApproved,
        SceneImageReferenceConsentState consentState) => new()
        {
            IdentityPackId = packId,
            AssetKind = SceneImageReferenceAssetKind.Face,
            FaceView = SceneImageReferenceFaceView.Front,
            FileRelativePath = $"identity/char-1/{Guid.NewGuid():N}.png",
            MediaType = "image/png",
            Width = 512,
            Height = 512,
            ByteLength = 1234,
            Sha256 = FaceSha,
            SourceLabel = "curated reference",
            ConsentState = consentState,
            IsApproved = isApproved
        };

    private static SceneImageReferenceAsset FullBodyAsset(string packId) => new()
    {
        IdentityPackId = packId,
        AssetKind = SceneImageReferenceAssetKind.FullBody,
        FileRelativePath = $"identity/char-1/{Guid.NewGuid():N}.png",
        MediaType = "image/png",
        Width = 1024,
        Height = 1536,
        ByteLength = 5678,
        Sha256 = BodySha,
        SourceLabel = "curated reference",
        ConsentState = SceneImageReferenceConsentState.NotApplicable,
        IsApproved = false
    };

    private static Task<CharacterImageIdentityRepository> CreateRepoAsync(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"character-identity-repo-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        return Task.FromResult(new CharacterImageIdentityRepository(options));
    }

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // A parallel Windows test host can retain a transient SQLite handle after disposal.
            }
        }
    }
}
