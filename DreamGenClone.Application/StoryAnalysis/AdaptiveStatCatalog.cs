namespace DreamGenClone.Application.StoryAnalysis;

public sealed class AdaptiveStatDefinition
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string LowMeaning { get; init; }

    public required string HighMeaning { get; init; }

    public int DefaultValue { get; init; } = 50;
}

public static class AdaptiveStatCatalog
{
    public const int MinValue = 0;
    public const int MaxValue = 100;
    public const int DefaultValue = 50;

    public static readonly IReadOnlyList<AdaptiveStatDefinition> CanonicalStats =
    [
        new AdaptiveStatDefinition
        {
            Name = "Desire",
            Description = "Overall intensity of desire and physical activation.",
            LowMeaning = "Calm, detached, or emotionally cool.",
            HighMeaning = "Strong attraction, urgency, and physical pull."
        },
        new AdaptiveStatDefinition
        {
            Name = "Restraint",
            Description = "Strength of restraint and internal brakes.",
            LowMeaning = "Impulsive, permissive, and less self-restrained.",
            HighMeaning = "Cautious, guarded, and likely to hold back."
        },
        new AdaptiveStatDefinition
        {
            Name = "Tension",
            Description = "Conflict pressure from uncertainty, risk, or emotional strain.",
            LowMeaning = "Stable, low-friction, and relatively safe.",
            HighMeaning = "Volatile, high-stakes, and emotionally charged."
        },
        new AdaptiveStatDefinition
        {
            Name = "Connection",
            Description = "Personal confidence that the current dynamic feels safe and reliable.",
            LowMeaning = "Defensive, suspicious, and emotionally guarded.",
            HighMeaning = "Open, reassured, and more willing to engage."
        },
        new AdaptiveStatDefinition
        {
            Name = "Dominance",
            Description = "Perceived ability to choose, act, and steer outcomes.",
            LowMeaning = "Passive, cornered, or acted upon.",
            HighMeaning = "Decisive, self-directed, and in control of choices."
        },
        new AdaptiveStatDefinition
        {
            Name = "Loyalty",
            Description = "Strength of commitment and bond to the current relationship dynamic.",
            LowMeaning = "Detached, opportunistic, or weakly committed.",
            HighMeaning = "Steadfast, committed, and invested in continuity."
        },
        new AdaptiveStatDefinition
        {
            Name = "SelfRespect",
            Description = "Stability of personal boundaries and self-valuing under pressure.",
            LowMeaning = "Boundary erosion, self-compromise, or diminished self-regard.",
            HighMeaning = "Firm boundaries, self-valuing, and principled choices."
        }
    ];

    public static readonly IReadOnlyList<string> CanonicalStatNames = CanonicalStats.Select(x => x.Name).ToArray();

    private static readonly IReadOnlyDictionary<string, string> CanonicalNameLookup = CanonicalStats
        .ToDictionary(x => x.Name, x => x.Name, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> LegacyStatToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Arousal"] = "Desire",
        ["Inhibition"] = "Restraint",
        ["Trust"] = "Connection",
        ["Agency"] = "Dominance",
        ["Jealousy"] = "Tension",
        ["DominanceDrive"] = "Dominance",
        ["Shame"] = "Restraint",
        ["RiskAppetite"] = "Dominance"
    };

    public static string NormalizeLegacyStatName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var trimmed = name.Trim();
        if (CanonicalNameLookup.TryGetValue(trimmed, out var canonical)) return canonical;
        if (LegacyStatToCanonical.TryGetValue(trimmed, out var mapped)) return mapped;
        return trimmed;
    }

    public static Dictionary<string, int> CreateDefaultStatMap()
    {
        return CanonicalStats.ToDictionary(x => x.Name, x => x.DefaultValue, StringComparer.OrdinalIgnoreCase);
    }

    public static Dictionary<string, int> NormalizeComplete(IReadOnlyDictionary<string, int>? values)
    {
        var normalized = CreateDefaultStatMap();
        var partial = NormalizePartial(values);
        foreach (var (name, value) in partial)
        {
            normalized[name] = value;
        }

        return normalized;
    }

    public static Dictionary<string, int> NormalizePartial(IReadOnlyDictionary<string, int>? values)
    {
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (values is null || values.Count == 0)
        {
            return normalized;
        }

        var legacyBuckets = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rawName, rawValue) in values)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                continue;
            }

            var name = rawName.Trim();
            var clamped = Math.Clamp(rawValue, MinValue, MaxValue);

            if (CanonicalNameLookup.TryGetValue(name, out var canonicalName))
            {
                normalized[canonicalName] = clamped;
                continue;
            }

            if (!LegacyStatToCanonical.TryGetValue(name, out var mappedCanonical))
            {
                continue;
            }

            if (!legacyBuckets.TryGetValue(mappedCanonical, out var bucket))
            {
                bucket = [];
                legacyBuckets[mappedCanonical] = bucket;
            }

            bucket.Add(clamped);
        }

        foreach (var (mappedCanonical, bucket) in legacyBuckets)
        {
            if (normalized.ContainsKey(mappedCanonical) || bucket.Count == 0)
            {
                continue;
            }

            var average = (int)Math.Round(bucket.Average(), MidpointRounding.AwayFromZero);
            normalized[mappedCanonical] = Math.Clamp(average, MinValue, MaxValue);
        }

        return normalized;
    }
}