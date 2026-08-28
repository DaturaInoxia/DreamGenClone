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
    private readonly ISceneImageLLMPromptBuilder _preprocessor;
    private readonly SdxlSceneImagePromptBuilder _sdxlPreprocessor;
    private readonly IRolePlayStateRepository _stateRepository;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly StoryAnalysisFacade _storyAnalysis;
    private readonly IScenarioService _scenarioService;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly ICompletionClient _completionClient;
    private readonly SceneImageTurnResolver _turnResolver;
    private readonly ILogger<SceneImagePromptGenerationJobHandler> _logger;

    public SceneImagePromptGenerationJobHandler(
        ISessionService sessionService,
        ISceneImageRepository repository,
        ISceneImageLLMPromptBuilder preprocessor,
        SdxlSceneImagePromptBuilder sdxlPreprocessor,
        IRolePlayStateRepository stateRepository,
        IRolePlayDebugEventSink debugEventSink,
        StoryAnalysisFacade storyAnalysis,
        IScenarioService scenarioService,
        IModelResolutionService modelResolutionService,
        ICompletionClient completionClient,
        SceneImageTurnResolver turnResolver,
        ILogger<SceneImagePromptGenerationJobHandler> logger)
    {
        _sessionService = sessionService;
        _repository = repository;
        _preprocessor = preprocessor;
        _sdxlPreprocessor = sdxlPreprocessor;
        _stateRepository = stateRepository;
        _debugEventSink = debugEventSink;
        _storyAnalysis = storyAnalysis;
        _scenarioService = scenarioService;
        _modelResolutionService = modelResolutionService;
        _completionClient = completionClient;
        _turnResolver = turnResolver;
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
        var selectedBeat = JsonSerializer.Deserialize<SceneImageBeat>(record.BeatSnapshotJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene image prompt record has an invalid selected beat snapshot.");

        if (record.Status == SceneImagePromptStatus.Complete)
        {
            _logger.LogDebug("Skipping scene image prompt generation; already complete: PromptRecordId={PromptRecordId}", record.Id);
            return;
        }

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
            // studio's AllowExplicitImage. Unknown checkpoint families fail fast (no fallback).
            var resolvedImageModel = await _modelResolutionService.ResolveImageModelAsync(null, cancellationToken);
            var modelFamily = SceneImageModelFamilyResolver.Classify(resolvedImageModel.ModelIdentifier);
            ISceneImageLLMPromptBuilder builder = modelFamily switch
            {
                SceneImageModelFamily.Pony => _preprocessor,
                SceneImageModelFamily.Sdxl => _sdxlPreprocessor,
                _ => throw new InvalidOperationException(
                    $"Unsupported scene-image model family for checkpoint '{resolvedImageModel.ModelIdentifier}'. " +
                    "Register a Pony or SDXL/Juggernaut model as the RolePlaySceneImage default in Model Manager.")
            };

            var resolvedTextModel = await _modelResolutionService.ResolveImagePromptModelAsync(
                session.SessionModelId,
                cancellationToken);
            var (systemPrompt, userPrompt) = builder.BuildMessages(
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
            var parsed = _preprocessor.ParseOutput(rawResponse);

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
