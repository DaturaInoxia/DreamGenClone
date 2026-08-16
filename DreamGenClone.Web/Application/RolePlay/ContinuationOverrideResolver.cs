using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Applies the sticky <see cref="ContinuationOverride"/> at the exact resolution points the
/// theme markers are read from. Single decision path per dimension: override first, then
/// theme marker, then (for scene direction) phase default. No fallback/default branch is
/// introduced — a null override field simply falls through to the existing theme/phase value.
/// </summary>
public static class ContinuationOverrideResolver
{
    public static ResolvedIntensityData ApplySceneDirection(ResolvedIntensityData intensity, ContinuationOverride? ov)
    {
        if (ov is null || !ov.HasSceneDirectionOverride)
            return intensity;

        var baseDir = intensity.SceneDirection ?? new SceneDirection();
        var overridden = baseDir with
        {
            Pacing = ov.Pacing ?? baseDir.Pacing,
            BeatScope = ov.BeatScope ?? baseDir.BeatScope,
            TimeShift = ov.TimeShift ?? baseDir.TimeShift,
            Granularity = ov.Granularity ?? baseDir.Granularity,
            Deepening = ov.Deepening ?? baseDir.Deepening,
            RequireScenePresence = ov.RequireScenePresence ?? baseDir.RequireScenePresence,
        };

        return intensity with { SceneDirection = overridden };
    }

    public static ResolvedWritingStyleData ApplyWordCount(ResolvedWritingStyleData style, ContinuationOverride? ov)
    {
        if (ov is null || !ov.HasWordCountOverride)
            return style;

        var min = ov.WordTargetMin ?? style.WordTargetMin;
        var max = ov.WordTargetMax ?? style.WordTargetMax;

        return style with
        {
            WordTargetMin = min,
            WordTargetMax = max,
            WordTargetMarker = "override",
        };
    }

    public static bool ResolveMultiEncounterClimax(RolePlaySession session, RPTheme? theme)
        => session.ContinuationOverride?.ForceMultiEncounterClimax
           ?? (theme is not null && RolePlayAssistantPrompts.IsMultiEncounterClimax(theme, "Climax"));

    public static bool ResolveAftermathHusbandContrast(RolePlaySession session, RPTheme? theme, string phase)
    {
        // Aftermath is out of scope in Reset (B-056) — the override does not re-enable it there.
        if (string.Equals(phase, "Reset", StringComparison.OrdinalIgnoreCase))
            return false;

        return session.ContinuationOverride?.ForceAftermathHusbandContrast
            ?? (theme is not null && RolePlayAssistantPrompts.IsAftermathHusbandContrast(theme, phase));
    }
}
