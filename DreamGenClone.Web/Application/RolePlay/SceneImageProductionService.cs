using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneImageProductionService : ISceneImageProductionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneImageProductionGroupRepository _productionRepository;
    private readonly ISceneImageRepository _imageRepository;
    private readonly ISceneAssetRepository _assetRepository;
    private readonly ISceneImageStorageService _storage;
    private readonly TimeProvider _timeProvider;
    private readonly ISceneBeatProductionPlanRepository? _plans;
    private readonly ISceneMomentSetRepository? _momentSets;
    private readonly ISceneMomentEnrichmentRepository? _enrichments;
    private readonly ICompiledMediaBriefRepository? _briefs;
    private readonly IMultimodalMediaCompilationService? _compilationService;
    private readonly ILogger<SceneImageProductionService> _logger;

    public SceneImageProductionService(
        ISceneImageProductionGroupRepository productionRepository,
        ISceneImageRepository imageRepository,
        ISceneAssetRepository assetRepository,
        ISceneImageStorageService storage,
        TimeProvider timeProvider,
        ILogger<SceneImageProductionService> logger)
        : this(productionRepository, imageRepository, assetRepository, storage, timeProvider,
            null, null, null, null, null, logger)
    {
    }

    public SceneImageProductionService(
        ISceneImageProductionGroupRepository productionRepository,
        ISceneImageRepository imageRepository,
        ISceneAssetRepository assetRepository,
        ISceneImageStorageService storage,
        TimeProvider timeProvider,
        ISceneBeatProductionPlanRepository plans,
        ISceneMomentSetRepository momentSets,
        ISceneMomentEnrichmentRepository enrichments,
        ICompiledMediaBriefRepository briefs,
        IMultimodalMediaCompilationService compilationService,
        ILogger<SceneImageProductionService> logger)
    {
        _productionRepository = productionRepository;
        _imageRepository = imageRepository;
        _assetRepository = assetRepository;
        _storage = storage;
        _timeProvider = timeProvider;
        _plans = plans;
        _momentSets = momentSets;
        _enrichments = enrichments;
        _briefs = briefs;
        _compilationService = compilationService;
        _logger = logger;
    }

    public async Task<CompiledMediaBrief> GetOrCreateStillBriefAsync(
        string productionGroupId,
        CancellationToken cancellationToken = default)
    {
        Require(productionGroupId, "Production group id");
        var plans = _plans ?? throw new InvalidOperationException("Still compilation requires the Beat Production Plan repository.");
        var momentSets = _momentSets ?? throw new InvalidOperationException("Still compilation requires the Moment Set repository.");
        var enrichments = _enrichments ?? throw new InvalidOperationException("Still compilation requires the Moment Enrichment repository.");
        var briefs = _briefs ?? throw new InvalidOperationException("Still compilation requires the compiled media brief repository.");
        var compilationService = _compilationService ?? throw new InvalidOperationException("Still compilation requires the multimodal compilation service.");

        var group = await _productionRepository.GetAsync(productionGroupId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Production group '{productionGroupId}' was not found.");
        if (group.Status == SceneImageProductionGroupStatus.Archived)
            throw new InvalidOperationException($"Production group '{group.Id}' is archived.");
        var plan = await plans.GetCurrentAsync(group.CatalogueId, group.BeatId, cancellationToken);
        if (plan is null || plan.Status != SceneBeatCatalogueStatus.Complete
            || !string.Equals(plan.Id, group.BeatProductionPlanId, StringComparison.Ordinal)
            || plan.Version != group.BeatProductionPlanVersion)
            throw new InvalidOperationException("Production group Beat Production Plan is not the exact current completed version.");
        var momentSet = await momentSets.GetCurrentAsync(plan.Id, cancellationToken);
        if (momentSet is null || momentSet.Status != SceneBeatCatalogueStatus.Complete
            || !string.Equals(momentSet.Id, group.MomentSetId, StringComparison.Ordinal)
            || momentSet.Version != group.MomentSetVersion)
            throw new InvalidOperationException("Production group Moment Set is not the exact current completed version.");
        var momentMatches = momentSet.Moments
            .Where(moment => string.Equals(moment.MomentId, group.MomentId, StringComparison.Ordinal))
            .ToList();
        if (momentMatches.Count != 1)
            throw new InvalidOperationException("Production group Moment is absent or ambiguous in the exact current Moment Set.");
        var enrichment = await enrichments.GetCurrentAsync(momentSet.Id, group.MomentId, cancellationToken);
        if (enrichment is null || enrichment.Status != SceneBeatCatalogueStatus.Complete
            || !string.Equals(enrichment.Id, group.MomentEnrichmentId, StringComparison.Ordinal)
            || enrichment.Revision != group.MomentEnrichmentRevision)
            throw new InvalidOperationException("Production group Moment Enrichment is not the exact current completed revision.");

        var existing = (await briefs.ListByMomentEnrichmentAsync(enrichment.Id, cancellationToken))
            .Where(brief => IsExactStillBrief(brief, group))
            .OrderByDescending(brief => brief.CreatedUtc)
            .ThenByDescending(brief => brief.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (existing is not null)
            return existing;

        var capabilities = new HashSet<MediaCompilerCapability>
        {
            MediaCompilerCapability.FrozenVisualState,
            MediaCompilerCapability.TypedMediaReferences
        };
        return await compilationService.CompileAndPersistAsync(new CompileMediaBriefRequest(
            plan,
            momentSet,
            momentMatches[0],
            enrichment,
            new MediaCompilerTargetProfile(
                "canonical-still", "1", MediaProductionKind.StillImage,
                "canonical", "deterministic", "1", capabilities, "canonical-request-v1"),
            null,
            [],
            [],
            [],
            null,
            null), cancellationToken);
    }

    private static bool IsExactStillBrief(CompiledMediaBrief brief, SceneImageProductionGroup group)
    {
        var lineage = brief.Lineage;
        return brief.MediaKind == MediaProductionKind.StillImage
            && brief.Status == MediaCompilerStatus.Complete
            && string.Equals(brief.TargetProfileId, "canonical-still", StringComparison.Ordinal)
            && string.Equals(brief.TargetProfileVersion, "1", StringComparison.Ordinal)
            && string.Equals(brief.FamilyKey, "canonical", StringComparison.Ordinal)
            && string.Equals(brief.CompilerKey, "deterministic", StringComparison.Ordinal)
            && string.Equals(brief.CompilerVersion, "1", StringComparison.Ordinal)
            && string.Equals(brief.ProviderRequestContractVersion, "canonical-request-v1", StringComparison.Ordinal)
            && string.Equals(lineage.CatalogueId, group.CatalogueId, StringComparison.Ordinal)
            && string.Equals(lineage.BeatId, group.BeatId, StringComparison.Ordinal)
            && string.Equals(lineage.BeatProductionPlanId, group.BeatProductionPlanId, StringComparison.Ordinal)
            && lineage.BeatProductionPlanVersion == group.BeatProductionPlanVersion
            && string.Equals(lineage.MomentSetId, group.MomentSetId, StringComparison.Ordinal)
            && lineage.MomentSetVersion == group.MomentSetVersion
            && string.Equals(lineage.MomentId, group.MomentId, StringComparison.Ordinal)
            && string.Equals(lineage.MomentEnrichmentId, group.MomentEnrichmentId, StringComparison.Ordinal)
            && lineage.MomentEnrichmentRevision == group.MomentEnrichmentRevision;
    }

    public async Task<SceneImageProductionGroup> GetOrCreateGroupAsync(
        CreateSceneImageProductionGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Require(request.SessionId, "Session id");
        Require(request.InteractionId, "Interaction id");
        Require(request.CatalogueId, "Catalogue id");
        Require(request.BeatId, "Beat id");
        Require(request.BeatProductionPlanId, "Beat Production Plan id");
        Require(request.MomentSetId, "Moment Set id");
        Require(request.MomentId, "Moment id");
        Require(request.MomentEnrichmentId, "Moment Enrichment id");
        Require(request.Pov, "POV");
        ValidateOptionalJson(request.CameraIntentSnapshotJson, "Camera intent snapshot");

        var current = await _productionRepository.GetCurrentAsync(
            request.MomentEnrichmentId.Trim(), request.Pov.Trim(), cancellationToken);
        if (current is not null)
        {
            EnsureGroupMatchesRequest(current, request);
            return current;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var group = new SceneImageProductionGroup
        {
            SessionId = request.SessionId.Trim(),
            InteractionId = request.InteractionId.Trim(),
            CatalogueId = request.CatalogueId.Trim(),
            BeatId = request.BeatId.Trim(),
            BeatProductionPlanId = request.BeatProductionPlanId.Trim(),
            BeatProductionPlanVersion = request.BeatProductionPlanVersion,
            MomentSetId = request.MomentSetId.Trim(),
            MomentSetVersion = request.MomentSetVersion,
            MomentId = request.MomentId.Trim(),
            MomentEnrichmentId = request.MomentEnrichmentId.Trim(),
            MomentEnrichmentRevision = request.MomentEnrichmentRevision,
            Pov = request.Pov.Trim(),
            CameraIntentSnapshotJson = string.IsNullOrWhiteSpace(request.CameraIntentSnapshotJson)
                ? null
                : request.CameraIntentSnapshotJson.Trim(),
            Status = SceneImageProductionGroupStatus.Draft,
            IdentityPolicy = SceneImageIdentityPolicy.Required,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        await _productionRepository.CreateAsync(group, cancellationToken);
        return group;
    }

    public Task<SceneImageProductionGroup?> GetCurrentGroupAsync(
        string momentEnrichmentId,
        string pov,
        CancellationToken cancellationToken = default)
        => _productionRepository.GetCurrentAsync(momentEnrichmentId, pov, cancellationToken);

    public Task<IReadOnlyList<SceneImageRecord>> ListAttemptsAsync(
        string groupId,
        CancellationToken cancellationToken = default)
        => _imageRepository.ListImagesByProductionGroupAsync(groupId, cancellationToken);

    public Task<IReadOnlyList<ApprovedSceneFrameDecision>> ListApprovalDecisionsAsync(
        string groupId,
        CancellationToken cancellationToken = default)
        => _productionRepository.ListApprovalDecisionsAsync(groupId, cancellationToken);

    public async Task SetDispositionAsync(
        string imageId,
        string groupId,
        SceneImageAttemptDisposition expectedDisposition,
        SceneImageAttemptDisposition nextDisposition,
        CancellationToken cancellationToken = default)
    {
        var changed = await _imageRepository.TrySetDispositionAsync(
            imageId, groupId, expectedDisposition, nextDisposition, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        if (!changed)
        {
            throw new InvalidOperationException(
                $"Scene image '{imageId}' disposition changed concurrently or does not belong to production group '{groupId}'.");
        }
    }

    public Task<ApprovedSceneFrameDecision> ApproveAsync(
        string groupId,
        string imageId,
        string sha256,
        string decidedBy,
        string? note,
        CancellationToken cancellationToken = default)
        => _productionRepository.ApproveAsync(
            groupId, imageId, sha256, decidedBy, note, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public Task<SceneImageAttemptRetentionPolicy?> GetRetentionPolicyAsync(
        CancellationToken cancellationToken = default)
        => _productionRepository.GetRetentionPolicyAsync(cancellationToken);

    public Task<SceneImageAttemptRetentionPolicy> SaveRetentionPolicyAsync(
        SceneImageAttemptRetentionPolicy policy,
        long? expectedVersion,
        CancellationToken cancellationToken = default)
        => _productionRepository.SaveRetentionPolicyAsync(policy, expectedVersion, cancellationToken);

    public async Task PurgeRejectedBytesAsync(
        string imageId,
        string requestedBy,
        CancellationToken cancellationToken = default)
    {
        Require(imageId, "Scene image id");
        Require(requestedBy, "Purge actor");
        if (await _productionRepository.GetRetentionPolicyAsync(cancellationToken) is null)
            throw new InvalidOperationException("Scene image attempt retention policy is not configured.");

        var reservation = await _imageRepository.ReserveRejectedBytesPurgeAsync(
            imageId.Trim(), _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        try
        {
            await _storage.DeleteAsync(reservation.FileRelativePath, cancellationToken);
        }
        catch
        {
            await _imageRepository.ReleaseRejectedBytesPurgeAsync(reservation, CancellationToken.None);
            throw;
        }

        await _imageRepository.CompleteRejectedBytesPurgeAsync(reservation, cancellationToken);
        _logger.LogInformation(
            "Purged rejected scene image bytes: ImageId={ImageId}, RequestedBy={RequestedBy}",
            reservation.ImageId,
            requestedBy.Trim());
    }

    public async Task<SceneAsset> PromoteApprovedFrameAsync(
        string groupId,
        string name,
        SceneAssetType type,
        string? associationMetadataJson,
        string? characterProfileId,
        string requestedBy,
        CancellationToken cancellationToken = default)
    {
        Require(groupId, "Production group id");
        Require(name, "Asset name");
        Require(requestedBy, "Promotion actor");
        if (!Enum.IsDefined(type))
            throw new InvalidOperationException("Scene asset type is invalid.");
        ValidateOptionalJson(associationMetadataJson, "Association metadata");

        var group = await _productionRepository.GetAsync(groupId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Production group '{groupId}' was not found.");
        if (string.IsNullOrWhiteSpace(group.CurrentApprovedDecisionId))
            throw new InvalidOperationException($"Production group '{group.Id}' has no current approved frame.");
        var decision = await _productionRepository.GetApprovalDecisionAsync(
            group.CurrentApprovedDecisionId, cancellationToken)
            ?? throw new InvalidOperationException($"Current approval decision '{group.CurrentApprovedDecisionId}' was not found.");
        if (decision.Decision != ApprovedSceneFrameDecisionState.Approved)
            throw new InvalidOperationException($"Approval decision '{decision.Id}' is not Approved.");
        var image = await _imageRepository.GetImageAsync(decision.SceneImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Approved scene image '{decision.SceneImageId}' was not found.");
        if (image.Status != SceneImageStatus.Complete
            || !string.Equals(image.ProductionGroupId, group.Id, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(image.FileRelativePath)
            || string.IsNullOrWhiteSpace(image.Sha256)
            || !string.Equals(image.Sha256, decision.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Approved scene image '{image.Id}' must be complete, belong to the group, and exactly match the decision path and checksum.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var provenance = JsonSerializer.Serialize(new
        {
            promotedBy = requestedBy.Trim(),
            promotedUtc = now,
            approvalDecisionId = decision.Id,
            approvalVersion = decision.Version,
            decisionUtc = decision.DecisionUtc,
            productionGroupId = group.Id,
            sceneImageId = image.Id,
            image.Operation,
            image.SourceImageId,
            image.ProductionStage,
            image.PromptSnapshot,
            image.ModelIdentifier,
            image.ProviderName,
            image.SettingsJson,
            image.TypedReferenceSnapshotJson,
            group.CatalogueId,
            group.BeatId,
            group.BeatProductionPlanId,
            group.BeatProductionPlanVersion,
            group.MomentSetId,
            group.MomentSetVersion,
            group.MomentId,
            group.MomentEnrichmentId,
            group.MomentEnrichmentRevision
        }, JsonOptions);
        var asset = new SceneAsset
        {
            Name = name.Trim(),
            Kind = SceneAssetKind.PromotedApprovedFrame,
            Status = SceneAssetStatus.Complete,
            Type = type,
            AssociationMetadataJson = string.IsNullOrWhiteSpace(associationMetadataJson) ? null : associationMetadataJson.Trim(),
            CharacterProfileId = string.IsNullOrWhiteSpace(characterProfileId) ? null : characterProfileId.Trim(),
            Prompt = image.PromptSnapshot,
            ModelSnapshotJson = image.SettingsJson,
            FileRelativePath = image.FileRelativePath,
            MediaType = MediaTypeFromPath(image.FileRelativePath),
            Sha256 = image.Sha256,
            SourceApprovalDecisionId = decision.Id,
            SourceSceneImageId = image.Id,
            SourceSha256 = image.Sha256,
            SourceProvenanceJson = provenance,
            CreatedUtc = now,
            CompletedUtc = now,
            UpdatedUtc = now
        };
        await _assetRepository.CreatePromotedAsync(asset, cancellationToken);
        _logger.LogInformation(
            "Promoted approved scene frame: GroupId={GroupId}, DecisionId={DecisionId}, AssetId={AssetId}, RequestedBy={RequestedBy}",
            group.Id,
            decision.Id,
            asset.Id,
            requestedBy.Trim());
        return asset;
    }

    private static void ValidateOptionalJson(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{label} must be valid JSON.", exception);
        }
    }

    private static string MediaTypeFromPath(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png"
        };

    private static void EnsureGroupMatchesRequest(
        SceneImageProductionGroup group,
        CreateSceneImageProductionGroupRequest request)
    {
        if (!string.Equals(group.SessionId, request.SessionId.Trim(), StringComparison.Ordinal)
            || !string.Equals(group.InteractionId, request.InteractionId.Trim(), StringComparison.Ordinal)
            || !string.Equals(group.CatalogueId, request.CatalogueId.Trim(), StringComparison.Ordinal)
            || !string.Equals(group.BeatId, request.BeatId.Trim(), StringComparison.Ordinal)
            || !string.Equals(group.BeatProductionPlanId, request.BeatProductionPlanId.Trim(), StringComparison.Ordinal)
            || group.BeatProductionPlanVersion != request.BeatProductionPlanVersion
            || !string.Equals(group.MomentSetId, request.MomentSetId.Trim(), StringComparison.Ordinal)
            || group.MomentSetVersion != request.MomentSetVersion
            || !string.Equals(group.MomentId, request.MomentId.Trim(), StringComparison.Ordinal)
            || group.MomentEnrichmentRevision != request.MomentEnrichmentRevision)
        {
            throw new InvalidOperationException(
                $"Current production group '{group.Id}' does not match the requested immutable lineage.");
        }
    }

    private static void Require(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{label} is required.");
    }
}