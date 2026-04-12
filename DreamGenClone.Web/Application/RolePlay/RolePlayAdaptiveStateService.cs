using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayAdaptiveStateService : IRolePlayAdaptiveStateService
{
    private readonly IThemeCatalogService _themeCatalogService;
    private readonly IThemePreferenceService? _themePreferenceService;
    private readonly ISteeringProfileService? _steeringProfileService;
    private readonly IRolePlayDebugEventSink? _debugEventSink;
    private readonly ILogger<RolePlayAdaptiveStateService>? _logger;

    public RolePlayAdaptiveStateService(IThemeCatalogService themeCatalogService)
    {
        _themeCatalogService = themeCatalogService;
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
    {
        _themeCatalogService = themeCatalogService;
        _debugEventSink = debugEventSink;
        _logger = logger;
    }

    public RolePlayAdaptiveStateService(
        IThemeCatalogService themeCatalogService,
        IThemePreferenceService themePreferenceService,
        ISteeringProfileService styleProfileService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
    {
        _themeCatalogService = themeCatalogService;
        _themePreferenceService = themePreferenceService;
        _steeringProfileService = styleProfileService;
        _debugEventSink = debugEventSink;
        _logger = logger;
    }

    public async Task<RolePlayAdaptiveState> UpdateFromInteractionAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(interaction);

        var catalogEntries = await _themeCatalogService.GetAllAsync(includeDisabled: false, cancellationToken);

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
            // T044: Skip blocked themes, increment SuppressedHitCount
            if (state.ThemeTracker.Themes.TryGetValue(entry.Id, out var trackerItem) && trackerItem.Blocked)
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

            UpdateTheme(state, interaction, entry.Label, entry.Id, contentLower, entry.Keywords, entry.Weight, affinityMultiplier);

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
        state.ThemeTracker.UpdatedUtc = DateTime.UtcNow;

        session.AdaptiveState = state;

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
        double affinityMultiplier = 1.0)
    {
        var rawSignal = Score(contentLower, keywords, weight);
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
            Rationale = $"Matched keywords for {themeName}: {string.Join(", ", keywords.Where(contentLower.Contains))}"
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
        var catalogEntries = await _themeCatalogService.GetAllAsync(includeDisabled: false, cancellationToken);
        EnsureThemeCatalog(state.ThemeTracker, catalogEntries);

        // --- T030: Resolve ThemeProfile preferences and apply ChoiceSignal ---
        var blockedCount = 0;
        if (_themePreferenceService is not null && !string.IsNullOrWhiteSpace(session.SelectedThemeProfileId))
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