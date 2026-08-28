using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Domain.RolePlay;
using NarrativePhase = DreamGenClone.Domain.RolePlay.NarrativePhase;

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
        var selectedScale = NormalizeCharacterScale(baseIntensityLevel.HasValue ? (int)baseIntensityLevel.Value : 1);
        var adaptiveScale = NormalizeCharacterScale(adaptiveIntensityLevel.HasValue ? (int)adaptiveIntensityLevel.Value : selectedScale);
        var baseScale = session.IsIntensityManuallyPinned ? selectedScale : adaptiveScale;
        var reasonParts = new List<string>
        {
            $"selected={(IntensityLevel)NormalizeCharacterScale(selectedScale)}",
            $"adaptive={(IntensityLevel)NormalizeCharacterScale(adaptiveScale)}"
        };

        if (session.IsIntensityManuallyPinned)
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
            && session.AdaptiveState.CurrentPhase == NarrativePhase.Approaching
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

}

