using System.Reflection;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public static class CharacterStatProfileAccessor
{
    private static readonly IReadOnlyDictionary<string, PropertyInfo> StatProperties = BuildStatProperties();
    private static readonly IReadOnlyDictionary<string, string> CanonicalNameByComparableKey = BuildCanonicalKeyLookup();

    public static IReadOnlyList<string> CanonicalStatNames => AdaptiveStatCatalog.CanonicalStatNames;

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
        => TryGetStat(profile, statName, out var value) ? value : fallback;

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

    private static string ToComparableKey(string value)
    {
        var normalized = new string(value
            .Trim()
            .Where(c => c != '_' && c != '-' && c != ' ')
            .ToArray());
        return normalized.ToUpperInvariant();
    }
}