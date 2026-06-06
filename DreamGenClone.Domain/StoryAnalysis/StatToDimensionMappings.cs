namespace DreamGenClone.Domain.StoryAnalysis;

/// <summary>
/// A single stat-to-encounter-dimension drift rule.
/// When a stat changes by <paramref name="Slope"/> × delta units, the named dimension drifts accordingly.
/// </summary>
public sealed record DimensionDriftRule(
    string StatName,
    string TargetRole,
    string DimensionName,
    double Slope,
    int Floor,
    int Ceiling);

/// <summary>
/// Static catalog of all stat-to-encounter-dimension drift rules.
/// Wife has 8 rules; Husband has 6 rules; OtherMan has none.
/// </summary>
public static class StatToDimensionMappings
{
    private static readonly IReadOnlyList<DimensionDriftRule> WifeRules =
    [
        new("Desire",      "Wife", "Exhibitionism",      +0.90, 0, 100),
        new("Desire",      "Wife", "DiscoveryCaution",   -0.60, 0, 100),
        new("Restraint",   "Wife", "DiscoveryCaution",   +0.90, 0, 100),
        new("Restraint",   "Wife", "Exhibitionism",      -0.60, 0, 100),
        new("Restraint",   "Wife", "PostEncounterGuilt", +0.45, 0, 100),
        new("SelfRespect", "Wife", "DiscoveryCaution",   +0.60, 0, 100),
        new("Loyalty",     "Wife", "EmotionalEngagement",+0.60, 0, 100),
        new("Loyalty",     "Wife", "PostEncounterGuilt", +0.75, 0, 100),
    ];

    private static readonly IReadOnlyList<DimensionDriftRule> HusbandRules =
    [
        new("Dominance",   "Husband", "Acceptance",    -1.05, 0, 100),
        new("Dominance",   "Husband", "Voyeurism",     -0.75, 0, 100),
        new("Dominance",   "Husband", "Participation", -0.60, 0, 100),
        new("Dominance",   "Husband", "Encouragement", -0.75, 0, 100),
        new("SelfRespect", "Husband", "Acceptance",    -0.60, 0, 100),
        new("SelfRespect", "Husband", "Encouragement", -0.60, 0, 100),
    ];

    /// <summary>
    /// Returns all drift rules for the given target role.
    /// OtherMan and unrecognized roles return an empty list.
    /// </summary>
    public static IReadOnlyList<DimensionDriftRule> GetRules(string targetRole)
    {
        if (targetRole.Equals("Wife", StringComparison.OrdinalIgnoreCase))
        {
            return WifeRules;
        }

        if (targetRole.Equals("Husband", StringComparison.OrdinalIgnoreCase))
        {
            return HusbandRules;
        }

        return [];
    }

    /// <summary>
    /// Applies drift to <paramref name="encounterStats"/> for the given stat delta.
    /// For each matching rule: encounterStats[dim] = Clamp(current + Round(slope × delta), floor, ceiling).
    /// No-op when <paramref name="statDelta"/> is zero.
    /// </summary>
    public static void ApplyDelta(
        Dictionary<string, int> encounterStats,
        string targetRole,
        string statName,
        int statDelta)
    {
        if (statDelta == 0)
        {
            return;
        }

        var rules = GetRules(targetRole);
        foreach (var rule in rules)
        {
            if (!rule.StatName.Equals(statName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var current = encounterStats.TryGetValue(rule.DimensionName, out var v) ? v : 50;
            var driftRaw = rule.Slope * statDelta;
            var drift = (int)Math.Round(driftRaw);
            var next = Math.Clamp(current + drift, rule.Floor, rule.Ceiling);
            encounterStats[rule.DimensionName] = next;
        }
    }
}
