using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.RegularExpressions;
using NarrativePhase = DreamGenClone.Domain.RolePlay.NarrativePhase;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayAdaptiveStateService : IRolePlayAdaptiveStateService
{
    private const int MaxAdaptiveTransitionHistory = 25;
    private const int DefaultThemeAffinityStackLimit = 1;
    private const int DefaultEarlyTurnThreshold = 3;
    private const int DefaultEarlyTurnPerStatDeltaCap = 2;
    private const int DefaultPerTurnTotalDeltaBudget = 10;
    private const double DefaultSuppressedEvidenceMultiplier = 0.20;
    private const double DefaultSuppressedEvidencePerTurnCap = 1.5;
    private const double DefaultSemanticEvidencePerTurnCap = 25.0;
    private static readonly Regex SemanticSignalRegex = new(
        "\\[\\[semantic:(?<event>[a-zA-Z0-9._-]+):(?<confidence>-?[0-9]+(?:\\.[0-9]+)?)\\]\\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IThemeCatalogService _themeCatalogService;
    private readonly IIntensityProfileService? _intensityProfileService;
    private readonly IThemePreferenceService? _themePreferenceService;
    private readonly IRPThemeService? _rpThemeService;
    private readonly IStatKeywordCategoryService? _statKeywordCategoryService;
    private readonly ISteeringProfileService? _steeringProfileService;
    private readonly IRolePlayDebugEventSink? _debugEventSink;
    private readonly ILogger<RolePlayAdaptiveStateService>? _logger;
    private readonly int _themeAffinityStackLimit;
    private readonly int _earlyTurnThreshold;
    private readonly int _earlyTurnPerStatDeltaCap;
    private readonly int _perTurnTotalDeltaBudget;
    private readonly int _themeAffinityCapBuildUp;
    private readonly int _themeAffinityCapCommitted;
    private readonly int _themeAffinityCapApproaching;
    private readonly int _themeAffinityCapClimax;
    private readonly int _themeAffinityCapReset;
    private readonly double _suppressedEvidenceMultiplier;
    private readonly double _suppressedEvidencePerTurnCap;
    private readonly double _semanticEvidencePerTurnCap;

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService)
    {
        _themeCatalogService = themeCatalogService;
        _themeAffinityStackLimit = DefaultThemeAffinityStackLimit;
        _earlyTurnThreshold = DefaultEarlyTurnThreshold;
        _earlyTurnPerStatDeltaCap = DefaultEarlyTurnPerStatDeltaCap;
        _perTurnTotalDeltaBudget = DefaultPerTurnTotalDeltaBudget;
        _themeAffinityCapBuildUp = 0;
        _themeAffinityCapCommitted = 1;
        _themeAffinityCapApproaching = 1;
        _themeAffinityCapClimax = 2;
        _themeAffinityCapReset = 0;
        _suppressedEvidenceMultiplier = DefaultSuppressedEvidenceMultiplier;
        _suppressedEvidencePerTurnCap = DefaultSuppressedEvidencePerTurnCap;
        _semanticEvidencePerTurnCap = DefaultSemanticEvidencePerTurnCap;
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IIntensityProfileService intensityProfileService)
    {
        _themeCatalogService = themeCatalogService;
        _intensityProfileService = intensityProfileService;
        _themeAffinityStackLimit = DefaultThemeAffinityStackLimit;
        _earlyTurnThreshold = DefaultEarlyTurnThreshold;
        _earlyTurnPerStatDeltaCap = DefaultEarlyTurnPerStatDeltaCap;
        _perTurnTotalDeltaBudget = DefaultPerTurnTotalDeltaBudget;
        _themeAffinityCapBuildUp = 0;
        _themeAffinityCapCommitted = 1;
        _themeAffinityCapApproaching = 1;
        _themeAffinityCapClimax = 2;
        _themeAffinityCapReset = 0;
        _suppressedEvidenceMultiplier = DefaultSuppressedEvidenceMultiplier;
        _suppressedEvidencePerTurnCap = DefaultSuppressedEvidencePerTurnCap;
        _semanticEvidencePerTurnCap = DefaultSemanticEvidencePerTurnCap;
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
    {
        _themeCatalogService = themeCatalogService;
        _debugEventSink = debugEventSink;
        _logger = logger;
        _themeAffinityStackLimit = DefaultThemeAffinityStackLimit;
        _earlyTurnThreshold = DefaultEarlyTurnThreshold;
        _earlyTurnPerStatDeltaCap = DefaultEarlyTurnPerStatDeltaCap;
        _perTurnTotalDeltaBudget = DefaultPerTurnTotalDeltaBudget;
        _themeAffinityCapBuildUp = 0;
        _themeAffinityCapCommitted = 1;
        _themeAffinityCapApproaching = 1;
        _themeAffinityCapClimax = 2;
        _themeAffinityCapReset = 0;
        _suppressedEvidenceMultiplier = DefaultSuppressedEvidenceMultiplier;
        _suppressedEvidencePerTurnCap = DefaultSuppressedEvidencePerTurnCap;
        _semanticEvidencePerTurnCap = DefaultSemanticEvidencePerTurnCap;
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
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
        _themePreferenceService = themePreferenceService;
        _rpThemeService = rpThemeService;
        _statKeywordCategoryService = statKeywordCategoryService;
        _steeringProfileService = styleProfileService;
        _debugEventSink = debugEventSink;
        _logger = logger;
        _themeAffinityStackLimit = Math.Max(1, storyAnalysisOptions?.Value.AdaptiveThemeAffinityStackLimit ?? DefaultThemeAffinityStackLimit);
        _earlyTurnThreshold = Math.Max(1, storyAnalysisOptions?.Value.AdaptiveEarlyTurnThreshold ?? DefaultEarlyTurnThreshold);
        _earlyTurnPerStatDeltaCap = Math.Max(1, storyAnalysisOptions?.Value.AdaptiveEarlyTurnPerStatDeltaCap ?? DefaultEarlyTurnPerStatDeltaCap);
        _perTurnTotalDeltaBudget = Math.Max(1, storyAnalysisOptions?.Value.AdaptivePerTurnTotalDeltaBudget ?? DefaultPerTurnTotalDeltaBudget);
        _themeAffinityCapBuildUp = Math.Max(0, storyAnalysisOptions?.Value.AdaptiveThemeAffinityCapBuildUp ?? 0);
        _themeAffinityCapCommitted = Math.Max(0, storyAnalysisOptions?.Value.AdaptiveThemeAffinityCapCommitted ?? 1);
        _themeAffinityCapApproaching = Math.Max(0, storyAnalysisOptions?.Value.AdaptiveThemeAffinityCapApproaching ?? 1);
        _themeAffinityCapClimax = Math.Max(0, storyAnalysisOptions?.Value.AdaptiveThemeAffinityCapClimax ?? 2);
        _themeAffinityCapReset = Math.Max(0, storyAnalysisOptions?.Value.AdaptiveThemeAffinityCapReset ?? 0);
        _suppressedEvidenceMultiplier = Math.Clamp(storyAnalysisOptions?.Value.SuppressedEvidenceMultiplier ?? DefaultSuppressedEvidenceMultiplier, 0.0, 1.0);
        _suppressedEvidencePerTurnCap = Math.Max(0.0, storyAnalysisOptions?.Value.SuppressedEvidencePerTurnCap ?? DefaultSuppressedEvidencePerTurnCap);
        _semanticEvidencePerTurnCap = Math.Max(0.0, storyAnalysisOptions?.Value.SemanticEvidencePerTurnCap ?? DefaultSemanticEvidencePerTurnCap);
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IThemePreferenceService themePreferenceService,
        IRPThemeService? rpThemeService,
        ISteeringProfileService styleProfileService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
        : this(themeCatalogService, themePreferenceService, rpThemeService, null, styleProfileService, debugEventSink, logger)
    {
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IThemePreferenceService themePreferenceService,
        ISteeringProfileService styleProfileService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
        : this(themeCatalogService, themePreferenceService, null, null, styleProfileService, debugEventSink, logger)
    {
    }

    public async Task<AdaptiveScenarioState> UpdateFromInteractionAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(interaction);

        var catalogEntries = await LoadRuntimeCatalogEntriesAsync(session, cancellationToken);
        var groupedKeywordsByThemeId = await LoadRpThemeKeywordGroupsByThemeIdAsync(session, cancellationToken);

        var state = session.AdaptiveState;
        EnsureThemeCatalog(state, catalogEntries);
        RemoveNonCharacterStats(state);
        RemoveNonCanonicalStatEntries(state);

        var actorKey = string.IsNullOrWhiteSpace(interaction.ActorName) ? "Unknown" : interaction.ActorName.Trim();
        var trackCharacterStats = !IsNarrativeOrSystemInteraction(interaction, actorKey);
        CharacterStatProfileV2? actorStats = null;
        Dictionary<string, int>? statsBefore = null;
        var statDeltaContributors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var rawStatDeltasForEvent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (trackCharacterStats)
        {
            actorStats = GetCharacterStats(state, actorKey);
            if (actorStats is not null)
            {
                statsBefore = CharacterStatProfileV2Accessor.GetAllStats(actorStats);
                actorStats.LastStatDeltas ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }

        var phaseAffinityCap = GetThemeAffinityPhaseCap(state.CurrentPhase);
        var themeAffinityCandidates = new List<ThemeAffinityCandidate>();

        var primaryBefore = state.PrimaryThemeId;
        var secondaryBefore = state.SecondaryThemeId;

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

                ApplyTrackedDelta(actorStats, normalizedStatName, keywordDelta, $"keyword:{reasonKey}");
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
            if (!state.ThemeScores.TryGetValue(entry.Id, out var trackerItem))
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

                            state.RecentEvidence.Add(new ThemeEvidenceRecord
                            {
                                InteractionId = interaction.Id,
                                ThemeId = entry.Id,
                                SignalType = "suppressed-interaction-evidence",
                                Delta = suppressedDelta,
                                Confidence = 0.45,
                                Rationale = BuildKeywordRationale(entry.Label, contentLower, entry.Keywords, suppressedGroupedKeywords)
                            });

                            TrimEvidence(state);
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
                if (state.ThemeScores.TryGetValue(entry.Id, out var item) && item.Score > 0)
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
                    ApplyTrackedDelta(actorStats, statName, delta, $"theme-affinity:{candidate.ThemeId}");
                }
            }
        }

        ApplyInteractionDeltaPolicyCaps();
        await ApplySemanticEvidenceAsync(session, interaction, state, inferredSignals: null, cancellationToken);

        var gateMinTurns = state.SelectionMinimumTurns;
        var gateTurnCount = state.ObservedTurnCount;
        var gatePrimaryBefore = state.PrimaryThemeId;
        RecalculateSelectedThemes(state, interaction);
        _logger?.LogInformation(
            "ThemeGate session={SessionId} interaction={InteractionId}: minTurns={MinTurns} observedTurns={ObservedTurns} primaryBefore={PrimaryBefore} primaryAfter={PrimaryAfter} rule={Rule}",
            session.Id, interaction.Id,
            gateMinTurns, gateTurnCount,
            gatePrimaryBefore ?? "(none)", state.PrimaryThemeId ?? "(none)",
            state.ThemeSelectionRule ?? "(none)");
        state.ThemeTrackerUpdatedUtc = DateTime.UtcNow;
        RemoveNonCanonicalStatEntries(state);

        session.AdaptiveState = state;

        if (_debugEventSink is not null)
        {
            try
            {
                var statDeltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var statDeltaReasons = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
                if (actorStats is not null && statsBefore is not null)
                {
                    foreach (var stat in CharacterStatProfileV2Accessor.GetAllStats(actorStats))
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

                var topThemes = state.ThemeScores.Values
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
                        semanticStepSucceeded = state.SemanticStepSucceeded,
                        semanticEvents = state.SemanticEvents,
                        semanticDeltaBreakdowns = state.SemanticDeltaBreakdowns,
                        semanticStatDeltaBreakdowns = state.SemanticStatDeltaBreakdowns,
                        rawStatDeltas = rawStatDeltasForEvent,
                        statDeltas,
                        statDeltaReasons,
                        primaryThemeBefore = primaryBefore,
                        secondaryThemeBefore = secondaryBefore,
                        primaryThemeAfter = state.PrimaryThemeId,
                        secondaryThemeAfter = state.SecondaryThemeId,
                        topThemes,
                        recentEvidence = state.RecentEvidence.TakeLast(8)
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
            CharacterStatProfileV2 profile,
            string statName,
            int delta,
            string reason)
        {
            if (delta == 0)
            {
                return;
            }

            CharacterStatProfileV2Accessor.ApplyDelta(profile, statName, delta);

            // Encounter dimension drift: evolve RuntimeEncounterStats based on stat change.
            if (state.CharacterRoles.TryGetValue(actorKey, out var actorTargetRole))
            {
                // InitializeRuntimeEncounterStatsIfNeeded (spec step 12):
                // Seed from BehavioralDimensionCatalog at 50 on first mutation if not yet initialized.
                if (profile.RuntimeEncounterStats is not { Count: > 0 })
                {
                    profile.RuntimeEncounterStats = BehavioralDimensionCatalog
                        .GetDimensions(actorTargetRole)
                        .ToDictionary(d => d.Name, _ => 50, StringComparer.OrdinalIgnoreCase);
                }
                StatToDimensionMappings.ApplyDelta(profile.RuntimeEncounterStats, actorTargetRole, statName, delta);
            }

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

            var rawDeltas = BuildCurrentDeltas(CharacterStatProfileV2Accessor.GetAllStats(actorStats), statsBefore);
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
            if (totalBeforeBudgetCap > _perTurnTotalDeltaBudget)
            {
                var beforeBudgetDeltas = new Dictionary<string, int>(adjustedDeltas, StringComparer.OrdinalIgnoreCase);
                var currentTotal = totalBeforeBudgetCap;
                while (currentTotal > _perTurnTotalDeltaBudget)
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
                            $"policy:per-turn-budget-cap({beforeBudgetDelta}->{afterBudgetDelta},total={totalBeforeBudgetCap}->{_perTurnTotalDeltaBudget})");
                    }
                }
            }

            foreach (var (statName, finalDelta) in adjustedDeltas)
            {
                var baseline = statsBefore.TryGetValue(statName, out var before) ? before : AdaptiveStatCatalog.DefaultValue;
                CharacterStatProfileV2Accessor.SetStat(actorStats, statName, Math.Clamp(baseline + finalDelta, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue));
            }

            var effectiveDeltas = adjustedDeltas
                .Where(x => x.Value != 0)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            if (effectiveDeltas.Count > 0)
            {
                actorStats.LastStatDeltas = effectiveDeltas;
                actorStats.LastStatDeltaUpdatedUtc = DateTime.UtcNow;
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

            return actorTurnCount <= _earlyTurnThreshold;
        }
    }

    public async Task<AdaptiveScenarioState> ApplyInferredSemanticEvidenceAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        IReadOnlyList<IRolePlayAdaptiveStateService.InferredSemanticSignal> inferredSignals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(inferredSignals);

        var state = session.AdaptiveState;
        await ApplySemanticEvidenceAsync(session, interaction, state, inferredSignals, cancellationToken);

        // Semantic evidence has updated theme scores; recompute primary/secondary selection
        // here as well so the semantic-only path (RolePlayFeatureFlags:EnableAdaptiveStateUpdates=false)
        // still promotes a theme once scores cross the gate. The Observing gate inside
        // RecalculateSelectedThemes is still honoured.
        var gateMinTurns = state.SelectionMinimumTurns;
        var gateTurnCount = state.ObservedTurnCount;
        var gatePrimaryBefore = state.PrimaryThemeId;
        RecalculateSelectedThemes(state, interaction);
        _logger?.LogInformation(
            "ThemeGate (semantic) session={SessionId} interaction={InteractionId}: minTurns={MinTurns} observedTurns={ObservedTurns} primaryBefore={PrimaryBefore} primaryAfter={PrimaryAfter} rule={Rule}",
            session.Id, interaction.Id,
            gateMinTurns, gateTurnCount,
            gatePrimaryBefore ?? "(none)", state.PrimaryThemeId ?? "(none)",
            state.ThemeSelectionRule ?? "(none)");
        state.ThemeTrackerUpdatedUtc = DateTime.UtcNow;

        session.AdaptiveState = state;

        if (_debugEventSink is not null)
        {
            try
            {
                var hasContribution = state.SemanticEvents.Count > 0
                    || state.SemanticDeltaBreakdowns.Count > 0
                    || state.SemanticStatDeltaBreakdowns.Count > 0;

                await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                {
                    SessionId = session.Id,
                    InteractionId = interaction.Id,
                    EventKind = hasContribution ? "SemanticInferredEvidenceApplied" : "SemanticInferredEvidenceNoContribution",
                    Severity = "Info",
                    ActorName = interaction.ActorName,
                    Summary = hasContribution
                        ? $"Semantic inferred evidence applied: {inferredSignals.Count} signal(s), {state.SemanticDeltaBreakdowns.Count} theme delta(s), {state.SemanticStatDeltaBreakdowns.Count} stat delta(s)."
                        : $"Semantic inferred evidence produced no contribution from {inferredSignals.Count} signal(s).",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        interactionId = interaction.Id,
                        inferredSignalCount = inferredSignals.Count,
                        inferredSignals = inferredSignals.Select(x => new
                        {
                            eventId = x.EventId,
                            confidence = x.Confidence,
                            actorName = x.ActorName,
                            targetCharacterName = x.TargetCharacterName,
                            evidenceSpan = x.EvidenceSpan
                        }),
                        semanticStepSucceeded = state.SemanticStepSucceeded,
                        semanticEvents = state.SemanticEvents,
                        semanticDeltaBreakdowns = state.SemanticDeltaBreakdowns,
                        semanticStatDeltaBreakdowns = state.SemanticStatDeltaBreakdowns
                    })
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to emit SemanticInferredEvidenceApplied debug event for session {SessionId}", session.Id);
            }
        }

        return state;
    }

    private async Task ApplySemanticEvidenceAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        AdaptiveScenarioState state,
        IReadOnlyList<IRolePlayAdaptiveStateService.InferredSemanticSignal>? inferredSignals,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation(
            "Semantic processing started: SessionId={SessionId}, InteractionId={InteractionId}",
            session.Id,
            interaction.Id);

        state.SemanticStepSucceeded = true;
        state.SemanticEvents.Clear();
        // Keep SemanticDeltaBreakdowns and SemanticStatDeltaBreakdowns — they accumulate per-interaction
        // so the Semantic Analysis modal can show history for any prior interaction in the session.

        var matches = SemanticSignalRegex.Matches(interaction.Content ?? string.Empty);
        var inferredCount = inferredSignals?.Count ?? 0;
        if (matches.Count == 0 && inferredCount == 0)
        {
            _logger?.LogInformation(
                "Semantic processing no contribution: SessionId={SessionId}, InteractionId={InteractionId}, ReasonCode={ReasonCode}",
                session.Id,
                interaction.Id,
                DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticNoContribution);

            state.RecentEvidence.Add(new ThemeEvidenceRecord
            {
                InteractionId = interaction.Id,
                ThemeId = "semantic",
                SignalType = "semantic-diagnostic",
                Delta = 0,
                Confidence = 1.0,
                Rationale = DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticNoContribution
            });
            TrimEvidence(state);
            return;
        }

        if (_rpThemeService is null)
        {
            state.SemanticStepSucceeded = false;
            throw new InvalidOperationException($"{DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.MissingSemanticConfiguration}: RP theme service unavailable for semantic mapping resolution.");
        }

        // Per product requirement:
        //  * Semantic Event Mappings drive theme scoring; once a primary theme is committed
        //    they are not needed anymore (the score race is over).
        //  * Semantic Stat Mappings drive per-character stats and always run while the
        //    session is live.
        //  * SessionThemeSelections is authoritative for live sessions; SelectedRPThemeProfileId
        //    is only a seed at session create time. Resolve mappings from SessionThemeSelections
        //    when available. Missing configuration fails fast — no silent no-ops.
        // The theme-score deltas (built from semantic event mappings) are gated below by
        // ActiveScenarioId so they stop accumulating after primary commit; stat deltas always
        // apply regardless of theme-commit status.

        IReadOnlyDictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticEventMapping>> mappingsByEvent;
        IReadOnlyDictionary<string, IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>> statMappingsByEvent;

        var sessionThemeIds = (session.SessionThemeSelections ?? [])
            .Select(x => x.ThemeId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Compute commit status early — used below to suppress theme-score deltas once the
        // narrative has progressed past BuildUp. "Committed" = phase advanced beyond BuildUp.
        // Note: stat mapping scoping is based on ActiveScenarioId alone (see below), not this flag.
        var primaryThemeCommitted = !string.IsNullOrWhiteSpace(session.AdaptiveState.ActiveScenarioId)
            && session.AdaptiveState.CurrentPhase != NarrativePhase.BuildUp;

        if (sessionThemeIds.Count > 0)
        {
            mappingsByEvent = await _rpThemeService.ResolveSemanticEventMappingsByThemeIdsAsync(sessionThemeIds, cancellationToken);
            statMappingsByEvent = await _rpThemeService.ResolveSemanticStatMappingsByThemeIdsAsync(sessionThemeIds, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(session.SelectedRPThemeProfileId))
        {
            mappingsByEvent = await _rpThemeService.ResolveSemanticEventMappingsByProfileAsync(session.SelectedRPThemeProfileId, cancellationToken);
            statMappingsByEvent = await _rpThemeService.ResolveSemanticStatMappingsByProfileAsync(session.SelectedRPThemeProfileId, cancellationToken);
        }
        else
        {
            state.SemanticStepSucceeded = false;
            throw new InvalidOperationException(
                $"{DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.MissingSemanticConfiguration}: session '{session.Id}' has no SessionThemeSelections and no SelectedRPThemeProfileId; cannot resolve semantic mappings.");
        }

        // Scope stat mappings to avoid multi-theme stacking:
        //  * Active theme set (ActiveScenarioId is non-empty): a theme has been picked — only that
        //    theme's stat mappings apply, across all phases including BuildUp. Other selected themes
        //    must not stack their stat deltas once the narrative is tracking a specific theme.
        //  * No active theme yet: deduplicate across all selected themes — for each
        //    (eventId, targetStat) pair keep only the single highest-magnitude mapping. This lets
        //    all themes participate in the selection race without stacking identical-event deltas
        //    from similar themes (e.g. infidelity-brief-disappearance duplicating
        //    infidelity-public-facade-v2 entries).
        if (!string.IsNullOrWhiteSpace(session.AdaptiveState.ActiveScenarioId))
        {
            var activeThemeId = session.AdaptiveState.ActiveScenarioId;
            statMappingsByEvent = statMappingsByEvent
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => (IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>)kvp.Value
                        .Where(m => string.Equals(m.ThemeId, activeThemeId, StringComparison.OrdinalIgnoreCase))
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);
            _logger?.LogInformation(
                "Semantic stat mappings scoped to active theme. SessionId={SessionId} ActiveThemeId={ActiveThemeId} Phase={Phase}",
                session.Id, activeThemeId, session.AdaptiveState.CurrentPhase);
        }
        else
        {
            // No active theme yet — deduplicate by (eventId, targetStat), keeping highest absolute delta per pair.
            statMappingsByEvent = statMappingsByEvent
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => (IReadOnlyList<DreamGenClone.Domain.RolePlay.RPSemanticStatMapping>)kvp.Value
                        .GroupBy(m => m.TargetStat, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.OrderByDescending(m => Math.Abs(m.Delta)).First())
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }

        var pending = new List<(string EventId, decimal Confidence, DreamGenClone.Domain.RolePlay.RPSemanticEventMapping Mapping, string TargetCharacterId)>();
        var pendingStat = new List<(string EventId, decimal Confidence, DreamGenClone.Domain.RolePlay.RPSemanticStatMapping Mapping, string TargetCharacterId)>();
        var priorInteraction = session.Interactions
            .Where(x => !string.Equals(x.Id, interaction.Id, StringComparison.OrdinalIgnoreCase))
            .LastOrDefault();
        var priorEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (priorInteraction is not null)
        {
            var priorMatches = SemanticSignalRegex.Matches(priorInteraction.Content ?? string.Empty);
            foreach (Match prior in priorMatches)
            {
                var priorEventId = prior.Groups["event"].Value;
                if (!string.IsNullOrWhiteSpace(priorEventId))
                {
                    priorEventIds.Add(priorEventId);
                }
            }
        }

        var perThemeAppliedThisInteraction = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var perThemeSemanticCap = Math.Max(0m, decimal.Round((decimal)_semanticEvidencePerTurnCap, 4, MidpointRounding.AwayFromZero));

        var defaultTargetCharacterId = string.IsNullOrWhiteSpace(interaction.ActorName)
            ? "Unknown"
            : interaction.ActorName.Trim();

        foreach (Match match in matches)
        {
            var eventId = match.Groups["event"].Value;
            var confidenceText = match.Groups["confidence"].Value;
            if (!decimal.TryParse(confidenceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var confidence))
            {
                state.SemanticStepSucceeded = false;
                throw new InvalidOperationException($"{DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticPayloadInvalid}: invalid confidence '{confidenceText}' for event '{eventId}'.");
            }

            if (mappingsByEvent.TryGetValue(eventId, out var mappings)
                && mappings.Count > 0)
            {
                foreach (var mapping in mappings)
                {
                    if (confidence < mapping.ConfidenceMin || confidence > mapping.ConfidenceMax)
                    {
                        state.SemanticStepSucceeded = false;
                        throw new InvalidOperationException(
                            $"{DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.ConfidenceOutOfRange}: confidence {confidence.ToString(CultureInfo.InvariantCulture)} for event '{eventId}' is outside configured range [{mapping.ConfidenceMin.ToString(CultureInfo.InvariantCulture)}, {mapping.ConfidenceMax.ToString(CultureInfo.InvariantCulture)}].");
                    }

                    pending.Add((eventId, confidence, mapping, defaultTargetCharacterId));
                }
            }

            if (statMappingsByEvent.TryGetValue(eventId, out var statMappings) && statMappings.Count > 0)
            {
                foreach (var mapping in statMappings)
                {
                    if (confidence < mapping.ConfidenceMin || confidence > mapping.ConfidenceMax)
                    {
                        state.SemanticStepSucceeded = false;
                        throw new InvalidOperationException(
                            $"{DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.ConfidenceOutOfRange}: confidence {confidence.ToString(CultureInfo.InvariantCulture)} for event '{eventId}' is outside configured stat range [{mapping.ConfidenceMin.ToString(CultureInfo.InvariantCulture)}, {mapping.ConfidenceMax.ToString(CultureInfo.InvariantCulture)}].");
                    }

                    pendingStat.Add((eventId, confidence, mapping, defaultTargetCharacterId));
                }
            }
        }

        if (inferredSignals is not null)
        {
            foreach (var inferred in inferredSignals)
            {
                var eventId = inferred.EventId;
                var confidence = inferred.Confidence;
                var scopedTargetCharacterId = string.IsNullOrWhiteSpace(inferred.TargetCharacterName)
                    ? (!string.IsNullOrWhiteSpace(inferred.ActorName) ? inferred.ActorName!.Trim() : defaultTargetCharacterId)
                    : inferred.TargetCharacterName!.Trim();

                if (mappingsByEvent.TryGetValue(eventId, out var mappings)
                    && mappings.Count > 0)
                {
                    foreach (var mapping in mappings)
                    {
                        if (confidence < mapping.ConfidenceMin || confidence > mapping.ConfidenceMax)
                        {
                            state.SemanticStepSucceeded = false;
                            throw new InvalidOperationException(
                                $"{DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.ConfidenceOutOfRange}: confidence {confidence.ToString(CultureInfo.InvariantCulture)} for event '{eventId}' is outside configured range [{mapping.ConfidenceMin.ToString(CultureInfo.InvariantCulture)}, {mapping.ConfidenceMax.ToString(CultureInfo.InvariantCulture)}].");
                        }

                        pending.Add((eventId, confidence, mapping, scopedTargetCharacterId));
                    }
                }

                if (statMappingsByEvent.TryGetValue(eventId, out var statMappings) && statMappings.Count > 0)
                {
                    foreach (var mapping in statMappings)
                    {
                        if (confidence < mapping.ConfidenceMin || confidence > mapping.ConfidenceMax)
                        {
                            state.SemanticStepSucceeded = false;
                            throw new InvalidOperationException(
                                $"{DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.ConfidenceOutOfRange}: confidence {confidence.ToString(CultureInfo.InvariantCulture)} for event '{eventId}' is outside configured stat range [{mapping.ConfidenceMin.ToString(CultureInfo.InvariantCulture)}, {mapping.ConfidenceMax.ToString(CultureInfo.InvariantCulture)}].");
                        }

                        pendingStat.Add((eventId, confidence, mapping, defaultTargetCharacterId));
                    }
                }
            }
        }

        // Per product requirement:
        //  * Semantic Event Mappings drive theme scoring; once a primary theme is committed,
        //    non-active themes no longer participate (the score race is over for them) —
        //    otherwise every theme eventually pegs at the 100 cap as evidence keeps accumulating.
        //    The active theme continues to accumulate InteractionEvidenceSignal so the panel
        //    reflects ongoing narrative engagement.
        //  * Semantic Stat Mappings drive per-character stats and always run while the
        //    session is live, independent of theme-commit status.
        //  * SessionThemeSelections is authoritative for live sessions; SelectedRPThemeProfileId
        //    is only a seed at session create time. Resolve mappings from SessionThemeSelections
        //    when available. Missing configuration fails fast — no silent no-ops.
        if (primaryThemeCommitted && pending.Count > 0)
        {
            var activeId = session.AdaptiveState.ActiveScenarioId;
            var suppressedCount = pending.RemoveAll(x =>
                !string.Equals(x.Mapping.ThemeId, activeId, StringComparison.OrdinalIgnoreCase));
            _logger?.LogInformation(
                "Semantic theme-delta pass: non-active themes suppressed (primary committed). SessionId={SessionId} ActiveScenarioId={ActiveScenarioId} Phase={Phase} SuppressedCount={SuppressedCount} RemainingCount={RemainingCount}",
                session.Id,
                activeId,
                session.AdaptiveState.CurrentPhase,
                suppressedCount,
                pending.Count);
        }

        foreach (var item in pending)
        {
            if (!state.ThemeScores.TryGetValue(item.Mapping.ThemeId, out var trackerItem))
            {
                state.SemanticStepSucceeded = false;
                throw new InvalidOperationException($"{DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.MissingSemanticConfiguration}: mapped theme '{item.Mapping.ThemeId}' is not active in tracker.");
            }

            var rawDelta = item.Mapping.Delta;
            if (string.Equals(item.Mapping.Direction, "decrease", StringComparison.OrdinalIgnoreCase))
                rawDelta = -Math.Abs(rawDelta);
            decimal appliedDelta = rawDelta;
            decimal cappedDelta = 0m;
            decimal suppressedDelta = 0m;
            string? suppressionReasonCode = null;

            if (priorEventIds.Contains(item.EventId))
            {
                appliedDelta = 0m;
                suppressedDelta = rawDelta;
                suppressionReasonCode = DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticSuppressedAdjacentCooldown;
            }

            if (suppressionReasonCode is null && trackerItem.Blocked)
            {
                appliedDelta = 0m;
                suppressedDelta = rawDelta;
                suppressionReasonCode = DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticSuppressedThemeBlocked;
                trackerItem.Score = 0;
                trackerItem.Breakdown.InteractionEvidenceSignal = 0;
            }

            if (suppressionReasonCode is null && perThemeSemanticCap > 0m)
            {
                perThemeAppliedThisInteraction.TryGetValue(item.Mapping.ThemeId, out var alreadyAppliedMagnitude);
                var requestedMagnitude = Math.Abs(rawDelta);
                var remainingMagnitude = Math.Max(0m, perThemeSemanticCap - alreadyAppliedMagnitude);

                if (remainingMagnitude <= 0m)
                {
                    appliedDelta = 0m;
                    cappedDelta = rawDelta;
                    suppressionReasonCode = DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticCappedPerTurn;
                }
                else if (requestedMagnitude > remainingMagnitude)
                {
                    var sign = rawDelta < 0m ? -1m : 1m;
                    appliedDelta = sign * remainingMagnitude;
                    cappedDelta = rawDelta - appliedDelta;
                    suppressionReasonCode = DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticCappedPerTurn;
                }
            }

            if (appliedDelta != 0m)
            {
                trackerItem.Score = Math.Clamp(trackerItem.Score + (double)appliedDelta, 0, 100);
                trackerItem.Breakdown.InteractionEvidenceSignal = Math.Clamp(trackerItem.Breakdown.InteractionEvidenceSignal + (double)appliedDelta, 0, 100);
                perThemeAppliedThisInteraction.TryGetValue(item.Mapping.ThemeId, out var appliedMagnitude);
                perThemeAppliedThisInteraction[item.Mapping.ThemeId] = appliedMagnitude + Math.Abs(appliedDelta);
            }

            state.SemanticEvents.Add(new SemanticEventRecord
            {
                InteractionId = interaction.Id,
                EventId = item.EventId,
                Confidence = item.Confidence,
                MappingId = $"{item.Mapping.ThemeId}:{item.Mapping.ReasonCode}",
                Direction = item.Mapping.Direction,
                ThemeTargets = [item.Mapping.ThemeId],
                ProcessedUtc = DateTime.UtcNow
            });

            state.SemanticDeltaBreakdowns.Add(new SemanticThemeDeltaBreakdown
            {
                InteractionId = interaction.Id,
                ThemeId = item.Mapping.ThemeId,
                SourceType = "semantic",
                RawDelta = rawDelta,
                AppliedDelta = appliedDelta,
                CappedDelta = cappedDelta,
                SuppressedDelta = suppressedDelta,
                SuppressionReasonCode = suppressionReasonCode
            });

            state.RecentEvidence.Add(new ThemeEvidenceRecord
            {
                InteractionId = interaction.Id,
                ThemeId = item.Mapping.ThemeId,
                SignalType = "semantic-evidence",
                Delta = (double)appliedDelta,
                Confidence = (double)item.Confidence,
                Rationale = item.Mapping.ReasonCode
            });
        }

        var perCharacterStatAppliedMagnitude = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var perCharacterAppliedDeltas = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in pendingStat)
        {
            if (!state.ThemeScores.TryGetValue(item.Mapping.ThemeId, out var trackerItem))
            {
                state.SemanticStepSucceeded = false;
                throw new InvalidOperationException($"{DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.MissingSemanticConfiguration}: mapped theme '{item.Mapping.ThemeId}' is not active in tracker.");
            }

            var rawDelta = item.Mapping.Delta;
            if (string.Equals(item.Mapping.Direction, "decrease", StringComparison.OrdinalIgnoreCase))
                rawDelta = -Math.Abs(rawDelta);
            decimal appliedDelta = rawDelta;
            decimal cappedDelta = 0m;
            decimal suppressedDelta = 0m;
            string? suppressionReasonCode = null;

            if (priorEventIds.Contains(item.EventId))
            {
                appliedDelta = 0m;
                suppressedDelta = rawDelta;
                suppressionReasonCode = DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticSuppressedAdjacentCooldown;
            }

            if (suppressionReasonCode is null && trackerItem.Blocked)
            {
                appliedDelta = 0m;
                suppressedDelta = rawDelta;
                suppressionReasonCode = DreamGenClone.Domain.RolePlay.RPSemanticDiagnosticReasonCodes.SemanticSuppressedThemeBlocked;
            }

            // Per-turn semantic stat cap removed: trust configured mapping Delta values.
            // Stat range is still clamped by ApplyDelta to AdaptiveStatCatalog.MinValue/MaxValue.

            if (appliedDelta != 0m)
            {
                var deltaInt = appliedDelta > 0m
                    ? (int)Math.Floor(appliedDelta)
                    : (int)Math.Ceiling(appliedDelta);
                if (deltaInt != 0)
                {
                    var targetStats = GetCharacterStats(state, item.TargetCharacterId);
                    if (targetStats is null) continue;
                    if (CharacterStatProfileV2Accessor.ApplyDelta(targetStats, item.Mapping.TargetStat, deltaInt))
                    {
                        if (!perCharacterAppliedDeltas.TryGetValue(item.TargetCharacterId, out var charDeltas))
                        {
                            charDeltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                            perCharacterAppliedDeltas[item.TargetCharacterId] = charDeltas;
                        }
                        charDeltas.TryGetValue(item.Mapping.TargetStat, out var existingDelta);
                        charDeltas[item.Mapping.TargetStat] = existingDelta + deltaInt;

                        // Encounter dimension drift (mirrors ApplyTrackedDelta in UpdateFromInteractionAsync).
                        // EnableAdaptiveStateUpdates=false skips the inline keyword path entirely, so this
                        // is the ONLY place RuntimeEncounterStats gets seeded and drifted during a session.
                        // Do NOT remove this — without it, behavioral dimensions are never initialized.
                        if (state.CharacterRoles.TryGetValue(item.TargetCharacterId, out var semanticTargetRole)
                            && !string.IsNullOrWhiteSpace(semanticTargetRole))
                        {
                            if (targetStats.RuntimeEncounterStats is not { Count: > 0 })
                            {
                                targetStats.RuntimeEncounterStats = BehavioralDimensionCatalog
                                    .GetDimensions(semanticTargetRole)
                                    .ToDictionary(d => d.Name, _ => 50, StringComparer.OrdinalIgnoreCase);
                            }
                            StatToDimensionMappings.ApplyDelta(
                                targetStats.RuntimeEncounterStats, semanticTargetRole, item.Mapping.TargetStat, deltaInt);
                        }
                    }
                }

                var magnitudeKey = $"{item.Mapping.TargetStat}";
                perCharacterStatAppliedMagnitude.TryGetValue(magnitudeKey, out var appliedMagnitude);
                perCharacterStatAppliedMagnitude[magnitudeKey] = appliedMagnitude + Math.Abs(appliedDelta);
            }

            state.SemanticStatDeltaBreakdowns.Add(new SemanticStatDeltaRecord
            {
                InteractionId = interaction.Id,
                CharacterId = item.TargetCharacterId,
                StatName = item.Mapping.TargetStat,
                SourceType = "semantic",
                RawDelta = rawDelta,
                AppliedDelta = appliedDelta,
                CappedDelta = cappedDelta,
                SuppressedDelta = suppressedDelta,
                SuppressionReasonCode = suppressionReasonCode,
                ReasonCode = item.Mapping.ReasonCode
            });
        }

        foreach (var (characterId, appliedDeltas) in perCharacterAppliedDeltas)
        {
            var profile = GetCharacterStats(state, characterId);
            if (profile is null) continue;
            profile.LastStatDeltas = appliedDeltas;
            profile.LastStatDeltaUpdatedUtc = DateTime.UtcNow;
            profile.UpdatedUtc = DateTime.UtcNow;
        }

        TrimEvidence(state);

        _logger?.LogInformation(
            "Semantic processing completed: SessionId={SessionId}, InteractionId={InteractionId}, EventCount={EventCount}, BreakdownCount={BreakdownCount}",
            session.Id,
            interaction.Id,
            state.SemanticEvents.Count,
            state.SemanticDeltaBreakdowns.Count);
    }

    public async Task EvaluateAdaptiveIntensityTransitionAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        CancellationToken cancellationToken)
    {
        if (_intensityProfileService is null)
        {
            _logger?.LogWarning("IntensityTransition session={SessionId}: skipped — no intensity profile service", session.Id);
            return;
        }

        session.AdaptiveIntensityTransitions ??= [];

        if (session.IsIntensityManuallyPinned)
        {
            session.AdaptiveIntensityLastTransitionReason = "manual-pin-suppressed";
            _logger?.LogInformation("IntensityTransition session={SessionId}: manual-pin-suppressed", session.Id);
            return;
        }

        var profiles = await _intensityProfileService.ListAsync(cancellationToken);
        if (profiles.Count == 0)
        {
            session.AdaptiveIntensityLastTransitionReason = "no-intensity-profiles";
            _logger?.LogWarning("IntensityTransition session={SessionId}: no profiles in database", session.Id);
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
            _logger?.LogWarning("IntensityTransition session={SessionId}: adaptive profile id={ProfileId} not found in {Count} profiles",
                session.Id, session.AdaptiveIntensityProfileId, profiles.Count);
            return;
        }

        var selectedProfile = !string.IsNullOrWhiteSpace(session.SelectedIntensityProfileId)
            ? profiles.FirstOrDefault(x => string.Equals(x.Id, session.SelectedIntensityProfileId, StringComparison.OrdinalIgnoreCase))
            : null;
        // Intro/Atmospheric is not a valid character intensity baseline (it is filtered from the
        // UI dropdown). Treat it as null so the Emotional anchor is used instead.
        if (selectedProfile?.Intensity == IntensityLevel.Intro)
        {
            selectedProfile = null;
        }
        // Anchor to the lowest non-Intro profile (Emotional, level 1) when no selected profile
        // exists. Using currentProfile as fallback causes a ratchet effect where each transition
        // raises the baseline — BuildUp would climb Atmospheric?Emotional?Suggestive?Sensual?
        // Erotic?Hardcore in a handful of interactions instead of staying stable per phase.
        var phaseBaselineSourceProfile = selectedProfile
            ?? profiles.FirstOrDefault(p => p.Intensity == IntensityLevel.Emotional)
            ?? currentProfile;
        // Phase ladder: Observer/Reset = base, BuildUp = base+1, Committed = base+2,
        // Approaching = base+3, Climax = base+4. Floor and ceiling clamp the result.
        var phaseStep = session.AdaptiveState.CurrentPhase switch
        {
            NarrativePhase.BuildUp     => 1,
            NarrativePhase.Committed   => 2,
            NarrativePhase.Approaching => 3,
            NarrativePhase.Climax      => 4,
            NarrativePhase.Reset       => 0,
            _                          => 0
        };
        var selectedScale = (int)phaseBaselineSourceProfile.Intensity;
        var flowBaselineScale = Math.Clamp(selectedScale + phaseStep, 1, 5);

        var floor = RolePlayStyleResolver.ParseBoundScale(session.IntensityFloorOverride);
        var ceiling = RolePlayStyleResolver.ParseBoundScale(session.IntensityCeilingOverride);
        var targetScale = Math.Clamp(flowBaselineScale, 1, 5);

        var reasonCode = $"phase-driven|phase={session.AdaptiveState.CurrentPhase}|phase-step={phaseStep}";

        _logger?.LogInformation(
            "IntensityTransition session={SessionId} phase={Phase} baselineProfile={BaselineProfile}({BaselineScale}) " +
            "currentAdaptive={CurrentAdaptive} phaseStep={PhaseStep} flowScale={FlowScale} floor={Floor} ceiling={Ceiling} targetScale={TargetScale}",
            session.Id, session.AdaptiveState.CurrentPhase,
            phaseBaselineSourceProfile.Name, selectedScale,
            currentProfile.Name,
            phaseStep, flowBaselineScale,
            session.IntensityFloorOverride ?? "(none)", session.IntensityCeilingOverride ?? "(none)",
            targetScale);

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
        if (session.AdaptiveState.CurrentPhase == NarrativePhase.Approaching
            && targetScale > (int)IntensityLevel.Explicit)
        {
            targetScale = (int)IntensityLevel.Explicit;
            reasonCode += "-approaching-capped-at-erotic";
        }

        var targetProfile = profiles.FirstOrDefault(x => (int)x.Intensity == targetScale);
        if (targetProfile is null)
        {
            session.AdaptiveIntensityLastTransitionReason = reasonCode + "-target-profile-missing";
            _logger?.LogWarning("IntensityTransition session={SessionId}: no profile found for targetScale={TargetScale}", session.Id, targetScale);
            return;
        }

        if (string.Equals(targetProfile.Id, currentProfile.Id, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("IntensityTransition session={SessionId}: no change needed — already at {Profile} (scale={Scale})",
                session.Id, targetProfile.Name, targetScale);
            session.AdaptiveIntensityLastTransitionReason = reasonCode;
            return;
        }

        _logger?.LogInformation("IntensityTransition session={SessionId}: TRANSITIONING {From} ? {To} (scale {FromScale}?{ToScale}) reason={Reason}",
            session.Id, currentProfile.Name, targetProfile.Name, (int)currentProfile.Intensity, targetScale, reasonCode);

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

    private static double AverageCharacterStat(AdaptiveScenarioState state, string statName)
    {
        // Only scenario-bound characters (those with a CharacterRole) contribute to averages.
        var tracked = state.CharacterStats.Values
            .Where(x => !string.IsNullOrEmpty(x.CharacterRole))
            .ToList();

        if (tracked.Count == 0)
        {
            return AdaptiveStatCatalog.DefaultValue;
        }

        return tracked
            .Select(x => CharacterStatProfileV2Accessor.GetStatOrDefault(x, statName))
            .Average();
    }

    private static double Clamp01(double value) => Math.Clamp(Math.Round(value, 4), 0.0, 1.0);

    private static void EnsureThemeCatalog(AdaptiveScenarioState state, IReadOnlyList<ThemeCatalogEntry> catalogEntries)
    {
        var validIds = catalogEntries.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownIds = state.ThemeScores.Keys.Where(x => !validIds.Contains(x)).ToList();
        foreach (var unknownId in unknownIds)
        {
            state.ThemeScores.Remove(unknownId);
        }

        foreach (var entry in catalogEntries)
        {
            if (!state.ThemeScores.ContainsKey(entry.Id))
            {
                state.ThemeScores[entry.Id] = new ThemeScoreState
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
        // Per-session theme selections take precedence over everything else.
        // Only the explicitly selected themes are seeded into the tracker.
        if (_rpThemeService is not null && session.SessionThemeSelections.Count > 0)
        {
            var selectedIds = session.SessionThemeSelections
                .Select(x => x.ThemeId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var selectedThemes = await _rpThemeService.ListThemesAsync(includeDisabled: false, cancellationToken: cancellationToken);
            return selectedThemes
                .Where(t => selectedIds.Contains(t.Id))
                .Select(MapRpThemeToCatalogEntry)
                .ToList();
        }

        if (_rpThemeService is not null && !string.IsNullOrWhiteSpace(session.SelectedRPThemeProfileId))
        {
            var rpThemes = await _rpThemeService.ListThemesByProfileAsync(session.SelectedRPThemeProfileId, includeDisabled: false, cancellationToken);
            if (rpThemes.Count > 0)
            {
                return rpThemes
                    .Select(MapRpThemeToCatalogEntry)
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

    public void RebindEncounterProfile(
        AdaptiveScenarioState state,
        string characterId,
        string? profileId,
        IReadOnlyDictionary<string, int>? profileEncounterStats = null,
        string? targetRole = null)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            state.CharacterEncounterProfileIds.Remove(characterId);
        else
            state.CharacterEncounterProfileIds[characterId] = profileId;

        if (!string.IsNullOrWhiteSpace(targetRole))
        {
            state.CharacterRoles[characterId] = targetRole;
            if (state.CharacterStats.TryGetValue(characterId, out var roleProfile))
                roleProfile.CharacterRole = targetRole;
        }

        if (state.CharacterStats.TryGetValue(characterId, out var profile))
        {
            profile.RuntimeEncounterStats = profileEncounterStats is { Count: > 0 }
                ? new Dictionary<string, int>(profileEncounterStats, StringComparer.OrdinalIgnoreCase)
                : null;
        }
    }

    private static CharacterStatProfileV2? GetCharacterStats(AdaptiveScenarioState state, string actorKey)
    {
        state.CharacterStats.TryGetValue(actorKey, out var existing);
        return existing;
    }

    private static void EnsureDefaultStats(Dictionary<string, int> stats) { }

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
        AdaptiveScenarioState state,
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

        var trackerItem = state.ThemeScores[themeId];

        trackerItem.Score = Math.Clamp(trackerItem.Score + signal, 0, 100);
        trackerItem.Intensity = trackerItem.Score switch
        {
            < 20 => "Minor",
            < 45 => "Moderate",
            < 70 => "Major",
            _ => "Central"
        };

        trackerItem.Breakdown.InteractionEvidenceSignal = Math.Clamp(trackerItem.Breakdown.InteractionEvidenceSignal + signal, 0, 100);

        state.RecentEvidence.Add(new ThemeEvidenceRecord
        {
            InteractionId = interaction.Id,
            ThemeId = themeId,
            SignalType = "interaction-evidence",
            Delta = signal,
            Confidence = 0.65,
            Rationale = BuildKeywordRationale(themeName, contentLower, keywords, groupedKeywords)
        });

        TrimEvidence(state);
    }

    private static void RecalculateSelectedThemes(AdaptiveScenarioState state, RolePlayInteraction interaction)
    {
        // Decrement completion cooldowns before selection each interaction
        foreach (var theme in state.ThemeScores.Values.Where(t => t.CompletionCooldownTurns > 0))
        {
            theme.CompletionCooldownTurns--;
        }

        // Turn count is incremented at user-turn boundaries (see RolePlayEngineService
        // turn-start sites), not per interaction. Do not increment here.

        var ordered = state.ThemeScores.Values
            .Where(x => x.CompletionCooldownTurns == 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.ThemeId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Observation window guard: must run BEFORE ActiveScenarioLock so that a stale
        // ActiveScenarioId left in state from a prior cycle (e.g. a PayloadJson race where the
        // just-completed scenario was not cleared before this function is called) cannot re-lock
        // the tracker. While observing we enforce null on ActiveScenarioId so every downstream
        // consumer (background job loader, AlignPromptNarrativeStateWithV2Async, HydrateV2State)
        // sees a consistent observation-window state.
        if (state.SelectionMinimumTurns > 0 && state.ObservedTurnCount <= state.SelectionMinimumTurns)
        {
            state.ThemeSelectionRule = "Observing";
            state.ActiveScenarioId = null;
            // Clear any stale selection from a prior pass so downstream consumers (semantic
            // theme-delta application, UI panel, prompt builders) see a consistent Observing
            // state. Without this, a PrimaryThemeId left over from a previous Top1/Top2Blend
            // recalculation would incorrectly short-circuit "theme already picked" gates while
            // the score race is still open.
            state.PrimaryThemeId = null;
            state.SecondaryThemeId = null;
            return;
        }

        if (string.Equals(state.ThemeSelectionRule, "ManualOverride", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(state.ActiveScenarioId)
            && state.ThemeScores.ContainsKey(state.ActiveScenarioId))
        {
            state.PrimaryThemeId = state.ActiveScenarioId;
            state.SecondaryThemeId = ordered
                .FirstOrDefault(x => !string.Equals(x.ThemeId, state.ActiveScenarioId, StringComparison.OrdinalIgnoreCase))
                ?.ThemeId;
            return;
        }

        // Active-scenario lock: once the engine (or any caller) has committed an ActiveScenarioId,
        // the tracker's primary slot must follow it so prompt builders, style resolver, and the
        // continuation service all see a consistent primary theme. Secondary is the highest-scoring
        // non-primary theme so the secondary-theme guidance still flows into prompts.
        if (!string.IsNullOrWhiteSpace(state.ActiveScenarioId)
            && state.ThemeScores.ContainsKey(state.ActiveScenarioId))
        {
            state.PrimaryThemeId = state.ActiveScenarioId;
            state.SecondaryThemeId = ordered
                .FirstOrDefault(x => !string.Equals(x.ThemeId, state.ActiveScenarioId, StringComparison.OrdinalIgnoreCase))
                ?.ThemeId;
            state.ThemeSelectionRule = "ActiveScenarioLock";
            return;
        }

        var previousPrimary = state.PrimaryThemeId;
        var previousSecondary = state.SecondaryThemeId;

        state.PrimaryThemeId = ordered.FirstOrDefault()?.ThemeId;
        state.SecondaryThemeId = null;
        state.ThemeSelectionRule = "Top1";

        if (ordered.Count >= 2)
        {
            state.SecondaryThemeId = ordered[1].ThemeId;
            state.ThemeSelectionRule = "Top2Blend";
        }

        if (!string.Equals(previousPrimary, state.PrimaryThemeId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previousSecondary, state.SecondaryThemeId, StringComparison.OrdinalIgnoreCase))
        {
            var selectedThemes = new[] { state.PrimaryThemeId, state.SecondaryThemeId }
                .Where(x => !string.IsNullOrWhiteSpace(x));
            state.RecentEvidence.Add(new ThemeEvidenceRecord
            {
                InteractionId = interaction.Id,
                ThemeId = "theme-selection",
                SignalType = "selection-rule",
                Delta = 0,
                Confidence = 0.8,
                Rationale = $"Applied {state.ThemeSelectionRule}: {string.Join(", ", selectedThemes)}"
            });
            TrimEvidence(state);
        }
    }

    private static void TrimEvidence(AdaptiveScenarioState state)
    {
        if (state.RecentEvidence.Count > 100)
        {
            state.RecentEvidence.RemoveRange(0, state.RecentEvidence.Count - 100);
        }
    }

    private async Task<Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>> LoadRpThemeKeywordGroupsByThemeIdAsync(
        RolePlaySession session,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.OrdinalIgnoreCase);
        if (_rpThemeService is null || string.IsNullOrWhiteSpace(session.SelectedRPThemeProfileId))
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

    private static void RemoveNonCharacterStats(AdaptiveScenarioState state)
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

    private static void RemoveNonCanonicalStatEntries(AdaptiveScenarioState state)
    {
        // V2 CharacterStatProfileV2 uses fixed typed fields for each canonical stat;
        // non-canonical stat entries cannot be added, so this is intentionally a no-op.
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

        var state = session.AdaptiveState;

        // --- T030: Initialize ThemeTracker from catalog entries ---
        var catalogEntries = await LoadRuntimeCatalogEntriesAsync(session, cancellationToken);
        EnsureThemeCatalog(state, catalogEntries);

        // --- T030: Resolve ThemeProfile preferences and apply ChoiceSignal ---
        var blockedCount = 0;
        if (session.SessionThemeSelections.Count > 0)
        {
            // Per-session selections: apply tier signals from each selection directly.
            // The tracker already contains only the selected themes (set by LoadRuntimeCatalogEntriesAsync),
            // so we iterate selections and apply the stored tier to the matching tracker item.
            foreach (var selection in session.SessionThemeSelections)
            {
                if (!state.ThemeScores.TryGetValue(selection.ThemeId, out var trackerItem))
                {
                    continue;
                }

                var choiceSignal = selection.Tier switch
                {
                    DreamGenClone.Domain.RolePlay.RPThemeTier.MustHave => 15,
                    DreamGenClone.Domain.RolePlay.RPThemeTier.StronglyPrefer => 8,
                    DreamGenClone.Domain.RolePlay.RPThemeTier.NiceToHave => 3,
                    DreamGenClone.Domain.RolePlay.RPThemeTier.Discouraged => -5,
                    _ => 0
                };

                trackerItem.Breakdown.ChoiceSignal = choiceSignal;
                trackerItem.Score = Math.Clamp(trackerItem.Score + choiceSignal, 0, 100);

                if (selection.Tier == DreamGenClone.Domain.RolePlay.RPThemeTier.MustHave)
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
        else if (_rpThemeService is not null && !string.IsNullOrWhiteSpace(session.SelectedRPThemeProfileId))
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

                var matchedTracker = state.ThemeScores.Values.FirstOrDefault(x =>
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

                if (!state.ThemeScores.TryGetValue(matchedEntry.Id, out var trackerItem)) continue;

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
            if (!state.ThemeScores.TryGetValue(entry.Id, out var trackerItem)) continue;
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
                    var current = CharacterStatProfileV2Accessor.GetStatOrDefault(charBlock, normalized);
                    CharacterStatProfileV2Accessor.SetStat(charBlock, normalized, Math.Clamp(current + bias, 0, 100));
                }
            }
        }

        // Apply StatAffinities deltas from scoring catalog themes
        foreach (var entry in catalogEntries)
        {
            if (entry.StatAffinities is not { Count: > 0 }) continue;
            if (!state.ThemeScores.TryGetValue(entry.Id, out var trackerItem)) continue;
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
                    var current = CharacterStatProfileV2Accessor.GetStatOrDefault(charBlock, normalized);
                    CharacterStatProfileV2Accessor.SetStat(charBlock, normalized, Math.Clamp(current + affinityDelta, 0, 100));
                }
            }
        }

        // Compute theme selection minimum from profile settings.
        // Resolve profile from session first, then fall through to scenario default.
        state.ObservedTurnCount = 0;
        var activeThemeCount = state.ThemeScores.Values.Count(t => !t.Blocked);
        var resolvedProfileId = !string.IsNullOrWhiteSpace(session.SelectedRPThemeProfileId)
            ? session.SelectedRPThemeProfileId
            : scenario.DefaultRPThemeProfileId;
        if (activeThemeCount > 1 && _rpThemeService is not null && !string.IsNullOrWhiteSpace(resolvedProfileId))
        {
            var themeProfile = await _rpThemeService.GetProfileAsync(resolvedProfileId, cancellationToken);
            if (themeProfile is null)
            {
                throw new InvalidOperationException(
                    $"RP theme profile '{resolvedProfileId}' not found; cannot compute theme selection minimum for session '{session.Id}'.");
            }

            state.SelectionMinimumTurns = (activeThemeCount - 1) * themeProfile.ThemeSelectionTurnsPerTheme;
        }
        else
        {
            state.SelectionMinimumTurns = 0;
        }

        // Set the initial selection rule based on whether the observer window is active.
        // Without this, the domain-model default "Top1" would be persisted from seed, causing
        // the UI to show "Top1" instead of "Observing" before the first engine cycle runs.
        if (state.SelectionMinimumTurns > 0)
        {
            state.ThemeSelectionRule = "Observing";
            state.PrimaryThemeId = null;
            state.SecondaryThemeId = null;
        }

        state.ThemeTrackerUpdatedUtc = DateTime.UtcNow;
        RemoveNonCanonicalStatEntries(state);
        session.AdaptiveState = state;

        // --- T034: Logging ---
        var topSeeded = state.ThemeScores.Values
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

        _logger?.LogInformation(
            "ThemeGate seed session={SessionId}: activeThemes={ActiveThemeCount} resolvedProfile={ProfileId} sessionProfileId={SessionProfileId} scenarioProfileId={ScenarioProfileId} minTurns={MinTurns}",
            session.Id,
            activeThemeCount,
            resolvedProfileId ?? "(none)",
            session.SelectedRPThemeProfileId ?? "(none)",
            scenario.DefaultRPThemeProfileId ?? "(none)",
            state.SelectionMinimumTurns);
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

        var state = session.AdaptiveState;
        if (!state.ThemeScores.TryGetValue(requestedScenarioId, out var requestedTheme) || requestedTheme.Blocked)
        {
            return false;
        }

        state.ActiveScenarioId = requestedScenarioId;
        state.ScenarioCommitmentTimeUtc = DateTime.UtcNow;
        state.TurnsSinceCommitment = 0;
        state.TurnsInApproaching = 0;

        var previousPrimary = state.PrimaryThemeId;
        state.PrimaryThemeId = requestedScenarioId;
        if (!string.Equals(previousPrimary, requestedScenarioId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(previousPrimary)
            && state.ThemeScores.ContainsKey(previousPrimary))
        {
            state.SecondaryThemeId = previousPrimary;
        }

        state.ThemeSelectionRule = "ManualOverride";
        state.ThemeTrackerUpdatedUtc = DateTime.UtcNow;

        requestedTheme.Breakdown.ChoiceSignal = Math.Max(requestedTheme.Breakdown.ChoiceSignal, 30);
        requestedTheme.IsScenarioCandidate = true;
        requestedTheme.LastCandidateEvaluationTimeUtc = DateTime.UtcNow;

        session.AdaptiveState = state;

        _logger?.LogInformation(
            "Manual adaptive override applied for session {SessionId}: requestedScenarioId={ScenarioId}, phase={Phase}",
            session.Id,
            requestedScenarioId,
            state.CurrentPhase);

        return true;
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