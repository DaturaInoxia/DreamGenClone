using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Public orchestration surface for the scene-image feature. Enqueues the two-stage pipeline
/// (pre-processor prompt generation + image rendering) onto the background job queue and provides
/// query/delete operations for the studio, gallery, and workspace.
/// </summary>
public sealed class SceneImageService : ISceneImageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISessionService _sessionService;
    private readonly ISceneImageRepository _repository;
    private readonly ISceneImageEditRepository _editRepository;
    private readonly ISceneImageStorageService _storage;
    private readonly IBackgroundJobQueue _backgroundJobQueue;
    private readonly SceneImageTurnResolver? _turnResolver;
    private readonly ISceneImageProductionGroupRepository? _productionGroupRepository;
    private readonly ISceneMomentEnrichmentRepository? _momentEnrichmentRepository;
    private readonly ICompiledMediaBriefRepository? _compiledMediaBriefRepository;
    private readonly ISceneImageProductionService? _productionService;
    private readonly IModelResolutionService? _modelResolutionService;
    private readonly ILogger<SceneImageService> _logger;
    private readonly SceneImageRenderingJobHandler? _renderingJobHandler;
    private readonly SceneImageEditingJobHandler? _editingJobHandler;
    private readonly IImageEditorModelResolver? _imageEditorModelResolver;
    private readonly IImageEditorEndpointReadiness? _imageEditorEndpointReadiness;
    private readonly IDurableBackgroundJobQueue? _durableJobQueue;

    public SceneImageService(
        ISessionService sessionService,
        ISceneImageRepository repository,
        ISceneImageEditRepository editRepository,
        ISceneImageStorageService storage,
        IBackgroundJobQueue backgroundJobQueue,
        ILogger<SceneImageService> logger)
        : this(sessionService, repository, editRepository, storage, backgroundJobQueue, null, null, null, null, null, null, logger)
    {
    }

    public SceneImageService(
        ISessionService sessionService,
        ISceneImageRepository repository,
        ISceneImageEditRepository editRepository,
        ISceneImageStorageService storage,
        IBackgroundJobQueue backgroundJobQueue,
        SceneImageTurnResolver? turnResolver,
        ILogger<SceneImageService> logger)
        : this(sessionService, repository, editRepository, storage, backgroundJobQueue, turnResolver, null, null, null, null, null, logger)
    {
    }

    public SceneImageService(
        ISessionService sessionService,
        ISceneImageRepository repository,
        ISceneImageEditRepository editRepository,
        ISceneImageStorageService storage,
        IBackgroundJobQueue backgroundJobQueue,
        SceneImageTurnResolver? turnResolver,
        ISceneImageProductionGroupRepository? productionGroupRepository,
        ISceneMomentEnrichmentRepository? momentEnrichmentRepository,
        ICompiledMediaBriefRepository? compiledMediaBriefRepository,
        ILogger<SceneImageService> logger)
        : this(sessionService, repository, editRepository, storage, backgroundJobQueue, turnResolver,
            productionGroupRepository, momentEnrichmentRepository, compiledMediaBriefRepository, null, null, logger)
    {
    }

    public SceneImageService(
        ISessionService sessionService,
        ISceneImageRepository repository,
        ISceneImageEditRepository editRepository,
        ISceneImageStorageService storage,
        IBackgroundJobQueue backgroundJobQueue,
        SceneImageTurnResolver? turnResolver,
        ISceneImageProductionGroupRepository? productionGroupRepository,
        ISceneMomentEnrichmentRepository? momentEnrichmentRepository,
        ICompiledMediaBriefRepository? compiledMediaBriefRepository,
        ISceneImageProductionService? productionService,
        IModelResolutionService? modelResolutionService,
        ILogger<SceneImageService> logger,
        SceneImageRenderingJobHandler? renderingJobHandler = null,
        SceneImageEditingJobHandler? editingJobHandler = null,
        IImageEditorModelResolver? imageEditorModelResolver = null,
        IImageEditorEndpointReadiness? imageEditorEndpointReadiness = null,
        IDurableBackgroundJobQueue? durableJobQueue = null)
    {
        _sessionService = sessionService;
        _repository = repository;
        _editRepository = editRepository;
        _storage = storage;
        _backgroundJobQueue = backgroundJobQueue;
        _turnResolver = turnResolver;
        _productionGroupRepository = productionGroupRepository;
        _momentEnrichmentRepository = momentEnrichmentRepository;
        _compiledMediaBriefRepository = compiledMediaBriefRepository;
        _productionService = productionService;
        _modelResolutionService = modelResolutionService;
        _logger = logger;
        _renderingJobHandler = renderingJobHandler;
        _editingJobHandler = editingJobHandler;
        _imageEditorModelResolver = imageEditorModelResolver;
        _imageEditorEndpointReadiness = imageEditorEndpointReadiness;
        _durableJobQueue = durableJobQueue;
    }

    public Task<SceneImageBeatAnalysisRecord?> GetBeatAnalysisByTurnAsync(
        string sessionId, string turnId, CancellationToken cancellationToken = default)
        => _repository.GetBeatAnalysisByTurnAsync(sessionId, turnId, cancellationToken);

    public async Task<SceneImagePromptRecord> EnqueuePromptAsync(ScenePromptRequest request, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(request.SessionId, cancellationToken);
        var interaction = FindInteraction(session, request.InteractionId);
        var hasProductionGroup = !string.IsNullOrWhiteSpace(request.ProductionGroupId);
        var hasCompiledBrief = !string.IsNullOrWhiteSpace(request.CompiledMediaBriefId);
        if (hasProductionGroup != hasCompiledBrief)
            throw new InvalidOperationException("Canonical prompt generation requires both a production group id and compiled Still brief id.");
        if (hasProductionGroup)
            return await EnqueueProductionPromptAsync(request, session.Id, interaction.Id, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.BeatAnalysisId))
            throw new InvalidOperationException("A completed beat analysis is required to generate an image prompt.");
        if (string.IsNullOrWhiteSpace(request.BeatSnapshotJson))
            throw new InvalidOperationException("A selected beat is required to generate an image prompt.");
        if (string.IsNullOrWhiteSpace(request.Pov))
            throw new InvalidOperationException("A POV is required to generate an image prompt.");

        var turnResolver = _turnResolver
            ?? throw new InvalidOperationException("Scene image prompt generation requires the turn resolver service.");
        var fullTurn = await turnResolver.ResolveAsync(session, interaction.Id, cancellationToken);
        if (fullTurn.Turn is null)
            throw new InvalidOperationException("Scene image prompt generation requires a persisted RolePlayV2Turn.");
        var analysis = await _repository.GetBeatAnalysisByTurnAsync(session.Id, fullTurn.Turn.TurnId, cancellationToken);
        if (analysis is null || analysis.Status != SceneImageBeatAnalysisStatus.Complete)
            throw new InvalidOperationException("Generate a completed beat analysis before generating an image prompt.");
        if (!string.Equals(analysis.Id, request.BeatAnalysisId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected beat belongs to a replaced analysis. Select a beat from the current analysis.");

        var requestedBeat = JsonSerializer.Deserialize<SceneImageBeat>(request.BeatSnapshotJson, JsonOptions)
            ?? throw new InvalidOperationException("The selected beat snapshot is invalid.");
        var currentBeats = JsonSerializer.Deserialize<IReadOnlyList<SceneImageBeat>>(analysis.BeatsJson, JsonOptions)
            ?? throw new InvalidOperationException("The completed beat analysis has an invalid beat list.");
        var selectedBeat = currentBeats.FirstOrDefault(x => string.Equals(x.BeatId, requestedBeat.BeatId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected beat is not part of the current completed analysis.");
        if (selectedBeat.SchemaVersion != SceneImageBeatAnalysisService.CurrentSchemaVersion)
            throw new InvalidOperationException("The selected beat analysis uses an older schema. Generate beats again.");
        if (!string.Equals(request.Pov, SceneImagePovFramer.Omniscient, StringComparison.OrdinalIgnoreCase)
            && !selectedBeat.Characters.Any(x => string.Equals(x.Name, request.Pov, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The selected character POV is not associated with the selected beat.");

        var record = new SceneImagePromptRecord
        {
            SessionId = session.Id,
            InteractionId = interaction.Id,
            BeatAnalysisId = request.BeatAnalysisId.Trim(),
            BeatSnapshotJson = JsonSerializer.Serialize(selectedBeat, JsonOptions),
            Pov = request.Pov.Trim(),
            SettingsJson = JsonSerializer.Serialize(request.Settings),
            InputExcerpt = request.ExcerptOverride ?? string.Empty,
            RefineInstruction = string.IsNullOrWhiteSpace(request.RefineInstruction) ? null : request.RefineInstruction.Trim(),
            Status = SceneImagePromptStatus.Pending
        };

        await _repository.UpsertPromptAsync(record, cancellationToken);

        var payloadJson = JsonSerializer.Serialize(new SceneImagePromptGenerationJobPayload
        {
            SessionId = session.Id,
            InteractionId = interaction.Id,
                PromptRecordId = record.Id
        });

        _backgroundJobQueue.Enqueue(
            BackgroundJobTypes.SceneImagePromptGeneration,
            payloadJson,
            dedupeKey: $"{BackgroundJobTypes.SceneImagePromptGeneration}:{record.Id}");

        _logger.LogInformation(
            "Enqueued scene image prompt generation: SessionId={SessionId}, InteractionId={InteractionId}, PromptRecordId={PromptRecordId}",
            session.Id,
            interaction.Id,
            record.Id);

        return record;
    }

    public async Task<SceneImageRecord> EnqueueFinishAsync(
        SceneImageFinishRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(request.SessionId, cancellationToken);
        var interaction = FindInteraction(session, request.InteractionId);
        Require(request.ProductionGroupId, "Production group id");
        Require(request.SourceImageId, "Finish source image id");
        Require(request.Instruction, "Finish instruction");
        if (!request.FinishChangeClass.HasValue)
            throw new InvalidOperationException("FinishChangeClass is required for every Finish edit.");
        var groupRepository = _productionGroupRepository
            ?? throw new InvalidOperationException("Finish enqueue requires the production group repository.");
        var group = await groupRepository.GetAsync(request.ProductionGroupId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Production group '{request.ProductionGroupId}' was not found.");
        if (!string.Equals(group.SessionId, session.Id, StringComparison.Ordinal)
            || !string.Equals(group.InteractionId, interaction.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("The production group must belong to the selected session and interaction.");
        var parent = await _repository.GetImageAsync(request.SourceImageId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Source scene image '{request.SourceImageId}' was not found.");
        if (parent.Status != SceneImageStatus.Complete)
            throw new InvalidOperationException("Finish requires a completed parent attempt.");
        var identityParent = parent.ProductionStage == SceneImageProductionStage.Identity;
        var skippedCompositionParent = parent.ProductionStage == SceneImageProductionStage.Composition
            && group.IdentityPolicy == SceneImageIdentityPolicy.SkippedByUser;
        if (!identityParent && !skippedCompositionParent)
            throw new InvalidOperationException("Finish requires a completed Identity attempt, or a completed Composition when identity is skipped.");
        if (string.IsNullOrWhiteSpace(parent.FileRelativePath) || string.IsNullOrWhiteSpace(parent.Sha256))
            throw new InvalidOperationException("The completed Finish parent must have stored bytes and a checksum.");
        if (request.RequestAdultContent)
        {
            var modelResolution = _modelResolutionService
                ?? throw new InvalidOperationException("Finish adult-content policy cannot be resolved because model resolution is unavailable.");
            var resolved = await modelResolution.ResolveImageModelAsync(null, cancellationToken);
            if (resolved.ContentPolicy is ImageContentPolicy.SfwFiltered or ImageContentPolicy.Unknown)
                throw new InvalidOperationException("Adult-content Finish edits are unavailable because the resolved editor model does not allow them.");
        }
        var appliedReferenceBindingsJson = SerializeReferenceApplications(request.ReferenceApplications)
            ?? parent.AppliedReferenceBindingsJson;

        var record = new SceneImageRecord
        {
            SessionId = session.Id,
            InteractionId = interaction.Id,
            PromptRecordId = parent.PromptRecordId,
            PromptSnapshot = request.Instruction.Trim(),
            Status = SceneImageStatus.Pending,
            Operation = SceneImageOperation.Edit,
            SourceImageId = parent.Id,
            ImageSize = parent.ImageSize,
            Style = parent.Style,
            SettingsJson = parent.SettingsJson,
            BeatId = parent.BeatId,
            Pov = parent.Pov,
            ProductionGroupId = group.Id,
            CompiledMediaBriefId = parent.CompiledMediaBriefId,
            ProductionStage = SceneImageProductionStage.Finish,
            FinishChangeClass = request.FinishChangeClass,
            IdentityStale = identityParent && request.FinishChangeClass == SceneImageFinishChangeClass.Geometry,
            EditIntentSnapshot = JsonSerializer.Serialize(new { request.RequestAdultContent }),
            Disposition = SceneImageAttemptDisposition.Active,
            CatalogueId = parent.CatalogueId,
            BeatProductionPlanId = parent.BeatProductionPlanId,
            BeatProductionPlanVersion = parent.BeatProductionPlanVersion,
            MomentSetId = parent.MomentSetId,
            MomentSetVersion = parent.MomentSetVersion,
            MomentId = parent.MomentId,
            MomentEnrichmentId = parent.MomentEnrichmentId,
            MomentEnrichmentRevision = parent.MomentEnrichmentRevision,
            AppliedReferenceBindingsJson = appliedReferenceBindingsJson
        };
        await _repository.InsertImageAsync(record, cancellationToken);
        var resolvedEditorModel = await ResolveEditorModelForDispatchAsync(cancellationToken);
        return await DispatchEditAsync(
            record,
            new SceneImageEditingJobPayload { SessionId = session.Id, InteractionId = interaction.Id, ImageRecordId = record.Id },
            resolvedEditorModel.ImageProtocol,
            resolvedEditorModel,
            cancellationToken);
    }

    public async Task<SceneImageRecord> EnqueueIdentityAsync(
        SceneImageIdentityRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(request.SessionId, cancellationToken);
        var interaction = FindInteraction(session, request.InteractionId);
        Require(request.ProductionGroupId, "Production group id");
        Require(request.SourceImageId, "Composition source image id");
        Require(request.Instruction, "Identity instruction");
        var productionService = _productionService
            ?? throw new InvalidOperationException("Identity enqueue requires the production service.");
        var groupRepository = _productionGroupRepository
            ?? throw new InvalidOperationException("Identity enqueue requires the production group repository.");
        var group = await groupRepository.GetAsync(request.ProductionGroupId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Production group '{request.ProductionGroupId}' was not found.");
        if (!string.Equals(group.SessionId, session.Id, StringComparison.Ordinal)
            || !string.Equals(group.InteractionId, interaction.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("The production group must belong to the selected session and interaction.");
        var parent = await _repository.GetImageAsync(request.SourceImageId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Source scene image '{request.SourceImageId}' was not found.");
        if (parent.Status != SceneImageStatus.Complete
            || !string.Equals(parent.ProductionGroupId, group.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Identity requires a completed attempt in the same production group.");
        if (string.IsNullOrWhiteSpace(parent.FileRelativePath) || string.IsNullOrWhiteSpace(parent.Sha256))
            throw new InvalidOperationException("The completed Identity source must have stored bytes and a checksum.");

        var readiness = await productionService.ResolveIdentityReadinessAsync(
            group.Id, request.IdentityReferences, cancellationToken);
        var bindings = readiness.Select((item, index) => new
        {
            ordinal = index + 1,
            characterId = item.CharacterId,
            characterName = item.CharacterName,
            identityPackId = item.IdentityPackId,
            identityPackVersion = item.IdentityPackVersion,
            canonicalFaceAssetId = item.CanonicalFaceAssetId,
            faceView = item.FaceView,
            fileRelativePath = item.FileRelativePath,
            sha256 = item.Sha256
        }).ToArray();
        if (bindings.Length == 0)
            throw new InvalidOperationException("Identity requires at least one known visible character.");
        var faceOnlyInstruction = BuildFaceOnlyIdentityInstruction(
            bindings.Select(binding => (binding.ordinal, binding.characterName)).ToList());
        var appliedReferenceBindingsJson = SerializeReferenceApplications(request.ReferenceApplications)
            ?? parent.AppliedReferenceBindingsJson;

        var record = new SceneImageRecord
        {
            SessionId = session.Id,
            InteractionId = interaction.Id,
            PromptRecordId = parent.PromptRecordId,
            PromptSnapshot = faceOnlyInstruction,
            Status = SceneImageStatus.Pending,
            Operation = SceneImageOperation.Edit,
            SourceImageId = parent.Id,
            ImageSize = parent.ImageSize,
            Style = parent.Style,
            SettingsJson = parent.SettingsJson,
            BeatId = parent.BeatId,
            Pov = parent.Pov,
            ProductionGroupId = group.Id,
            CompiledMediaBriefId = parent.CompiledMediaBriefId,
            ProductionStage = SceneImageProductionStage.Identity,
            Disposition = SceneImageAttemptDisposition.Active,
            CatalogueId = parent.CatalogueId,
            BeatProductionPlanId = parent.BeatProductionPlanId,
            BeatProductionPlanVersion = parent.BeatProductionPlanVersion,
            MomentSetId = parent.MomentSetId,
            MomentSetVersion = parent.MomentSetVersion,
            MomentId = parent.MomentId,
            MomentEnrichmentId = parent.MomentEnrichmentId,
            MomentEnrichmentRevision = parent.MomentEnrichmentRevision,
            IdentityReferenceBindingsJson = JsonSerializer.Serialize(bindings, JsonOptions),
            AppliedReferenceBindingsJson = appliedReferenceBindingsJson
        };
        await _repository.InsertImageAsync(record, cancellationToken);
        var resolvedEditorModel = await ResolveEditorModelForDispatchAsync(cancellationToken);
        return await DispatchEditAsync(
            record,
            new SceneImageEditingJobPayload
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                ImageRecordId = record.Id
            },
            resolvedEditorModel.ImageProtocol,
            resolvedEditorModel,
            cancellationToken);
    }

    internal static string BuildFaceOnlyIdentityInstruction(
        IReadOnlyList<(int Ordinal, string CharacterName)> selectedCharacters)
    {
        var names = string.Join(", ", selectedCharacters
            .Select(selected => selected.CharacterName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(names))
            throw new InvalidOperationException("Identity requires named selected characters.");
        var referenceMapping = string.Join(" ", selectedCharacters.Select(selected =>
            $"Reference image {selected.Ordinal + 1} is the approved face identity reference for {selected.CharacterName.Trim()} and applies only to that character's face in image 1."));

        return $"Identity correction only for the selected character faces: {names}. " +
            "Image 1 is the existing scene and must remain the base image. " +
            $"{referenceMapping} " +
            "The additional approved face images are identity references only, not replacement images or composition sources. " +
            "Transfer the approved reference identity into the matching face region: preserve and reproduce the reference's distinguishing facial geometry, eye color and shape, eyebrows, nose, lips, freckles, complexion markers, and hairline-adjacent facial details, adapted to the existing face's scale, angle, expression, and lighting. " +
            "Use the reference only to correct face-local identity details for those selected characters. " +
            "Treat the existing scene's visible neck and body skin tone as authoritative: harmonize the corrected face skin tone, undertone, exposure, and shading with that body under the existing scene lighting, without importing a mismatched complexion from the reference. " +
            "Preserve everything outside those selected face regions exactly: every person and unselected face, bodies, poses, hands, clothing, accessories, expression, action, scene geometry, framing, camera, crop, background, objects, lighting, color, and composition. " +
            "Do not copy the reference image framing, background, body, pose, clothing, or lighting. " +
            "Do not add, remove, move, restyle, or otherwise alter anything outside the selected character face regions.";
    }

    private async Task<SceneImagePromptRecord> EnqueueProductionPromptAsync(
        ScenePromptRequest request,
        string sessionId,
        string interactionId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.BeatAnalysisId) || !string.IsNullOrWhiteSpace(request.BeatSnapshotJson))
            throw new InvalidOperationException("Canonical prompt generation cannot include legacy beat-analysis lineage.");
        var (group, brief) = await ValidateCanonicalProductionAsync(
            request.ProductionGroupId!, request.CompiledMediaBriefId!, sessionId, interactionId, request.Pov, cancellationToken);
        var record = new SceneImagePromptRecord
        {
            SessionId = sessionId,
            InteractionId = interactionId,
            BeatAnalysisId = string.Empty,
            BeatSnapshotJson = string.Empty,
            ProductionGroupId = group.Id,
            CompiledMediaBriefId = brief.Id,
            Pov = group.Pov,
            SettingsJson = JsonSerializer.Serialize(request.Settings),
            InputExcerpt = request.ExcerptOverride ?? string.Empty,
            RefineInstruction = string.IsNullOrWhiteSpace(request.RefineInstruction) ? null : request.RefineInstruction.Trim(),
            Status = SceneImagePromptStatus.Pending
        };
        await _repository.UpsertPromptAsync(record, cancellationToken);
        _backgroundJobQueue.Enqueue(
            BackgroundJobTypes.SceneImagePromptGeneration,
            JsonSerializer.Serialize(new SceneImagePromptGenerationJobPayload
            {
                SessionId = sessionId,
                InteractionId = interactionId,
                PromptRecordId = record.Id
            }),
            dedupeKey: $"{BackgroundJobTypes.SceneImagePromptGeneration}:{record.Id}");
        _logger.LogInformation(
            "Enqueued canonical scene image prompt generation: SessionId={SessionId}, InteractionId={InteractionId}, ProductionGroupId={ProductionGroupId}, CompiledMediaBriefId={CompiledMediaBriefId}, PromptRecordId={PromptRecordId}",
            sessionId, interactionId, group.Id, brief.Id, record.Id);
        return record;
    }

    public async Task<SceneImageRecord> EnqueueRenderAsync(SceneRenderRequest request, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(request.SessionId, cancellationToken);
        FindInteraction(session, request.InteractionId);

        if (string.IsNullOrWhiteSpace(request.PromptRecordId))
        {
            throw new InvalidOperationException("A prompt record id is required to render a scene image.");
        }

        var promptRecord = await _repository.GetPromptAsync(request.PromptRecordId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene image prompt record '{request.PromptRecordId}' was not found.");
        if (!string.Equals(promptRecord.SessionId, session.Id, StringComparison.Ordinal)
            || !string.Equals(promptRecord.InteractionId, request.InteractionId, StringComparison.Ordinal))
            throw new InvalidOperationException("The prompt record must belong to the render session and interaction.");

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new InvalidOperationException("A non-empty prompt is required to render a scene image.");
        }

        var settingsJson = string.IsNullOrWhiteSpace(request.SettingsJson) ? "{}" : request.SettingsJson;

        if (request.RenderMode == SceneImageRenderMode.IdentityControlled
            && string.IsNullOrWhiteSpace(request.IdentityPackId)
            && (request.IdentityPacks is null || request.IdentityPacks.Count == 0))
        {
            throw new InvalidOperationException("At least one approved identity pack is required for identity-controlled rendering.");
        }
        ValidateIdentitySelections(request);

        SceneImageProductionGroup? productionGroup = null;
        string? typedReferenceSnapshotJson = null;
        if (!string.IsNullOrWhiteSpace(request.ProductionGroupId))
        {
            // B-103 part A: one-pass identity is an explicit opt-in ("Generate Composition + Identity").
            // This deliberately allows identity conditioning on a production Composition attempt, a
            // documented deviation from FR-053/FR-054's strict Composition -> Identity stage split.
            // The two-pass identity-after-composition stage remains future work.
            if (string.IsNullOrWhiteSpace(request.CompiledMediaBriefId))
                throw new InvalidOperationException("A compiled Still brief id is required for a production Composition attempt.");
            var canonical = await ValidateCanonicalProductionAsync(
                request.ProductionGroupId, request.CompiledMediaBriefId, session.Id, request.InteractionId,
                request.Pov, cancellationToken);
            productionGroup = canonical.Group;
            if (!string.Equals(promptRecord.ProductionGroupId, productionGroup.Id, StringComparison.Ordinal)
                || !string.Equals(promptRecord.CompiledMediaBriefId, canonical.Brief.Id, StringComparison.Ordinal))
                throw new InvalidOperationException("The production prompt, group, and compiled Still brief must exactly match.");
            typedReferenceSnapshotJson = ExtractTypedReferences(canonical.Brief);
            if (!string.IsNullOrWhiteSpace(request.RegenerateOfId))
            {
                var parent = await _repository.GetImageAsync(request.RegenerateOfId, cancellationToken)
                    ?? throw new InvalidOperationException($"Regeneration source scene image '{request.RegenerateOfId}' was not found.");
                if (parent.Status != SceneImageStatus.Complete
                    || parent.ProductionStage != SceneImageProductionStage.Composition
                    || !string.Equals(parent.ProductionGroupId, productionGroup.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A production regeneration source must be a completed Composition attempt in the same production group.");
                }
            }
        }

        var firstPackId = request.IdentityPacks?.FirstOrDefault()?.PackId;
        var appliedReferenceBindingsJson = SerializeReferenceApplications(request.ReferenceApplications);
        var record = new SceneImageRecord
        {
            SessionId = session.Id,
            InteractionId = request.InteractionId,
            PromptRecordId = promptRecord.Id,
            PromptSnapshot = request.Prompt,
            Status = SceneImageStatus.Pending,
            ImageSize = request.ImageSize,
            RequestedModelId = request.RequestedModelId,
            SettingsJson = settingsJson,
            RegenerateOfId = request.RegenerateOfId,
            RenderMode = request.RenderMode,
            IdentityPackId = request.RenderMode == SceneImageRenderMode.IdentityControlled
                ? (firstPackId ?? request.IdentityPackId)
                : null,
            IdentityPacksJson = request.RenderMode == SceneImageRenderMode.IdentityControlled && request.IdentityPacks is { Count: > 0 }
                ? JsonSerializer.Serialize(request.IdentityPacks, JsonOptions)
                : null,
            ProductionGroupId = productionGroup?.Id,
            CompiledMediaBriefId = productionGroup is null ? null : request.CompiledMediaBriefId,
            ProductionStage = productionGroup is null ? null : SceneImageProductionStage.Composition,
            Disposition = productionGroup is null ? null : SceneImageAttemptDisposition.Active,
            CatalogueId = productionGroup?.CatalogueId,
            BeatProductionPlanId = productionGroup?.BeatProductionPlanId,
            BeatProductionPlanVersion = productionGroup?.BeatProductionPlanVersion,
            MomentSetId = productionGroup?.MomentSetId,
            MomentSetVersion = productionGroup?.MomentSetVersion,
            MomentId = productionGroup?.MomentId,
            MomentEnrichmentId = productionGroup?.MomentEnrichmentId,
            MomentEnrichmentRevision = productionGroup?.MomentEnrichmentRevision,
            TypedReferenceSnapshotJson = typedReferenceSnapshotJson,
            AppliedReferenceBindingsJson = appliedReferenceBindingsJson,
            BeatId = productionGroup?.BeatId ?? request.BeatId,
            Pov = productionGroup?.Pov ?? request.Pov
        };

        // Extract the style/size labels from the settings snapshot so the image card can display
        // them without a separate join. Best-effort metadata only — never a fallback gate.
        try
        {
            var settings = JsonSerializer.Deserialize<SceneImageStudioSettings>(settingsJson);
            if (settings is not null)
            {
                if (!string.IsNullOrWhiteSpace(settings.Style))
                {
                    record.Style = settings.Style;
                }
                if (string.IsNullOrWhiteSpace(record.ImageSize) && !string.IsNullOrWhiteSpace(settings.ImageSize))
                {
                    record.ImageSize = settings.ImageSize;
                }
            }
        }
        catch (JsonException)
        {
            // The settings snapshot is informational; a malformed snapshot does not block rendering.
        }

        await _repository.InsertImageAsync(record, cancellationToken);

        var resolvedImageModel = await ResolveRenderModelForDispatchAsync(record, cancellationToken);
        var payload = new SceneImageRenderingJobPayload
        {
            SessionId = session.Id,
            InteractionId = request.InteractionId,
            ImageRecordId = record.Id
        };
        return await DispatchRenderAsync(record, payload, resolvedImageModel.ImageProtocol, cancellationToken);
    }

    private static string? SerializeReferenceApplications(
        IReadOnlyList<ReferenceApplicationSelection>? applications)
    {
        if (applications is not { Count: > 0 })
            return null;
        if (applications.Any(application => string.IsNullOrWhiteSpace(application.ElementKey)
            || string.IsNullOrWhiteSpace(application.SemanticRole)
            || string.IsNullOrWhiteSpace(application.Strategy)))
        {
            throw new InvalidOperationException("Every reference application requires an element key, semantic role, and strategy.");
        }
        if (applications.Select(application => application.ElementKey.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != applications.Count)
        {
            throw new InvalidOperationException("Reference application element keys must be unique.");
        }

        foreach (var application in applications)
        {
            var hasAsset = !string.IsNullOrWhiteSpace(application.SceneAssetId);
            if (hasAsset && (string.IsNullOrWhiteSpace(application.SceneAssetImageId)
                || application.SceneAssetVersion is null
                || string.IsNullOrWhiteSpace(application.SceneAssetSha256)))
            {
                throw new InvalidOperationException($"Reference application '{application.ElementKey}' is missing an exact approved asset version or checksum.");
            }
            if (!hasAsset && !string.Equals(application.Strategy, "TextOnly", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Reference application '{application.ElementKey}' requires an approved asset for strategy '{application.Strategy}'.");
            }
            if (application.Strength is < 0m or > 1m)
            {
                throw new InvalidOperationException($"Reference application '{application.ElementKey}' strength must be between 0 and 1.");
            }
        }

        return JsonSerializer.Serialize(applications, JsonOptions);
    }

    private static void ValidateIdentitySelections(SceneRenderRequest request)
    {
        if (request.RenderMode != SceneImageRenderMode.IdentityControlled
            || request.IdentityPacks is not { Count: > 1 } selections)
        {
            return;
        }

        if (selections.Any(selection => string.IsNullOrWhiteSpace(selection.PackId)))
            throw new InvalidOperationException("Every identity actor requires an exact identity pack id.");
        if (selections.Any(selection => string.IsNullOrWhiteSpace(selection.CharacterLabel)))
            throw new InvalidOperationException("Every identity actor requires a character label for ownership.");
        if (selections.Select(selection => selection.PackId).Distinct(StringComparer.Ordinal).Count() != selections.Count)
            throw new InvalidOperationException("Multi-actor identity rendering requires a distinct identity pack for each actor.");
        if (selections.Select(selection => selection.CharacterLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selections.Count)
            throw new InvalidOperationException("Multi-actor identity rendering requires a distinct character label for each actor.");

        foreach (var selection in selections)
        {
            var region = selection.Region
                ?? throw new InvalidOperationException($"Identity actor '{selection.CharacterLabel}' requires an explicit normalized region.");
            if (!double.IsFinite(region.X) || !double.IsFinite(region.Y)
                || !double.IsFinite(region.Width) || !double.IsFinite(region.Height)
                || region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
                || region.X + region.Width > 1 || region.Y + region.Height > 1)
            {
                throw new InvalidOperationException($"Identity actor '{selection.CharacterLabel}' has an invalid normalized region.");
            }
        }

        for (var leftIndex = 0; leftIndex < selections.Count - 1; leftIndex++)
        {
            var left = selections[leftIndex].Region!;
            for (var rightIndex = leftIndex + 1; rightIndex < selections.Count; rightIndex++)
            {
                var right = selections[rightIndex].Region!;
                var overlaps = left.X < right.X + right.Width
                    && right.X < left.X + left.Width
                    && left.Y < right.Y + right.Height
                    && right.Y < left.Y + left.Height;
                if (overlaps)
                {
                    throw new InvalidOperationException(
                        $"Identity actor regions for '{selections[leftIndex].CharacterLabel}' and '{selections[rightIndex].CharacterLabel}' overlap.");
                }
            }
        }
    }

    public async Task<SceneImageRecord> EnqueueEditAsync(SceneImageEditRequest request, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(request.SessionId, cancellationToken);
        var interaction = FindInteraction(session, request.InteractionId);
        if (string.IsNullOrWhiteSpace(request.SourceImageId))
            throw new InvalidOperationException("A source image id is required to edit a scene image.");
        if (string.IsNullOrWhiteSpace(request.EditSessionId)
            || string.IsNullOrWhiteSpace(request.CompilationAttemptId)
            || string.IsNullOrWhiteSpace(request.PromptRevisionId)
            || string.IsNullOrWhiteSpace(request.SourceImageSha256)
            || string.IsNullOrWhiteSpace(request.PromptSha256))
            throw new InvalidOperationException("An exact compiled edit session, attempt, prompt revision, source checksum, and prompt checksum are required.");
        if (string.IsNullOrWhiteSpace(request.EditorModelId))
            throw new InvalidOperationException("An exact enabled image editor model is required.");
        var resolvedEditorModel = await ResolveEditorModelByIdForDispatchAsync(request.EditorModelId, cancellationToken);

        var source = await _repository.GetImageAsync(request.SourceImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Source scene image '{request.SourceImageId}' was not found.");
        if (source.Status != SceneImageStatus.Complete)
            throw new InvalidOperationException("Only completed scene images can be edited.");
        if (source.BytesPurgedUtc is not null)
            throw new InvalidOperationException("A purged scene image cannot be edited.");
        if (!string.Equals(source.SessionId, session.Id, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.InteractionId, interaction.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The source scene image must belong to the selected session and interaction.");
        }
        if (string.IsNullOrWhiteSpace(source.FileRelativePath))
            throw new InvalidOperationException("The completed source scene image has no stored image path.");

        var editSession = await _editRepository.GetSessionAsync(request.EditSessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Edit session '{request.EditSessionId}' was not found.");
        if (!string.Equals(editSession.SourceImageId, source.Id, StringComparison.Ordinal)
            || !string.Equals(editSession.SessionId, session.Id, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(editSession.InteractionId, interaction.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The edit session does not belong to the selected source image, session, and interaction.");
        var revision = await _editRepository.GetExecutableRevisionAsync(
            editSession.Id,
            request.CompilationAttemptId,
            request.PromptRevisionId,
            request.SourceImageSha256,
            request.PromptSha256,
            cancellationToken);
        var attempt = await _editRepository.GetAttemptAsync(request.CompilationAttemptId, cancellationToken)
            ?? throw new InvalidOperationException($"Compilation attempt '{request.CompilationAttemptId}' was not found.");
        var provenanceJson = JsonSerializer.Serialize(new
        {
            editSessionId = editSession.Id,
            compilationAttemptId = attempt.Id,
            attemptOrdinal = attempt.Ordinal,
            promptRevisionId = revision.Id,
            revisionOrdinal = revision.Ordinal,
            sourceImageSha256 = editSession.SourceImageSha256,
            promptSha256 = revision.PromptSha256,
            attempt.CompilerSchemaVersion,
            attempt.SystemPromptVersion,
            resolvedModelSnapshot = JsonSerializer.Deserialize<JsonElement>(attempt.ResolvedModelSnapshotJson)
        }, JsonOptions);
        var appliedReferenceBindingsJson = SerializeReferenceApplications(request.ReferenceApplications);

        var record = new SceneImageRecord
        {
            SessionId = session.Id,
            InteractionId = interaction.Id,
            PromptRecordId = source.PromptRecordId,
            PromptSnapshot = revision.Prompt,
            Status = SceneImageStatus.Pending,
            Operation = SceneImageOperation.Edit,
            RequestedModelId = request.EditorModelId.Trim(),
            SourceImageId = source.Id,
            EditSessionId = editSession.Id,
            EditCompilationAttemptId = attempt.Id,
            EditPromptRevisionId = revision.Id,
            EditIntentSnapshot = attempt.RawIntent,
            EditCompilerProvenanceJson = provenanceJson,
            ImageSize = source.ImageSize,
            Style = source.Style,
            SettingsJson = source.SettingsJson,
            BeatId = source.BeatId,
            Pov = source.Pov,
            ProductionGroupId = source.ProductionGroupId,
            CompiledMediaBriefId = source.CompiledMediaBriefId,
            CatalogueId = source.CatalogueId,
            BeatProductionPlanId = source.BeatProductionPlanId,
            BeatProductionPlanVersion = source.BeatProductionPlanVersion,
            MomentSetId = source.MomentSetId,
            MomentSetVersion = source.MomentSetVersion,
            MomentId = source.MomentId,
            MomentEnrichmentId = source.MomentEnrichmentId,
            MomentEnrichmentRevision = source.MomentEnrichmentRevision,
            TypedReferenceSnapshotJson = source.TypedReferenceSnapshotJson,
            AppliedReferenceBindingsJson = appliedReferenceBindingsJson
        };
        await _repository.InsertImageAsync(record, cancellationToken);
        return await DispatchEditAsync(
            record,
            new SceneImageEditingJobPayload
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                ImageRecordId = record.Id,
                EditorModelId = request.EditorModelId.Trim()
            },
            resolvedEditorModel.ImageProtocol,
            resolvedEditorModel,
            cancellationToken);
    }

    public Task<SceneImagePromptRecord?> GetPromptAsync(string sessionId, string promptId, CancellationToken cancellationToken = default)
        => _repository.GetPromptAsync(promptId, cancellationToken);

    public Task<SceneImagePromptRecord?> GetLatestPromptAsync(string sessionId, string interactionId, CancellationToken cancellationToken = default)
        => _repository.GetLatestPromptAsync(sessionId, interactionId, cancellationToken);

    public Task<SceneImagePromptRecord?> GetLatestCompletedPromptAsync(
        string sessionId,
        string interactionId,
        string beatAnalysisId,
        string beatId,
        string pov,
        CancellationToken cancellationToken = default)
        => _repository.GetLatestCompletedPromptAsync(
            sessionId, interactionId, beatAnalysisId, beatId, pov, cancellationToken);

    public Task<SceneImagePromptRecord?> GetLatestCompletedProductionPromptAsync(
        string sessionId,
        string interactionId,
        string productionGroupId,
        string compiledMediaBriefId,
        CancellationToken cancellationToken = default)
        => _repository.GetLatestCompletedProductionPromptAsync(
            sessionId, interactionId, productionGroupId, compiledMediaBriefId, cancellationToken);

    public async Task UpdatePromptOutputAsync(string sessionId, string promptId, string outputPrompt, CancellationToken cancellationToken = default)
    {
        await _repository.UpdatePromptOutputAsync(promptId, outputPrompt, cancellationToken);

        _logger.LogInformation(
            "Updated scene image prompt output: SessionId={SessionId}, PromptRecordId={PromptRecordId}",
            sessionId,
            promptId);
    }

    public Task<IReadOnlyList<SceneImageRecord>> ListImagesByInteractionAsync(string sessionId, string interactionId, CancellationToken cancellationToken = default)
        => _repository.ListImagesByInteractionAsync(sessionId, interactionId, cancellationToken);

    public Task<IReadOnlyList<SceneImageRecord>> ListImagesBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => _repository.ListImagesBySessionAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<SceneImageRecord>> ListImagesByProductionGroupAsync(
        string productionGroupId, CancellationToken cancellationToken = default)
        => _repository.ListImagesByProductionGroupAsync(productionGroupId, cancellationToken);

    public Task<bool> TryCancelImageAsync(
        string sessionId,
        string imageId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default)
        => _repository.TryCancelImageAsync(imageId, sessionId, cancelledUtc, cancellationToken);

    public async Task SetDispositionAsync(
        string imageId,
        string productionGroupId,
        SceneImageAttemptDisposition expectedDisposition,
        SceneImageAttemptDisposition nextDisposition,
        CancellationToken cancellationToken = default)
    {
        if (!IsAllowedDispositionTransition(expectedDisposition, nextDisposition))
            throw new InvalidOperationException($"Scene image disposition transition from {expectedDisposition} to {nextDisposition} is not allowed.");
        var updated = await _repository.TrySetDispositionAsync(
            imageId, productionGroupId, expectedDisposition, nextDisposition, DateTime.UtcNow, cancellationToken);
        if (!updated)
            throw new InvalidOperationException("Scene image disposition changed concurrently or the image does not belong to the production group.");
    }

    private static bool IsAllowedDispositionTransition(
        SceneImageAttemptDisposition expectedDisposition,
        SceneImageAttemptDisposition nextDisposition)
        => (expectedDisposition, nextDisposition) switch
        {
            (SceneImageAttemptDisposition.Active, SceneImageAttemptDisposition.Shortlisted) => true,
            (SceneImageAttemptDisposition.Shortlisted, SceneImageAttemptDisposition.Active) => true,
            (SceneImageAttemptDisposition.Active, SceneImageAttemptDisposition.Rejected) => true,
            (SceneImageAttemptDisposition.Shortlisted, SceneImageAttemptDisposition.Rejected) => true,
            (SceneImageAttemptDisposition.Rejected, SceneImageAttemptDisposition.Archived) => true,
            _ => false
        };

    private async Task<ResolvedImageModel> ResolveRenderModelForDispatchAsync(
        SceneImageRecord record,
        CancellationToken cancellationToken)
    {
        var resolver = _modelResolutionService
            ?? throw new InvalidOperationException("Image render dispatch requires the model resolution service.");
        return string.IsNullOrWhiteSpace(record.RequestedModelId)
            ? await resolver.ResolveImageModelAsync(null, cancellationToken)
            : await resolver.ResolveImageModelByIdAsync(record.RequestedModelId, cancellationToken);
    }

    private async Task<SceneImageRecord> DispatchRenderAsync(
        SceneImageRecord record,
        SceneImageRenderingJobPayload payload,
        ImageProtocol protocol,
        CancellationToken cancellationToken)
    {
        await EnqueueDurableAsync(
            BackgroundJobTypes.SceneImageRendering,
            DurableJobLane.ImageRender,
            payload,
            record.Id,
            protocol,
            null,
            cancellationToken);
        return record;
    }

    private async Task<ResolvedImageEditorModel> ResolveEditorModelForDispatchAsync(CancellationToken cancellationToken)
    {
        var resolver = _imageEditorModelResolver
            ?? throw new InvalidOperationException("Image edit dispatch requires the image editor model resolver.");
        return await resolver.ResolveAsync(cancellationToken);
    }

    private async Task<ResolvedImageEditorModel> ResolveEditorModelByIdForDispatchAsync(
        string modelId,
        CancellationToken cancellationToken)
    {
        var resolver = _imageEditorModelResolver
            ?? throw new InvalidOperationException("Image edit dispatch requires the image editor model resolver.");
        return await resolver.ResolveByIdAsync(modelId.Trim(), cancellationToken);
    }

    private async Task<SceneImageRecord> DispatchEditAsync(
        SceneImageRecord record,
        SceneImageEditingJobPayload payload,
        ImageProtocol protocol,
        ResolvedImageEditorModel? editorModel,
        CancellationToken cancellationToken)
    {
        var queueStatus = await ResolveEditQueueStatusAsync(protocol, editorModel, cancellationToken);
        await EnqueueDurableAsync(
            BackgroundJobTypes.SceneImageEditing,
            DurableJobLane.ImageEdit,
            payload,
            record.Id,
            protocol,
            queueStatus,
            cancellationToken);
        return record;
    }

    private async Task<DurableBackgroundJobStatus> ResolveEditQueueStatusAsync(
        ImageProtocol protocol,
        ResolvedImageEditorModel? editorModel,
        CancellationToken cancellationToken)
    {
        if (protocol != ImageProtocol.ComfyUiServerless)
            return DurableBackgroundJobStatus.Queued;
        if (editorModel is null || _imageEditorEndpointReadiness is null)
            return DurableBackgroundJobStatus.Staged;
        return await _imageEditorEndpointReadiness.IsWarmAsync(editorModel, cancellationToken)
            ? DurableBackgroundJobStatus.Queued
            : DurableBackgroundJobStatus.Staged;
    }

    private async Task EnqueueDurableAsync<TPayload>(
        string jobType,
        DurableJobLane lane,
        TPayload payload,
        string imageRecordId,
        ImageProtocol protocol,
        DurableBackgroundJobStatus? requestedStatus,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(protocol))
            throw new InvalidOperationException($"Unsupported image protocol '{protocol}' for production queue admission.");
        var queue = _durableJobQueue
            ?? throw new InvalidOperationException("Studio image dispatch requires the persisted production queue.");
        var now = DateTime.UtcNow;
        var status = requestedStatus ?? (protocol == ImageProtocol.ComfyUiServerless
            ? DurableBackgroundJobStatus.Staged
            : DurableBackgroundJobStatus.Queued);
        var queued = await queue.TryEnqueueAsync(new DurableBackgroundJob
        {
            Id = Guid.NewGuid().ToString("N"),
            JobType = jobType,
            Lane = lane,
            PayloadJson = JsonSerializer.Serialize(payload),
            DedupeKey = $"{jobType}:{imageRecordId}",
            Status = status,
            MaxAttempts = 1,
            CreatedUtc = now,
            UpdatedUtc = now
        }, cancellationToken);
        _logger.LogInformation(
            "Admitted Studio image work to the persisted production queue: ImageRecordId={ImageRecordId}, JobType={JobType}, Protocol={Protocol}, Status={Status}, Enqueued={Enqueued}",
            imageRecordId, jobType, protocol, status, queued);
    }

    public Task<Dictionary<string, int>> CountImagesByInteractionAsync(string sessionId, CancellationToken cancellationToken = default)
        => _repository.CountImagesByInteractionAsync(sessionId, cancellationToken);

    public async Task DeleteImageAsync(string sessionId, string imageId, CancellationToken cancellationToken = default)
    {
        var image = await _repository.GetImageAsync(imageId, cancellationToken);
        if (image is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(image.FileRelativePath))
        {
            await _storage.DeleteAsync(image.FileRelativePath, cancellationToken);
        }

        await _repository.DeleteImageAsync(imageId, cancellationToken);

        _logger.LogInformation(
            "Deleted scene image: SessionId={SessionId}, ImageId={ImageId}",
            sessionId,
            imageId);
    }

    private async Task<RolePlaySession> LoadSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Session id is required for scene image generation.");
        }

        var session = await _sessionService.LoadRolePlaySessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{sessionId}' was not found for scene image generation.");

        return session;
    }

    private async Task<(SceneImageProductionGroup Group, CompiledMediaBrief Brief)> ValidateCanonicalProductionAsync(
        string productionGroupId,
        string compiledMediaBriefId,
        string sessionId,
        string interactionId,
        string? pov,
        CancellationToken cancellationToken)
    {
        var groupRepository = _productionGroupRepository
            ?? throw new InvalidOperationException("Production rendering requires the production group repository.");
        var enrichmentRepository = _momentEnrichmentRepository
            ?? throw new InvalidOperationException("Production rendering requires the Moment Enrichment repository.");
        var briefRepository = _compiledMediaBriefRepository
            ?? throw new InvalidOperationException("Canonical production requires the compiled media brief repository.");
        var group = await groupRepository.GetAsync(productionGroupId, cancellationToken)
            ?? throw new InvalidOperationException($"Production group '{productionGroupId}' was not found.");
        if (group.Status == SceneImageProductionGroupStatus.Archived)
            throw new InvalidOperationException($"Production group '{group.Id}' is archived.");
        if (!string.Equals(group.SessionId, sessionId, StringComparison.Ordinal)
            || !string.Equals(group.InteractionId, interactionId, StringComparison.Ordinal))
            throw new InvalidOperationException("Production group session and interaction must exactly match the request.");
        if (!string.Equals(group.Pov, pov, StringComparison.Ordinal))
            throw new InvalidOperationException("Production group POV must exactly match the request POV.");

        var current = await enrichmentRepository.GetCurrentAsync(group.MomentSetId, group.MomentId, cancellationToken);
        if (current is null || current.Status != SceneBeatCatalogueStatus.Complete
            || !string.Equals(current.Id, group.MomentEnrichmentId, StringComparison.Ordinal)
            || current.Revision != group.MomentEnrichmentRevision)
            throw new InvalidOperationException("Production group Moment Enrichment is not the exact current completed revision.");
        EnsureGroupLineage(group, current);
        var brief = await briefRepository.GetAsync(compiledMediaBriefId, cancellationToken)
            ?? throw new InvalidOperationException($"Compiled media brief '{compiledMediaBriefId}' was not found.");
        if (brief.MediaKind != MediaProductionKind.StillImage
            || brief.Status != MediaCompilerStatus.Complete)
            throw new InvalidOperationException("Production Composition requires a complete compiled Still brief.");
        EnsureBriefMatchesGroup(brief, group);
        return (group, brief);
    }

    private static void EnsureBriefMatchesGroup(CompiledMediaBrief brief, SceneImageProductionGroup group)
    {
        var lineage = brief.Lineage;
        if (!string.Equals(lineage.CatalogueId, group.CatalogueId, StringComparison.Ordinal)
            || !string.Equals(lineage.BeatId, group.BeatId, StringComparison.Ordinal)
            || !string.Equals(lineage.BeatProductionPlanId, group.BeatProductionPlanId, StringComparison.Ordinal)
            || lineage.BeatProductionPlanVersion != group.BeatProductionPlanVersion
            || !string.Equals(lineage.MomentSetId, group.MomentSetId, StringComparison.Ordinal)
            || lineage.MomentSetVersion != group.MomentSetVersion
            || !string.Equals(lineage.MomentId, group.MomentId, StringComparison.Ordinal)
            || !string.Equals(lineage.MomentEnrichmentId, group.MomentEnrichmentId, StringComparison.Ordinal)
            || lineage.MomentEnrichmentRevision != group.MomentEnrichmentRevision)
            throw new InvalidOperationException("Compiled Still brief lineage does not exactly match the production group.");
    }

    private static string ExtractTypedReferences(CompiledMediaBrief brief)
    {
        try
        {
            using var document = JsonDocument.Parse(brief.SemanticInputSnapshotJson);
            if (!document.RootElement.TryGetProperty("typedReferences", out var references)
                || references.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("The compiled Still brief semantic snapshot has no typed-reference array.");
            return references.GetRawText();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The compiled Still brief semantic snapshot is invalid JSON.", exception);
        }
    }

    private static void EnsureGroupLineage(SceneImageProductionGroup group, SceneMomentEnrichment enrichment)
    {
        if (!string.Equals(group.CatalogueId, enrichment.CatalogueId, StringComparison.Ordinal)
            || !string.Equals(group.BeatId, enrichment.BeatId, StringComparison.Ordinal)
            || !string.Equals(group.BeatProductionPlanId, enrichment.BeatProductionPlanId, StringComparison.Ordinal)
            || group.BeatProductionPlanVersion != enrichment.BeatProductionPlanVersion
            || !string.Equals(group.MomentSetId, enrichment.MomentSetId, StringComparison.Ordinal)
            || group.MomentSetVersion != enrichment.MomentSetVersion
            || !string.Equals(group.MomentId, enrichment.MomentId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Production group lineage does not exactly match the current Moment Enrichment.");
        }
    }

    private static void ValidateJsonArray(string json, string fieldName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"The {fieldName} must be a JSON array.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"The {fieldName} must be valid JSON.", exception);
        }
    }

    private static void Require(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{fieldName} is required.");
    }

    private static RolePlayInteraction FindInteraction(RolePlaySession session, string interactionId)
    {
        if (string.IsNullOrWhiteSpace(interactionId))
        {
            throw new InvalidOperationException("Interaction id is required for scene image generation.");
        }

        return session.Interactions.FirstOrDefault(x => string.Equals(x.Id, interactionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Interaction '{interactionId}' was not found in role-play session '{session.Id}'.");
    }
}
