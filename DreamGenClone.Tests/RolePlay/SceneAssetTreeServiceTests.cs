using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneAssetTreeServiceTests
{
    [Fact]
    public async Task BuildTreeAsync_CharacterWithPacks_IsAStudioEntry()
    {
        var scenario = new Scenario
        {
            Name = "Test",
            Characters = [new Character { Id = "char-1", Name = "Dean", TemplateId = "tpl-dean" }]
        };
        var packs = new Dictionary<string, CharacterImageIdentityPack>
        {
            ["pack-v2"] = new() { Id = "pack-v2", CharacterTemplateId = "tpl-dean", Version = 2, Status = CharacterImageIdentityPackStatus.Draft },
            ["pack-v1"] = new() { Id = "pack-v1", CharacterTemplateId = "tpl-dean", Version = 1, Status = CharacterImageIdentityPackStatus.Approved }
        };

        var service = new SceneAssetTreeService(
            new StubScenarios([scenario]),
            new StubIdentityService(packs, new Dictionary<string, SceneImageReferenceAsset>()),
            new StubSceneAssets([]),
            new StubLocations([], []),
            new StubOwners([scenario]));

        var roots = await service.BuildTreeAsync();

        var root = Assert.Single(roots);
        Assert.Equal("Character", root.Owner.RootKind);
        Assert.Equal("Dean", root.Owner.OwnerName);
        // B-127: the root is the character TEMPLATE, not the scenario instance it was reached through.
        Assert.Equal("/characters/tpl-dean", root.Owner.Href);
        Assert.Empty(root.Groups);
    }

    [Fact]
    public async Task BuildTreeAsync_MergesScenarioInstancesOfOneCharacterTemplate()
    {
        var scenarioA = new Scenario
        {
            Name = "Campground",
            Characters = [new Character { Id = "dean-a", Name = "Dean", TemplateId = "tpl-dean" }]
        };
        var scenarioB = new Scenario
        {
            Name = "The Party",
            Characters = [new Character { Id = "dean-b", Name = "Dean", TemplateId = "tpl-dean" }]
        };
        var packs = new Dictionary<string, CharacterImageIdentityPack>
        {
            ["pack-a"] = new() { Id = "pack-a", CharacterTemplateId = "tpl-dean", Version = 1, Status = CharacterImageIdentityPackStatus.Draft }
        };

        var service = new SceneAssetTreeService(
            new StubScenarios([scenarioA, scenarioB]),
            new StubIdentityService(packs, new Dictionary<string, SceneImageReferenceAsset>()),
            new StubSceneAssets([]),
            new StubLocations([], []),
            new StubOwners([scenarioA, scenarioB]));

        var roots = await service.BuildTreeAsync();

        var root = Assert.Single(roots);
        Assert.Equal("Character", root.Owner.RootKind);
        Assert.Equal("Dean", root.Owner.OwnerName);
        Assert.Equal("/characters/tpl-dean", root.Owner.Href);
    }

    [Fact]
    public async Task BuildTreeAsync_TwoCharactersWithTheSameName_StayTwoRoots()
    {
        // The old behaviour merged by display name and linked to whichever id it saw first, so one character's
        // studio could be opened for the other's identity (B-127). Same name, different templates = two characters.
        var scenarioA = new Scenario
        {
            Name = "Campground",
            Characters = [new Character { Id = "dean-a", Name = "Dean", TemplateId = "tpl-dean" }]
        };
        var scenarioB = new Scenario
        {
            Name = "The Party",
            Characters = [new Character { Id = "dean-b", Name = "Dean", TemplateId = "tpl-other-dean" }]
        };
        var packs = new Dictionary<string, CharacterImageIdentityPack>
        {
            ["pack-a"] = new() { Id = "pack-a", CharacterTemplateId = "tpl-dean", Version = 1, Status = CharacterImageIdentityPackStatus.Draft },
            ["pack-b"] = new() { Id = "pack-b", CharacterTemplateId = "tpl-other-dean", Version = 1, Status = CharacterImageIdentityPackStatus.Draft }
        };

        var service = new SceneAssetTreeService(
            new StubScenarios([scenarioA, scenarioB]),
            new StubIdentityService(packs, new Dictionary<string, SceneImageReferenceAsset>()),
            new StubSceneAssets([]),
            new StubLocations([], []),
            new StubOwners([scenarioA, scenarioB]));

        var roots = await service.BuildTreeAsync();

        Assert.Equal(2, roots.Count(r => r.Owner.RootKind == "Character"));
        Assert.Contains(roots, r => r.Owner.Href == "/characters/tpl-dean");
        Assert.Contains(roots, r => r.Owner.Href == "/characters/tpl-other-dean");
    }

    [Fact]
    public async Task BuildTreeAsync_CreatesLocationRootFromProfileReferences()
    {
        var profiles = new List<ReferenceBootstrapLocationProfile>
        {
            new() { Id = "loc-1", Name = "Lakeside Cabin" }
        };
        var references = new List<ReferenceBootstrapLocationReference>
        {
            new() { Id = "ref-1", ProfileId = "loc-1", AssetId = "plate-1", OrderedIndex = 0 }
        };
        var libraryAssets = new List<SceneAsset>
        {
            new() { Id = "plate-1", Name = "Cabin plate", Type = SceneAssetType.Location, Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete }
        };

        var service = new SceneAssetTreeService(
            new StubScenarios([]),
            new StubIdentityService(new Dictionary<string, CharacterImageIdentityPack>(), new Dictionary<string, SceneImageReferenceAsset>()),
            new StubSceneAssets(libraryAssets),
            new StubLocations(profiles, references),
            new StubOwners([]));

        var roots = await service.BuildTreeAsync();

        var location = Assert.Single(roots, r => r.Owner.RootKind == "Location");
        Assert.Equal("Lakeside Cabin", location.Owner.OwnerName);
        Assert.Equal("/locations/loc-1", location.Owner.Href);
        var item = Assert.Single(Assert.Single(location.Groups).Items);
        Assert.Equal("plate-1", item.AssetId);
        Assert.Equal("Library", item.SourceStore);

        // The referenced plate must not also appear under the cleanup bucket.
        Assert.DoesNotContain(roots, r => r.Owner.RootKind == "Cleanup"
            && r.Groups.SelectMany(g => g.Items).Any(i => i.AssetId == "plate-1"));
    }

    [Fact]
    public async Task BuildTreeAsync_LocationAsset_IsAStudioEntry()
    {
        var container = new SceneAsset
        {
            Id = "loc-container",
            Name = "Trailer",
            Type = SceneAssetType.Location,
            IsContainerOnly = true,
            Kind = SceneAssetKind.Uploaded,
            Status = SceneAssetStatus.Complete
        };

        var service = new SceneAssetTreeService(
            new StubScenarios([]),
            new StubIdentityService(new Dictionary<string, CharacterImageIdentityPack>(), new Dictionary<string, SceneImageReferenceAsset>()),
            new StubSceneAssets([container]),
            new StubLocations([], []),
            new StubOwners([]));

        var roots = await service.BuildTreeAsync();

        var location = Assert.Single(roots, r => r.Owner.RootKind == "Location");
        Assert.Equal("Trailer", location.Owner.OwnerName);
        Assert.Equal("/locations/loc-container", location.Owner.Href);
        Assert.Empty(location.Groups);
    }

    [Fact]
    public async Task BuildTreeAsync_UnassignedAssets_GoToCleanupBucket()
    {
        var stray = new SceneAsset
        {
            Id = "stray-face",
            Name = "Old face",
            Type = SceneAssetType.CharacterFace,
            Kind = SceneAssetKind.Uploaded,
            Status = SceneAssetStatus.Complete
        };

        var service = new SceneAssetTreeService(
            new StubScenarios([]),
            new StubIdentityService(new Dictionary<string, CharacterImageIdentityPack>(), new Dictionary<string, SceneImageReferenceAsset>()),
            new StubSceneAssets([stray]),
            new StubLocations([], []),
            new StubOwners([]));

        var roots = await service.BuildTreeAsync();

        var cleanup = Assert.Single(roots, r => r.Owner.RootKind == "Cleanup");
        Assert.Equal("Cleanup", cleanup.Owner.OwnerName);
        Assert.Null(cleanup.Owner.Href);
        var group = Assert.Single(cleanup.Groups);
        Assert.Equal("CharacterFace", group.Label);
        Assert.Contains(group.Items, i => i.AssetId == "stray-face");
    }

    /// <summary>
    /// Resolves each scenario character through its <c>TemplateId</c> — the payload reference the running resolver
    /// reads — so the tree tests exercise the real rule (owner = template), not a name.
    /// </summary>
    private sealed class StubOwners(IReadOnlyList<Scenario> scenarios) : ICharacterIdentityOwnerResolver
    {
        public Task<CharacterIdentityOwner> ResolveAsync(string ownerId, CancellationToken cancellationToken = default)
        {
            foreach (var scenario in scenarios)
            {
                foreach (var character in scenario.Characters)
                {
                    if (string.Equals(character.Id, ownerId, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(character.TemplateId))
                    {
                        var name = string.IsNullOrWhiteSpace(character.Name) ? character.Id : character.Name!;
                        return Task.FromResult(new CharacterIdentityOwner(
                            CharacterIdentityOwnerKind.ScenarioCharacter,
                            character.TemplateId!,
                            name,
                            character.Id,
                            name));
                    }
                }
            }

            throw new InvalidOperationException($"'{ownerId}' has no character template.");
        }

        public Task<CharacterIdentityOwnerKind?> IdentifyAsync(
            string ownerId, CancellationToken cancellationToken = default)
            => Task.FromResult<CharacterIdentityOwnerKind?>(CharacterIdentityOwnerKind.ScenarioCharacter);

        public Task<IReadOnlyList<CharacterIdentityOwner>> ListInstancesAsync(
            string characterTemplateId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CharacterIdentityCandidate>> ListUnlinkedAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubScenarios(IReadOnlyList<Scenario> scenarios) : IScenarioService
    {
        public Task<List<Scenario>> GetAllScenariosAsync() => Task.FromResult(scenarios.ToList());
        public Task<Scenario> CreateScenarioAsync(string name, string? description = null) => throw new NotSupportedException();
        public Task<Scenario?> GetScenarioAsync(string id) => throw new NotSupportedException();
        public Task<Scenario> SaveScenarioAsync(Scenario scenario) => throw new NotSupportedException();
        public Task<bool> DeleteScenarioAsync(string id) => throw new NotSupportedException();
        public Task<Scenario> CloneScenarioAsync(string id, string newName) => throw new NotSupportedException();
    }

    private sealed class StubIdentityService(
        IReadOnlyDictionary<string, CharacterImageIdentityPack> packs,
        IReadOnlyDictionary<string, SceneImageReferenceAsset> assets) : ICharacterImageIdentityService
    {
        public Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(string characterProfileId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CharacterImageIdentityPack>>(
                packs.Values.Where(p => p.CharacterTemplateId == characterProfileId).OrderByDescending(p => p.Version).ToList());
        public Task<IReadOnlyList<SceneImageReferenceAsset>> ListAssetsAsync(string packId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneImageReferenceAsset>>(assets.Values.Where(a => a.IdentityPackId == packId).ToList());
        public Task<CharacterImageIdentityPack?> GetPackAsync(string packId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterImageIdentityPack> CreateDraftPackAsync(string characterProfileId, CharacterImageIdentityPackScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterImageIdentityPack> SetDraftPackScopeAsync(string packId, CharacterImageIdentityPackScope scope, string? canonicalFullBodyAssetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterImageIdentityPack> ApprovePackAsync(string packId, string descriptorSnapshotJson, string canonicalFaceAssetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CharacterImageIdentityPack> SupersedePackAsync(string packId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePackAsync(string packId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneImageReferenceAsset> UploadAssetAsync(string packId, SceneImageReferenceAssetKind kind, string fileName, Stream content, SceneImageReferenceFaceView? faceView = null, SceneImageReferenceBodyView? bodyView = null, SceneImageReferenceBodyState? bodyState = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetAssetProvenanceAsync(string assetId, string sourceLabel, SceneImageReferenceConsentState consentState, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetAssetApprovalAsync(string assetId, bool isApproved, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetAssetQualityAsync(string assetId, SceneImageReferenceQuality quality, string qualityNotes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneImageReferenceAsset> AnalyzeAssetQualityAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubSceneAssets(
        IReadOnlyList<SceneAsset> assets,
        IReadOnlyDictionary<string, IReadOnlyList<SceneAssetImage>>? images = null) : ISceneAssetService
    {
        public Task<IReadOnlyList<SceneAsset>> ListAssetsAsync(CancellationToken cancellationToken = default) => Task.FromResult(assets);
        public Task<SceneAsset> CreateAssetAsync(string name, SceneAssetType type, string? characterProfileId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAssetImage> AddGeneratedImageAsync(string assetId, string prompt, string modelId, string imageSize, CancellationToken cancellationToken = default, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null, string? candidateBatchId = null, SceneAssetImageGenerationOptions? options = null) => throw new NotSupportedException();
        public Task<SceneAssetImage> AddUploadedImageAsync(string assetId, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null) => throw new NotSupportedException();

        public Task<SceneAssetImage> AddDerivedImageAsync(string assetId, string sourceImageId, MediaEditOperationKind operation, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null) => throw new NotSupportedException();
        public Task<SceneAssetImage> EnqueueImageEditAsync(string assetId, string sourceImageId, string editPrompt, string modelId, CancellationToken cancellationToken = default, string? candidateBatchId = null, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAssetImage>>(
                images is not null && images.TryGetValue(assetId, out var list) ? list : []);
        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAssetImage>> ListImagesByCandidateBatchAsync(string candidateBatchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetImageCandidateDecisionAsync(string imageId, SceneAssetCandidateDecision decision, string? notes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetImageValidationResultAsync(string imageId, string? validationResultJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetImagePipelineStepsAsync(string imageId, string? pipelineStepsJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAssetImage> ApproveImageForProductionAsync(string imageId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(SceneAsset Asset, SceneAssetImage Image, Stream Stream)> OpenImageForDownloadAsync(string imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> CreateFromPromptAsync(string name, string prompt, SceneAssetType type, string modelId, string imageSize, string? candidateBatchId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> CreateFromUploadAsync(string name, SceneAssetType type, string fileName, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> EnqueueEditAsync(string sourceAssetId, string name, string editPrompt, string modelId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EnqueueProfilePackAsync(SceneAssetProfilePackJobPayload payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAsset>> ListAssetsByPackAsync(string identityPackId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> ApproveForProductionAsync(string assetId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(SceneAsset Asset, Stream Stream)> OpenForDownloadAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubLocations(
        IReadOnlyList<ReferenceBootstrapLocationProfile> profiles,
        IReadOnlyList<ReferenceBootstrapLocationReference> references) : IReferenceBootstrapRepository
    {
        public Task<ReferenceBootstrapLocationProfile?> GetLocationProfileAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(profiles.FirstOrDefault(p => p.Id == id));
        public Task<IReadOnlyList<ReferenceBootstrapLocationProfile>> ListLocationProfilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(profiles);
        public Task<IReadOnlyList<ReferenceBootstrapLocationReference>> ListLocationReferencesAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReferenceBootstrapLocationReference>>(references.Where(r => r.ProfileId == profileId).ToList());
        public Task<ReferenceBootstrapBatch?> GetBatchAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReferenceBootstrapBatch>> ListBatchesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertBatchAsync(ReferenceBootstrapBatch batch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteBatchAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertLocationProfileAsync(ReferenceBootstrapLocationProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteLocationProfileAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReferenceBootstrapLocationReference?> GetLocationReferenceAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertLocationReferenceAsync(ReferenceBootstrapLocationReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteLocationReferenceAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
