using System.Reflection;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.RolePlay;

public static class CharacterStatProfileV2Accessor
{
    private static readonly IReadOnlyDictionary<string, PropertyInfo> StatProperties = BuildStatProperties();
    private static readonly IReadOnlyDictionary<string, string> CanonicalNameByComparableKey = BuildCanonicalKeyLookup();
    private static readonly IReadOnlyDictionary<string, string> BehavioralDimensionNameByComparableKey = BuildBehavioralDimensionKeyLookup();

    public static IReadOnlyList<string> CanonicalStatNames => AdaptiveStatCatalog.CanonicalStatNames;

    /// <summary>
    /// All behavioral dimension names that can be resolved from <see cref="CharacterStatProfileV2.RuntimeEncounterStats"/>.
    /// </summary>
    public static IReadOnlySet<string> BehavioralDimensionNames => BehavioralDimensionCatalog.AllDimensionNames;

    public static CharacterStatProfileV2 CreateFromStats(string characterId, IReadOnlyDictionary<string, int>? stats)
    {
        var profile = new CharacterStatProfileV2
        {
            CharacterId = characterId ?? string.Empty
        };
        var normalized = AdaptiveStatCatalog.NormalizeComplete(stats);

        foreach (var (name, value) in normalized)
        {
            SetStat(profile, name, value);
        }

        return profile;
    }

    public static CharacterStatProfileV2 CreateDefault(string characterId)
        => CreateFromStats(characterId, null);

    public static bool TryGetStat(CharacterStatProfileV2 profile, string statName, out int value)
    {
        value = AdaptiveStatCatalog.DefaultValue;
        if (profile is null || !TryResolveCanonicalStatName(statName, out var canonicalStatName))
        {
            return false;
        }

        if (!StatProperties.TryGetValue(canonicalStatName, out var property))
        {
            return false;
        }

        value = (int)(property.GetValue(profile) ?? AdaptiveStatCatalog.DefaultValue);
        return true;
    }

    public static int GetStatOrDefault(CharacterStatProfileV2 profile, string statName, int fallback = AdaptiveStatCatalog.DefaultValue)
    {
        if (TryGetStat(profile, statName, out var value))
        {
            return value;
        }

        if (TryGetBehavioralDimension(profile, statName, out var dimValue))
        {
            return dimValue;
        }

        return fallback;
    }

    /// <summary>
    /// Reads a behavioral dimension value from <see cref="CharacterStatProfileV2.RuntimeEncounterStats"/>.
    /// Returns false if the name is not a recognized behavioral dimension or the stats dictionary is null.
    /// </summary>
    public static bool TryGetBehavioralDimension(CharacterStatProfileV2 profile, string statName, out int value)
    {
        value = AdaptiveStatCatalog.DefaultValue;
        if (profile?.RuntimeEncounterStats is null || string.IsNullOrWhiteSpace(statName))
        {
            return false;
        }

        var key = ToComparableKey(statName);
        if (!BehavioralDimensionNameByComparableKey.TryGetValue(key, out var canonicalName))
        {
            return false;
        }

        if (profile.RuntimeEncounterStats.TryGetValue(canonicalName, out var dimValue))
        {
            value = dimValue;
            return true;
        }

        return false;
    }

    public static bool SetStat(CharacterStatProfileV2 profile, string statName, int value)
    {
        if (profile is null || !TryResolveCanonicalStatName(statName, out var canonicalStatName))
        {
            return false;
        }

        if (!StatProperties.TryGetValue(canonicalStatName, out var property))
        {
            return false;
        }

        property.SetValue(profile, Math.Clamp(value, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue));
        return true;
    }

    public static bool ApplyDelta(CharacterStatProfileV2 profile, string statName, int delta)
    {
        if (!TryGetStat(profile, statName, out var current))
        {
            return false;
        }

        return SetStat(profile, statName, current + delta);
    }

    /// <summary>
    /// Returns a snapshot dictionary of all canonical stat values for <paramref name="profile"/>.
    /// Equivalent to V1's <c>new Dictionary&lt;string,int&gt;(charBlock.Stats)</c>.
    /// </summary>
    public static Dictionary<string, int> GetAllStats(CharacterStatProfileV2 profile)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var statName in AdaptiveStatCatalog.CanonicalStatNames)
            result[statName] = GetStatOrDefault(profile, statName);
        return result;
    }

    /// <summary>
    /// Applies every entry in <paramref name="deltas"/> to <paramref name="profile"/> via <see cref="ApplyDelta"/>.
    /// </summary>
    public static void ApplyAllDeltas(CharacterStatProfileV2 profile, IReadOnlyDictionary<string, int> deltas)
    {
        foreach (var (name, delta) in deltas)
            ApplyDelta(profile, name, delta);
    }

    /// <summary>
    /// Overwrites every entry in <paramref name="stats"/> into <paramref name="profile"/> via <see cref="SetStat"/>.
    /// </summary>
    public static void SetAllStats(CharacterStatProfileV2 profile, IReadOnlyDictionary<string, int> stats)
    {
        foreach (var (name, value) in stats)
            SetStat(profile, name, value);
    }

    private static bool TryResolveCanonicalStatName(string statName, out string canonicalStatName)
    {
        canonicalStatName = string.Empty;
        if (string.IsNullOrWhiteSpace(statName))
        {
            return false;
        }

        var key = ToComparableKey(statName);
        if (!CanonicalNameByComparableKey.TryGetValue(key, out var resolvedName)
            || string.IsNullOrWhiteSpace(resolvedName))
        {
            return false;
        }

        canonicalStatName = resolvedName;
        return true;
    }

    private static IReadOnlyDictionary<string, PropertyInfo> BuildStatProperties()
    {
        var properties = typeof(CharacterStatProfileV2)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.CanWrite && p.PropertyType == typeof(int))
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        return properties;
    }

    private static IReadOnlyDictionary<string, string> BuildCanonicalKeyLookup()
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var statName in AdaptiveStatCatalog.CanonicalStatNames)
        {
            lookup[ToComparableKey(statName)] = statName;
        }

        return lookup;
    }

    private static IReadOnlyDictionary<string, string> BuildBehavioralDimensionKeyLookup()
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dimName in BehavioralDimensionCatalog.AllDimensionNames)
        {
            lookup[ToComparableKey(dimName)] = dimName;
        }

        return lookup;
    }

    private static string ToComparableKey(string value)
    {
        var normalized = new string(value
            .Trim()
            .Where(c => c != '_' && c != '-' && c != ' ')
            .ToArray());
        return normalized.ToUpperInvariant();
    }
}