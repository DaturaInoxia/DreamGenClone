using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ScenarioLocationContainerServiceTests
{
    [Fact]
    public async Task EnsureContainers_CreatesTheWorldAndOneChildPerLocation()
    {
        var scenario = new Scenario
        {
            Id = "scenario-1",
            Name = "Campground Intimacy",
            Setting = new Setting
            {
                WorldDescription = "A trailer park in mid-summer",
                WorldLocation = new WorldLocationSetting { Name = "Camp Ground" }
            },
            Locations =
            [
                new Location { Id = "loc-1", Name = "Husband and Wife Trailer" },
                new Location { Id = "loc-2", Name = "Bathrooms" }
            ]
        };
        var assets = new RecordingAssetService();

        var result = await new ScenarioLocationContainerService(assets).EnsureContainersAsync(scenario);

        Assert.True(result.WorldContainerCreated);
        Assert.Equal(2, result.LocationContainersCreated);
        Assert.Equal("Camp Ground", assets.Created[0].Name);
        Assert.Null(assets.Created[0].ParentAssetId);
        Assert.Equal(result.WorldContainerId, scenario.Setting.WorldLocation!.AssetContainerId);

        var trailer = assets.Created.Single(a => a.Name == "Husband and Wife Trailer");
        Assert.Equal(result.WorldContainerId, trailer.ParentAssetId);
        Assert.Equal("scenario-1", trailer.ScenarioId);
        Assert.Equal("loc-1", trailer.ScenarioLocationId);
    }

    [Fact]
    public async Task EnsureContainers_RefusesABlankWorldName()
    {
        var scenario = new Scenario
        {
            Id = "scenario-1",
            Name = "Unnamed World",
            Setting = new Setting { WorldLocation = new WorldLocationSetting() }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ScenarioLocationContainerService(new RecordingAssetService()).EnsureContainersAsync(scenario));
        Assert.Contains("world container name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureContainers_ReusesAnExistingMappedContainerAndCorrectsItsParent()
    {
        var scenario = new Scenario
        {
            Id = "scenario-1",
            Name = "Campground Intimacy",
            Setting = new Setting
            {
                WorldLocation = new WorldLocationSetting { Name = "Camp Ground", AssetContainerId = "world-1" }
            },
            Locations = [new Location { Id = "loc-1", Name = "Husband and Wife Trailer" }]
        };
        var assets = new RecordingAssetService
        {
            Existing =
            [
                new SceneAsset { Id = "world-1", Name = "Camp Ground", Type = SceneAssetType.Location },
                new SceneAsset
                {
                    Id = "trailer-1", Name = "stale name", Type = SceneAssetType.Location,
                    ScenarioId = "scenario-1", ScenarioLocationId = "loc-1"
                }
            ]
        };

        var result = await new ScenarioLocationContainerService(assets).EnsureContainersAsync(scenario);

        Assert.False(result.WorldContainerCreated);
        Assert.Equal(0, result.LocationContainersCreated);
        Assert.Equal(1, result.LocationContainersReused);
        var update = Assert.Single(assets.Updated);
        Assert.Equal("trailer-1", update.AssetId);
        Assert.Equal("Husband and Wife Trailer", update.Name);
        Assert.Equal("world-1", update.ParentAssetId);
    }

    private sealed class RecordingAssetService : ISceneAssetService
    {
        public List<SceneAsset> Created { get; } = [];
        public List<(string AssetId, string Name, string? ParentAssetId)> Updated { get; } = [];
        public List<SceneAsset> Existing { get; init; } = [];

        public Task<IReadOnlyList<SceneAsset>> ListAssetsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAsset>>(Existing.Concat(Created).ToList());

        public Task<SceneAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult(Existing.Concat(Created).FirstOrDefault(a => a.Id == assetId));

        public Task<SceneAsset> CreateLocationContainerAsync(string name, string description, string? scenarioId, string? scenarioLocationId, string? parentAssetId, CancellationToken cancellationToken = default)
        {
            var asset = new SceneAsset
            {
                Id = $"created-{Created.Count + 1}",
                Name = name,
                Type = SceneAssetType.Location,
                IsContainerOnly = true,
                Prompt = description,
                ScenarioId = scenarioId,
                ScenarioLocationId = scenarioLocationId,
                ParentAssetId = parentAssetId
            };
            Created.Add(asset);
            return Task.FromResult(asset);
        }

        public Task<SceneAsset> UpdateLocationContainerAsync(string assetId, string name, string? description, string? parentAssetId, CancellationToken cancellationToken = default)
        {
            Updated.Add((assetId, name, parentAssetId));
            var asset = Existing.First(a => a.Id == assetId);
            asset.Name = name;
            asset.ParentAssetId = parentAssetId;
            return Task.FromResult(asset);
        }

        public Task<SceneAsset> CreateAssetAsync(string name, SceneAssetType type, string? characterProfileId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAssetImage> AddGeneratedImageAsync(string assetId, string prompt, string modelId, string imageSize, CancellationToken cancellationToken = default, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null, string? candidateBatchId = null, SceneAssetImageGenerationOptions? options = null) => throw new NotSupportedException();
        public Task<SceneAssetImage> AddUploadedImageAsync(string assetId, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null) => throw new NotSupportedException();
        public Task<SceneAssetImage> AddDerivedImageAsync(string assetId, string sourceImageId, DreamGenClone.Web.Application.RolePlay.Editing.MediaEditOperationKind operation, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null) => throw new NotSupportedException();
        public Task<SceneAssetImage> EnqueueImageEditAsync(string assetId, string sourceImageId, string editPrompt, string modelId, CancellationToken cancellationToken = default, string? candidateBatchId = null, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public Task<IReadOnlyList<SceneAsset>> ListAssetsByPackAsync(string identityPackId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> ApproveForProductionAsync(string assetId, string sourceProvenanceJson, SceneAssetConsentState consentState, SceneAssetLicenseState licenseState, string licenseLabel, SceneAssetApprovedUseScope approvedUseScope, string contentPolicyKey, string compatibilityMetadataJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(SceneAsset Asset, Stream Stream)> OpenForDownloadAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAsset> RenameAssetAsync(string assetId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAssetAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
