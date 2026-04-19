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
    private const int ManualScenarioOverrideLockInteractions = 8;
    private const int DefaultThemeAffinityStackLimit = 1;
    private const int DefaultEarlyTurnInteractionThreshold = 3;
    private const int DefaultEarlyTurnPerStatDeltaCap = 2;
    private const int DefaultPerInteractionTotalDeltaBudget = 10;
    private const int ResetExtremeLowThreshold = 10;
    private const int ResetExtremeHighThreshold = 90;
    private const int ResetExtremeHardTarget = 60;
    private const double ResetSoftRetentionFactor = 0.35;
    private const double DefaultSuppressedEvidenceMultiplier = 0.20;
    private const double DefaultSuppressedEvidencePerTurnCap = 1.5;
    private const int DefaultActiveScenarioNoHitStaleTurns = 2;
    private const double DefaultPivotOvertakeMarginDefault = 2.0;
    private const double DefaultPivotOvertakeMarginWhenStale = 1.0;
    private const int DefaultPivotCommittedInteractionWindow = 3;
    private const int DefaultPivotCommittedInteractionWindowWhenStale = 8;

    private readonly IThemeCatalogService _themeCatalogService;
    private readonly IIntensityProfileService? _intensityProfileService;
    private readonly IScenarioDefinitionService? _scenarioDefinitionService;
    private readonly IScenarioSelectionEngine? _scenarioSelectionEngine;
    private readonly INarrativePhaseManager? _narrativePhaseManager;
    private readonly IThemePreferenceService? _themePreferenceService;
    private readonly IRPThemeService? _rpThemeService;
    private readonly IStatKeywordCategoryService? _statKeywordCategoryService;
    private readonly ISteeringProfileService? _steeringProfileService;
    private readonly IRolePlayDebugEventSink? _debugEventSink;
    private readonly ILogger<RolePlayAdaptiveStateService>? _logger;
    private readonly bool _useScenarioDefinitionsForAdaptiveRuntime;
    private readonly bool _useRpThemeSubsystem;
    private readonly bool _useRpThemeSubsystemForNewSessionsOnly;
    private readonly int _themeAffinityStackLimit;
    private readonly int _earlyTurnInteractionThreshold;
    private readonly int _earlyTurnPerStatDeltaCap;
    private readonly int _perInteractionTotalDeltaBudget;
    private readonly int _themeAffinityCapBuildUp;
    private readonly int _themeAffinityCapCommitted;
    private readonly int _themeAffinityCapApproaching;
    private readonly int _themeAffinityCapClimax;
    private readonly int _themeAffinityCapReset;
    private readonly double _suppressedEvidenceMultiplier;
    private readonly double _suppressedEvidencePerTurnCap;
    private readonly int _activeScenarioNoHitStaleTurns;
    private readonly double _pivotOvertakeMarginDefault;
    private readonly double _pivotOvertakeMarginWhenStale;
    private readonly int _pivotCommittedInteractionWindow;
    private readonly int _pivotCommittedInteractionWindowWhenStale;

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
        _themeAffinityStackLimit = DefaultThemeAffinityStackLimit;
        _earlyTurnInteractionThreshold = DefaultEarlyTurnInteractionThreshold;
        _earlyTurnPerStatDeltaCap = DefaultEarlyTurnPerStatDeltaCap;
        _perInteractionTotalDeltaBudget = DefaultPerInteractionTotalDeltaBudget;
        _themeAffinityCapBuildUp = 0;
        _themeAffinityCapCommitted = 1;
        _themeAffinityCapApproaching = 1;
        _themeAffinityCapClimax = 2;
        _themeAffinityCapReset = 0;
        _suppressedEvidenceMultiplier = DefaultSuppressedEvidenceMultiplier;
        _suppressedEvidencePerTurnCap = DefaultSuppressedEvidencePerTurnCap;
        _activeScenarioNoHitStaleTurns = DefaultActiveScenarioNoHitStaleTurns;
        _pivotOvertakeMarginDefault = DefaultPivotOvertakeMarginDefault;
        _pivotOvertakeMarginWhenStale = DefaultPivotOvertakeMarginWhenStale;
        _pivotCommittedInteractionWindow = DefaultPivotCommittedInteractionWindow;
        _pivotCommittedInteractionWindowWhenStale = DefaultPivotCommittedInteractionWindowWhenStale;
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
        _themeAffinityStackLimit = DefaultThemeAffinityStackLimit;
        _earlyTurnInteractionThreshold = DefaultEarlyTurnInteractionThreshold;
        _earlyTurnPerStatDeltaCap = DefaultEarlyTurnPerStatDeltaCap;
        _perInteractionTotalDeltaBudget = DefaultPerInteractionTotalDeltaBudget;
        _themeAffinityCapBuildUp = 0;
        _themeAffinityCapCommitted = 1;
        _themeAffinityCapApproaching = 1;
        _themeAffinityCapClimax = 2;
        _themeAffinityCapReset = 0;
        _suppressedEvidenceMultiplier = DefaultSuppressedEvidenceMultiplier;
        _suppressedEvidencePerTurnCap = DefaultSuppressedEvidencePerTurnCap;
        _activeScenarioNoHitStaleTurns = DefaultActiveScenarioNoHitStaleTurns;
        _pivotOvertakeMarginDefault = DefaultPivotOvertakeMarginDefault;
        _pivotOvertakeMarginWhenStale = DefaultPivotOvertakeMarginWhenStale;
        _pivotCommittedInteractionWindow = DefaultPivotCommittedInteractionWindow;
        _pivotCommittedInteractionWindowWhenStale = DefaultPivotCommittedInteractionWindowWhenStale;
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
        _themeAffinityStackLimit = DefaultThemeAffinityStackLimit;
        _earlyTurnInteractionThreshold = DefaultEarlyTurnInteractionThreshold;
        _earlyTurnPerStatDeltaCap = DefaultEarlyTurnPerStatDeltaCap;
        _perInteractionTotalDeltaBudget = DefaultPerInteractionTotalDeltaBudget;
        _themeAffinityCapBuildUp = 0;
        _themeAffinityCapCommitted = 1;
        _themeAffinityCapApproaching = 1;
        _themeAffinityCapClimax = 2;
        _themeAffinityCapReset = 0;
        _suppressedEvidenceMultiplier = DefaultSuppressedEvidenceMultiplier;
        _suppressedEvidencePerTurnCap = DefaultSuppressedEvidencePerTurnCap;
        _activeScenarioNoHitStaleTurns = DefaultActiveScenarioNoHitStaleTurns;
        _pivotOvertakeMarginDefault = DefaultPivotOvertakeMarginDefault;
        _pivotOvertakeMarginWhenStale = DefaultPivotOvertakeMarginWhenStale;
        _pivotCommittedInteractionWindow = DefaultPivotCommittedInteractionWindow;
        _pivotCommittedInteractionWindowWhenStale = DefaultPivotCommittedInteractionWindowWhenStale;
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
        IStatKeywordCategoryService? statKeywordCategoryService,
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
        _statKeywordCategoryService = statKeywordCategoryService;
        _steeringProfileService = styleProfileService;
        _debugEventSink = debugEventSink;
        _logger = logger;
        _useScenarioDefinitionsForAdaptiveRuntime = storyAnalysisOptions?.Value.UseScenarioDefinitionsForAdaptiveRuntime == true;
        _useRpThemeSubsystem = storyAnalysisOptions?.Value.UseRpThemeSubsystem ?? true;
        _useRpThemeSubsystemForNewSessionsOnly = storyAnalysisOptions?.Value.UseRpThemeSubsystemForNewSessionsOnly ?? true;
        _themeAffinityStackLimit = Math.Max(1, storyAnalysisOptions?.Value.AdaptiveThemeAffinityStackLimit ?? DefaultThemeAffinityStackLimit);
        _earlyTurnInteractionThreshold = Math.Max(1, storyAnalysisOptions?.Value.AdaptiveEarlyTurnInteractionThreshold ?? DefaultEarlyTurnInteractionThreshold);
        _earlyTurnPerStatDeltaCap = Math.Max(1, storyAnalysisOptions?.Value.AdaptiveEarlyTurnPerStatDeltaCap ?? DefaultEarlyTurnPerStatDeltaCap);
        _perInteractionTotalDeltaBudget = Math.Max(1, storyAnalysisOptions?.Value.AdaptivePerInteractionTotalDeltaBudget ?? DefaultPerInteractionTotalDeltaBudget);
        _themeAffinityCapBuildUp = Math.Max(0, storyAnalysisOptions?.Value.AdaptiveThemeAffinityCapBuildUp ?? 0);
        _themeAffinityCapCommitted = Math.Max(0, storyAnalysisOptions?.Value.AdaptiveThemeAffinityCapCommitted ?? 1);
        _themeAffinityCapApproaching = Math.Max(0, storyAnalysisOptions?.Value.AdaptiveThemeAffinityCapApproaching ?? 1);
        _themeAffinityCapClimax = Math.Max(0, storyAnalysisOptions?.Value.AdaptiveThemeAffinityCapClimax ?? 2);
        _themeAffinityCapReset = Math.Max(0, storyAnalysisOptions?.Value.AdaptiveThemeAffinityCapReset ?? 0);
        _suppressedEvidenceMultiplier = Math.Clamp(storyAnalysisOptions?.Value.SuppressedEvidenceMultiplier ?? DefaultSuppressedEvidenceMultiplier, 0.0, 1.0);
        _suppressedEvidencePerTurnCap = Math.Max(0.0, storyAnalysisOptions?.Value.SuppressedEvidencePerTurnCap ?? DefaultSuppressedEvidencePerTurnCap);
        _activeScenarioNoHitStaleTurns = Math.Max(1, storyAnalysisOptions?.Value.ActiveScenarioNoHitStaleTurns ?? DefaultActiveScenarioNoHitStaleTurns);
        _pivotOvertakeMarginDefault = Math.Max(0.0, storyAnalysisOptions?.Value.PivotOvertakeMarginDefault ?? DefaultPivotOvertakeMarginDefault);
        _pivotOvertakeMarginWhenStale = Math.Max(0.0, storyAnalysisOptions?.Value.PivotOvertakeMarginWhenStale ?? DefaultPivotOvertakeMarginWhenStale);
        _pivotCommittedInteractionWindow = Math.Max(1, storyAnalysisOptions?.Value.PivotCommittedInteractionWindow ?? DefaultPivotCommittedInteractionWindow);
        _pivotCommittedInteractionWindowWhenStale = Math.Max(_pivotCommittedInteractionWindow, storyAnalysisOptions?.Value.PivotCommittedInteractionWindowWhenStale ?? DefaultPivotCommittedInteractionWindowWhenStale);
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IScenarioSelectionEngine? scenarioSelectionEngine,
        IThemePreferenceService themePreferenceService,
        IRPThemeService? rpThemeService,
        ISteeringProfileService styleProfileService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
        : this(themeCatalogService, null, scenarioSelectionEngine, null, themePreferenceService, rpThemeService, null, styleProfileService, debugEventSink, logger)
    {
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IThemePreferenceService themePreferenceService,
        IRPThemeService? rpThemeService,
        ISteeringProfileService styleProfileService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
        : this(themeCatalogService, null, null, null, themePreferenceService, rpThemeService, null, styleProfileService, debugEventSink, logger)
    {
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IThemePreferenceService themePreferenceService,
        ISteeringProfileService styleProfileService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
        : this(themeCatalogService, null, null, null, themePreferenceService, null, null, styleProfileService, debugEventSink, logger)
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
        RemoveNonCanonicalStatEntries(state);

        var actorKey = string.IsNullOrWhiteSpace(interaction.ActorName) ? "Unknown" : interaction.ActorName.Trim();
        var trackCharacterStats = !IsNarrativeOrSystemInteraction(interaction, actorKey);
        CharacterStatBlock? actorStats = null;
        Dictionary<string, int>? statsBefore = null;
        var statDeltaContributors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var rawStatDeltasForEvent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (trackCharacterStats)
        {
            actorStats = GetOrCreateCharacterStats(state, actorKey);
            statsBefore = new Dictionary<string, int>(actorStats.Stats, StringComparer.OrdinalIgnoreCase);
        }

        var phaseAffinityCap = GetThemeAffinityPhaseCap(state.CurrentNarrativePhase);
        var themeAffinityCandidates = new List<ThemeAffinityCandidate>();

        var primaryBefore = state.ThemeTracker.PrimaryThemeId;
        var secondaryBefore = state.ThemeTracker.SecondaryThemeId;

        var content = interaction.Content ?? string.Empty;
        var contentLower = content.ToLowerInvariant();
        var statKeywordCategories = await LoadStatKeywordCategoriesAsync(cancellationToken);

        // Direct stat mutation driven by keyword categories.
        if (actorStats is not null)
        {
            foreach (var category in statKeywordCategories)
            {
                var normalizedStatName = ResolveSupportedStatName(category.StatName);
                if (normalizedStatName is null)
                {
                    continue;
                }

                var keywords = category.Keywords
                    .Where(x => !string.IsNullOrWhiteSpace(x.Keyword))
                    .Select(x => x.Keyword.Trim())
                    .ToList();
                if (keywords.Count == 0)
                {
                    continue;
                }

                var keywordDelta = ScoreStatSignalWithDirection(contentLower, keywords, category.PerKeywordDelta, category.MaxAbsDelta);
                if (keywordDelta == 0)
                {
                    continue;
                }

                var reasonKey = string.IsNullOrWhiteSpace(category.Name)
                    ? normalizedStatName.ToLowerInvariant()
                    : category.Name.Trim().ToLowerInvariant().Replace(' ', '-');

                ApplyTrackedDelta(actorStats.Stats, normalizedStatName, keywordDelta, $"keyword:{reasonKey}");
            }

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
                groupedKeywordsByThemeId.TryGetValue(entry.Id, out var suppressedGroupedKeywords);
                suppressedSignal += suppressedGroupedKeywords is null ? 0 : ScoreGroupedKeywordCoverage(contentLower, suppressedGroupedKeywords);

                if (suppressedSignal > 0)
                {
                    trackerItem.SuppressedHitCount++;
                    if (!trackerItem.Blocked && _suppressedEvidenceMultiplier > 0 && _suppressedEvidencePerTurnCap > 0)
                    {
                        var suppressedDelta = Math.Min(_suppressedEvidencePerTurnCap, suppressedSignal * _suppressedEvidenceMultiplier);
                        if (suppressedDelta > 0)
                        {
                            trackerItem.Score = Math.Clamp(trackerItem.Score + suppressedDelta, 0, 100);
                            trackerItem.Intensity = trackerItem.Score switch
                            {
                                < 20 => "Minor",
                                < 45 => "Moderate",
                                < 70 => "Major",
                                _ => "Central"
                            };
                            trackerItem.Breakdown.InteractionEvidenceSignal = Math.Clamp(trackerItem.Breakdown.InteractionEvidenceSignal + suppressedDelta, 0, 100);

                            state.ThemeTracker.RecentEvidence.Add(new ThemeEvidenceEvent
                            {
                                InteractionId = interaction.Id,
                                ThemeId = entry.Id,
                                SignalType = "suppressed-interaction-evidence",
                                Delta = suppressedDelta,
                                Confidence = 0.45,
                                Rationale = BuildKeywordRationale(entry.Label, contentLower, entry.Keywords, suppressedGroupedKeywords)
                            });

                            TrimEvidence(state.ThemeTracker);
                        }
                    }
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

            // T043: Collect theme-affinity candidates; apply by policy after ranking.
            if (actorStats is not null && entry.StatAffinities is { Count: > 0 } && phaseAffinityCap > 0)
            {
                if (state.ThemeTracker.Themes.TryGetValue(entry.Id, out var item) && item.Score > 0)
                {
                    var themeSignal = Score(contentLower, entry.Keywords, entry.Weight);
                    if (themeSignal > 0)
                    {
                        var candidateStatDeltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        foreach (var (statName, affinityDelta) in entry.StatAffinities)
                        {
                            var normalized = ResolveSupportedStatName(statName);
                            if (normalized is null)
                            {
                                continue;
                            }

                            var normalizedDelta = NormalizeInteractionAffinityDelta(affinityDelta);
                            if (normalizedDelta == 0)
                            {
                                continue;
                            }

                            var phaseAdjustedDelta = ApplyThemeAffinityPhaseCap(normalizedDelta, phaseAffinityCap);
                            if (phaseAdjustedDelta == 0)
                            {
                                continue;
                            }

                            if (!candidateStatDeltas.TryGetValue(normalized, out var existing))
                            {
                                existing = 0;
                            }

                            candidateStatDeltas[normalized] = existing + phaseAdjustedDelta;
                        }

                        if (candidateStatDeltas.Count > 0)
                        {
                            themeAffinityCandidates.Add(new ThemeAffinityCandidate(entry.Id, themeSignal, item.Score, candidateStatDeltas));
                        }
                    }
                }
            }
        }

        if (actorStats is not null && themeAffinityCandidates.Count > 0)
        {
            var selectedThemeAffinityCandidates = SelectThemeAffinityCandidates(themeAffinityCandidates, _themeAffinityStackLimit);
            foreach (var candidate in selectedThemeAffinityCandidates)
            {
                foreach (var (statName, delta) in candidate.StatDeltas)
                {
                    ApplyTrackedDelta(actorStats.Stats, statName, delta, $"theme-affinity:{candidate.ThemeId}");
                }
            }
        }

        ApplyInteractionDeltaPolicyCaps();

        RecalculateSelectedThemes(state, interaction);
        await EvaluateScenarioCommitmentAsync(session, interaction, state, cancellationToken);
        state.ThemeTracker.UpdatedUtc = DateTime.UtcNow;
        RemoveNonCanonicalStatEntries(state);

        session.AdaptiveState = state;
        await EvaluateAdaptiveIntensityTransitionAsync(session, interaction, cancellationToken);

        if (_debugEventSink is not null)
        {
            try
            {
                var statDeltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var statDeltaReasons = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
                if (actorStats is not null && statsBefore is not null)
                {
                    foreach (var stat in actorStats.Stats)
                    {
                        var before = statsBefore.TryGetValue(stat.Key, out var existing) ? existing : 50;
                        if (before != stat.Value)
                        {
                            statDeltas[stat.Key] = stat.Value - before;
                            if (statDeltaContributors.TryGetValue(stat.Key, out var reasons) && reasons.Count > 0)
                            {
                                statDeltaReasons[stat.Key] = reasons.ToArray();
                            }
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
                        rawStatDeltas = rawStatDeltasForEvent,
                        statDeltas,
                        statDeltaReasons,
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

        void ApplyTrackedDelta(
            Dictionary<string, int> stats,
            string statName,
            int delta,
            string reason)
        {
            if (delta == 0)
            {
                return;
            }

            ApplyDelta(stats, statName, delta);

            if (!statDeltaContributors.TryGetValue(statName, out var reasons))
            {
                reasons = [];
                statDeltaContributors[statName] = reasons;
            }

            var sign = delta > 0 ? "+" : string.Empty;
            reasons.Add($"{reason}({sign}{delta})");
        }

        void ApplyInteractionDeltaPolicyCaps()
        {
            if (actorStats is null || statsBefore is null || !trackCharacterStats)
            {
                return;
            }

            var rawDeltas = BuildCurrentDeltas(actorStats.Stats, statsBefore);
            if (rawDeltas.Count == 0)
            {
                return;
            }

            rawStatDeltasForEvent = new Dictionary<string, int>(rawDeltas, StringComparer.OrdinalIgnoreCase);

            var adjustedDeltas = new Dictionary<string, int>(rawDeltas, StringComparer.OrdinalIgnoreCase);
            var isEarlyActorTurn = IsEarlyActorTurn(session, interaction, actorKey);
            if (isEarlyActorTurn)
            {
                foreach (var statName in adjustedDeltas.Keys.ToList())
                {
                    var original = adjustedDeltas[statName];
                    var capped = Math.Sign(original) * Math.Min(Math.Abs(original), _earlyTurnPerStatDeltaCap);
                    if (capped != original)
                    {
                        adjustedDeltas[statName] = capped;
                        AppendPolicyReason(statName, $"policy:early-turn-per-stat-cap({original}->{capped})");
                    }
                }
            }

            var totalBeforeBudgetCap = adjustedDeltas.Values.Sum(x => Math.Abs(x));
            if (totalBeforeBudgetCap > _perInteractionTotalDeltaBudget)
            {
                var beforeBudgetDeltas = new Dictionary<string, int>(adjustedDeltas, StringComparer.OrdinalIgnoreCase);
                var currentTotal = totalBeforeBudgetCap;
                while (currentTotal > _perInteractionTotalDeltaBudget)
                {
                    var keyToReduce = adjustedDeltas
                        .Where(x => x.Value != 0)
                        .OrderByDescending(x => Math.Abs(x.Value))
                        .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.Key)
                        .FirstOrDefault();

                    if (string.IsNullOrWhiteSpace(keyToReduce))
                    {
                        break;
                    }

                    adjustedDeltas[keyToReduce] += adjustedDeltas[keyToReduce] > 0 ? -1 : 1;
                    currentTotal--;
                }

                foreach (var (statName, beforeBudgetDelta) in beforeBudgetDeltas)
                {
                    var afterBudgetDelta = adjustedDeltas[statName];
                    if (beforeBudgetDelta != afterBudgetDelta)
                    {
                        AppendPolicyReason(
                            statName,
                            $"policy:per-turn-budget-cap({beforeBudgetDelta}->{afterBudgetDelta},total={totalBeforeBudgetCap}->{_perInteractionTotalDeltaBudget})");
                    }
                }
            }

            foreach (var (statName, finalDelta) in adjustedDeltas)
            {
                var baseline = statsBefore.TryGetValue(statName, out var before) ? before : AdaptiveStatCatalog.DefaultValue;
                actorStats.Stats[statName] = Math.Clamp(baseline + finalDelta, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue);
            }
        }

        static Dictionary<string, int> BuildCurrentDeltas(
            IReadOnlyDictionary<string, int> current,
            IReadOnlyDictionary<string, int> baseline)
        {
            var deltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var statKeys = current.Keys
                .Concat(baseline.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var statName in statKeys)
            {
                var now = current.TryGetValue(statName, out var currentValue) ? currentValue : AdaptiveStatCatalog.DefaultValue;
                var before = baseline.TryGetValue(statName, out var beforeValue) ? beforeValue : AdaptiveStatCatalog.DefaultValue;
                var delta = now - before;
                if (delta != 0)
                {
                    deltas[statName] = delta;
                }
            }

            return deltas;
        }

        void AppendPolicyReason(string statName, string policyReason)
        {
            if (!statDeltaContributors.TryGetValue(statName, out var reasons))
            {
                reasons = [];
                statDeltaContributors[statName] = reasons;
            }

            reasons.Add(policyReason);
        }

        bool IsEarlyActorTurn(RolePlaySession currentSession, RolePlayInteraction currentInteraction, string currentActorKey)
        {
            var actorTurnCount = currentSession.Interactions
                .Count(existing => !IsNarrativeOrSystemInteraction(
                    existing,
                    string.IsNullOrWhiteSpace(existing.ActorName) ? "Unknown" : existing.ActorName.Trim()));

            var interactionAlreadyCounted = currentSession.Interactions.Any(existing =>
                string.Equals(existing.Id, currentInteraction.Id, StringComparison.OrdinalIgnoreCase));

            if (!interactionAlreadyCounted && !IsNarrativeOrSystemInteraction(currentInteraction, currentActorKey))
            {
                actorTurnCount++;
            }

            return actorTurnCount <= _earlyTurnInteractionThreshold;
        }
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

        // Keep adaptive escalation in Approaching below Hardcore.
        // Hardcore is reserved for Climax unless user manually pins intensity.
        if (session.AdaptiveState.CurrentNarrativePhase == NarrativePhase.Approaching
            && targetScale > (int)IntensityLevel.Explicit)
        {
            targetScale = (int)IntensityLevel.Explicit;
            reasonCode += "-approaching-capped-at-erotic";
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
            await HandleManualOverrideAsync(session, state, manualOverrideScenarioId!, cancellationToken);
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

        if (IsManualOverrideLockActive(state))
        {
            IncrementPhaseCounters(state);
            await EvaluatePhaseTransitionAsync(session, interaction, state, cancellationToken);
            return;
        }

        if (state.CurrentNarrativePhase == NarrativePhase.BuildUp
            && state.BuildUpCooldownInteractionsRemaining > 0)
        {
            state.BuildUpCooldownInteractionsRemaining--;
            _logger?.LogInformation(
                "BuildUp cooldown active for session {SessionId}: remaining={Remaining}, interactionCount={InteractionCount}",
                session.Id,
                state.BuildUpCooldownInteractionsRemaining,
                session.Interactions.Count + 1);
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
                var wasDifferentScenario = !string.Equals(
                    state.ActiveScenarioId,
                    result.SelectedScenarioId,
                    StringComparison.OrdinalIgnoreCase);
                state.ActiveScenarioId = result.SelectedScenarioId;
                state.ScenarioCommitmentTimeUtc ??= DateTime.UtcNow;

                var shouldEnterCommitted = wasUncommitted
                    || wasDifferentScenario
                    || state.CurrentNarrativePhase is NarrativePhase.BuildUp or NarrativePhase.Reset;

                if (shouldEnterCommitted)
                {
                    state.CurrentNarrativePhase = NarrativePhase.Committed;
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

        if (TryPivotActiveScenarioFromThemeMomentum(state))
        {
            _logger?.LogInformation(
                "Scenario pivoted by context momentum for session {SessionId}: newActiveScenarioId={ActiveScenarioId}, phase={Phase}, interactionCount={InteractionCount}",
                session.Id,
                state.ActiveScenarioId,
                state.CurrentNarrativePhase,
                session.Interactions.Count + 1);
        }

        IncrementPhaseCounters(state);
        await EvaluatePhaseTransitionAsync(session, interaction, state, cancellationToken);
    }

    private static bool IsManualOverrideLockActive(RolePlayAdaptiveState state)
    {
        if (!string.Equals(state.ThemeTracker.ThemeSelectionRule, "ManualOverride", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            return false;
        }

        return state.InteractionsSinceCommitment < ManualScenarioOverrideLockInteractions;
    }

    private bool TryPivotActiveScenarioFromThemeMomentum(RolePlayAdaptiveState state)
    {
        if (string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            return false;
        }

        if (state.CurrentNarrativePhase is not (NarrativePhase.BuildUp or NarrativePhase.Committed))
        {
            return false;
        }

        if (!state.ThemeTracker.Themes.TryGetValue(state.ActiveScenarioId, out var activeTheme) || activeTheme.Blocked)
        {
            return false;
        }

        var isActiveScenarioStale = IsActiveScenarioStale(state, activeTheme.ThemeId);
        var committedInteractionWindow = isActiveScenarioStale
            ? _pivotCommittedInteractionWindowWhenStale
            : _pivotCommittedInteractionWindow;

        // Keep pivots to early flow unless active scenario has gone stale.
        if (state.CurrentNarrativePhase == NarrativePhase.Committed
            && state.InteractionsSinceCommitment > committedInteractionWindow)
        {
            return false;
        }

        var topCandidate = state.ThemeTracker.Themes.Values
            .Where(x => !x.Blocked)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Breakdown.ChoiceSignal)
            .ThenByDescending(x => x.Breakdown.InteractionEvidenceSignal)
            .FirstOrDefault();

        if (topCandidate is null)
        {
            return false;
        }

        var momentumCandidate = ResolveMomentumCandidate(state);
        if (momentumCandidate is not null
            && !string.Equals(momentumCandidate.ThemeId, activeTheme.ThemeId, StringComparison.OrdinalIgnoreCase)
            && !momentumCandidate.Blocked)
        {
            var hasMeaningfulMomentum = momentumCandidate.Score >= (activeTheme.Score - 1.5)
                || momentumCandidate.Breakdown.InteractionEvidenceSignal >= (activeTheme.Breakdown.InteractionEvidenceSignal + 1.5);

            if (hasMeaningfulMomentum)
            {
                topCandidate = momentumCandidate;
            }
        }

        if (string.Equals(topCandidate.ThemeId, activeTheme.ThemeId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        const double absoluteScoreFloor = 6.0;
        var overtakeMargin = isActiveScenarioStale
            ? _pivotOvertakeMarginWhenStale
            : _pivotOvertakeMarginDefault;

        if (topCandidate.Score < absoluteScoreFloor)
        {
            return false;
        }

        if (topCandidate.Score < activeTheme.Score + overtakeMargin)
        {
            return false;
        }

        state.ActiveScenarioId = topCandidate.ThemeId;
        state.CurrentNarrativePhase = NarrativePhase.Committed;
        state.ScenarioCommitmentTimeUtc = DateTime.UtcNow;
        state.InteractionsSinceCommitment = 0;
        state.InteractionsInApproaching = 0;

        foreach (var item in state.ThemeTracker.Themes.Values)
        {
            item.IsScenarioCandidate = string.Equals(item.ThemeId, state.ActiveScenarioId, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private bool IsActiveScenarioStale(RolePlayAdaptiveState state, string activeThemeId)
    {
        var evidenceByInteraction = state.ThemeTracker.RecentEvidence
            .Where(x => !string.IsNullOrWhiteSpace(x.InteractionId)
                && string.Equals(x.SignalType, "interaction-evidence", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.InteractionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                InteractionId = group.Key,
                HasActiveHit = group.Any(item => string.Equals(item.ThemeId, activeThemeId, StringComparison.OrdinalIgnoreCase))
            })
            .ToList();

        if (evidenceByInteraction.Count < _activeScenarioNoHitStaleTurns)
        {
            return false;
        }

        var recent = evidenceByInteraction.TakeLast(_activeScenarioNoHitStaleTurns);
        return recent.All(x => !x.HasActiveHit);
    }

    private static ThemeTrackerItem? ResolveMomentumCandidate(RolePlayAdaptiveState state)
    {
        var momentumThemeId = state.ThemeTracker.RecentEvidence
            .TakeLast(8)
            .GroupBy(x => x.ThemeId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Sum(item => Math.Abs(item.Delta) * Math.Max(0.1, item.Confidence)))
            .Select(group => group.Key)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(momentumThemeId))
        {
            return null;
        }

        return state.ThemeTracker.Themes.TryGetValue(momentumThemeId, out var momentum)
            ? momentum
            : null;
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
                state.InteractionsInApproaching++;
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
                ExplicitClimaxRequested: ContainsCommand(interaction.Content, ["/climax"])
                    || ContainsAny(interaction.Content, ["trigger climax"]),
                ClimaxCompletionDetected: ContainsCommand(interaction.Content, ["/completeclimax", "/endclimax", "/end-climax"]),
                ManualScenarioOverrideRequested: false,
                ManualOverrideScenarioId: null,
                CompletedScenarios: state.CompletedScenarios),
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
        else if (nextPhase == NarrativePhase.Climax)
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

            await ApplyResetSnapshotAsync(session, state, resetSnapshot, cancellationToken);
            state.CurrentNarrativePhase = NarrativePhase.BuildUp;
        }
    }

    private async Task HandleManualOverrideAsync(
        RolePlaySession session,
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

            await ApplyResetSnapshotAsync(session, state, resetSnapshot, cancellationToken);
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

    private static bool ContainsCommand(string? content, IReadOnlyList<string> commands)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var tokens = content
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.Any(token => commands.Any(command => string.Equals(token, command, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task ApplyResetSnapshotAsync(
        RolePlaySession session,
        RolePlayAdaptiveState state,
        AdaptiveScenarioSnapshot snapshot,
        CancellationToken cancellationToken)
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
        state.BuildUpCooldownInteractionsRemaining = 1;

        RecenterCharacterStatsForReset(state);

        if (Enum.TryParse<NarrativePhase>(snapshot.CurrentNarrativePhase, out var parsedPhase))
        {
            state.CurrentNarrativePhase = parsedPhase;
        }

        await ForceAdaptiveIntensityToFloorAsync(session, cancellationToken);
    }

    private async Task ForceAdaptiveIntensityToFloorAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        if (_intensityProfileService is null)
        {
            return;
        }

        var floorScale = RolePlayStyleResolver.ParseBoundScale(session.IntensityFloorOverride);
        if (!floorScale.HasValue)
        {
            session.AdaptiveIntensityLastTransitionReason = "reset-floor-unavailable";
            return;
        }

        var profiles = await _intensityProfileService.ListAsync(cancellationToken);
        if (profiles.Count == 0)
        {
            session.AdaptiveIntensityLastTransitionReason = "reset-floor-no-profiles";
            return;
        }

        var targetProfile = profiles.FirstOrDefault(x => (int)x.Intensity == floorScale.Value);
        if (targetProfile is null)
        {
            session.AdaptiveIntensityLastTransitionReason = "reset-floor-target-profile-missing";
            return;
        }

        if (string.Equals(session.AdaptiveIntensityProfileId, targetProfile.Id, StringComparison.OrdinalIgnoreCase))
        {
            session.AdaptiveIntensityLastTransitionReason = "reset-floor-already-applied";
            return;
        }

        session.AdaptiveIntensityTransitions ??= [];
        var fromProfileId = session.AdaptiveIntensityProfileId ?? session.SelectedIntensityProfileId ?? string.Empty;
        session.AdaptiveIntensityProfileId = targetProfile.Id;
        session.AdaptiveIntensityLastFromProfileId = fromProfileId;
        session.AdaptiveIntensityLastToProfileId = targetProfile.Id;
        session.AdaptiveIntensityLastTransitionReason = "reset-floor-applied";
        session.AdaptiveIntensityLastTransitionUtc = DateTime.UtcNow;
        session.AdaptiveIntensityTransitions.Add(new AdaptiveIntensityTransitionRecord
        {
            FromProfileId = fromProfileId,
            ToProfileId = targetProfile.Id,
            ReasonCode = "reset-floor-applied",
            Source = "reset-engine",
            OccurredUtc = session.AdaptiveIntensityLastTransitionUtc.Value
        });

        if (session.AdaptiveIntensityTransitions.Count > MaxAdaptiveTransitionHistory)
        {
            var trim = session.AdaptiveIntensityTransitions.Count - MaxAdaptiveTransitionHistory;
            session.AdaptiveIntensityTransitions.RemoveRange(0, trim);
        }
    }

    private static void RecenterCharacterStatsForReset(RolePlayAdaptiveState state)
    {
        foreach (var block in state.CharacterStats.Values)
        {
            EnsureDefaultStats(block.Stats);
            foreach (var statName in AdaptiveStatCatalog.CanonicalStatNames)
            {
                if (!block.Stats.TryGetValue(statName, out var current))
                {
                    current = AdaptiveStatCatalog.DefaultValue;
                }

                block.Stats[statName] = ComputeResetStatTarget(current);
            }

            block.UpdatedUtc = DateTime.UtcNow;
        }
    }

    private static int ComputeResetStatTarget(int current)
    {
        var clamped = Math.Clamp(current, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue);
        var baseline = AdaptiveStatCatalog.DefaultValue;

        if (clamped <= ResetExtremeLowThreshold || clamped >= ResetExtremeHighThreshold)
        {
            return ResetExtremeHardTarget;
        }

        var distanceFromBaseline = clamped - baseline;
        var softened = baseline + (int)Math.Round(distanceFromBaseline * ResetSoftRetentionFactor, MidpointRounding.AwayFromZero);
        return Math.Clamp(softened, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue);
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
            ScenarioFitRules = BuildScenarioFitRulesJson(theme),
            IsEnabled = theme.IsEnabled,
            IsBuiltIn = false,
            CreatedUtc = theme.CreatedUtc,
            UpdatedUtc = theme.UpdatedUtc
        };
    }

    private static string BuildScenarioFitRulesJson(DreamGenClone.Domain.RolePlay.RPTheme theme)
    {
        return RPThemeFitRulesConverter.BuildScenarioFitRulesJson(theme);
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

    private async Task<IReadOnlyList<StatKeywordCategory>> LoadStatKeywordCategoriesAsync(CancellationToken cancellationToken)
    {
        if (_statKeywordCategoryService is not null)
        {
            return await _statKeywordCategoryService.ListEnabledAsync(cancellationToken);
        }

        return DefaultStatKeywordCategories;
    }

    private static int ScoreStatSignalWithDirection(
        string content,
        IReadOnlyList<string> keywords,
        int perKeywordDelta,
        int maxAbsDelta)
    {
        var sign = Math.Sign(perKeywordDelta);
        if (sign == 0)
        {
            return 0;
        }

        var magnitude = ScoreStatSignal(content, keywords, Math.Abs(perKeywordDelta), Math.Max(1, maxAbsDelta));
        return sign * magnitude;
    }

    private static readonly IReadOnlyList<StatKeywordCategory> DefaultStatKeywordCategories =
    [
        BuildDefaultStatKeywordCategory("desire", "Desire", "Desire", 1, 4, ["kiss", "touch", "desire", "want", "close", "heat"], 10),
        BuildDefaultStatKeywordCategory("restraint", "Restraint", "Restraint", 1, 3, ["can't", "wrong", "shouldn't", "hesitate", "guilt"], 20),
        BuildDefaultStatKeywordCategory("tension", "Tension", "Tension", 1, 4, ["fear", "caught", "risk", "panic", "nervous"], 30),
        BuildDefaultStatKeywordCategory("connection", "Connection", "Connection", 1, 3, ["safe", "comfort", "trust", "reassure"], 40),
        BuildDefaultStatKeywordCategory("dominance", "Dominance", "Dominance", 1, 3, ["control", "command", "obey", "claim", "choose", "decide", "insist"], 50),
        BuildDefaultStatKeywordCategory("loyalty-positive", "Loyalty Positive", "Loyalty", 1, 5, ["husband", "wife", "promise", "vow", "faithful", "devoted", "commitment"], 60),
        BuildDefaultStatKeywordCategory("loyalty-negative", "Loyalty Negative", "Loyalty", -1, 5, ["affair", "betray", "cheat", "secret", "sneak", "stranger"], 70),
        BuildDefaultStatKeywordCategory("selfrespect-positive", "SelfRespect Positive", "SelfRespect", 1, 5, ["boundary", "boundaries", "respect", "dignity", "self-worth", "walk away", "no"], 80),
        BuildDefaultStatKeywordCategory("selfrespect-negative", "SelfRespect Negative", "SelfRespect", -1, 5, ["humiliate", "ashamed", "used", "degraded", "demean"], 90)
    ];

    private static StatKeywordCategory BuildDefaultStatKeywordCategory(
        string id,
        string name,
        string statName,
        int perKeywordDelta,
        int maxAbsDelta,
        IReadOnlyList<string> keywords,
        int sortOrder)
    {
        return new StatKeywordCategory
        {
            Id = id,
            Name = name,
            StatName = statName,
            PerKeywordDelta = perKeywordDelta,
            MaxAbsDelta = maxAbsDelta,
            SortOrder = sortOrder,
            IsEnabled = true,
            Keywords = keywords.Select((keyword, index) => new StatKeywordRule
            {
                Id = $"{id}-{index + 1}",
                CategoryId = id,
                Keyword = keyword,
                SortOrder = index + 1
            }).ToList()
        };
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

    private int GetThemeAffinityPhaseCap(NarrativePhase phase)
    {
        return phase switch
        {
            NarrativePhase.BuildUp => _themeAffinityCapBuildUp,
            NarrativePhase.Committed => _themeAffinityCapCommitted,
            NarrativePhase.Approaching => _themeAffinityCapApproaching,
            NarrativePhase.Climax => _themeAffinityCapClimax,
            NarrativePhase.Reset => _themeAffinityCapReset,
            _ => 0
        };
    }

    private static int ApplyThemeAffinityPhaseCap(int delta, int phaseCap)
    {
        if (phaseCap <= 0 || delta == 0)
        {
            return 0;
        }

        var magnitude = Math.Min(Math.Abs(delta), phaseCap);
        return Math.Sign(delta) * magnitude;
    }

    private static IReadOnlyList<ThemeAffinityCandidate> SelectThemeAffinityCandidates(
        IReadOnlyList<ThemeAffinityCandidate> candidates,
        int limit)
    {
        return candidates
            .OrderByDescending(x => x.ThemeSignal)
            .ThenByDescending(x => x.ThemeScore)
            .ThenBy(x => x.ThemeId, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, limit))
            .ToList();
    }

    private sealed record ThemeAffinityCandidate(
        string ThemeId,
        int ThemeSignal,
        double ThemeScore,
        IReadOnlyDictionary<string, int> StatDeltas);

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

        if (string.Equals(tracker.ThemeSelectionRule, "ManualOverride", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(state.ActiveScenarioId)
            && tracker.Themes.ContainsKey(state.ActiveScenarioId))
        {
            tracker.PrimaryThemeId = state.ActiveScenarioId;
            tracker.SecondaryThemeId = ordered
                .FirstOrDefault(x => !string.Equals(x.ThemeId, state.ActiveScenarioId, StringComparison.OrdinalIgnoreCase))
                ?.ThemeId;
            return;
        }

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

    private static void RemoveNonCanonicalStatEntries(RolePlayAdaptiveState state)
    {
        foreach (var block in state.CharacterStats.Values)
        {
            var unsupported = block.Stats.Keys
                .Where(x => !AdaptiveStatCatalog.CanonicalStatNames.Contains(x, StringComparer.OrdinalIgnoreCase))
                .ToList();

            foreach (var statName in unsupported)
            {
                block.Stats.Remove(statName);
            }
        }
    }

    private static string? ResolveSupportedStatName(string statName)
    {
        var normalized = AdaptiveStatCatalog.NormalizeLegacyStatName(statName);
        return AdaptiveStatCatalog.CanonicalStatNames.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : null;
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
                    var normalized = ResolveSupportedStatName(statName);
                    if (normalized is null)
                    {
                        continue;
                    }
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
                    var normalized = ResolveSupportedStatName(statName);
                    if (normalized is null)
                    {
                        continue;
                    }
                    if (!charBlock.Stats.TryGetValue(normalized, out var current))
                    {
                        current = AdaptiveStatCatalog.DefaultValue;
                    }
                    charBlock.Stats[normalized] = Math.Clamp(current + affinityDelta, 0, 100);
                }
            }
        }

        state.ThemeTracker.UpdatedUtc = DateTime.UtcNow;
        RemoveNonCanonicalStatEntries(state);
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

    public async Task<bool> ApplyManualScenarioOverrideAsync(
        RolePlaySession session,
        string requestedScenarioId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrWhiteSpace(requestedScenarioId))
        {
            return false;
        }

        var state = session.AdaptiveState ?? new RolePlayAdaptiveState();
        if (!state.ThemeTracker.Themes.TryGetValue(requestedScenarioId, out var requestedTheme) || requestedTheme.Blocked)
        {
            return false;
        }

        await HandleManualOverrideAsync(session, state, requestedScenarioId, cancellationToken);

        state.ActiveScenarioId = requestedScenarioId;
        state.ScenarioCommitmentTimeUtc = DateTime.UtcNow;
        state.InteractionsSinceCommitment = 0;
        state.InteractionsInApproaching = 0;

        var previousPrimary = state.ThemeTracker.PrimaryThemeId;
        state.ThemeTracker.PrimaryThemeId = requestedScenarioId;
        if (!string.Equals(previousPrimary, requestedScenarioId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(previousPrimary)
            && state.ThemeTracker.Themes.ContainsKey(previousPrimary))
        {
            state.ThemeTracker.SecondaryThemeId = previousPrimary;
        }

        state.ThemeTracker.ThemeSelectionRule = "ManualOverride";
        state.ThemeTracker.UpdatedUtc = DateTime.UtcNow;

        requestedTheme.Breakdown.ChoiceSignal = Math.Max(requestedTheme.Breakdown.ChoiceSignal, 30);
        requestedTheme.IsScenarioCandidate = true;
        requestedTheme.LastCandidateEvaluationTimeUtc = DateTime.UtcNow;

        session.AdaptiveState = state;

        _logger?.LogInformation(
            "Manual adaptive override applied for session {SessionId}: requestedScenarioId={ScenarioId}, phase={Phase}",
            session.Id,
            requestedScenarioId,
            state.CurrentNarrativePhase);

        return true;
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