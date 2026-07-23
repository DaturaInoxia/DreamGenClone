using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Options;


namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SemanticInteractionAnalysisJobHandler : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISessionService _sessionService;
    private readonly ISemanticInteractionAnalysisRepository _analysisRepository;
    private readonly IRPThemeService _rpThemeService;
    private readonly ISemanticEventInferenceService _inferenceService;
    private readonly IRolePlayAdaptiveStateService _adaptiveStateService;
    private readonly IRolePlayEngineService _engineService;
    private readonly IRolePlayStateRepository _stateRepository;
    private readonly ILogger<SemanticInteractionAnalysisJobHandler> _logger;
    private readonly RolePlayFeatureFlagsOptions _featureFlags;

    public SemanticInteractionAnalysisJobHandler(
        ISessionService sessionService,
        ISemanticInteractionAnalysisRepository analysisRepository,
        IRPThemeService rpThemeService,
        ISemanticEventInferenceService inferenceService,
        IRolePlayAdaptiveStateService adaptiveStateService,
        IRolePlayEngineService engineService,
        IRolePlayStateRepository stateRepository,
        IOptions<RolePlayFeatureFlagsOptions> featureFlags,
        ILogger<SemanticInteractionAnalysisJobHandler> logger)
    {
        _sessionService = sessionService;
        _analysisRepository = analysisRepository;
        _rpThemeService = rpThemeService;
        _inferenceService = inferenceService;
        _adaptiveStateService = adaptiveStateService;
        _engineService = engineService;
        _stateRepository = stateRepository;
        _featureFlags = featureFlags?.Value ?? new RolePlayFeatureFlagsOptions();
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.SemanticInteractionAnalysis;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
    {
        if (!_featureFlags.EnableSemanticInference)
        {
            _logger.LogInformation(
                "Skipping semantic analysis job {JobId}; RolePlayFeatureFlags:EnableSemanticInference is false.",
                job.JobId);
            return;
        }

        var payload = JsonSerializer.Deserialize<SemanticInteractionAnalysisJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Semantic analysis job payload is missing or invalid.");

        if (string.IsNullOrWhiteSpace(payload.SessionId))
        {
            throw new InvalidOperationException("Semantic analysis job payload is missing SessionId.");
        }

        if (string.IsNullOrWhiteSpace(payload.InteractionId))
        {
            throw new InvalidOperationException("Semantic analysis job payload is missing InteractionId.");
        }

        if (string.IsNullOrWhiteSpace(payload.CharacterId))
        {
            throw new InvalidOperationException("Semantic analysis job payload is missing CharacterId.");
        }

        var existing = await _analysisRepository.GetBySessionAndInteractionAsync(
            payload.SessionId,
            payload.InteractionId,
            cancellationToken);

        if (existing?.Status == SemanticAnalysisStatus.Complete)
        {
            _logger.LogDebug(
                "Skipping semantic analysis for session {SessionId} interaction {InteractionId}; already complete",
                payload.SessionId,
                payload.InteractionId);
            return;
        }

        var createdUtc = existing?.CreatedUtc ?? DateTime.UtcNow;
        await _analysisRepository.UpsertAsync(new SemanticInteractionAnalysisState
        {
            SessionId = payload.SessionId,
            InteractionId = payload.InteractionId,
            CharacterId = payload.CharacterId,
            Status = SemanticAnalysisStatus.Analyzing,
            UpdatedUtc = DateTime.UtcNow,
            CreatedUtc = createdUtc
        }, cancellationToken);

        try
        {
            var session = await _sessionService.LoadRolePlaySessionAsync(payload.SessionId, cancellationToken)
                ?? throw new InvalidOperationException($"Role-play session '{payload.SessionId}' was not found for semantic analysis.");

            var interaction = session.Interactions.FirstOrDefault(x => string.Equals(x.Id, payload.InteractionId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Interaction '{payload.InteractionId}' was not found in role-play session '{payload.SessionId}'.");

            // Seed session.AdaptiveState from V2 persisted tables rather than PayloadJson so
            // InteractionEvidenceSignal (and all other theme-score breakdown components) start
            // from the ACCUMULATED values written by previous background-job runs. PayloadJson
            // only holds the base engine-pipeline scores (IES=0); each background run would
            // otherwise overwrite V2ThemeScores with the same per-run delta instead of
            // accumulating correctly across interactions.
            var persistedAdaptiveState = await _stateRepository.LoadAdaptiveStateAsync(payload.SessionId, cancellationToken);
            if (persistedAdaptiveState is not null)
            {
                session.AdaptiveState = persistedAdaptiveState;
            }

            // Per-session theme selections are authoritative for live sessions. The RP theme
            // profile is only a seed at create time; it may be cleared once the user customises
            // the per-session theme list. Source the allowed event ids from SessionThemeSelections.
            var sessionThemeIds = (session.SessionThemeSelections ?? [])
                .Select(x => x.ThemeId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (sessionThemeIds.Count == 0)
            {
                throw new InvalidOperationException(
                    $"MissingSemanticConfiguration: session '{session.Id}' has no SessionThemeSelections; cannot run semantic inference. Add themes to the session.");
            }

            if (session.ContextWindowSize <= 0)
            {
                throw new InvalidOperationException("MissingSemanticConfiguration: session ContextWindowSize must be greater than zero for semantic inference.");
            }

            var mappingsByEvent = await _rpThemeService.ResolveSemanticEventMappingsByThemeIdsAsync(sessionThemeIds, cancellationToken);
            var allowedEventIds = mappingsByEvent.Keys
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Multi-encounter Climax: exclude `encounter-completed` from the async job.
            // That event is owned by the sync detection path (TryDetectEncounterBoundaryAsync
            // in the engine), which runs inline so the counter advances before the next prompt.
            if (allowedEventIds.Contains("encounter-completed", StringComparer.OrdinalIgnoreCase)
                && await IsMultiEncounterClimaxActiveAsync(session))
            {
                allowedEventIds = allowedEventIds
                    .Where(x => !string.Equals(x, "encounter-completed", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                _logger.LogDebug("Excluded encounter-completed from async semantic job (multi-encounter active): SessionId={SessionId}", session.Id);
            }

            if (allowedEventIds.Count == 0)
            {
                throw new InvalidOperationException("MissingSemanticConfiguration: session themes have no enabled semantic event mappings.");
            }

            var watermark = await _analysisRepository.GetLatestBySessionAndCharacterAsync(
                payload.SessionId,
                payload.CharacterId,
                cancellationToken);

            var interactionIndex = session.Interactions.FindIndex(x => string.Equals(x.Id, interaction.Id, StringComparison.OrdinalIgnoreCase));
            if (interactionIndex < 0)
            {
                throw new InvalidOperationException($"Interaction '{interaction.Id}' index could not be resolved in session '{session.Id}'.");
            }

            var watermarkIndex = -1;
            if (!string.IsNullOrWhiteSpace(watermark?.InteractionId))
            {
                watermarkIndex = session.Interactions.FindIndex(x => string.Equals(x.Id, watermark.InteractionId, StringComparison.OrdinalIgnoreCase));
            }

            var contextStartIndexByWindow = Math.Max(0, interactionIndex - session.ContextWindowSize);
            var contextStartIndexByWatermark = watermarkIndex >= 0 ? watermarkIndex + 1 : 0;
            var contextStartIndex = Math.Max(contextStartIndexByWindow, contextStartIndexByWatermark);

            var contextTurns = session.Interactions
                .Skip(contextStartIndex)
                .Take(Math.Max(0, interactionIndex - contextStartIndex))
                .Where(x => !x.IsExcluded)
                .Select(x => $"[{x.InteractionType}] {x.ActorName}: {x.Content}")
                .ToList();

            var inferenceResult = await _inferenceService.InferAsync(new SemanticEventInferenceRequest
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                ActorName = string.IsNullOrWhiteSpace(interaction.ActorName) ? payload.CharacterId : interaction.ActorName,
                InteractionText = interaction.Content ?? string.Empty,
                ContextTurns = contextTurns,
                AllowedEventIds = allowedEventIds
            }, cancellationToken);

            if (!inferenceResult.Success)
            {
                await _analysisRepository.UpsertAsync(new SemanticInteractionAnalysisState
                {
                    SessionId = payload.SessionId,
                    InteractionId = payload.InteractionId,
                    CharacterId = payload.CharacterId,
                    Status = SemanticAnalysisStatus.Error,
                    ErrorMessage = inferenceResult.ErrorMessage ?? "Model resolution failed",
                    UpdatedUtc = DateTime.UtcNow,
                    CreatedUtc = createdUtc,
                    AnalyzedUtc = DateTime.UtcNow
                }, cancellationToken);
                _logger.LogWarning(
                    "Semantic analysis aborted for session {SessionId} interaction {InteractionId}: {Error}",
                    payload.SessionId, payload.InteractionId, inferenceResult.ErrorMessage);
                return;
            }

            var inferredSignals = inferenceResult.Events
                .Select(x => new IRolePlayAdaptiveStateService.InferredSemanticSignal(
                    x.EventId,
                    x.Confidence,
                    x.ActorName,
                    x.TargetCharacterName,
                    x.EvidenceSpan))
                .ToList();

            session.AdaptiveState = await _adaptiveStateService.ApplyInferredSemanticEvidenceAsync(
                session,
                interaction,
                inferredSignals,
                cancellationToken);

            // ---- Separate active-in-encounter inference (independent of theme analysis) ----
            // Runs as its own LLM call so it cannot interfere with theme/stat semantic events.
            try
            {
                var activeResult = await _inferenceService.InferAsync(new SemanticEventInferenceRequest
                {
                    SessionId = session.Id,
                    InteractionId = interaction.Id,
                    ActorName = string.IsNullOrWhiteSpace(interaction.ActorName) ? payload.CharacterId : interaction.ActorName,
                    InteractionText = interaction.Content ?? string.Empty,
                    ContextTurns = contextTurns,
                    AllowedEventIds = ["active-in-encounter"],
                    EventDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["active-in-encounter"] = "The actor is engaged in or witnessing sexual or erotic activity — explicit or subtle. Includes: intercourse, foreplay, orgasm, aftermath; exhibitionism (down-blouse, revealing clothing, flashing, posing); voyeurism (watching, noticing someone undressed, staring); suggestive physical contact (brushing against, lingering touch, proximity charged with tension); flirtation with sexual undertones; being the target of another character's sexual attention or advances."
                    }
                }, cancellationToken);

                if (activeResult.Success)
                {
                    var activeEvent = activeResult.Events
                        .FirstOrDefault(x => string.Equals(x.EventId, "active-in-encounter", StringComparison.OrdinalIgnoreCase));
                    if (activeEvent is not null)
                    {
                        var confirmedActor = string.IsNullOrWhiteSpace(activeEvent.ActorName)
                            ? payload.CharacterId
                            : activeEvent.ActorName;
                        if (!session.AdaptiveState.CharacterEncounterStates.TryGetValue(confirmedActor, out var encState))
                        {
                            encState = new CharacterEncounterState();
                            session.AdaptiveState.CharacterEncounterStates[confirmedActor] = encState;
                        }
                        encState.IsHavingSexConfirmed = true;
                        encState.EncounterNumber = session.AdaptiveState.CurrentEncounterNumber;
                        encState.EnteredEncounterUtc ??= DateTime.UtcNow;
                        _logger.LogDebug(
                            "active-in-encounter confirmed for {Actor} in session {SessionId} interaction {InteractionId}",
                            confirmedActor, payload.SessionId, payload.InteractionId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "active-in-encounter inference failed (non-fatal) SessionId={SessionId} InteractionId={InteractionId}",
                    payload.SessionId, payload.InteractionId);
            }

            // Persist ONLY the semantic-owned adaptive-state fields to V2 tables. Background
            // semantic analysis must never write the session blob (PayloadJson) or touch
            // pipeline-managed columns (CurrentPhase, TurnCountInPhase, ActiveScenarioId,
            // TimeSkipPhase, etc.). SaveAdaptiveStateSemanticFieldsAsync updates only
            // CharacterSnapshots, ThemeScores, ThemeTrackerMeta, SemanticEvents, and
            // breakdowns — leaving all pipeline fields intact so the foreground engine's
            // phase transitions and turn counts are never overwritten by a background job.
            await _stateRepository.SaveAdaptiveStateSemanticFieldsAsync(session.AdaptiveState, cancellationToken);

            // Background semantic-analysis writes happen on a copy loaded directly from the store.
            // Drop the engine's in-memory cache for this session so the next UI read pulls the
            // freshly-persisted CharacterStats (otherwise the adaptive tab keeps showing stale
            // pre-delta values until the user navigates away and back).
            _engineService.InvalidateSessionCache(session.Id);

            var resultJson = JsonSerializer.Serialize(new SemanticInteractionAnalysisResult
            {
                InteractionId = interaction.Id,
                ActorName = interaction.ActorName,
                CompletedUtc = DateTime.UtcNow,
                ResultType = "inferred-semantic",
                InferredEventCount = inferenceResult.Events.Count,
                ContextTurnsCount = contextTurns.Count,
                InferenceRawOutput = inferenceResult.RawModelOutput,
                InferredEvents = inferenceResult.Events
                    .Select(e => new InferredEventRecord
                    {
                        EventId = e.EventId,
                        Confidence = e.Confidence,
                        ActorName = e.ActorName,
                        TargetCharacterName = e.TargetCharacterName,
                        EvidenceSpan = e.EvidenceSpan
                    })
                    .ToList()
            }, JsonOptions);

            await _analysisRepository.UpsertAsync(new SemanticInteractionAnalysisState
            {
                SessionId = payload.SessionId,
                InteractionId = payload.InteractionId,
                CharacterId = payload.CharacterId,
                Status = SemanticAnalysisStatus.Complete,
                ResultJson = resultJson,
                UpdatedUtc = DateTime.UtcNow,
                CreatedUtc = createdUtc,
                AnalyzedUtc = DateTime.UtcNow
            }, cancellationToken);

            _logger.LogInformation(
                "Background semantic analysis completed for session {SessionId} interaction {InteractionId}",
                payload.SessionId,
                payload.InteractionId);
        }
        catch (Exception ex)
        {
            await _analysisRepository.UpsertAsync(new SemanticInteractionAnalysisState
            {
                SessionId = payload.SessionId,
                InteractionId = payload.InteractionId,
                CharacterId = payload.CharacterId,
                Status = SemanticAnalysisStatus.Error,
                ErrorMessage = ex.Message,
                UpdatedUtc = DateTime.UtcNow,
                CreatedUtc = createdUtc,
                AnalyzedUtc = DateTime.UtcNow
            }, cancellationToken);

            throw;
        }
    }

    private async Task<bool> IsMultiEncounterClimaxActiveAsync(RolePlaySession session)
    {
        var activeThemeId = session.AdaptiveState.ActiveScenarioId;
        if (string.IsNullOrWhiteSpace(activeThemeId) || _rpThemeService is null) return false;
        if (session.AdaptiveState.CurrentPhase != DreamGenClone.Domain.RolePlay.NarrativePhase.Climax) return false;
        try
        {
            var theme = await _rpThemeService.GetThemeAsync(activeThemeId, CancellationToken.None);
            return RolePlayAssistantPrompts.IsMultiEncounterClimax(theme, "Climax");
        }
        catch (Exception ex) { _logger.LogDebug(ex, "IsMultiEncounterClimaxActive: could not load theme {ThemeId}", activeThemeId); return false; }
    }

    private sealed class SemanticInteractionAnalysisResult
    {
        public string InteractionId { get; set; } = string.Empty;

        public string ActorName { get; set; } = string.Empty;

        public DateTime CompletedUtc { get; set; }

        public string ResultType { get; set; } = string.Empty;

        public int InferredEventCount { get; set; }

        public int ContextTurnsCount { get; set; }

        public string InferenceRawOutput { get; set; } = string.Empty;

        public string PromptSystem { get; set; } = string.Empty;

        public string PromptUser { get; set; } = string.Empty;

        public List<InferredEventRecord> InferredEvents { get; set; } = [];
    }

    private sealed class InferredEventRecord
    {
        public string EventId { get; set; } = string.Empty;

        public decimal Confidence { get; set; }

        public string? ActorName { get; set; }

        public string? TargetCharacterName { get; set; }

        public string? EvidenceSpan { get; set; }
    }
}
