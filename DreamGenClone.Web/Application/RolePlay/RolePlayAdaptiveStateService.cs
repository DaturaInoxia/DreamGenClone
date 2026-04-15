using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayAdaptiveStateService : IRolePlayAdaptiveStateService
{
    private const int MaxAdaptiveTransitionHistory = 25;

    private readonly IThemeCatalogService _themeCatalogService;
    private readonly IIntensityProfileService? _intensityProfileService;
    private readonly IScenarioDefinitionService? _scenarioDefinitionService;
    private readonly IScenarioSelectionEngine? _scenarioSelectionEngine;
    private readonly INarrativePhaseManager? _narrativePhaseManager;
    private readonly IThemePreferenceService? _themePreferenceService;
    private readonly IRPThemeService? _rpThemeService;
    private readonly ISteeringProfileService? _steeringProfileService;
    private readonly IRolePlayDebugEventSink? _debugEventSink;
    private readonly ILogger<RolePlayAdaptiveStateService>? _logger;
    private readonly bool _useScenarioDefinitionsForAdaptiveRuntime;
    private readonly bool _useRpThemeSubsystem;
    private readonly bool _useRpThemeSubsystemForNewSessionsOnly;

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IScenarioSelectionEngine? scenarioSelectionEngine = null,
        INarrativePhaseManager? narrativePhaseManager = null)
    {
        _themeCatalogService = themeCatalogService;
        _intensityProfileService = null;
        _scenarioDefinitionService = null;
        _scenarioSelectionEngine = scenarioSelectionEngine;
        _narrativePhaseManager = narrativePhaseManager;
        _useScenarioDefinitionsForAdaptiveRuntime = false;
        _useRpThemeSubsystem = true;
        _useRpThemeSubsystemForNewSessionsOnly = true;
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IIntensityProfileService intensityProfileService,
        IScenarioSelectionEngine? scenarioSelectionEngine = null,
        INarrativePhaseManager? narrativePhaseManager = null)
    {
        _themeCatalogService = themeCatalogService;
        _intensityProfileService = intensityProfileService;
        _scenarioDefinitionService = null;
        _scenarioSelectionEngine = scenarioSelectionEngine;
        _narrativePhaseManager = narrativePhaseManager;
        _useScenarioDefinitionsForAdaptiveRuntime = false;
        _useRpThemeSubsystem = true;
        _useRpThemeSubsystemForNewSessionsOnly = true;
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IScenarioSelectionEngine? scenarioSelectionEngine,
        INarrativePhaseManager? narrativePhaseManager,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
    {
        _themeCatalogService = themeCatalogService;
        _intensityProfileService = null;
        _scenarioDefinitionService = null;
        _scenarioSelectionEngine = scenarioSelectionEngine;
        _narrativePhaseManager = narrativePhaseManager;
        _debugEventSink = debugEventSink;
        _logger = logger;
        _useScenarioDefinitionsForAdaptiveRuntime = false;
        _useRpThemeSubsystem = true;
        _useRpThemeSubsystemForNewSessionsOnly = true;
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IScenarioSelectionEngine? scenarioSelectionEngine,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
        : this(themeCatalogService, scenarioSelectionEngine, null, debugEventSink, logger)
    {
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
        : this(themeCatalogService, (IScenarioSelectionEngine?)null, (INarrativePhaseManager?)null, debugEventSink, logger)
    {
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IScenarioDefinitionService? scenarioDefinitionService,
        IScenarioSelectionEngine? scenarioSelectionEngine,
        INarrativePhaseManager? narrativePhaseManager,
        IThemePreferenceService themePreferenceService,
        IRPThemeService? rpThemeService,
        ISteeringProfileService styleProfileService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger,
        IIntensityProfileService? intensityProfileService = null,
        IOptions<StoryAnalysisOptions>? storyAnalysisOptions = null)
    {
        _themeCatalogService = themeCatalogService;
        _intensityProfileService = intensityProfileService;
        _scenarioDefinitionService = scenarioDefinitionService;
        _scenarioSelectionEngine = scenarioSelectionEngine;
        _narrativePhaseManager = narrativePhaseManager;
        _themePreferenceService = themePreferenceService;
        _rpThemeService = rpThemeService;
        _steeringProfileService = styleProfileService;
        _debugEventSink = debugEventSink;
        _logger = logger;
        _useScenarioDefinitionsForAdaptiveRuntime = storyAnalysisOptions?.Value.UseScenarioDefinitionsForAdaptiveRuntime == true;
        _useRpThemeSubsystem = storyAnalysisOptions?.Value.UseRpThemeSubsystem ?? true;
        _useRpThemeSubsystemForNewSessionsOnly = storyAnalysisOptions?.Value.UseRpThemeSubsystemForNewSessionsOnly ?? true;
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IScenarioSelectionEngine? scenarioSelectionEngine,
        IThemePreferenceService themePreferenceService,
        IRPThemeService? rpThemeService,
        ISteeringProfileService styleProfileService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
        : this(themeCatalogService, null, scenarioSelectionEngine, null, themePreferenceService, rpThemeService, styleProfileService, debugEventSink, logger)
    {
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IThemePreferenceService themePreferenceService,
        IRPThemeService? rpThemeService,
        ISteeringProfileService styleProfileService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
        : this(themeCatalogService, null, null, null, themePreferenceService, rpThemeService, styleProfileService, debugEventSink, logger)
    {
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IThemePreferenceService themePreferenceService,
        ISteeringProfileService styleProfileService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
        : this(themeCatalogService, null, null, null, themePreferenceService, null, styleProfileService, debugEventSink, logger)
    {
    }

    public async Task<RolePlayAdaptiveState> UpdateFromInteractionAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(interaction);

        var catalogEntries = await LoadRuntimeCatalogEntriesAsync(session, cancellationToken);
        var groupedKeywordsByThemeId = await LoadRpThemeKeywordGroupsByThemeIdAsync(session, cancellationToken);

        var state = session.AdaptiveState ?? new RolePlayAdaptiveState();
        EnsureThemeCatalog(state.ThemeTracker, catalogEntries);
        RemoveNonCharacterStats(state);

        var actorKey = string.IsNullOrWhiteSpace(interaction.ActorName) ? "Unknown" : interaction.ActorName.Trim();
        var trackCharacterStats = !IsNarrativeOrSystemInteraction(interaction, actorKey);
        CharacterStatBlock? actorStats = null;
        Dictionary<string, int>? statsBefore = null;
        if (trackCharacterStats)
        {
            actorStats = GetOrCreateCharacterStats(state, actorKey);
            statsBefore = new Dictionary<string, int>(actorStats.Stats, StringComparer.OrdinalIgnoreCase);
        }

        var primaryBefore = state.ThemeTracker.PrimaryThemeId;
        var secondaryBefore = state.ThemeTracker.SecondaryThemeId;

        var content = interaction.Content ?? string.Empty;
        var contentLower = content.ToLowerInvariant();

        // Lightweight keyword heuristics for v1 foundation.
        if (actorStats is not null)
        {
            ApplyDelta(actorStats.Stats, "Desire", ScoreStatSignal(contentLower, ["kiss", "touch", "desire", "want", "close", "heat"], 1, 4));
            ApplyDelta(actorStats.Stats, "Restraint", ScoreStatSignal(contentLower, ["can't", "wrong", "shouldn't", "hesitate", "guilt"], 1, 3));
            ApplyDelta(actorStats.Stats, "Tension", ScoreStatSignal(contentLower, ["fear", "caught", "risk", "panic", "nervous"], 1, 4));
            ApplyDelta(actorStats.Stats, "Connection", ScoreStatSignal(contentLower, ["safe", "comfort", "trust", "reassure"], 1, 3));
            ApplyDelta(actorStats.Stats, "Dominance", ScoreStatSignal(contentLower, ["control", "command", "obey", "claim", "choose", "decide", "insist"], 1, 3));
            ApplyDelta(actorStats.Stats, "Loyalty", ScoreStatSignal(contentLower, ["husband", "wife", "promise", "vow", "faithful", "devoted", "commitment"], 1, 5));
            ApplyDelta(actorStats.Stats, "Loyalty", -ScoreStatSignal(contentLower, ["affair", "betray", "cheat", "secret", "sneak", "stranger"], 1, 5));
            ApplyDelta(actorStats.Stats, "SelfRespect", ScoreStatSignal(contentLower, ["boundary", "boundaries", "respect", "dignity", "self-worth", "walk away", "no"], 1, 5));
            ApplyDelta(actorStats.Stats, "SelfRespect", -ScoreStatSignal(contentLower, ["humiliate", "ashamed", "used", "degraded", "demean"], 1, 5));

            actorStats.UpdatedUtc = DateTime.UtcNow;
        }

        // T042: Load StyleProfile for ThemeAffinities multiplication
        SteeringProfile? interactionStyleProfile = null;
        if (_steeringProfileService is not null && !string.IsNullOrWhiteSpace(session.SelectedSteeringProfileId))
        {
            interactionStyleProfile = await _steeringProfileService.GetAsync(session.SelectedSteeringProfileId, cancellationToken);
        }

        foreach (var entry in catalogEntries)
        {
            if (!state.ThemeTracker.Themes.TryGetValue(entry.Id, out var trackerItem))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(state.ActiveScenarioId)
                && !string.Equals(state.ActiveScenarioId, entry.Id, StringComparison.OrdinalIgnoreCase))
            {
                var suppressedSignal = Score(contentLower, entry.Keywords, entry.Weight);
                if (suppressedSignal > 0)
                {
                    trackerItem.SuppressedHitCount++;
                }
                continue;
            }

            // T044: Skip blocked themes, increment SuppressedHitCount
            if (trackerItem.Blocked)
            {
                var blockedSignal = Score(contentLower, entry.Keywords, entry.Weight);
                if (blockedSignal > 0)
                {
                    trackerItem.SuppressedHitCount++;
                }
                continue;
            }

            // T042: Apply ThemeAffinities multiplier
            var affinityMultiplier = 1.0;
            if (interactionStyleProfile?.ThemeAffinities is { Count: > 0 }
                && interactionStyleProfile.ThemeAffinities.TryGetValue(entry.Id, out var affinity)
                && affinity != 0)
            {
                affinityMultiplier = 1.0 + affinity * 0.1;
            }

            groupedKeywordsByThemeId.TryGetValue(entry.Id, out var groupedKeywords);
            UpdateTheme(state, interaction, entry.Label, entry.Id, contentLower, entry.Keywords, entry.Weight, affinityMultiplier, groupedKeywords);

            // T043: Apply StatAffinities to acting character when theme scores
            if (actorStats is not null && entry.StatAffinities is { Count: > 0 })
            {
                if (state.ThemeTracker.Themes.TryGetValue(entry.Id, out var item) && item.Score > 0)
                {
                    var themeSignal = Score(contentLower, entry.Keywords, entry.Weight);
                    if (themeSignal > 0)
                    {
                        foreach (var (statName, affinityDelta) in entry.StatAffinities)
                        {
                            var normalized = AdaptiveStatCatalog.NormalizeLegacyStatName(statName);
                            ApplyDelta(actorStats.Stats, normalized, NormalizeInteractionAffinityDelta(affinityDelta));
                        }
                    }
                }
            }
        }

        RecalculateSelectedThemes(state, interaction);
        await EvaluateScenarioCommitmentAsync(session, interaction, state, cancellationToken);
        state.ThemeTracker.UpdatedUtc = DateTime.UtcNow;

        session.AdaptiveState = state;
        await EvaluateAdaptiveIntensityTransitionAsync(session, interaction, cancellationToken);

        if (_debugEventSink is not null)
        {
            try
            {
                var statDeltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (actorStats is not null && statsBefore is not null)
                {
                    foreach (var stat in actorStats.Stats)
                    {
                        var before = statsBefore.TryGetValue(stat.Key, out var existing) ? existing : 50;
                        if (before != stat.Value)
                        {
                            statDeltas[stat.Key] = stat.Value - before;
                        }
                    }
                }

                var topThemes = state.ThemeTracker.Themes.Values
                    .OrderByDescending(x => x.Score)
                    .Take(5)
                    .Select(x => new { x.ThemeId, x.ThemeName, x.Score, x.Intensity })
                    .ToList();

                await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                {
                    SessionId = session.Id,
                    InteractionId = interaction.Id,
                    EventKind = "AdaptiveStateUpdated",
                    Severity = "Info",
                    ActorName = actorKey,
                    Summary = $"Adaptive state updated for {actorKey}",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        interactionId = interaction.Id,
                        actorKey,
                        statDeltas,
                        primaryThemeBefore = primaryBefore,
                        secondaryThemeBefore = secondaryBefore,
                        primaryThemeAfter = state.ThemeTracker.PrimaryThemeId,
                        secondaryThemeAfter = state.ThemeTracker.SecondaryThemeId,
                        topThemes,
                        recentEvidence = state.ThemeTracker.RecentEvidence.TakeLast(8)
                    })
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to emit adaptive debug event for session {SessionId}", session.Id);
            }
        }

        return state;
    }

    private async Task EvaluateAdaptiveIntensityTransitionAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        CancellationToken cancellationToken)
    {
        if (_intensityProfileService is null)
        {
            return;
        }

        session.AdaptiveIntensityTransitions ??= [];

        if (session.IsIntensityManuallyPinned)
        {
            session.AdaptiveIntensityLastTransitionReason = "manual-pin-suppressed";
            return;
        }

        var profiles = await _intensityProfileService.ListAsync(cancellationToken);
        if (profiles.Count == 0)
        {
            session.AdaptiveIntensityLastTransitionReason = "no-intensity-profiles";
            return;
        }

        if (string.IsNullOrWhiteSpace(session.AdaptiveIntensityProfileId))
        {
            session.AdaptiveIntensityProfileId = session.SelectedIntensityProfileId;
        }

        var currentProfile = !string.IsNullOrWhiteSpace(session.AdaptiveIntensityProfileId)
            ? profiles.FirstOrDefault(x => string.Equals(x.Id, session.AdaptiveIntensityProfileId, StringComparison.OrdinalIgnoreCase))
            : null;
        if (currentProfile is null)
        {
            session.AdaptiveIntensityLastTransitionReason = "adaptive-profile-not-found";
            return;
        }

        var desire = AverageCharacterStat(session.AdaptiveState, "Desire");
        var restraint = AverageCharacterStat(session.AdaptiveState, "Restraint");
        var tension = AverageCharacterStat(session.AdaptiveState, "Tension");

        var reasonCode = "stable";
        var delta = 0;

        if (desire >= 82 && restraint <= 42)
        {
            delta = 1;
            reasonCode = "desire-high-restraint-low-escalate";
        }
        else if (desire <= 38 || restraint >= 72)
        {
            delta = -1;
            reasonCode = "desire-low-or-restraint-high-deescalate";
        }

        if (tension >= 85 && restraint >= 65)
        {
            delta = -1;
            reasonCode = "tension-and-restraint-high-deescalate";
        }

        var interactionCount = session.Interactions.Count + 1;
        if (interactionCount <= 3 && delta > 0)
        {
            delta = 0;
            reasonCode = "early-phase-no-escalation";
        }
        else if (interactionCount >= 18 && delta == 0 && desire >= 68)
        {
            delta = 1;
            reasonCode = "late-phase-gentle-escalation";
        }

        var selectedProfile = !string.IsNullOrWhiteSpace(session.SelectedIntensityProfileId)
            ? profiles.FirstOrDefault(x => string.Equals(x.Id, session.SelectedIntensityProfileId, StringComparison.OrdinalIgnoreCase))
            : null;
        var phaseBaselineSourceProfile = selectedProfile ?? currentProfile;
        var phaseBaselineDelta = phaseBaselineSourceProfile.GetPhaseOffset(session.AdaptiveState.CurrentNarrativePhase);
        var selectedScale = selectedProfile is not null
            ? (int)selectedProfile.Intensity
            : (int)currentProfile.Intensity;

        var flowBaselineScale = Math.Clamp(selectedScale + phaseBaselineDelta, 1, 5);
        var boundedStatDelta = Math.Clamp(delta, -1, 1);

        var floor = RolePlayStyleResolver.ParseBoundScale(session.IntensityFloorOverride);
        var ceiling = RolePlayStyleResolver.ParseBoundScale(session.IntensityCeilingOverride);
        var targetScale = flowBaselineScale + boundedStatDelta;
        targetScale = Math.Clamp(targetScale, 1, 5);

        reasonCode += $"|phase={session.AdaptiveState.CurrentNarrativePhase}|phase-delta={phaseBaselineDelta}|stat-delta={boundedStatDelta}";

        if (floor.HasValue && targetScale < floor.Value)
        {
            targetScale = floor.Value;
            reasonCode += "-blocked-by-floor";
        }

        if (ceiling.HasValue && targetScale > ceiling.Value)
        {
            targetScale = ceiling.Value;
            reasonCode += "-blocked-by-ceiling";
        }

        var targetProfile = profiles.FirstOrDefault(x => (int)x.Intensity == targetScale);
        if (targetProfile is null)
        {
            session.AdaptiveIntensityLastTransitionReason = reasonCode + "-target-profile-missing";
            return;
        }

        if (string.Equals(targetProfile.Id, currentProfile.Id, StringComparison.OrdinalIgnoreCase))
        {
            session.AdaptiveIntensityLastTransitionReason = reasonCode;
            return;
        }

        session.AdaptiveIntensityProfileId = targetProfile.Id;
        session.AdaptiveIntensityLastFromProfileId = currentProfile.Id;
        session.AdaptiveIntensityLastToProfileId = targetProfile.Id;
        session.AdaptiveIntensityLastTransitionReason = reasonCode;
        session.AdaptiveIntensityLastTransitionUtc = DateTime.UtcNow;
        session.AdaptiveIntensityTransitions.Add(new AdaptiveIntensityTransitionRecord
        {
            FromProfileId = currentProfile.Id,
            ToProfileId = targetProfile.Id,
            ReasonCode = reasonCode,
            Source = "adaptive-engine",
            OccurredUtc = session.AdaptiveIntensityLastTransitionUtc.Value
        });

        if (session.AdaptiveIntensityTransitions.Count > MaxAdaptiveTransitionHistory)
        {
            var trim = session.AdaptiveIntensityTransitions.Count - MaxAdaptiveTransitionHistory;
            session.AdaptiveIntensityTransitions.RemoveRange(0, trim);
        }
    }

    private async Task EvaluateScenarioCommitmentAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        RolePlayAdaptiveState state,
        CancellationToken cancellationToken)
    {
        var manualOverrideScenarioId = ExtractManualOverrideScenarioId(interaction.Content, state);
        var manualOverrideRequested = !string.IsNullOrWhiteSpace(manualOverrideScenarioId);

        if (manualOverrideRequested)
        {
            await HandleManualOverrideAsync(state, manualOverrideScenarioId!, cancellationToken);
            _logger?.LogInformation(
                "Manual scenario override requested for session {SessionId}: phase={Phase}, activeScenarioId={ActiveScenarioId}, requestedScenario={ScenarioId}, interactionCount={InteractionCount}",
                session.Id,
                state.CurrentNarrativePhase,
                state.ActiveScenarioId,
                manualOverrideScenarioId,
                session.Interactions.Count + 1);
            return;
        }

        if (_scenarioSelectionEngine is null && _narrativePhaseManager is null)
        {
            return;
        }

        var candidates = state.ThemeTracker.Themes.Values
            .Select(item => new ScenarioCandidateInput(
                item.ThemeId,
                Clamp01(item.Score / 12.0),
                Clamp01((item.Breakdown.InteractionEvidenceSignal + item.Breakdown.ScenarioPhaseSignal) / 12.0),
                Clamp01(item.Breakdown.ChoiceSignal <= 0 ? 0.0 : item.Breakdown.ChoiceSignal / 20.0),
                !item.Blocked && item.Score > 0))
            .ToList();

        ScenarioSelectionResult? result = null;
        if (_scenarioSelectionEngine is not null)
        {
            result = await _scenarioSelectionEngine.EvaluateAsync(
            new AdaptiveScenarioSnapshot(
                state.ActiveScenarioId,
                state.CurrentNarrativePhase.ToString(),
                session.Interactions.Count + 1,
                AverageCharacterStat(state, "Desire"),
                AverageCharacterStat(state, "Restraint"),
                state.ActiveScenarioId is null
                    ? 0
                    : state.ThemeTracker.Themes.TryGetValue(state.ActiveScenarioId, out var activeItem)
                        ? activeItem.Score
                        : 0),
            candidates,
            new ScenarioSelectionContext(
                BuildUpInteractionCount: session.Interactions.Count + 1,
                ManualOverrideRequested: false,
                ManualOverrideScenarioId: null),
            cancellationToken);
        }

        if (result is not null)
        {
            var rankedById = result.RankedCandidates.ToDictionary(x => x.ScenarioId, StringComparer.OrdinalIgnoreCase);
            foreach (var item in state.ThemeTracker.Themes.Values)
            {
                if (rankedById.TryGetValue(item.ThemeId, out var ranked))
                {
                    item.IsScenarioCandidate = true;
                    item.NarrativeFitScore = ranked.FitScore;
                    item.LastCandidateEvaluationTimeUtc = DateTime.UtcNow;
                }
                else
                {
                    item.IsScenarioCandidate = false;
                    item.NarrativeFitScore = 0;
                }
            }

            if (!string.IsNullOrWhiteSpace(result.SelectedScenarioId))
            {
                var wasUncommitted = state.ActiveScenarioId is null;
                state.ActiveScenarioId = result.SelectedScenarioId;
                state.ScenarioCommitmentTimeUtc ??= DateTime.UtcNow;
                state.CurrentNarrativePhase = NarrativePhase.Committed;

                if (wasUncommitted)
                {
                    state.InteractionsSinceCommitment = 0;
                    state.InteractionsInApproaching = 0;
                }

                foreach (var item in state.ThemeTracker.Themes.Values)
                {
                    if (!string.Equals(item.ThemeId, state.ActiveScenarioId, StringComparison.OrdinalIgnoreCase)
                        && !item.Blocked)
                    {
                        item.IsScenarioCandidate = false;
                    }
                }
            }

            if (result.DeferredForTie && state.ActiveScenarioId is null)
            {
                state.CurrentNarrativePhase = NarrativePhase.BuildUp;
            }

            _logger?.LogInformation(
                "Scenario commitment evaluation for session {SessionId}: phase={Phase}, activeScenarioId={ActiveScenarioId}, selected={SelectedScenarioId}, deferred={DeferredForTie}, candidateCount={CandidateCount}, interactionCount={InteractionCount}",
                session.Id,
                state.CurrentNarrativePhase,
                state.ActiveScenarioId,
                result.SelectedScenarioId,
                result.DeferredForTie,
                result.RankedCandidates.Count,
                session.Interactions.Count + 1);
        }

        IncrementPhaseCounters(state);
        await EvaluatePhaseTransitionAsync(session, interaction, state, cancellationToken);
    }

    private static void IncrementPhaseCounters(RolePlayAdaptiveState state)
    {
        if (state.ActiveScenarioId is null)
        {
            return;
        }

        switch (state.CurrentNarrativePhase)
        {
            case NarrativePhase.Committed:
                state.InteractionsSinceCommitment++;
                break;
            case NarrativePhase.Approaching:
                state.InteractionsSinceCommitment++;
                state.InteractionsInApproaching++;
                break;
            case NarrativePhase.Climax:
                state.InteractionsSinceCommitment++;
                break;
        }
    }

    private async Task EvaluatePhaseTransitionAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        RolePlayAdaptiveState state,
        CancellationToken cancellationToken)
    {
        if (_narrativePhaseManager is null)
        {
            return;
        }

        var transition = await _narrativePhaseManager.EvaluateTransitionAsync(
            new AdaptiveScenarioSnapshot(
                state.ActiveScenarioId,
                state.CurrentNarrativePhase.ToString(),
                session.Interactions.Count + 1,
                AverageCharacterStat(state, "Desire"),
                AverageCharacterStat(state, "Restraint"),
                state.ActiveScenarioId is not null && state.ThemeTracker.Themes.TryGetValue(state.ActiveScenarioId, out var activeItem)
                    ? activeItem.Score
                    : 0),
            new NarrativeSignalSnapshot(
                state.InteractionsSinceCommitment,
                state.InteractionsInApproaching,
                ExplicitClimaxRequested: ContainsAny(interaction.Content, ["/climax", "trigger climax"]),
                ClimaxCompletionDetected: ContainsAny(interaction.Content, ["/completeclimax", "climax complete"]),
                ManualScenarioOverrideRequested: false,
                ManualOverrideScenarioId: null),
            cancellationToken);

        if (!transition.Transitioned)
        {
            return;
        }

        if (!Enum.TryParse<NarrativePhase>(transition.NextPhase, out var nextPhase))
        {
            return;
        }

        state.CurrentNarrativePhase = nextPhase;
        if (nextPhase == NarrativePhase.Approaching)
        {
            state.InteractionsInApproaching = 0;
        }

        _logger?.LogInformation(
            "Narrative phase transition for session {SessionId}: current={CurrentPhase}, next={NextPhase}, reason={Reason}, activeScenarioId={ActiveScenarioId}, interactionCount={InteractionCount}",
            session.Id,
            transition.CurrentPhase,
            transition.NextPhase,
            transition.Reason,
            state.ActiveScenarioId,
            session.Interactions.Count + 1);

        if (nextPhase == NarrativePhase.Reset)
        {
            var resetSnapshot = await _narrativePhaseManager.ApplyResetAsync(
                new AdaptiveScenarioSnapshot(
                    state.ActiveScenarioId,
                    state.CurrentNarrativePhase.ToString(),
                    session.Interactions.Count + 1,
                    AverageCharacterStat(state, "Desire"),
                    AverageCharacterStat(state, "Restraint"),
                    state.ActiveScenarioId is not null && state.ThemeTracker.Themes.TryGetValue(state.ActiveScenarioId, out var active)
                        ? active.Score
                        : 0),
                new ResetTrigger("Automatic reset", false, null),
                cancellationToken);

            ApplyResetSnapshot(state, resetSnapshot);
            state.CurrentNarrativePhase = NarrativePhase.BuildUp;
        }
    }

    private async Task HandleManualOverrideAsync(
        RolePlayAdaptiveState state,
        string requestedScenarioId,
        CancellationToken cancellationToken)
    {
        if (_narrativePhaseManager is not null && !string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            var resetSnapshot = await _narrativePhaseManager.ApplyResetAsync(
                new AdaptiveScenarioSnapshot(
                    state.ActiveScenarioId,
                    state.CurrentNarrativePhase.ToString(),
                    0,
                    AverageCharacterStat(state, "Desire"),
                    AverageCharacterStat(state, "Restraint"),
                    state.ThemeTracker.Themes.TryGetValue(state.ActiveScenarioId, out var active) ? active.Score : 0),
                new ResetTrigger("Manual scenario override", true, requestedScenarioId),
                cancellationToken);

            ApplyResetSnapshot(state, resetSnapshot);
            state.CurrentNarrativePhase = NarrativePhase.BuildUp;
        }

        PrioritizeScenarioCandidate(state, requestedScenarioId);
    }

    private static void PrioritizeScenarioCandidate(RolePlayAdaptiveState state, string requestedScenarioId)
    {
        if (!state.ThemeTracker.Themes.TryGetValue(requestedScenarioId, out var requested))
        {
            return;
        }

        requested.Breakdown.ChoiceSignal = Math.Max(requested.Breakdown.ChoiceSignal, 20);
        requested.IsScenarioCandidate = true;
        requested.LastCandidateEvaluationTimeUtc = DateTime.UtcNow;
    }

    private static string? ExtractManualOverrideScenarioId(string? content, RolePlayAdaptiveState state)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var lower = content.ToLowerInvariant();
        var hasOverrideIntent = lower.Contains("override", StringComparison.Ordinal)
            || lower.Contains("switch scenario", StringComparison.Ordinal)
            || lower.Contains("set scenario", StringComparison.Ordinal);

        if (!hasOverrideIntent)
        {
            return null;
        }

        foreach (var themeId in state.ThemeTracker.Themes.Keys)
        {
            if (lower.Contains(themeId.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return themeId;
            }
        }

        return null;
    }

    private static bool ContainsAny(string? content, IReadOnlyList<string> markers)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var lower = content.ToLowerInvariant();
        return markers.Any(marker => lower.Contains(marker, StringComparison.Ordinal));
    }

    private static void ApplyResetSnapshot(RolePlayAdaptiveState state, AdaptiveScenarioSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            state.ScenarioHistory.Add(new ScenarioMetadata
            {
                ScenarioId = state.ActiveScenarioId,
                CompletedAtUtc = DateTime.UtcNow,
                InteractionCount = state.InteractionsSinceCommitment,
                PeakThemeScore = state.ThemeTracker.Themes.TryGetValue(state.ActiveScenarioId, out var active) ? (int)Math.Round(active.Score) : 0,
                PeakDesireLevel = (int)Math.Round(AverageCharacterStat(state, "Desire")),
                AverageRestraintLevel = AverageCharacterStat(state, "Restraint")
            });
            state.CompletedScenarios = state.ScenarioHistory.Count;
        }

        state.ActiveScenarioId = snapshot.ActiveScenarioId;
        state.ScenarioCommitmentTimeUtc = null;
        state.InteractionsSinceCommitment = 0;
        state.InteractionsInApproaching = 0;

        if (Enum.TryParse<NarrativePhase>(snapshot.CurrentNarrativePhase, out var parsedPhase))
        {
            state.CurrentNarrativePhase = parsedPhase;
        }
    }

    private static double AverageCharacterStat(RolePlayAdaptiveState state, string statName)
    {
        if (state.CharacterStats.Count == 0)
        {
            return AdaptiveStatCatalog.DefaultValue;
        }

        var values = state.CharacterStats.Values
            .Select(x => x.Stats.TryGetValue(statName, out var value) ? value : AdaptiveStatCatalog.DefaultValue)
            .ToList();

        return values.Average();
    }

    private static double Clamp01(double value) => Math.Clamp(Math.Round(value, 4), 0.0, 1.0);

    private static void EnsureThemeCatalog(ThemeTrackerState tracker, IReadOnlyList<ThemeCatalogEntry> catalogEntries)
    {
        var validIds = catalogEntries.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownIds = tracker.Themes.Keys.Where(x => !validIds.Contains(x)).ToList();
        foreach (var unknownId in unknownIds)
        {
            tracker.Themes.Remove(unknownId);
        }

        foreach (var entry in catalogEntries)
        {
            if (!tracker.Themes.ContainsKey(entry.Id))
            {
                tracker.Themes[entry.Id] = new ThemeTrackerItem
                {
                    ThemeId = entry.Id,
                    ThemeName = entry.Label,
                    Intensity = "None",
                    Score = 0
                };
            }
        }
    }

    private async Task<IReadOnlyList<ThemeCatalogEntry>> LoadRuntimeCatalogEntriesAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        if (_rpThemeService is not null && ShouldUseRpThemeSubsystem(session) && !string.IsNullOrWhiteSpace(session.SelectedRPThemeProfileId))
        {
            var rpThemes = await _rpThemeService.ListThemesByProfileAsync(session.SelectedRPThemeProfileId, includeDisabled: false, cancellationToken);
            if (rpThemes.Count > 0)
            {
                return rpThemes
                    .Select(MapRpThemeToCatalogEntry)
                    .ToList();
            }
        }

        if (_useScenarioDefinitionsForAdaptiveRuntime && _scenarioDefinitionService is not null)
        {
            var definitions = await _scenarioDefinitionService.GetAllAsync(includeDisabled: false, cancellationToken);
            if (definitions.Count > 0)
            {
                return definitions
                    .Select(MapScenarioDefinitionToCatalogEntry)
                    .ToList();
            }
        }

        return await _themeCatalogService.GetAllAsync(includeDisabled: false, cancellationToken);
    }

    private static ThemeCatalogEntry MapRpThemeToCatalogEntry(DreamGenClone.Domain.RolePlay.RPTheme theme)
    {
        var keywordList = theme.Keywords
            .Select(x => (x.Keyword ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var affinities = theme.StatAffinities
            .Where(x => !string.IsNullOrWhiteSpace(x.StatName))
            .GroupBy(x => x.StatName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Value), StringComparer.OrdinalIgnoreCase);

        return new ThemeCatalogEntry
        {
            Id = theme.Id,
            Label = string.IsNullOrWhiteSpace(theme.Label) ? theme.Id : theme.Label,
            Description = theme.Description,
            Keywords = keywordList,
            Weight = Math.Clamp(theme.Weight, 1, 10),
            Category = theme.Category,
            StatAffinities = affinities,
            ScenarioFitRules = string.Empty,
            IsEnabled = theme.IsEnabled,
            IsBuiltIn = false,
            CreatedUtc = theme.CreatedUtc,
            UpdatedUtc = theme.UpdatedUtc
        };
    }

    private static ThemeCatalogEntry MapScenarioDefinitionToCatalogEntry(ScenarioDefinitionEntity definition)
    {
        return new ThemeCatalogEntry
        {
            Id = definition.Id,
            Label = definition.Label,
            Description = definition.Description,
            Keywords = [.. definition.Keywords],
            Weight = definition.Weight,
            Category = definition.Category,
            StatAffinities = new Dictionary<string, int>(definition.StatAffinities, StringComparer.OrdinalIgnoreCase),
            ScenarioFitRules = definition.ScenarioFitRules,
            IsEnabled = definition.IsEnabled,
            IsBuiltIn = false,
            CreatedUtc = definition.CreatedUtc,
            UpdatedUtc = definition.UpdatedUtc
        };
    }

    private static CharacterStatBlock GetOrCreateCharacterStats(RolePlayAdaptiveState state, string actorKey)
    {
        if (state.CharacterStats.TryGetValue(actorKey, out var existing))
        {
            EnsureDefaultStats(existing.Stats);
            return existing;
        }

        var created = new CharacterStatBlock
        {
            CharacterId = actorKey
        };
        EnsureDefaultStats(created.Stats);
        state.CharacterStats[actorKey] = created;
        return created;
    }

    private static void EnsureDefaultStats(Dictionary<string, int> stats)
    {
        foreach (var statName in AdaptiveStatCatalog.CanonicalStatNames)
        {
            if (!stats.ContainsKey(statName))
            {
                stats[statName] = AdaptiveStatCatalog.DefaultValue;
            }
        }
    }

    private static int Score(string content, IReadOnlyList<string> keywords, int weight)
    {
        var matches = keywords.Count(content.Contains);
        return Math.Min(12, matches * weight);
    }

    private static int ScoreGroupedKeywordCoverage(string content, IReadOnlyDictionary<string, IReadOnlyList<string>> groupedKeywords)
    {
        if (groupedKeywords.Count == 0)
        {
            return 0;
        }

        var matchingGroups = groupedKeywords
            .Count(group => group.Value.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase)));

        if (matchingGroups <= 1)
        {
            return 0;
        }

        // Reward cross-group coverage to reflect stronger contextual alignment.
        return Math.Min(6, (matchingGroups - 1) * 2);
    }

    private static string BuildKeywordRationale(
        string themeName,
        string contentLower,
        IReadOnlyList<string> keywords,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? groupedKeywords)
    {
        if (groupedKeywords is not null && groupedKeywords.Count > 0)
        {
            var groupHits = groupedKeywords
                .Select(group => new
                {
                    Group = group.Key,
                    Hits = group.Value
                        .Where(keyword => contentLower.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .Where(x => x.Hits.Count > 0)
                .ToList();

            if (groupHits.Count > 0)
            {
                var groupedText = string.Join(" | ", groupHits.Select(x => $"{x.Group}: {string.Join(", ", x.Hits)}"));
                return $"Matched grouped keywords for {themeName}: {groupedText}";
            }
        }

        return $"Matched keywords for {themeName}: {string.Join(", ", keywords.Where(contentLower.Contains))}";
    }

    private static int ScoreStatSignal(string content, IReadOnlyList<string> keywords, int perKeywordDelta, int maxDelta)
    {
        var matches = keywords.Count(content.Contains);
        if (matches <= 0)
        {
            return 0;
        }

        return Math.Clamp(matches * perKeywordDelta, 0, maxDelta);
    }

    private static int NormalizeInteractionAffinityDelta(int affinityDelta)
    {
        if (affinityDelta == 0)
        {
            return 0;
        }

        var scaledMagnitude = (int)Math.Ceiling(Math.Abs(affinityDelta) / 3.0);
        return Math.Sign(affinityDelta) * Math.Clamp(scaledMagnitude, 1, 2);
    }

    private static void ApplyDelta(Dictionary<string, int> stats, string statName, int delta)
    {
        if (!stats.TryGetValue(statName, out var current))
        {
            current = AdaptiveStatCatalog.DefaultValue;
        }

        var boundedDelta = Math.Clamp(delta, -25, 25);
        stats[statName] = Math.Clamp(current + boundedDelta, 0, 100);
    }

    private static void UpdateTheme(
        RolePlayAdaptiveState state,
        RolePlayInteraction interaction,
        string themeName,
        string themeId,
        string contentLower,
        IReadOnlyList<string> keywords,
        int weight,
        double affinityMultiplier = 1.0,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? groupedKeywords = null)
    {
        var rawSignal = Score(contentLower, keywords, weight);
        rawSignal += groupedKeywords is null ? 0 : ScoreGroupedKeywordCoverage(contentLower, groupedKeywords);
        if (rawSignal <= 0)
        {
            return;
        }

        // T042: Apply affinity multiplier
        var signal = rawSignal * affinityMultiplier;

        var trackerItem = state.ThemeTracker.Themes[themeId];

        trackerItem.Score = Math.Clamp(trackerItem.Score + signal, 0, 100);
        trackerItem.Intensity = trackerItem.Score switch
        {
            < 20 => "Minor",
            < 45 => "Moderate",
            < 70 => "Major",
            _ => "Central"
        };

        trackerItem.Breakdown.InteractionEvidenceSignal = Math.Clamp(trackerItem.Breakdown.InteractionEvidenceSignal + signal, 0, 100);

        state.ThemeTracker.RecentEvidence.Add(new ThemeEvidenceEvent
        {
            InteractionId = interaction.Id,
            ThemeId = themeId,
            SignalType = "interaction-evidence",
            Delta = signal,
            Confidence = 0.65,
            Rationale = BuildKeywordRationale(themeName, contentLower, keywords, groupedKeywords)
        });

        TrimEvidence(state.ThemeTracker);
    }

    private static void RecalculateSelectedThemes(RolePlayAdaptiveState state, RolePlayInteraction interaction)
    {
        var tracker = state.ThemeTracker;
        var ordered = tracker.Themes.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.ThemeId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var previousPrimary = tracker.PrimaryThemeId;
        var previousSecondary = tracker.SecondaryThemeId;

        tracker.PrimaryThemeId = ordered.FirstOrDefault()?.ThemeId;
        tracker.SecondaryThemeId = null;
        tracker.ThemeSelectionRule = "Top1";

        if (ordered.Count >= 2)
        {
            tracker.SecondaryThemeId = ordered[1].ThemeId;
            tracker.ThemeSelectionRule = "Top2Blend";
        }

        if (!string.Equals(previousPrimary, tracker.PrimaryThemeId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previousSecondary, tracker.SecondaryThemeId, StringComparison.OrdinalIgnoreCase))
        {
            var selectedThemes = new[] { tracker.PrimaryThemeId, tracker.SecondaryThemeId }
                .Where(x => !string.IsNullOrWhiteSpace(x));
            tracker.RecentEvidence.Add(new ThemeEvidenceEvent
            {
                InteractionId = interaction.Id,
                ThemeId = "theme-selection",
                SignalType = "selection-rule",
                Delta = 0,
                Confidence = 0.8,
                Rationale = $"Applied {tracker.ThemeSelectionRule}: {string.Join(", ", selectedThemes)}"
            });
            TrimEvidence(tracker);
        }
    }

    private static void TrimEvidence(ThemeTrackerState tracker)
    {
        if (tracker.RecentEvidence.Count > 100)
        {
            tracker.RecentEvidence.RemoveRange(0, tracker.RecentEvidence.Count - 100);
        }
    }

    private async Task<Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>> LoadRpThemeKeywordGroupsByThemeIdAsync(
        RolePlaySession session,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.OrdinalIgnoreCase);
        if (_rpThemeService is null || !ShouldUseRpThemeSubsystem(session) || string.IsNullOrWhiteSpace(session.SelectedRPThemeProfileId))
        {
            return result;
        }

        var themes = await _rpThemeService.ListThemesByProfileAsync(session.SelectedRPThemeProfileId, includeDisabled: false, cancellationToken);
        foreach (var theme in themes)
        {
            var grouped = theme.Keywords
                .Where(x => !string.IsNullOrWhiteSpace(x.Keyword))
                .GroupBy(x => string.IsNullOrWhiteSpace(x.GroupName) ? "General" : x.GroupName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)g.Select(x => (x.Keyword ?? string.Empty).Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);

            result[theme.Id] = grouped;
        }

        return result;
    }

    private static bool IsNarrativeOrSystemInteraction(RolePlayInteraction interaction, string actorKey)
    {
        if (interaction.InteractionType == InteractionType.System)
        {
            return true;
        }

        return string.Equals(actorKey, "Narrative", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actorKey, "System", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actorKey, "Instruction", StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveNonCharacterStats(RolePlayAdaptiveState state)
    {
        var removals = state.CharacterStats.Keys
            .Where(x => string.Equals(x, "Narrative", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x, "System", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x, "Instruction", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in removals)
        {
            state.CharacterStats.Remove(key);
        }
    }

    public async Task SeedFromScenarioAsync(
        RolePlaySession session,
        Scenario scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(scenario);

        var state = session.AdaptiveState ?? new RolePlayAdaptiveState();

        // --- T030: Initialize ThemeTracker from catalog entries ---
        var catalogEntries = await LoadRuntimeCatalogEntriesAsync(session, cancellationToken);
        EnsureThemeCatalog(state.ThemeTracker, catalogEntries);

        // --- T030: Resolve ThemeProfile preferences and apply ChoiceSignal ---
        var blockedCount = 0;
        if (_rpThemeService is not null && ShouldUseRpThemeSubsystem(session) && !string.IsNullOrWhiteSpace(session.SelectedRPThemeProfileId))
        {
            var assignments = await _rpThemeService.ListProfileAssignmentsAsync(session.SelectedRPThemeProfileId, cancellationToken);
            var themes = await _rpThemeService.ListThemesAsync(includeDisabled: false, cancellationToken: cancellationToken);
            var themesById = themes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var assignment in assignments.Where(x => x.IsEnabled))
            {
                if (!themesById.TryGetValue(assignment.ThemeId, out var assignedTheme))
                {
                    continue;
                }

                var matchedTracker = state.ThemeTracker.Themes.Values.FirstOrDefault(x =>
                    string.Equals(x.ThemeId, assignedTheme.Id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.ThemeName, assignedTheme.Label, StringComparison.OrdinalIgnoreCase));

                if (matchedTracker is null)
                {
                    continue;
                }

                if (assignment.Tier == DreamGenClone.Domain.RolePlay.RPThemeTier.HardDealBreaker)
                {
                    matchedTracker.Blocked = true;
                    matchedTracker.Score = 0;
                    matchedTracker.Breakdown.ChoiceSignal = 0;
                    blockedCount++;
                    continue;
                }

                var choiceSignal = assignment.Tier switch
                {
                    DreamGenClone.Domain.RolePlay.RPThemeTier.MustHave => 15,
                    DreamGenClone.Domain.RolePlay.RPThemeTier.StronglyPrefer => 8,
                    DreamGenClone.Domain.RolePlay.RPThemeTier.NiceToHave => 3,
                    DreamGenClone.Domain.RolePlay.RPThemeTier.Discouraged => -5,
                    _ => 0
                };

                matchedTracker.Breakdown.ChoiceSignal = choiceSignal;
                matchedTracker.Score = Math.Clamp(matchedTracker.Score + choiceSignal, 0, 100);

                if (assignment.Tier == DreamGenClone.Domain.RolePlay.RPThemeTier.MustHave)
                {
                    matchedTracker.Score = Math.Clamp(matchedTracker.Score + 3, 0, 100);
                }

                matchedTracker.Intensity = matchedTracker.Score switch
                {
                    < 20 => "Minor",
                    < 45 => "Moderate",
                    < 70 => "Major",
                    _ => "Central"
                };
            }
        }
        else if (_themePreferenceService is not null && !string.IsNullOrWhiteSpace(session.SelectedThemeProfileId))
        {
            var preferences = await _themePreferenceService.ListByProfileAsync(session.SelectedThemeProfileId, cancellationToken);
            foreach (var pref in preferences)
            {
                var matchedEntry = FindCatalogEntryByPreference(catalogEntries, pref);
                if (matchedEntry is null) continue;

                if (!state.ThemeTracker.Themes.TryGetValue(matchedEntry.Id, out var trackerItem)) continue;

                if (pref.Tier == ThemeTier.HardDealBreaker)
                {
                    trackerItem.Blocked = true;
                    trackerItem.Score = 0;
                    trackerItem.Breakdown.ChoiceSignal = 0;
                    blockedCount++;
                    continue;
                }

                var choiceSignal = pref.Tier switch
                {
                    ThemeTier.MustHave => 15,
                    ThemeTier.StronglyPrefer => 8,
                    ThemeTier.NiceToHave => 3,
                    ThemeTier.Dislike => -5,
                    _ => 0
                };

                trackerItem.Breakdown.ChoiceSignal = choiceSignal;
                trackerItem.Score = Math.Clamp(trackerItem.Score + choiceSignal, 0, 100);

                // MustHave +3 persistent affinity bonus
                if (pref.Tier == ThemeTier.MustHave)
                {
                    trackerItem.Score = Math.Clamp(trackerItem.Score + 3, 0, 100);
                }

                trackerItem.Intensity = trackerItem.Score switch
                {
                    < 20 => "Minor",
                    < 45 => "Moderate",
                    < 70 => "Major",
                    _ => "Central"
                };
            }
        }

        // --- T031: Scenario text keyword scoring ---
        SteeringProfile? styleProfile = null;
        if (_steeringProfileService is not null && !string.IsNullOrWhiteSpace(session.SelectedSteeringProfileId))
        {
            styleProfile = await _steeringProfileService.GetAsync(session.SelectedSteeringProfileId, cancellationToken);
        }

        foreach (var entry in catalogEntries)
        {
            if (!state.ThemeTracker.Themes.TryGetValue(entry.Id, out var trackerItem)) continue;
            if (trackerItem.Blocked) continue;

            var scenarioPhaseSignal = ScoreScenarioKeywords(scenario, entry.Keywords, entry.Weight, styleProfile, entry.Id);
            if (scenarioPhaseSignal > 0)
            {
                trackerItem.Breakdown.ScenarioPhaseSignal = Math.Clamp(scenarioPhaseSignal, 0, 100);
                trackerItem.Score = Math.Clamp(trackerItem.Score + scenarioPhaseSignal, 0, 100);
                trackerItem.Intensity = trackerItem.Score switch
                {
                    < 20 => "Minor",
                    < 45 => "Moderate",
                    < 70 => "Major",
                    _ => "Central"
                };
            }
        }

        // --- T032: Apply StyleProfile.StatBias and ThemeCatalogEntry.StatAffinities ---
        if (styleProfile?.StatBias is { Count: > 0 })
        {
            foreach (var (actorKey, charBlock) in state.CharacterStats)
            {
                foreach (var (statName, bias) in styleProfile.StatBias)
                {
                    var normalized = AdaptiveStatCatalog.NormalizeLegacyStatName(statName);
                    if (!charBlock.Stats.TryGetValue(normalized, out var current))
                    {
                        current = AdaptiveStatCatalog.DefaultValue;
                    }
                    charBlock.Stats[normalized] = Math.Clamp(current + bias, 0, 100);
                }
            }
        }

        // Apply StatAffinities deltas from scoring catalog themes
        foreach (var entry in catalogEntries)
        {
            if (entry.StatAffinities is not { Count: > 0 }) continue;
            if (!state.ThemeTracker.Themes.TryGetValue(entry.Id, out var trackerItem)) continue;
            if (trackerItem.Blocked || trackerItem.Score <= 0) continue;

            foreach (var (actorKey, charBlock) in state.CharacterStats)
            {
                foreach (var (statName, affinityDelta) in entry.StatAffinities)
                {
                    var normalized = AdaptiveStatCatalog.NormalizeLegacyStatName(statName);
                    if (!charBlock.Stats.TryGetValue(normalized, out var current))
                    {
                        current = AdaptiveStatCatalog.DefaultValue;
                    }
                    charBlock.Stats[normalized] = Math.Clamp(current + affinityDelta, 0, 100);
                }
            }
        }

        state.ThemeTracker.UpdatedUtc = DateTime.UtcNow;
        session.AdaptiveState = state;

        // --- T034: Logging ---
        var topSeeded = state.ThemeTracker.Themes.Values
            .Where(t => !t.Blocked && t.Score > 0)
            .OrderByDescending(t => t.Score)
            .Take(3)
            .Select(t => $"{t.ThemeName}={t.Score:F0}")
            .ToList();

        _logger?.LogInformation(
            "Seeded adaptive state for session {SessionId}: {ThemeCount} themes, {BlockedCount} blocked, StatBias={StatBiasApplied}, top=[{TopThemes}]",
            session.Id,
            catalogEntries.Count,
            blockedCount,
            styleProfile?.StatBias?.Count > 0,
            string.Join(", ", topSeeded));
    }

    private bool ShouldUseRpThemeSubsystem(RolePlaySession session)
    {
        if (!_useRpThemeSubsystem)
        {
            return false;
        }

        if (!_useRpThemeSubsystemForNewSessionsOnly)
        {
            return true;
        }

        return session.UseRpThemeSubsystem;
    }

    private static ThemeCatalogEntry? FindCatalogEntryByPreference(
        IReadOnlyList<ThemeCatalogEntry> catalogEntries,
        ThemePreference pref)
    {
        // Match by name (case-insensitive) against catalog entry label or id
        return catalogEntries.FirstOrDefault(e =>
            string.Equals(e.Label, pref.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.Id, pref.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static double ScoreScenarioKeywords(
        Scenario scenario,
        IReadOnlyList<string> keywords,
        int weight,
        SteeringProfile? styleProfile,
        string themeId)
    {
        if (keywords.Count == 0) return 0;

        double total = 0;

        // Opening/Example text at 0.6× weight
        foreach (var opening in scenario.Openings)
        {
            if (!string.IsNullOrWhiteSpace(opening.Text))
            {
                total += ScoreText(opening.Text, keywords, weight) * 0.6;
            }
        }
        foreach (var example in scenario.Examples)
        {
            if (!string.IsNullOrWhiteSpace(example.Text))
            {
                total += ScoreText(example.Text, keywords, weight) * 0.6;
            }
        }

        // Plot/Setting/Narrative/Characters/Locations/Objects at 0.4× weight
        total += ScoreText(scenario.Plot.Description, keywords, weight) * 0.4;
        foreach (var conflict in scenario.Plot.Conflicts)
            total += ScoreText(conflict, keywords, weight) * 0.4;
        foreach (var goal in scenario.Plot.Goals)
            total += ScoreText(goal, keywords, weight) * 0.4;

        total += ScoreText(scenario.Setting.WorldDescription, keywords, weight) * 0.4;
        foreach (var detail in scenario.Setting.EnvironmentalDetails)
            total += ScoreText(detail, keywords, weight) * 0.4;

        total += ScoreText(scenario.Narrative.NarrativeTone, keywords, weight) * 0.4;
        total += ScoreText(scenario.Narrative.ProseStyle, keywords, weight) * 0.4;
        foreach (var guideline in scenario.Narrative.NarrativeGuidelines)
            total += ScoreText(guideline, keywords, weight) * 0.4;

        foreach (var character in scenario.Characters)
        {
            total += ScoreText(character.Name, keywords, weight) * 0.4;
            total += ScoreText(character.Description, keywords, weight) * 0.4;
        }

        foreach (var location in scenario.Locations)
        {
            total += ScoreText(location.Name, keywords, weight) * 0.4;
            total += ScoreText(location.Description, keywords, weight) * 0.4;
        }

        foreach (var obj in scenario.Objects)
        {
            total += ScoreText(obj.Name, keywords, weight) * 0.4;
            total += ScoreText(obj.Description, keywords, weight) * 0.4;
        }

        // Character stat deltas at 0.3× weight
        foreach (var character in scenario.Characters)
        {
            if (character.BaseStats.Count > 0)
            {
                foreach (var (statName, _) in character.BaseStats)
                {
                    total += ScoreText(statName, keywords, weight) * 0.3;
                }
            }
        }

        // Multiply by StyleProfile.ThemeAffinities when present
        if (styleProfile?.ThemeAffinities is { Count: > 0 }
            && styleProfile.ThemeAffinities.TryGetValue(themeId, out var affinityMultiplier)
            && affinityMultiplier != 0)
        {
            total *= (1.0 + affinityMultiplier * 0.1);
        }

        return total;
    }

    private static double ScoreText(string? text, IReadOnlyList<string> keywords, int weight)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var lower = text.ToLowerInvariant();
        var matches = keywords.Count(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase));
        return Math.Min(12, matches * weight);
    }
}