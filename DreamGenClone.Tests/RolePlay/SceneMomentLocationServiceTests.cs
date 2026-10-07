using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using DreamGenClone.Web.Application.RolePlay.ImageStep;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentLocationServiceTests
{
    [Fact]
    public async Task BindBackdrop_WritesBackdropAndMomentLink_KeyedOnMomentId()
    {
        var group = new SceneImageProductionGroup
        {
            Id = "group-1",
            MomentId = "moment-stable",
            MomentEnrichmentId = "enrichment-v1",
            MomentEnrichmentRevision = 1,
            Pov = "Dean"
        };
        var groups = new StubGroups(group);
        var links = new StubLinks();
        var assets = new StubAssets(new SceneAssetImage
        {
            Id = "img-a",
            AssetId = "loc-a",
            Sha256 = "ABC",
            ProductionVersion = 2,
            DisplayName = "Front",
            ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Approved
        }, new SceneAsset { Id = "loc-a", ScenarioLocationId = "scenario-loc-1" });

        var service = new SceneMomentLocationService(assets, groups, links);

        var backdrop = await service.BindBackdropAsync("group-1", "img-a");

        Assert.Equal("loc-a", backdrop.AssetId);
        Assert.Equal("img-a", backdrop.ImageId);
        Assert.Equal("Front", backdrop.Label);
        Assert.Equal("group-1", groups.LastBackdropGroupId);
        Assert.NotNull(groups.LastBackdropJson);
        Assert.Equal("moment-stable", links.LastLink!.MomentId);
        Assert.Equal("loc-a", links.LastLink.LocationAssetId);
        Assert.Equal("scenario-loc-1", links.LastLink.ScenarioLocationId);
    }

    [Fact]
    public async Task BindBackdrop_RefusesUnapprovedImage()
    {
        var group = new SceneImageProductionGroup { Id = "group-1", MomentId = "moment-1" };
        var groups = new StubGroups(group);
        var links = new StubLinks();
        var assets = new StubAssets(new SceneAssetImage
        {
            Id = "img-a",
            AssetId = "loc-a",
            Sha256 = "ABC",
            DisplayName = "Front",
            ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Draft
        }, new SceneAsset { Id = "loc-a" });

        var service = new SceneMomentLocationService(assets, groups, links);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.BindBackdropAsync("group-1", "img-a"));
        Assert.Contains("not approved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubGroups(SceneImageProductionGroup group) : ISceneImageProductionGroupRepository
    {
        public string? LastBackdropGroupId { get; private set; }
        public string? LastBackdropJson { get; private set; }

        public Task<SceneImageProductionGroup?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<SceneImageProductionGroup?>(id == group.Id ? group : null);

        public Task<SceneImageProductionGroup> SetLocationBackdropAsync(string groupId, string? locationBackdropJson, DateTime updatedUtc, CancellationToken cancellationToken = default)
        {
            LastBackdropGroupId = groupId;
            LastBackdropJson = locationBackdropJson;
            return Task.FromResult(group);
        }

        public Task<SceneImageAttemptRetentionPolicy?> GetRetentionPolicyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneImageAttemptRetentionPolicy> SaveRetentionPolicyAsync(SceneImageAttemptRetentionPolicy policy, long? expectedVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateAsync(SceneImageProductionGroup g, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneImageProductionGroup?> GetCurrentAsync(string momentEnrichmentId, string pov, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneImageProductionGroup> SetIdentityPolicyAsync(string groupId, SceneImageIdentityPolicy policy, string? reason, DateTime updatedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SceneImageProductionGroup>> ListByInteractionAsync(string sessionId, string interactionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApprovedSceneFrameDecision?> GetApprovalDecisionAsync(string decisionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ApprovedSceneFrameDecision>> ListApprovalDecisionsAsync(string groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApprovedSceneFrameDecision> ApproveAsync(string groupId, string imageId, string sha256, string decidedBy, string? note, DateTime decisionUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApprovedSceneFrameDecision> RevokeCurrentApprovalAsync(string groupId, string decidedBy, string? note, DateTime decisionUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubLinks : ISceneMomentLocationLinkRepository
    {
        public SceneMomentLocationLink? LastLink { get; private set; }

        public Task<SceneMomentLocationLink?> GetAsync(string momentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpsertAsync(SceneMomentLocationLink link, CancellationToken cancellationToken = default)
        {
            LastLink = link;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAssets(SceneAssetImage image, SceneAsset asset) : ISceneAssetService
    {
        public Task<SceneAssetImage?> GetImageAsync(string imageId, CancellationToken cancellationToken = default)
            => Task.FromResult<SceneAssetImage?>(imageId == image.Id ? image : null);

        public Task<SceneAsset?> GetAssetAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<SceneAsset?>(assetId == asset.Id ? asset : null);

        public Task<IReadOnlyList<SceneAssetImage>> ListImagesAsync(string assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAssetImage>>([image]);

        public Task<IReadOnlyList<SceneAsset>> ListAssetsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SceneAsset>>([asset]);

        public Task<SceneAsset> CreateAssetAsync(string name, SceneAssetType type, string? characterProfileId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneAssetImage> AddGeneratedImageAsync(string assetId, string prompt, string modelId, string imageSize, CancellationToken cancellationToken = default, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null, string? candidateBatchId = null, SceneAssetImageGenerationOptions? options = null) => throw new NotSupportedException();
        public Task<SceneAssetImage> AddUploadedImageAsync(string assetId, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null) => throw new NotSupportedException();
        public Task<SceneAssetImage> AddDerivedImageAsync(string assetId, string sourceImageId, MediaEditOperationKind operation, string fileName, Stream content, CancellationToken cancellationToken = default, string? candidateBatchId = null) => throw new NotSupportedException();
        public Task<SceneAssetImage> EnqueueImageEditAsync(string assetId, string sourceImageId, string editPrompt, string modelId, CancellationToken cancellationToken = default, string? candidateBatchId = null, IReadOnlyList<ReferenceApplicationSelection>? referenceApplications = null) => throw new NotSupportedException();
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
