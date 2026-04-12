using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public static class RolePlayStyleResolver
{
    private static readonly HashSet<string> LegacyEscalatingThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "dominance", "power-dynamics", "forbidden-risk", "humiliation", "infidelity"
    };

    public static (string Label, string Reason) ResolveEffectiveStyle(
        RolePlaySession session,
        IntensityLevel? baseIntensityLevel,
        SteeringProfile? styleProfile = null,
        IReadOnlyList<ThemePreference>? themePreferences = null)
    {
        var baseScale = baseIntensityLevel.HasValue ? (int)baseIntensityLevel.Value : 2;
        var reasonParts = new List<string>
        {
            $"base={(IntensityLevel)Math.Clamp(baseScale, 0, 5)}"
        };

        if (!session.IsIntensityManuallyPinned)
        {
            var desireValues = session.AdaptiveState.CharacterStats.Values
                .SelectMany(x => x.Stats.Where(kvp => string.Equals(kvp.Key, "Desire", StringComparison.OrdinalIgnoreCase)).Select(kvp => kvp.Value))
                .ToList();
            if (desireValues.Count > 0)
            {
                var avgDesire = desireValues.Average();
                if (avgDesire >= 85)
                {
                    baseScale += 2;
                    reasonParts.Add("desire=very-high(+2)");
                }
                else if (avgDesire >= 70)
                {
                    baseScale += 1;
                    reasonParts.Add("desire=high(+1)");
                }
                else if (avgDesire <= 35)
                {
                    baseScale -= 1;
                    reasonParts.Add("desire=low(-1)");
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

            // T039: HardDealBreaker suppression — check before escalation
            var primary = session.AdaptiveState.ThemeTracker.PrimaryThemeId ?? string.Empty;
            var secondary = session.AdaptiveState.ThemeTracker.SecondaryThemeId ?? string.Empty;
            var dealBreakerSuppressed = false;

            if (themePreferences is not null)
            {
                dealBreakerSuppressed = themePreferences.Any(p =>
                    p.Tier == ThemeTier.HardDealBreaker
                    && (string.Equals(p.Name, primary, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(p.Name, secondary, StringComparison.OrdinalIgnoreCase)));
            }

            if (dealBreakerSuppressed)
            {
                reasonParts.Add("dealbreaker-suppressed");
            }
            else
            {
                // T036/T037: Profile-driven escalating theme check with legacy fallback
                var escalatingThemeIds = styleProfile?.EscalatingThemeIds is { Count: > 0 }
                    ? styleProfile.EscalatingThemeIds
                    : null;

                if (IsEscalatingTheme(primary, escalatingThemeIds) || IsEscalatingTheme(secondary, escalatingThemeIds))
                {
                    baseScale += 1;
                    reasonParts.Add("theme=escalating(+1)");
                }

                // T038: MustHave +1 push
                if (themePreferences is not null)
                {
                    var mustHaveMatch = themePreferences.Any(p =>
                        p.Tier == ThemeTier.MustHave
                        && string.Equals(p.Name, primary, StringComparison.OrdinalIgnoreCase));
                    if (mustHaveMatch)
                    {
                        baseScale += 1;
                        reasonParts.Add("musthave-push(+1)");
                    }
                }
            }
        }
        else
        {
            reasonParts.Add("manual-pin=on(adaptive-deltas-suppressed)");
        }

        var floor = ParseBoundScale(session.IntensityFloorOverride);
        var ceiling = ParseBoundScale(session.IntensityCeilingOverride);

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
        return IntensityLadder.ParseScale(bound);
    }

    public static string ToStyleLabel(int scale)
    {
        return IntensityLadder.GetLabel(scale);
    }

    private static bool IsEscalatingTheme(string themeId, IReadOnlyList<string>? profileEscalatingThemeIds)
    {
        if (string.IsNullOrWhiteSpace(themeId)) return false;

        // T036: Use profile-driven list when available
        if (profileEscalatingThemeIds is not null)
        {
            return profileEscalatingThemeIds.Any(id =>
                string.Equals(id, themeId, StringComparison.OrdinalIgnoreCase));
        }

        // T037: Fallback to legacy hardcoded list
        return LegacyEscalatingThemes.Contains(themeId);
    }
}
