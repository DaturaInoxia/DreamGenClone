using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class ReferenceBootstrapService : IReferenceBootstrapService
{
    private const string CandidateImageSize = "1024x1024";

    private readonly IReferenceBootstrapRepository _batches;
    private readonly IProducedImageRepository _producedImages;
    private readonly ISceneAssetService _sceneAssets;
    private readonly ISceneAssetStorageService _sceneAssetStorage;
    private readonly ICharacterImageIdentityService _identityService;
    private readonly ICharacterAppearanceVersionRepository? _appearanceRepository;
    private readonly IDurableBackgroundJobQueue? _backgroundJobQueue;
    private readonly ISceneBeatAnalyzerResolver? _durableSettingsResolver;
    private readonly TimeProvider? _timeProvider;
    private readonly ILogger<ReferenceBootstrapService> _logger;

    public ReferenceBootstrapService(
        IReferenceBootstrapRepository batches,
        IProducedImageRepository producedImages,
        ISceneAssetService sceneAssets,
        IModelResolutionService modelResolution,
        ILogger<ReferenceBootstrapService> logger,
        ISceneAssetStorageService sceneAssetStorage,
        ICharacterImageIdentityService identityService,
        ICharacterAppearanceVersionRepository? appearanceRepository = null,
        IDurableBackgroundJobQueue? backgroundJobQueue = null,
        ISceneBeatAnalyzerResolver? durableSettingsResolver = null,
        TimeProvider? timeProvider = null)
    {
        _batches = batches;
        _producedImages = producedImages;
        _sceneAssets = sceneAssets;
        _sceneAssetStorage = sceneAssetStorage;
        _identityService = identityService;
        _appearanceRepository = appearanceRepository;
        _ = modelResolution;
        _backgroundJobQueue = backgroundJobQueue;
        _durableSettingsResolver = durableSettingsResolver;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ReferenceBootstrapBatch> CreateBatchAsync(
        ReferenceBootstrapBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await _batches.UpsertBatchAsync(batch, cancellationToken);
        return batch;
    }

    public async Task<IReadOnlyList<SceneAsset>> GenerateCandidatesAsync(
        string batchId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            throw new InvalidOperationException("A reference bootstrap batch id is required.");

        var batch = await _batches.GetBatchAsync(batchId, cancellationToken)
            ?? throw new InvalidOperationException($"Reference bootstrap batch '{batchId}' was not found.");
        if (string.IsNullOrWhiteSpace(batch.Description))
            throw new InvalidOperationException($"Reference bootstrap batch '{batch.Id}' has no description.");
        if (batch.TargetAssetType is null || !Enum.IsDefined(batch.TargetAssetType.Value))
            throw new InvalidOperationException($"Reference bootstrap batch '{batch.Id}' has no target asset type.");
        if (batch.RequestedCandidateCount <= 0)
            throw new InvalidOperationException($"Reference bootstrap batch '{batch.Id}' has no requested candidate count.");
        if (_backgroundJobQueue is null || _durableSettingsResolver is null || _timeProvider is null)
            throw new InvalidOperationException("Reference bootstrap generation requires durable job queue configuration.");

        var targetRef = batch.CharacterProfileId ?? batch.LocationProfileId;
        if (string.IsNullOrWhiteSpace(targetRef))
            throw new InvalidOperationException($"Reference bootstrap batch '{batch.Id}' has no target reference.");
        var referenceKind = MapReferenceKind(batch.TargetAssetType.Value);
        var backgroundJobQueue = _backgroundJobQueue
            ?? throw new InvalidOperationException("Reference bootstrap generation requires a durable job queue.");
        var durableSettingsResolver = _durableSettingsResolver
            ?? throw new InvalidOperationException("Reference bootstrap generation requires durable retry settings.");
        var timeProvider = _timeProvider
            ?? throw new InvalidOperationException("Reference bootstrap generation requires a time provider.");
        var settings = await durableSettingsResolver.ResolveAsync(cancellationToken);
        var createdUtc = timeProvider.GetUtcNow().UtcDateTime;
        for (var index = 0; index < batch.RequestedCandidateCount; index++)
        {
            var payload = new ProducedImageGenerationJobPayload
            {
                BatchId = batch.Id,
                TargetRef = targetRef.Trim(),
                ReferenceKind = referenceKind,
                VisionText = batch.Description.Trim(),
                ImageSize = CandidateImageSize
            };
            var payloadJson = JsonSerializer.Serialize(payload);
            var dedupeKey = $"{BackgroundJobTypes.ProducedImageGeneration}:{batch.Id}:{index + 1}";
            var job = new DurableBackgroundJob
            {
                Id = dedupeKey,
                JobType = BackgroundJobTypes.ProducedImageGeneration,
                Lane = DurableJobLane.ImageRender,
                PayloadJson = payloadJson,
                DedupeKey = dedupeKey,
                MaxAttempts = settings.RetryDelaysSeconds.Count + 1,
                CreatedUtc = createdUtc,
                UpdatedUtc = createdUtc
            };
            if (!await backgroundJobQueue.TryEnqueueAsync(job, cancellationToken))
                throw new InvalidOperationException($"A durable '{job.JobType}' job with key '{dedupeKey}' is already active.");
        }

        _logger.LogInformation(
            "Enqueued reference bootstrap candidates: BatchId={BatchId}, Count={Count}",
            batch.Id,
            batch.RequestedCandidateCount);
        return [];
    }

    private static ProducedImageReferenceKind MapReferenceKind(SceneAssetType assetType) => assetType switch
    {
        SceneAssetType.CharacterFace => ProducedImageReferenceKind.CharacterFace,
        SceneAssetType.CharacterBody => ProducedImageReferenceKind.CharacterBody,
        SceneAssetType.Wardrobe => ProducedImageReferenceKind.Wardrobe,
        SceneAssetType.Location => ProducedImageReferenceKind.Location,
        _ => throw new InvalidOperationException($"Scene asset type '{assetType}' cannot generate a reference candidate.")
    };

    public async Task<IReadOnlyList<ProducedImage>> ListCandidatesAsync(
        string batchId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            throw new InvalidOperationException("A reference bootstrap batch id is required.");

        return await _producedImages.ListByBatchAsync(batchId, cancellationToken);
    }

    public async Task SetCandidateDecisionAsync(
        string producedImageId,
        ProducedImageStatus status,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(producedImageId))
            throw new InvalidOperationException("A produced image id is required.");
        if (!Enum.IsDefined(status))
            throw new InvalidOperationException($"Produced image status '{status}' is invalid.");

        var image = await _producedImages.GetAsync(producedImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Produced image '{producedImageId}' was not found.");
        if (image.Kind != ProducedImageKind.ReferenceCandidate)
            throw new InvalidOperationException($"Produced image '{image.Id}' is not a reference bootstrap candidate.");

        image.Status = status;
        image.CandidateNotes = notes;
        image.UpdatedUtc = DateTime.UtcNow.ToString("O");
        await _producedImages.UpdateAsync(image, cancellationToken);
    }

    public async Task PromoteAcceptedCharacterFaceAsync(
        string batchId,
        string producedImageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            throw new InvalidOperationException("A reference bootstrap batch id is required.");
        if (string.IsNullOrWhiteSpace(producedImageId))
            throw new InvalidOperationException("A produced image id is required.");

        var batch = await _batches.GetBatchAsync(batchId, cancellationToken)
            ?? throw new InvalidOperationException($"Reference bootstrap batch '{batchId}' was not found.");
        if (string.IsNullOrWhiteSpace(batch.FrozenTextBlock))
            throw new InvalidOperationException(
                $"Reference bootstrap batch '{batch.Id}' cannot promote a reference without a frozen text block.");
        if (batch.TargetAssetType != SceneAssetType.CharacterFace)
            throw new InvalidOperationException(
                $"Reference bootstrap batch '{batch.Id}' does not target a character face.");
        if (string.IsNullOrWhiteSpace(batch.CharacterProfileId))
            throw new InvalidOperationException(
                $"Reference bootstrap batch '{batch.Id}' has no character profile target.");

        var candidate = await _producedImages.GetAsync(producedImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Produced image '{producedImageId}' was not found.");
        if (candidate.Kind != ProducedImageKind.ReferenceCandidate)
            throw new InvalidOperationException(
                $"Produced image '{candidate.Id}' is not a reference bootstrap candidate.");
        if (!string.Equals(candidate.BatchId, batch.Id, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Produced image '{candidate.Id}' does not belong to reference bootstrap batch '{batch.Id}'.");
        if (candidate.Status != ProducedImageStatus.Accepted)
            throw new InvalidOperationException(
                $"Produced image '{candidate.Id}' cannot be promoted because its status is not Accepted.");
        if (string.IsNullOrWhiteSpace(candidate.StoragePath))
            throw new InvalidOperationException(
                $"Produced image '{candidate.Id}' does not have stored image bytes.");

        var draftPack = await _identityService.CreateDraftPackAsync(batch.CharacterProfileId, cancellationToken);
        await using var source = await _sceneAssetStorage.OpenReadAsync(candidate.StoragePath, cancellationToken);
        var promoted = await _identityService.UploadAssetAsync(
            draftPack.Id,
            SceneImageReferenceAssetKind.Face,
            $"{candidate.Id}.png",
            source,
            SceneImageReferenceFaceView.Front,
            cancellationToken);
        await _identityService.SetAssetProvenanceAsync(
            promoted.Id,
            $"Reference bootstrap batch {batch.Id}. Frozen text block: {batch.FrozenTextBlock.Trim()}",
            SceneImageReferenceConsentState.NotApplicable,
            cancellationToken);
    }

    public async Task PromoteAcceptedCharacterBodyAsync(
        string batchId,
        string producedImageId,
        CancellationToken cancellationToken = default)
    {
        var (batch, candidate) = await RequireAcceptedCandidateAsync(
            batchId, producedImageId, SceneAssetType.CharacterBody, cancellationToken);
        if (string.IsNullOrWhiteSpace(batch.CharacterProfileId))
            throw new InvalidOperationException(
                $"Reference bootstrap batch '{batch.Id}' has no character profile target.");

        var draftPack = await _identityService.CreateDraftPackAsync(batch.CharacterProfileId, cancellationToken);
        await using var source = await _sceneAssetStorage.OpenReadAsync(candidate.StoragePath!, cancellationToken);
        var promoted = await _identityService.UploadAssetAsync(
            draftPack.Id,
            SceneImageReferenceAssetKind.FullBody,
            $"{candidate.Id}.png",
            source,
            cancellationToken: cancellationToken);
        await _identityService.SetAssetProvenanceAsync(
            promoted.Id,
            $"Reference bootstrap batch {batch.Id}. Frozen text block: {batch.FrozenTextBlock!.Trim()}",
            SceneImageReferenceConsentState.NotApplicable,
            cancellationToken);
    }

    public async Task PromoteAcceptedWardrobeAsync(
        string batchId,
        string producedImageId,
        CancellationToken cancellationToken = default)
    {
        var (batch, candidate) = await RequireAcceptedCandidateAsync(
            batchId, producedImageId, SceneAssetType.Wardrobe, cancellationToken);
        if (string.IsNullOrWhiteSpace(batch.CharacterProfileId))
            throw new InvalidOperationException(
                $"Reference bootstrap batch '{batch.Id}' has no character profile target.");

        var appearanceRepository = _appearanceRepository
            ?? throw new InvalidOperationException("Wardrobe promotion requires appearance repository configuration.");
        var look = await appearanceRepository.CreateWardrobeLookDraftAsync(new CharacterWardrobeLookVersion
        {
            CharacterProfileId = batch.CharacterProfileId,
            DescriptorSnapshotJson = JsonSerializer.Serialize(batch.FrozenTextBlock!.Trim())
        }, cancellationToken);
        await using var source = await _sceneAssetStorage.OpenReadAsync(candidate.StoragePath!, cancellationToken);
        var asset = await _sceneAssets.CreateFromUploadAsync(
            $"Reference bootstrap wardrobe {batch.Id}",
            SceneAssetType.Wardrobe,
            $"{candidate.Id}.png",
            source,
            cancellationToken);
        await appearanceRepository.AddWardrobeAssetBindingAsync(new CharacterWardrobeAssetBinding
        {
            WardrobeLookVersionId = look.Id,
            SceneAssetId = asset.Id,
            SemanticRole = "reference-bootstrap",
            Ordinal = 0
        }, cancellationToken);
    }

    public async Task PromoteAcceptedLocationAsync(
        string batchId,
        string producedImageId,
        CancellationToken cancellationToken = default)
    {
        var (batch, candidate) = await RequireAcceptedCandidateAsync(
            batchId, producedImageId, null, cancellationToken);
        if (string.IsNullOrWhiteSpace(batch.LocationProfileId))
            throw new InvalidOperationException(
                $"Reference bootstrap batch '{batch.Id}' has no location profile target.");

        var profile = await _batches.GetLocationProfileAsync(batch.LocationProfileId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Location profile '{batch.LocationProfileId}' was not found.");
        await using var source = await _sceneAssetStorage.OpenReadAsync(candidate.StoragePath!, cancellationToken);
        var asset = await _sceneAssets.CreateFromUploadAsync(
            $"Reference bootstrap location {batch.Id}",
            SceneAssetType.Location,
            $"{candidate.Id}.png",
            source,
            cancellationToken);
        var references = await _batches.ListLocationReferencesAsync(profile.Id, cancellationToken);
        await _batches.UpsertLocationProfileAsync(profile with
        {
            Description = batch.FrozenTextBlock!.Trim(),
            UpdatedUtc = DateTime.UtcNow
        }, cancellationToken);
        await _batches.UpsertLocationReferenceAsync(new ReferenceBootstrapLocationReference
        {
            ProfileId = profile.Id,
            OrderedIndex = references.Count,
            AssetId = asset.Id
        }, cancellationToken);
    }

    private async Task<(ReferenceBootstrapBatch Batch, ProducedImage Candidate)> RequireAcceptedCandidateAsync(
        string batchId,
        string producedImageId,
        SceneAssetType? targetAssetType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            throw new InvalidOperationException("A reference bootstrap batch id is required.");
        if (string.IsNullOrWhiteSpace(producedImageId))
            throw new InvalidOperationException("A produced image id is required.");

        var batch = await _batches.GetBatchAsync(batchId, cancellationToken)
            ?? throw new InvalidOperationException($"Reference bootstrap batch '{batchId}' was not found.");
        if (string.IsNullOrWhiteSpace(batch.FrozenTextBlock))
            throw new InvalidOperationException(
                $"Reference bootstrap batch '{batch.Id}' cannot promote a reference without a frozen text block.");
        if (batch.TargetAssetType != targetAssetType)
            throw new InvalidOperationException(
            $"Reference bootstrap batch '{batch.Id}' does not target the required reference kind.");

        var candidate = await _producedImages.GetAsync(producedImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Produced image '{producedImageId}' was not found.");
        if (candidate.Kind != ProducedImageKind.ReferenceCandidate)
            throw new InvalidOperationException(
                $"Produced image '{candidate.Id}' is not a reference bootstrap candidate.");
        if (!string.Equals(candidate.BatchId, batch.Id, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Produced image '{candidate.Id}' does not belong to reference bootstrap batch '{batch.Id}'.");
        if (candidate.Status != ProducedImageStatus.Accepted)
            throw new InvalidOperationException(
                $"Produced image '{candidate.Id}' cannot be promoted because its status is not Accepted.");
        if (string.IsNullOrWhiteSpace(candidate.StoragePath))
            throw new InvalidOperationException(
                $"Produced image '{candidate.Id}' does not have stored image bytes.");

        return (batch, candidate);
    }
}