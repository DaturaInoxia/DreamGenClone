using DreamGenClone.Web.Domain.RolePlay;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.StoryAnalysis;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayAdaptiveStateService : IRolePlayAdaptiveStateService
{
    private sealed record ThemeRule(string ThemeId, string ThemeName, string[] Keywords, int Weight);

    private static readonly ThemeRule[] ThemeCatalog =
    {
        new("intimacy", "Intimacy", new[] { "close", "touch", "tender", "soft", "gentle", "warm" }, 3),
        new("trust-building", "Trust Building", new[] { "trust", "safe", "reassure", "honest", "promise" }, 3),
        new("power-dynamics", "Power Dynamics", new[] { "control", "command", "obey", "submit", "claim" }, 4),
        new("jealousy-triangle", "Jealousy Triangle", new[] { "jealous", "envy", "comparison", "rival" }, 4),
        new("forbidden-risk", "Forbidden Risk", new[] { "secret", "hide", "risk", "danger", "caught", "forbidden" }, 4),
        new("confession", "Confession", new[] { "confess", "admit", "truth", "reveal", "tell you" }, 3),
        new("voyeurism", "Voyeurism", new[] { "watch", "hidden", "shadows", "peek", "observed" }, 4),
        new("infidelity", "Infidelity", new[] { "cheat", "betray", "affair", "husband", "wife" }, 4),
        new("humiliation", "Humiliation", new[] { "humiliate", "inferior", "embarrass", "degrade", "shame" }, 4),
        new("dominance", "Dominance", new[] { "dominate", "command", "control", "kneel", "order" }, 4)
    };

    private readonly IRolePlayDebugEventSink? _debugEventSink;
    private readonly ILogger<RolePlayAdaptiveStateService>? _logger;

    public RolePlayAdaptiveStateService()
    {
    }

    public RolePlayAdaptiveStateService(
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayAdaptiveStateService> logger)
    {
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

        var state = session.AdaptiveState ?? new RolePlayAdaptiveState();
        EnsureThemeCatalog(state.ThemeTracker);
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
            ApplyDelta(actorStats.Stats, "Arousal", Score(contentLower, ["kiss", "touch", "desire", "want", "close", "heat"], 3));
            ApplyDelta(actorStats.Stats, "Inhibition", -Score(contentLower, ["can't", "wrong", "shouldn't", "hesitate", "guilt"], 2));
            ApplyDelta(actorStats.Stats, "Tension", Score(contentLower, ["fear", "caught", "risk", "panic", "nervous"], 3));
            ApplyDelta(actorStats.Stats, "Trust", Score(contentLower, ["safe", "comfort", "trust", "reassure"], 2));
            ApplyDelta(actorStats.Stats, "Agency", Score(contentLower, ["control", "command", "obey", "claim", "choose", "decide", "insist"], 2));

            actorStats.UpdatedUtc = DateTime.UtcNow;
        }

        foreach (var theme in ThemeCatalog)
        {
            UpdateTheme(state, interaction, theme.ThemeName, theme.ThemeId, contentLower, theme.Keywords, theme.Weight);
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

    private static void EnsureThemeCatalog(ThemeTrackerState tracker)
    {
        var validIds = ThemeCatalog.Select(x => x.ThemeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownIds = tracker.Themes.Keys.Where(x => !validIds.Contains(x)).ToList();
        foreach (var unknownId in unknownIds)
        {
            tracker.Themes.Remove(unknownId);
        }

        foreach (var theme in ThemeCatalog)
        {
            if (!tracker.Themes.ContainsKey(theme.ThemeId))
            {
                tracker.Themes[theme.ThemeId] = new ThemeTrackerItem
                {
                    ThemeId = theme.ThemeId,
                    ThemeName = theme.ThemeName,
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
        int weight)
    {
        var signal = Score(contentLower, keywords, weight);
        if (signal <= 0)
        {
            return;
        }

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
}