using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class CharacterAppearanceVersionRepositoryTests
{
    private const string Sha256 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task ProductionAssetApproval_IsExplicitAndPersistsAllGovernance()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var asset = await fixture.CreateAssetAsync("body-1", SceneAssetType.CharacterBody);
            Assert.Null(asset.ProductionApprovalStatus);

            var approved = await fixture.Assets.ApproveForProductionAsync(
                asset.Id,
                "{\"source\":\"curator upload\"}",
                SceneAssetConsentState.Confirmed,
                SceneAssetLicenseState.Confirmed,
                "owned reference",
                SceneAssetApprovedUseScope.CharacterBody,
                "local-adult-production",
                "{\"families\":[\"sdxl\"]}");

            Assert.Equal(SceneAssetProductionApprovalStatus.Approved, approved.ProductionApprovalStatus);
            Assert.Equal(SceneAssetConsentState.Confirmed, approved.ConsentState);
            Assert.Equal(SceneAssetLicenseState.Confirmed, approved.LicenseState);
            Assert.Equal(SceneAssetApprovedUseScope.CharacterBody, approved.ApprovedUseScope);
            Assert.Equal(1, approved.ProductionVersion);
            Assert.NotNull(approved.ProductionApprovedUtc);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task ProductionAssetApproval_RejectsUnknownConsentAndMissingUseScope()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var asset = await fixture.CreateAssetAsync("body-1", SceneAssetType.CharacterBody);

            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Assets.ApproveForProductionAsync(
                asset.Id,
                "{\"source\":\"upload\"}",
                SceneAssetConsentState.Unknown,
                SceneAssetLicenseState.Confirmed,
                "owned reference",
                SceneAssetApprovedUseScope.CharacterBody,
                "policy",
                "{}"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Assets.ApproveForProductionAsync(
                asset.Id,
                "{\"source\":\"upload\"}",
                SceneAssetConsentState.Confirmed,
                SceneAssetLicenseState.Confirmed,
                "owned reference",
                0,
                "policy",
                "{}"));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task ApprovedProductionAsset_IsImmutableAndCannotBeDeleted()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var asset = await fixture.CreateApprovedAssetAsync(
                "body-1", SceneAssetType.CharacterBody, SceneAssetApprovedUseScope.CharacterBody);

            asset.Name = "changed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Assets.UpsertAsync(asset));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Assets.DeleteAsync(asset.Id));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task BodyProfileApproval_RequiresExactApprovedAssetUseScope()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var asset = await fixture.CreateApprovedAssetAsync(
                "body-1", SceneAssetType.CharacterBody, SceneAssetApprovedUseScope.CharacterWardrobe);
            var profile = await fixture.Appearance.CreateBodyProfileDraftAsync(BodyProfile());
            await fixture.Appearance.AddBodyAssetBindingAsync(BodyBinding(profile.Id, asset.Id));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Appearance.ApproveBodyProfileAsync(profile.Id, "{\"build\":\"athletic\"}"));

            Assert.Contains("CharacterBody use", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task BodyProfileApproval_FreezesBindings_AndSupersedeCopiesExactAsset()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var asset = await fixture.CreateApprovedAssetAsync(
                "body-1", SceneAssetType.CharacterBody, SceneAssetApprovedUseScope.CharacterBody);
            var profile = await fixture.Appearance.CreateBodyProfileDraftAsync(BodyProfile());
            await fixture.Appearance.AddBodyAssetBindingAsync(BodyBinding(profile.Id, asset.Id));

            var approved = await fixture.Appearance.ApproveBodyProfileAsync(profile.Id, "{\"build\":\"athletic\"}");
            Assert.Equal(CharacterAppearanceVersionStatus.Approved, approved.Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Appearance.AddBodyAssetBindingAsync(BodyBinding(profile.Id, asset.Id, 1, "detail")));

            var next = await fixture.Appearance.SupersedeBodyProfileAsync(profile.Id);
            var copied = Assert.Single(await fixture.Appearance.ListBodyAssetBindingsAsync(next.Id));
            Assert.Equal(2, next.Version);
            Assert.Equal(profile.Id, next.SupersedesId);
            Assert.Equal(asset.Id, copied.SceneAssetId);
            Assert.NotEqual(profile.Id, copied.BodyProfileVersionId);
            Assert.Equal(CharacterAppearanceVersionStatus.Superseded,
                (await fixture.Appearance.GetBodyProfileAsync(profile.Id))!.Status);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task WardrobeLookApprovalAndSupersession_PreserveCoverageAndBinding()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var asset = await fixture.CreateApprovedAssetAsync(
                "wardrobe-1", SceneAssetType.Wardrobe, SceneAssetApprovedUseScope.CharacterWardrobe);
            var look = await fixture.Appearance.CreateWardrobeLookDraftAsync(new CharacterWardrobeLookVersion
            {
                CharacterProfileId = "char-1",
                CoverageFactsJson = "{\"coverage\":[\"torso\"]}"
            });
            await fixture.Appearance.AddWardrobeAssetBindingAsync(new CharacterWardrobeAssetBinding
            {
                WardrobeLookVersionId = look.Id,
                SceneAssetId = asset.Id,
                SemanticRole = "primary outfit",
                Ordinal = 0,
                GarmentFactsJson = "{\"garment\":\"dress\"}",
                ColorFactsJson = "{\"primary\":\"red\"}",
                BodyCoverageJson = "{\"coverage\":[\"torso\"]}"
            });

            var approved = await fixture.Appearance.ApproveWardrobeLookAsync(
                look.Id,
                "{\"look\":\"evening\"}",
                "{\"coverage\":[\"torso\"]}");
            var next = await fixture.Appearance.SupersedeWardrobeLookAsync(approved.Id);

            Assert.Equal(CharacterAppearanceVersionStatus.Approved, approved.Status);
            Assert.Equal(2, next.Version);
            Assert.Equal(approved.CoverageFactsJson, next.CoverageFactsJson);
            Assert.Equal(asset.Id, Assert.Single(await fixture.Appearance.ListWardrobeAssetBindingsAsync(next.Id)).SceneAssetId);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task SharedAssetDeletion_IsBlockedWhileDraftBindingExists()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var asset = await fixture.CreateAssetAsync("body-1", SceneAssetType.CharacterBody);
            var profile = await fixture.Appearance.CreateBodyProfileDraftAsync(BodyProfile());
            await fixture.Appearance.AddBodyAssetBindingAsync(BodyBinding(profile.Id, asset.Id));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Assets.DeleteAsync(asset.Id));
            Assert.Contains("in use", exception.Message, StringComparison.OrdinalIgnoreCase);

            await fixture.Appearance.DeleteBodyProfileAsync(profile.Id);
            await fixture.Assets.DeleteAsync(asset.Id);
            Assert.Null(await fixture.Assets.GetAsync(asset.Id));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task TypedCatalog_ReturnsExactVersionedBodyAssetPickerBinding()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var asset = await fixture.CreateApprovedAssetAsync(
                "body-picker", SceneAssetType.CharacterBody, SceneAssetApprovedUseScope.CharacterBody);
            var profile = await fixture.Appearance.CreateBodyProfileDraftAsync(BodyProfile());
            await fixture.Appearance.AddBodyAssetBindingAsync(BodyBinding(profile.Id, asset.Id));
            profile = await fixture.Appearance.ApproveBodyProfileAsync(profile.Id, "{\"build\":\"athletic\"}");
            var service = new CharacterAssetCatalogService(fixture.Identity, fixture.Appearance, fixture.Assets);

            var versions = await service.LoadVersionsAsync("char-1");

            var version = Assert.Single(versions);
            var option = Assert.Single(version.Assets);
            Assert.Equal(CharacterAssetVersionKind.Body, version.Kind);
            Assert.Equal(profile.Id, option.VersionId);
            Assert.Equal(profile.Version, option.Version);
            Assert.Equal(asset.Id, option.SceneAssetId);
            Assert.Equal(asset.ProductionVersion, option.SceneAssetVersion);
            Assert.Equal(asset.Sha256, option.SceneAssetSha256);
            Assert.Equal("full body", option.SemanticRole);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static CharacterBodyProfileVersion BodyProfile() => new()
    {
        CharacterProfileId = "char-1",
        DescriptorSnapshotJson = "{}"
    };

    private static CharacterBodyAssetBinding BodyBinding(
        string versionId, string assetId, int ordinal = 0, string role = "full body") => new()
    {
        BodyProfileVersionId = versionId,
        SceneAssetId = assetId,
        SemanticRole = role,
        Ordinal = ordinal,
        CropFactsJson = "{\"crop\":\"full\"}",
        AngleFactsJson = "{\"view\":\"front\"}",
        BodyCoverageJson = "{\"coverage\":\"full\"}"
    };

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"appearance-repo-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False"
        });
        var assets = new SceneAssetRepository(options);
        await assets.GetAsync("__schema_probe__");
        return new Fixture(
            dbPath,
            assets,
            new CharacterAppearanceVersionRepository(options),
            new CharacterImageIdentityRepository(options));
    }

    private sealed class Fixture(
        string dbPath,
        SceneAssetRepository assets,
        CharacterAppearanceVersionRepository appearance,
        CharacterImageIdentityRepository identity) : IDisposable
    {
        public SceneAssetRepository Assets { get; } = assets;
        public CharacterAppearanceVersionRepository Appearance { get; } = appearance;
        public CharacterImageIdentityRepository Identity { get; } = identity;

        public async Task<SceneAsset> CreateAssetAsync(string id, SceneAssetType type)
        {
            var asset = new SceneAsset
            {
                Id = id,
                Name = id,
                Kind = SceneAssetKind.Uploaded,
                Status = SceneAssetStatus.Complete,
                Type = type,
                FileRelativePath = $"assets/{id}.png",
                MediaType = "image/png",
                Width = 1024,
                Height = 1024,
                ByteLength = 100,
                Sha256 = Sha256,
                CompletedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            await Assets.UpsertAsync(asset);
            return (await Assets.GetAsync(id))!;
        }

        public async Task<SceneAsset> CreateApprovedAssetAsync(
            string id, SceneAssetType type, SceneAssetApprovedUseScope useScope)
        {
            await CreateAssetAsync(id, type);
            return await Assets.ApproveForProductionAsync(
                id,
                "{\"source\":\"curator upload\"}",
                SceneAssetConsentState.Confirmed,
                SceneAssetLicenseState.Confirmed,
                "owned reference",
                useScope,
                "local-adult-production",
                "{\"families\":[\"sdxl\"]}");
        }

        public void Dispose()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(dbPath + suffix); } catch { }
            }
        }
    }
}