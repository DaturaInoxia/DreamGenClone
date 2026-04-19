using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public static class RolePlayStyleResolver
{
    public static (string Label, string Reason) ResolveEffectiveStyle(
        RolePlaySession session,
        IntensityLevel? baseIntensityLevel,
        IntensityLevel? adaptiveIntensityLevel = null,
        SteeringProfile? styleProfile = null,
        IReadOnlyList<ThemePreference>? themePreferences = null)
    {
        var selectedScale = NormalizeCharacterScale(baseIntensityLevel.HasValue ? (int)baseIntensityLevel.Value : 2);
        var adaptiveScale = NormalizeCharacterScale(adaptiveIntensityLevel.HasValue ? (int)adaptiveIntensityLevel.Value : selectedScale);
        var baseScale = session.IsIntensityManuallyPinned ? selectedScale : adaptiveScale;
        var reasonParts = new List<string>
        {
            $"selected={(IntensityLevel)NormalizeCharacterScale(selectedScale)}",
            $"adaptive={(IntensityLevel)NormalizeCharacterScale(adaptiveScale)}"
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
                var escalatingThemeIds = styleProfile?.EscalatingThemeIds;

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
            reasonParts.Add("manual-pin=on(resolved=selected)");
        }

        var floor = ParseBoundScale(session.IntensityFloorOverride);
        var ceiling = ParseBoundScale(session.IntensityCeilingOverride);

        if (floor.HasValue && ceiling.HasValue && floor.Value > ceiling.Value)
        {
            ceiling = floor;
            reasonParts.Add("bounds=normalized(floor>ceiling)");
        }

        var clamped = NormalizeCharacterScale(baseScale);
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

        if (!session.IsIntensityManuallyPinned
            && session.AdaptiveState.CurrentNarrativePhase == NarrativePhase.Approaching
            && clamped > (int)IntensityLevel.Explicit)
        {
            clamped = (int)IntensityLevel.Explicit;
            reasonParts.Add("approaching-capped-at-erotic");
        }

        return (ToStyleLabel(clamped), string.Join(", ", reasonParts));
    }

    public static int? ParseBoundScale(string? bound)
    {
        var parsed = IntensityLadder.ParseScale(bound);
        return parsed.HasValue ? NormalizeCharacterScale(parsed.Value) : null;
    }

    public static string ToStyleLabel(int scale)
    {
        return IntensityLadder.GetLabel(NormalizeCharacterScale(scale));
    }

    private static int NormalizeCharacterScale(int scale)
    {
        // Character turns never use Atmospheric/Intro; Emotional is the minimum.
        return Math.Clamp(scale, 1, 5);
    }

    private static bool IsEscalatingTheme(string themeId, IReadOnlyList<string>? profileEscalatingThemeIds)
    {
        if (string.IsNullOrWhiteSpace(themeId)) return false;

        if (profileEscalatingThemeIds is { Count: > 0 })
        {
            return profileEscalatingThemeIds.Any(id =>
                string.Equals(id, themeId, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }
}
