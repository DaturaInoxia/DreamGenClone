using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public static class RolePlayStyleResolver
{
    public static (string Label, string Reason) ResolveEffectiveStyle(RolePlaySession session, ToneIntensity? baseToneIntensity)
    {
        var baseScale = baseToneIntensity.HasValue ? (int)baseToneIntensity.Value : 2;
        var reasonParts = new List<string>
        {
            $"base={(ToneIntensity)Math.Clamp(baseScale, 0, 5)}"
        };

        if (!session.IsToneManuallyPinned)
        {
            var arousalValues = session.AdaptiveState.CharacterStats.Values
                .SelectMany(x => x.Stats.Where(kvp => string.Equals(kvp.Key, "Arousal", StringComparison.OrdinalIgnoreCase)).Select(kvp => kvp.Value))
                .ToList();
            if (arousalValues.Count > 0)
            {
                var avgArousal = arousalValues.Average();
                if (avgArousal >= 85)
                {
                    baseScale += 2;
                    reasonParts.Add("arousal=very-high(+2)");
                }
                else if (avgArousal >= 70)
                {
                    baseScale += 1;
                    reasonParts.Add("arousal=high(+1)");
                }
                else if (avgArousal <= 35)
                {
                    baseScale -= 1;
                    reasonParts.Add("arousal=low(-1)");
                }
            }

            if (session.Interactions.Count >= 14)
            {
                baseScale += 1;
                reasonParts.Add("progression=late(+1)");
            }
            else if (session.Interactions.Count <= 4)
            {
                baseScale -= 1;
                reasonParts.Add("progression=early(-1)");
            }

            var primary = session.AdaptiveState.ThemeTracker.PrimaryThemeId ?? string.Empty;
            var secondary = session.AdaptiveState.ThemeTracker.SecondaryThemeId ?? string.Empty;
            if (IsEscalatingTheme(primary) || IsEscalatingTheme(secondary))
            {
                baseScale += 1;
                reasonParts.Add("theme=escalating(+1)");
            }
        }
        else
        {
            reasonParts.Add("manual-pin=on(adaptive-deltas-suppressed)");
        }

        var floor = ParseBoundScale(session.StyleFloorOverride);
        var ceiling = ParseBoundScale(session.StyleCeilingOverride);

        if (floor.HasValue && ceiling.HasValue && floor.Value > ceiling.Value)
        {
            ceiling = floor;
            reasonParts.Add("bounds=normalized(floor>ceiling)");
        }

        var clamped = Math.Clamp(baseScale, 0, 5);
        if (floor.HasValue && clamped < floor.Value)
        {
            clamped = floor.Value;
            reasonParts.Add($"floor={ToStyleLabel(floor.Value)}");
        }

        if (ceiling.HasValue && clamped > ceiling.Value)
        {
            clamped = ceiling.Value;
            reasonParts.Add($"ceiling={ToStyleLabel(ceiling.Value)}");
        }

        return (ToStyleLabel(clamped), string.Join(", ", reasonParts));
    }

    public static int? ParseBoundScale(string? bound)
    {
        if (string.IsNullOrWhiteSpace(bound))
        {
            return null;
        }

        var value = bound.Trim().ToLowerInvariant();
        if (value.Contains("intro") || value.Contains("pg12") || value.Contains("pg-12")) return 0;
        if (value.Contains("emotional") || value.Contains("pg13") || value.Contains("pg-13")) return 1;
        if (value.Contains("suggestive")) return 2;
        if (value.Contains("sensual") || value.Contains("mature")) return 3;
        if (value.Contains("explicit") || value.Contains("erotic")) return 4;
        if (value.Contains("hardcore")) return 5;
        return null;
    }

    public static string ToStyleLabel(int scale)
    {
        return scale switch
        {
            0 => "Intro / PG-12",
            1 => "Emotional / PG-13",
            2 => "Suggestive / PG-13+",
            3 => "Sensual / Mature",
            4 => "Erotic / Explicit",
            _ => "Hardcore / Explicit+"
        };
    }

    private static bool IsEscalatingTheme(string themeId)
    {
        return string.Equals(themeId, "dominance", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeId, "power-dynamics", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeId, "forbidden-risk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeId, "humiliation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeId, "infidelity", StringComparison.OrdinalIgnoreCase);
    }
}
