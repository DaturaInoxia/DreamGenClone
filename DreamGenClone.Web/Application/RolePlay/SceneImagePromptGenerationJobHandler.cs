using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Application.StoryAnalysis;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Runs the pre-processor text model to turn an interaction + scene context + settings into an
/// editable image prompt. Marks the prompt record Complete/Failed and writes debug events.
/// </summary>
public sealed class SceneImagePromptGenerationJobHandler : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISessionService _sessionService;
    private readonly ISceneImageRepository _repository;
    private readonly ISceneImagePromptCompilerRegistry _compilerRegistry;
    private readonly IRolePlayStateRepository _stateRepository;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly StoryAnalysisFacade _storyAnalysis;
    private readonly IScenarioService _scenarioService;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly ICompletionClient _completionClient;
    private readonly SceneImageTurnResolver _turnResolver;
    private readonly ISceneImageProductionGroupRepository _productionGroups;
    private readonly ICompiledMediaBriefRepository _compiledMediaBriefs;
    private readonly ISceneMomentEnrichmentRepository _momentEnrichments;
    private readonly ILogger<SceneImagePromptGenerationJobHandler> _logger;

    public SceneImagePromptGenerationJobHandler(
        ISessionService sessionService,
        ISceneImageRepository repository,
        ISceneImagePromptCompilerRegistry compilerRegistry,
        IRolePlayStateRepository stateRepository,
        IRolePlayDebugEventSink debugEventSink,
        StoryAnalysisFacade storyAnalysis,
        IScenarioService scenarioService,
        IModelResolutionService modelResolutionService,
        ICompletionClient completionClient,
        SceneImageTurnResolver turnResolver,
        ISceneImageProductionGroupRepository productionGroups,
        ICompiledMediaBriefRepository compiledMediaBriefs,
        ISceneMomentEnrichmentRepository momentEnrichments,
        ILogger<SceneImagePromptGenerationJobHandler> logger)
    {
        _sessionService = sessionService;
        _repository = repository;
        _compilerRegistry = compilerRegistry;
        _stateRepository = stateRepository;
        _debugEventSink = debugEventSink;
        _storyAnalysis = storyAnalysis;
        _scenarioService = scenarioService;
        _modelResolutionService = modelResolutionService;
        _completionClient = completionClient;
        _turnResolver = turnResolver;
        _productionGroups = productionGroups;
        _compiledMediaBriefs = compiledMediaBriefs;
        _momentEnrichments = momentEnrichments;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.SceneImagePromptGeneration;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SceneImagePromptGenerationJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image prompt generation job payload is missing or invalid.");

        if (string.IsNullOrWhiteSpace(payload.SessionId))
            throw new InvalidOperationException("Scene image prompt generation payload is missing SessionId.");
        if (string.IsNullOrWhiteSpace(payload.InteractionId))
            throw new InvalidOperationException("Scene image prompt generation payload is missing InteractionId.");
        if (string.IsNullOrWhiteSpace(payload.PromptRecordId))
            throw new InvalidOperationException("Scene image prompt generation payload is missing PromptRecordId.");

        var record = await _repository.GetPromptAsync(payload.PromptRecordId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene image prompt record '{payload.PromptRecordId}' was not found.");
        if (!string.Equals(record.SessionId, payload.SessionId, StringComparison.Ordinal)
            || !string.Equals(record.InteractionId, payload.InteractionId, StringComparison.Ordinal))
            throw new InvalidOperationException("Scene image prompt job payload does not match the persisted prompt record.");

        if (record.Status == SceneImagePromptStatus.Complete)
        {
            _logger.LogDebug("Skipping scene image prompt generation; already complete: PromptRecordId={PromptRecordId}", record.Id);
            return;
        }

        if (!string.IsNullOrWhiteSpace(record.ProductionGroupId)
            || !string.IsNullOrWhiteSpace(record.CompiledMediaBriefId))
        {
            await HandleCanonicalAsync(record, cancellationToken);
            return;
        }

        var selectedBeat = JsonSerializer.Deserialize<SceneImageBeat>(record.BeatSnapshotJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image prompt record has an invalid selected beat snapshot.");

        var session = await _sessionService.LoadRolePlaySessionAsync(payload.SessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{payload.SessionId}' was not found for scene image prompt generation.");

        var interaction = session.Interactions.FirstOrDefault(x => string.Equals(x.Id, payload.InteractionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Interaction '{payload.InteractionId}' was not found in session '{payload.SessionId}'.");

        // Seed adaptive state from V2 persisted tables for accurate scene context (mirrors the
        // semantic job handler).
        var persistedAdaptiveState = await _stateRepository.LoadAdaptiveStateAsync(payload.SessionId, cancellationToken);
        if (persistedAdaptiveState is not null)
        {
            session.AdaptiveState = persistedAdaptiveState;
        }

        // Resolve the intensity label from the active intensity profiles (CR-004) so the prompt
        // shows the same resolved label the studio displays, instead of "unknown" when the
        // transient session field is null in the background job.
        await ResolveIntensityLabelAsync(session, cancellationToken);

        SceneImageStudioSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<SceneImageStudioSettings>(record.SettingsJson, JsonOptions)
                ?? new SceneImageStudioSettings();
        }
        catch (JsonException ex)
        {
            await FailRecordAsync(record, $"Scene image prompt record has invalid SettingsJson: {ex.Message}", cancellationToken);
            throw;
        }

        try
        {
            // The prompt draft reflects the user's explicitness intent; the render stage clamps
            // against the actual resolved provider policy (hard guarantee, see the render handler).
            var requestedPolicy = settings.AllowExplicitImage
                ? ImageContentPolicy.AdultAllowed
                : ImageContentPolicy.SfwFiltered;

            // Load the scenario characters so the pre-processor can inject their fixed visual
            // identity (likeness — same hair/eyes/body type across images). Best-effort: when the
            // scenario can't be loaded, the appearance block is simply omitted.
            IReadOnlyList<DreamGenClone.Web.Domain.Scenarios.Character>? characters = null;
            if (!string.IsNullOrWhiteSpace(session.ScenarioId))
            {
                var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
                characters = scenario?.Characters;
            }

            // Beat-backed prompts require the authoritative full turn. If this resolution fails,
            // the job must fail rather than silently dropping the canonical render brief.
            var fullTurn = await ResolveFullTurnAsync(session, interaction, cancellationToken);

            // Resolve the target DIFFUSION model (the checkpoint that will render the image) to
            // select the matching prompt builder. The preprocessor is an LLM that is an EXPERT in
            // that target image model: Pony → dense tag prompt; SDXL/Juggernaut → natural-language
            // photography brief. Explicitness (Pony rating_* tag / SDXL explicitness prose) is
            // driven by the narrative phase (theme intensity) per the approved mapping, not by the
            // studio's AllowExplicitImage. Missing compiler metadata fails fast (no fallback).
            var resolvedImageModel = await _modelResolutionService.ResolveImageModelAsync(null, cancellationToken);
            var compiler = _compilerRegistry.Resolve(
                resolvedImageModel.SceneImageModelFamily,
                resolvedImageModel.PromptDialect);

            var resolvedTextModel = await _modelResolutionService.ResolveImagePromptModelAsync(
                session.SessionModelId,
                cancellationToken);
            var (systemPrompt, userPrompt) = compiler.PromptBuilder.BuildMessages(
                session,
                fullTurn,
                session.AdaptiveState,
                settings,
                requestedPolicy,
                null,
                record.RefineInstruction,
                characters,
                selectedBeat,
                record.Pov);

            await WriteDebugEventAsync("SceneImagePromptProjected", session.Id, interaction.Id, new
            {
                promptRecordId = record.Id,
                requestedPolicy = requestedPolicy.ToString(),
                narrativePhase = session.AdaptiveState.CurrentPhase.ToString(),
                settingsJson = record.SettingsJson,
                turnId = fullTurn.Turn?.TurnId,
                beatAnalysisId = record.BeatAnalysisId,
                beatId = selectedBeat.BeatId,
                pov = record.Pov,
                turnInteractionCount = fullTurn.Interactions.Count,
                modelIdentifier = resolvedTextModel.ModelIdentifier,
                systemPrompt,
                userPrompt
            }, cancellationToken);

            var (rawResponse, reasoning) = await _completionClient.GenerateWithReasoningAsync(
                systemPrompt,
                userPrompt,
                resolvedTextModel,
                cancellationToken);
            var parsed = compiler.PromptBuilder.ParseOutput(rawResponse);

            record.OutputPrompt = parsed.Prompt;
            record.ModelIdentifier = resolvedTextModel.ModelIdentifier;
            record.Status = SceneImagePromptStatus.Complete;
            record.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertPromptAsync(record, cancellationToken);

            await WriteDebugEventAsync("SceneImagePromptProjectionCompleted", session.Id, interaction.Id, new
            {
                recordId = record.Id,
                stage = "llm-preprocessor",
                status = "Complete",
                outputPromptLength = record.OutputPrompt.Length,
                rawResponseLength = rawResponse.Length,
                reasoningLength = reasoning?.Length ?? 0,
                excerpt = parsed.Excerpt
            }, cancellationToken);

            _logger.LogInformation(
                "Scene image prompt projection completed: SessionId={SessionId}, InteractionId={InteractionId}, PromptRecordId={PromptRecordId}, PromptLength={PromptLength}",
                session.Id,
                interaction.Id,
                record.Id,
                record.OutputPrompt.Length);
        }
        catch (Exception ex)
        {
            await FailRecordAsync(record, ex.Message, cancellationToken);
            throw;
        }
    }

    private async Task HandleCanonicalAsync(
        SceneImagePromptRecord record,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(record.ProductionGroupId)
                || string.IsNullOrWhiteSpace(record.CompiledMediaBriefId))
                throw new InvalidOperationException("Canonical prompt lineage requires both production group and compiled Still brief ids.");
            var group = await _productionGroups.GetAsync(record.ProductionGroupId, cancellationToken)
                ?? throw new InvalidOperationException($"Production group '{record.ProductionGroupId}' was not found.");
            if (group.Status == SceneImageProductionGroupStatus.Archived)
                throw new InvalidOperationException($"Production group '{group.Id}' is archived.");
            if (!string.Equals(group.SessionId, record.SessionId, StringComparison.Ordinal)
                || !string.Equals(group.InteractionId, record.InteractionId, StringComparison.Ordinal)
                || !string.Equals(group.Pov, record.Pov, StringComparison.Ordinal))
                throw new InvalidOperationException("Canonical prompt record does not exactly match its production group ownership and POV.");

            var currentEnrichment = await _momentEnrichments.GetCurrentAsync(
                group.MomentSetId, group.MomentId, cancellationToken);
            if (currentEnrichment is null
                || currentEnrichment.Status != SceneBeatCatalogueStatus.Complete
                || !string.Equals(currentEnrichment.Id, group.MomentEnrichmentId, StringComparison.Ordinal)
                || currentEnrichment.Revision != group.MomentEnrichmentRevision)
                throw new InvalidOperationException("Production group Moment Enrichment is not the exact current completed revision.");
            EnsureGroupMatchesEnrichment(group, currentEnrichment);

            var brief = await _compiledMediaBriefs.GetAsync(record.CompiledMediaBriefId, cancellationToken)
                ?? throw new InvalidOperationException($"Compiled media brief '{record.CompiledMediaBriefId}' was not found.");
            if (brief.MediaKind != MediaProductionKind.StillImage
                || brief.Status != MediaCompilerStatus.Complete)
                throw new InvalidOperationException("Canonical prompt generation requires a complete compiled Still brief.");
            EnsureBriefMatchesGroup(brief, group);

            var settings = JsonSerializer.Deserialize<SceneImageStudioSettings>(record.SettingsJson, JsonOptions)
                ?? throw new InvalidOperationException("Canonical scene image prompt SettingsJson cannot be null.");
            var requestedPolicy = settings.AllowExplicitImage
                ? ImageContentPolicy.AdultAllowed
                : ImageContentPolicy.SfwFiltered;
            var resolvedImageModel = await _modelResolutionService.ResolveImageModelAsync(null, cancellationToken);
            var compiler = _compilerRegistry.Resolve(
                resolvedImageModel.SceneImageModelFamily,
                resolvedImageModel.PromptDialect);
            var resolvedTextModel = await _modelResolutionService.ResolveImagePromptModelAsync(null, cancellationToken);
            var (systemPrompt, userPrompt) = compiler.PromptBuilder.BuildMessages(
                brief, group.Pov, settings, requestedPolicy, record.RefineInstruction);

            await WriteDebugEventAsync("SceneImagePromptProjected", record.SessionId, record.InteractionId, new
            {
                promptRecordId = record.Id,
                productionGroupId = group.Id,
                compiledMediaBriefId = brief.Id,
                requestedPolicy = requestedPolicy.ToString(),
                settingsJson = record.SettingsJson,
                pov = group.Pov,
                modelIdentifier = resolvedTextModel.ModelIdentifier,
                systemPrompt,
                userPrompt
            }, cancellationToken);

            var (rawResponse, reasoning) = await _completionClient.GenerateWithReasoningAsync(
                systemPrompt, userPrompt, resolvedTextModel, cancellationToken);
            var parsed = compiler.PromptBuilder.ParseOutput(rawResponse);
            record.OutputPrompt = parsed.Prompt;
            record.ModelIdentifier = resolvedTextModel.ModelIdentifier;
            record.Status = SceneImagePromptStatus.Complete;
            record.ErrorMessage = null;
            record.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertPromptAsync(record, cancellationToken);

            await WriteDebugEventAsync("SceneImagePromptProjectionCompleted", record.SessionId, record.InteractionId, new
            {
                recordId = record.Id,
                productionGroupId = group.Id,
                compiledMediaBriefId = brief.Id,
                stage = "canonical-still-llm-preprocessor",
                status = "Complete",
                outputPromptLength = record.OutputPrompt.Length,
                rawResponseLength = rawResponse.Length,
                reasoningLength = reasoning?.Length ?? 0,
                excerpt = parsed.Excerpt
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            await FailRecordAsync(record, exception.Message, cancellationToken);
            throw;
        }
    }

    private static void EnsureGroupMatchesEnrichment(
        SceneImageProductionGroup group,
        SceneMomentEnrichment enrichment)
    {
        if (!string.Equals(group.CatalogueId, enrichment.CatalogueId, StringComparison.Ordinal)
            || !string.Equals(group.BeatId, enrichment.BeatId, StringComparison.Ordinal)
            || !string.Equals(group.BeatProductionPlanId, enrichment.BeatProductionPlanId, StringComparison.Ordinal)
            || group.BeatProductionPlanVersion != enrichment.BeatProductionPlanVersion
            || !string.Equals(group.MomentSetId, enrichment.MomentSetId, StringComparison.Ordinal)
            || group.MomentSetVersion != enrichment.MomentSetVersion
            || !string.Equals(group.MomentId, enrichment.MomentId, StringComparison.Ordinal))
            throw new InvalidOperationException("Production group lineage does not exactly match the current Moment Enrichment.");
    }

    private static void EnsureBriefMatchesGroup(
        CompiledMediaBrief brief,
        SceneImageProductionGroup group)
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

    private async Task FailRecordAsync(SceneImagePromptRecord record, string errorMessage, CancellationToken cancellationToken)
    {
        record.Status = SceneImagePromptStatus.Failed;
        record.ErrorMessage = errorMessage;
        record.UpdatedUtc = DateTime.UtcNow;
        await _repository.UpsertPromptAsync(record, cancellationToken);
        _logger.LogWarning("Scene image prompt generation failed: PromptRecordId={PromptRecordId}, Error={ErrorMessage}", record.Id, errorMessage);
    }

    /// <summary>
    /// Resolves the authoritative full-turn context required by a beat-backed prompt.
    /// </summary>
    private Task<FullTurnContext> ResolveFullTurnAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        CancellationToken cancellationToken)
        => _turnResolver.ResolveAsync(session, interaction.Id, cancellationToken);

    private async Task ResolveIntensityLabelAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        try
        {
            var intensityProfiles = await _storyAnalysis.ListIntensityProfilesAsync(cancellationToken);
            var byId = intensityProfiles
                .Where(x => x.Intensity != IntensityLevel.Intro)
                .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

            IntensityLevel? selectedIntensityLevel = IntensityLevel.Emotional;
            IntensityLevel? adaptiveIntensityLevel = null;

            if (!string.IsNullOrWhiteSpace(session.SelectedIntensityProfileId)
                && byId.TryGetValue(session.SelectedIntensityProfileId, out var intensityProfile))
            {
                selectedIntensityLevel = intensityProfile.Intensity;
            }

            if (!string.IsNullOrWhiteSpace(session.AdaptiveIntensityProfileId)
                && byId.TryGetValue(session.AdaptiveIntensityProfileId, out var adaptiveProfile))
            {
                adaptiveIntensityLevel = adaptiveProfile.Intensity;
            }

            var (label, _) = RolePlayStyleResolver.ResolveEffectiveStyle(session, selectedIntensityLevel, adaptiveIntensityLevel);
            session.LastResolvedIntensityLabel = label;
        }
        catch (Exception ex)
        {
            // Intensity label is informational context for the prompt; a resolution failure must
            // not fail prompt generation. The preprocessor already falls back to "unknown".
            _logger.LogDebug(ex, "Failed to resolve intensity label for scene image prompt; SessionId={SessionId}", session.Id);
        }
    }

    private async Task WriteDebugEventAsync<T>(string kind, string sessionId, string interactionId, T metadata, CancellationToken cancellationToken)
    {
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = sessionId,
            InteractionId = interactionId,
            EventKind = kind,
            Severity = "Info",
            Summary = kind,
            MetadataJson = JsonSerializer.Serialize(metadata, JsonOptions)
        }, cancellationToken);
    }

}
