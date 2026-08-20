using System.Diagnostics;
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
    private readonly IModelResolutionService _modelResolutionService;
    private readonly ISceneImagePromptPreprocessor _preprocessor;
    private readonly ICompletionClient _completionClient;
    private readonly IRolePlayStateRepository _stateRepository;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly StoryAnalysisFacade _storyAnalysis;
    private readonly IScenarioService _scenarioService;
    private readonly ILogger<SceneImagePromptGenerationJobHandler> _logger;

    public SceneImagePromptGenerationJobHandler(
        ISessionService sessionService,
        ISceneImageRepository repository,
        IModelResolutionService modelResolutionService,
        ISceneImagePromptPreprocessor preprocessor,
        ICompletionClient completionClient,
        IRolePlayStateRepository stateRepository,
        IRolePlayDebugEventSink debugEventSink,
        StoryAnalysisFacade storyAnalysis,
        IScenarioService scenarioService,
        ILogger<SceneImagePromptGenerationJobHandler> logger)
    {
        _sessionService = sessionService;
        _repository = repository;
        _modelResolutionService = modelResolutionService;
        _preprocessor = preprocessor;
        _completionClient = completionClient;
        _stateRepository = stateRepository;
        _debugEventSink = debugEventSink;
        _storyAnalysis = storyAnalysis;
        _scenarioService = scenarioService;
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

            var resolved = await _modelResolutionService.ResolveImagePromptModelAsync(session.SessionModelId, cancellationToken);

            // Load the scenario characters so the pre-processor can inject their fixed visual
            // identity (likeness — same hair/eyes/body type across images). Best-effort: when the
            // scenario can't be loaded, the appearance block is simply omitted.
            IReadOnlyList<DreamGenClone.Web.Domain.Scenarios.Character>? characters = null;
            if (!string.IsNullOrWhiteSpace(session.ScenarioId))
            {
                var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
                characters = scenario?.Characters;
            }

            var (systemPrompt, userPrompt) = _preprocessor.BuildMessages(
                session,
                interaction,
                session.AdaptiveState,
                settings,
                requestedPolicy,
                excerptOverride: string.IsNullOrWhiteSpace(record.InputExcerpt) ? null : record.InputExcerpt,
                refineInstruction: record.RefineInstruction,
                characters);

            await WriteDebugEventAsync("SceneImagePromptSent", session.Id, interaction.Id, new
            {
                promptRecordId = record.Id,
                modelIdentifier = resolved.ModelIdentifier,
                providerName = resolved.ProviderName,
                requestedPolicy = requestedPolicy.ToString(),
                settingsJson = record.SettingsJson,
                systemPrompt,
                userPrompt
            }, cancellationToken);

            var stopwatch = Stopwatch.StartNew();
            var rawOutput = await _completionClient.GenerateAsync(systemPrompt, userPrompt, resolved, cancellationToken);
            stopwatch.Stop();

            var parsed = _preprocessor.ParseOutput(rawOutput);

            record.OutputPrompt = parsed.Prompt;
            record.InputExcerpt = string.IsNullOrWhiteSpace(parsed.Excerpt) ? record.InputExcerpt : parsed.Excerpt;
            record.ModelIdentifier = resolved.ModelIdentifier;
            record.Status = SceneImagePromptStatus.Complete;
            record.UpdatedUtc = DateTime.UtcNow;
            await _repository.UpsertPromptAsync(record, cancellationToken);

            await WriteDebugEventAsync("SceneImageResponseReceived", session.Id, interaction.Id, new
            {
                recordId = record.Id,
                stage = "preprocessor",
                status = "Complete",
                rawOutputLength = rawOutput?.Length ?? 0,
                durationMs = stopwatch.ElapsedMilliseconds
            }, cancellationToken);

            _logger.LogInformation(
                "Scene image prompt generation completed: SessionId={SessionId}, InteractionId={InteractionId}, PromptRecordId={PromptRecordId}, Model={ModelIdentifier}, DurationMs={DurationMs}",
                session.Id,
                interaction.Id,
                record.Id,
                resolved.ModelIdentifier,
                stopwatch.ElapsedMilliseconds);
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
