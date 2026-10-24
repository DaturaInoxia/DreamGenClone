using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using System.Text.Json;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentLocationServiceTests
{
    [Fact]
    public async Task BindBackdrop_WritesBackdropAndThePerPovLink_KeyedOnMomentAndPov()
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
        var queue = new StubAnalysisQueue();
        var assets = new StubAssets(new SceneAssetImage
        {
            Id = "img-a",
            AssetId = "loc-a",
            Sha256 = "ABC",
            ProductionVersion = 2,
            DisplayName = "Front",
            ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Approved
        }, new SceneAsset { Id = "loc-a", ScenarioLocationId = "scenario-loc-1" });

        var service = new SceneMomentLocationService(assets, groups, links, queue);

        var backdrop = await service.BindBackdropAsync("group-1", "img-a");

        Assert.Equal("loc-a", backdrop.AssetId);
        Assert.Equal("img-a", backdrop.ImageId);
        Assert.Equal("Front", backdrop.Label);
        Assert.Equal("group-1", groups.LastBackdropGroupId);
        Assert.NotNull(groups.LastBackdropJson);
        Assert.Equal("moment-stable", links.LastLink!.MomentId);
        // The POV is half the key: Omniscient and each character keep their own location binding for the same moment.
        Assert.Equal("Dean", links.LastLink.Pov);
        Assert.Equal("loc-a", links.LastLink.LocationAssetId);
        Assert.Equal("scenario-loc-1", links.LastLink.ScenarioLocationId);
        // The durable half carries the image too, so a re-enrichment can restore it.
        Assert.Equal("Front", ReadBackdrop(links.LastLink.BackdropJson!).Label);

        // B-160: binding a location is what makes its contents relevant, so the reading is requested right here —
        // queued, not awaited, so a vision call never blocks the operator's own action.
        Assert.Equal("img-a", queue.LastRequestedImageId);
        Assert.False(queue.LastRequestWasForced);
    }

    /// <summary>
    /// A bind that is REFUSED must not queue a reading: a location image that cannot be bound is not a location the
    /// prompt will use, and reading it would spend a vision call on nothing.
    /// </summary>
    [Fact]
    public async Task BindBackdrop_ThatIsRefused_RequestsNoReading()
    {
        var group = new SceneImageProductionGroup { Id = "group-1", MomentId = "moment-1" };
        var groups = new StubGroups(group);
        var links = new StubLinks();
        var queue = new StubAnalysisQueue();
        var assets = new StubAssets(new SceneAssetImage
        {
            Id = "img-a",
            AssetId = "loc-a",
            Sha256 = "ABC",
            DisplayName = "Front",
            ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Draft
        }, new SceneAsset { Id = "loc-a" });

        var service = new SceneMomentLocationService(assets, groups, links, queue);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BindBackdropAsync("group-1", "img-a"));

        Assert.Null(queue.LastRequestedImageId);
    }

    [Fact]
    public async Task BindBackdrop_RefusesUnapprovedImage()
    {
        var group = new SceneImageProductionGroup { Id = "group-1", MomentId = "moment-1" };
        var groups = new StubGroups(group);
        var links = new StubLinks();
        var queue = new StubAnalysisQueue();
        var assets = new StubAssets(new SceneAssetImage
        {
            Id = "img-a",
            AssetId = "loc-a",
            Sha256 = "ABC",
            DisplayName = "Front",
            ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Draft
        }, new SceneAsset { Id = "loc-a" });

        var service = new SceneMomentLocationService(assets, groups, links, queue);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.BindBackdropAsync("group-1", "img-a"));
        Assert.Contains("not approved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A re-enrichment creates a NEW production group (the group is keyed on the enrichment), and the (moment, POV)
    /// link is what carries the operator's binding across it. This is the ONE thing that reads that record.
    /// </summary>
    [Fact]
    public async Task RestoreBackdrop_SeedsAFreshGroupFromThePovsRecordedBinding()
    {
        var group = new SceneImageProductionGroup
        {
            Id = "group-new",
            MomentId = "moment-stable",
            MomentEnrichmentId = "enrichment-v2",
            MomentEnrichmentRevision = 2,
            Pov = "Dean"
        };
        var groups = new StubGroups(group);
        var service = new SceneMomentLocationService(
            new StubAssets(ApprovedImage(), new SceneAsset { Id = "loc-a" }),
            groups,
            new StubLinks(new SceneMomentLocationLink
            {
                MomentId = "moment-stable",
                Pov = "Dean",
                LocationAssetId = "loc-a",
                Origin = "operator-bound",
                BackdropJson = RecordedBackdropJson()
            }),
            new StubAnalysisQueue());

        var restored = await service.RestoreBackdropAsync("group-new");

        Assert.Equal("group-new", groups.LastBackdropGroupId);
        Assert.NotNull(restored.LocationBackdropJson);
        Assert.Equal("Front", ReadBackdrop(restored.LocationBackdropJson!).Label);
    }

    /// <summary>This enrichment's group already carries the binding, so the durable record must not overrule it.</summary>
    [Fact]
    public async Task RestoreBackdrop_LeavesAGroupThatAlreadyCarriesItsOwnBackdrop()
    {
        var group = new SceneImageProductionGroup
        {
            Id = "group-1",
            MomentId = "moment-stable",
            Pov = "Dean",
            LocationBackdropJson = """{"assetId":"loc-a","imageId":"img-a","sha256":"ABC","productionVersion":2,"label":"Bound here"}"""
        };
        var groups = new StubGroups(group);
        var service = new SceneMomentLocationService(
            new StubAssets(ApprovedImage(), new SceneAsset { Id = "loc-a" }),
            groups,
            new StubLinks(new SceneMomentLocationLink
            {
                MomentId = "moment-stable",
                Pov = "Dean",
                LocationAssetId = "loc-a",
                Origin = "operator-bound",
                BackdropJson = RecordedBackdropJson()
            }),
            new StubAnalysisQueue());

        await service.RestoreBackdropAsync("group-1");

        Assert.Null(groups.LastBackdropGroupId);
    }

    /// <summary>A POV that never bound anything restores nothing — not a default, not another POV's image.</summary>
    [Fact]
    public async Task RestoreBackdrop_WithNoRecordedBinding_WritesNothing()
    {
        var group = new SceneImageProductionGroup { Id = "group-1", MomentId = "moment-stable", Pov = "Dean" };
        var groups = new StubGroups(group);
        var service = new SceneMomentLocationService(
            new StubAssets(ApprovedImage(), new SceneAsset { Id = "loc-a" }),
            groups,
            new StubLinks(),
            new StubAnalysisQueue());

        var restored = await service.RestoreBackdropAsync("group-1");

        Assert.Null(groups.LastBackdropGroupId);
        Assert.Null(restored.LocationBackdropJson);
    }

    /// <summary>
    /// A binding recorded before its image was revoked does not come back to life as an unapprovable backdrop: it fails
    /// loudly, naming the image, instead of being silently dropped.
    /// </summary>
    [Fact]
    public async Task RestoreBackdrop_RefusesARecordedImageThatIsNoLongerBindable()
    {
        var group = new SceneImageProductionGroup { Id = "group-new", MomentId = "moment-stable", Pov = "Dean" };
        var groups = new StubGroups(group);
        var revoked = ApprovedImage();
        revoked.ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Revoked;
        var service = new SceneMomentLocationService(
            new StubAssets(revoked, new SceneAsset { Id = "loc-a" }),
            groups,
            new StubLinks(new SceneMomentLocationLink
            {
                MomentId = "moment-stable",
                Pov = "Dean",
                LocationAssetId = "loc-a",
                Origin = "operator-bound",
                BackdropJson = RecordedBackdropJson()
            }),
            new StubAnalysisQueue());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RestoreBackdropAsync("group-new"));

        Assert.Contains("not approved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(groups.LastBackdropGroupId);
    }

    /// <summary>Two halves of one binding that name different containers are a contradiction, not a preference.</summary>
    [Fact]
    public async Task RestoreBackdrop_RefusesWhenTheRecordedContainerDisagreesWithTheLink()
    {
        var group = new SceneImageProductionGroup { Id = "group-new", MomentId = "moment-stable", Pov = "Dean" };
        var groups = new StubGroups(group);
        var service = new SceneMomentLocationService(
            new StubAssets(ApprovedImage(), new SceneAsset { Id = "loc-a" }),
            groups,
            new StubLinks(new SceneMomentLocationLink
            {
                MomentId = "moment-stable",
                Pov = "Dean",
                LocationAssetId = "loc-other",
                Origin = "operator-bound",
                BackdropJson = RecordedBackdropJson()
            }),
            new StubAnalysisQueue());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RestoreBackdropAsync("group-new"));

        Assert.Contains("disagree", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "Skip — text-only location" must clear the durable record too, or the next enrichment would restore the very
    /// binding the operator just cleared.
    /// </summary>
    [Fact]
    public async Task ClearBackdrop_ClearsTheRecordedImageOnThePovsLink()
    {
        var group = new SceneImageProductionGroup
        {
            Id = "group-1",
            MomentId = "moment-stable",
            Pov = "Dean",
            LocationBackdropJson = RecordedBackdropJson()
        };
        var groups = new StubGroups(group);
        var links = new StubLinks(new SceneMomentLocationLink
        {
            MomentId = "moment-stable",
            Pov = "Dean",
            LocationAssetId = "loc-a",
            Origin = "operator-bound",
            BackdropJson = RecordedBackdropJson()
        });
        var service = new SceneMomentLocationService(
            new StubAssets(ApprovedImage(), new SceneAsset { Id = "loc-a" }),
            groups,
            links,
            new StubAnalysisQueue());

        await service.ClearBackdropAsync("group-1");

        Assert.Null(groups.LastBackdropJson);
        Assert.NotNull(links.LastLink);
        Assert.Null(links.LastLink!.BackdropJson);
        // The place is unchanged: clearing the image is not un-resolving the location.
        Assert.Equal("loc-a", links.LastLink.LocationAssetId);
    }

    /// <summary>The naming half of the bindable rule, which only a location image carries.</summary>
    [Fact]
    public async Task BindBackdrop_RefusesAnUnnamedImage()
    {
        var group = new SceneImageProductionGroup { Id = "group-1", MomentId = "moment-1", Pov = "Dean" };
        var unnamed = ApprovedImage();
        unnamed.DisplayName = null;
        var service = new SceneMomentLocationService(
            new StubAssets(unnamed, new SceneAsset { Id = "loc-a" }),
            new StubGroups(group),
            new StubLinks(),
            new StubAnalysisQueue());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.BindBackdropAsync("group-1", "img-a"));

        Assert.Contains("no name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SceneAssetImage ApprovedImage() => new()
    {
        Id = "img-a",
        AssetId = "loc-a",
        Sha256 = "ABC",
        ProductionVersion = 2,
        DisplayName = "Front",
        ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Approved
    };

    private static string RecordedBackdropJson() => """
        {"assetId":"loc-a","imageId":"img-a","sha256":"ABC","productionVersion":2,"label":"Front"}
        """;

    /// <summary>
    /// Read with the SAME options the service writes with, because the stored JSON is the web-cased shape
    /// (<c>assetId</c>, <c>label</c>) rather than the record's property names.
    /// </summary>
    private static SceneImageLocationBackdrop ReadBackdrop(string json)
        => JsonSerializer.Deserialize<SceneImageLocationBackdrop>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    /// <summary>Records the reading a bind asked for, so "was it requested, and was it a re-read?" is assertable.</summary>
    private sealed class StubAnalysisQueue : ILocationImageAnalysisQueue
    {
        public string? LastRequestedImageId { get; private set; }
        public bool LastRequestWasForced { get; private set; }

        public Task<bool> RequestAsync(string imageId, bool force = false, CancellationToken cancellationToken = default)
        {
            LastRequestedImageId = imageId;
            LastRequestWasForced = force;
            return Task.FromResult(true);
        }
    }

    private sealed class StubGroups(SceneImageProductionGroup group) : ISceneImageProductionGroupRepository    {
        public string? LastBackdropGroupId { get; private set; }
        public string? LastBackdropJson { get; private set; }

        public Task<SceneImageProductionGroup?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<SceneImageProductionGroup?>(id == group.Id ? group : null);

        public Task<SceneImageProductionGroup> SetLocationBackdropAsync(string groupId, string? locationBackdropJson, DateTime updatedUtc, CancellationToken cancellationToken = default)
        {
            LastBackdropGroupId = groupId;
            LastBackdropJson = locationBackdropJson;
            group.LocationBackdropJson = locationBackdropJson;
            return Task.FromResult(group);
        }

        public Task<SceneImageProductionGroup> SetPromptDraftAsync(string groupId, string? promptDraftJson, DateTime updatedUtc, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

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

    private sealed class StubLinks(SceneMomentLocationLink? existing = null) : ISceneMomentLocationLinkRepository
    {
        private SceneMomentLocationLink? _current = existing;

        public SceneMomentLocationLink? LastLink { get; private set; }

        public Task<SceneMomentLocationLink?> GetAsync(string momentId, string pov, CancellationToken cancellationToken = default)
            => Task.FromResult(_current is not null
                && string.Equals(_current.MomentId, momentId, StringComparison.Ordinal)
                && string.Equals(_current.Pov, pov, StringComparison.Ordinal)
                    ? _current
                    : null);

        public Task UpsertAsync(SceneMomentLocationLink link, CancellationToken cancellationToken = default)
        {
            LastLink = link;
            _current = link;
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
