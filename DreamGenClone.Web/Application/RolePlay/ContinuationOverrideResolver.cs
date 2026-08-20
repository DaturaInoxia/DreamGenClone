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

        ValidateCoherentOverride(ov);

        var baseDir = intensity.SceneDirection ?? new SceneDirection();
        var overridden = ApplySceneDirectionOverride(baseDir, ov);
        return intensity with { SceneDirection = overridden };
    }

    /// <summary>
    /// B-089: applies a <see cref="ContinuationOverride"/> to a raw <see cref="SceneDirection"/>
    /// without needing the full <see cref="ResolvedIntensityData"/> wrapper. Used by prompt
    /// assembly (via <see cref="ApplySceneDirection"/>) and by engine decision points
    /// (e.g. time-skip Tempo resolution) so the two never diverge.
    /// </summary>
    public static SceneDirection ApplySceneDirectionOverride(SceneDirection baseDirection, ContinuationOverride? ov)
    {
        if (ov is null || !ov.HasSceneDirectionOverride)
            return baseDirection;

        ValidateCoherentOverride(ov);

        // B-089: Tempo/Span are the primary controls. A set Tempo wins over the raw
        // Pacing/TimeShift/Granularity (it IS their coherent bundle); a set Span wins over
        // BeatScope. The raw fields remain for power-user "Advanced" disclosure and are
        // applied on top if explicitly set.
        var overridden = baseDirection with
        {
            Pacing = ov.Tempo.HasValue ? SceneDirection.TempoBundle(ov.Tempo.Value).Pacing : baseDirection.Pacing,
            TimeShift = ov.Tempo.HasValue ? SceneDirection.TempoBundle(ov.Tempo.Value).TimeShift : baseDirection.TimeShift,
            Granularity = ov.Tempo.HasValue ? SceneDirection.TempoBundle(ov.Tempo.Value).Granularity : baseDirection.Granularity,
            BeatScope = ov.Span.HasValue ? SceneDirection.SpanToBeatScope(ov.Span.Value) : baseDirection.BeatScope,
        };

        // Raw-field overrides (Advanced) applied on top — they win for their single dimension.
        return overridden with
        {
            Pacing = ov.Pacing ?? overridden.Pacing,
            BeatScope = ResolveBeatScope(overridden, ov),
            TimeShift = ov.TimeShift ?? overridden.TimeShift,
            Granularity = ov.Granularity ?? overridden.Granularity,
            Deepening = ov.Deepening ?? overridden.Deepening,
        };
    }

    /// <summary>
    /// Single decision path for Beat Style: override first (raw BeatScope, then Span),
    /// then the resolved base value. Used by both prompt assembly and the beat-budget cursor
    /// so the two never diverge.
    /// </summary>
    public static BeatScope ResolveBeatScope(SceneDirection baseDirection, ContinuationOverride? ov)
        => ov?.BeatScope
           ?? (ov?.Span.HasValue == true ? SceneDirection.SpanToBeatScope(ov.Span.Value) : baseDirection.BeatScope);

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

    /// <summary>
    /// B-089 T8 — fail-fast on contradictory overrides. The Tempo bundle already makes the
    /// primary controls structurally coherent; the only remaining contradiction is a Tempo
    /// override combined with a raw field override that contradicts that Tempo's own bundle
    /// (e.g. Tempo=Linger + Pacing=Fast). This throws with an explicit diagnostic instead of
    /// silently letting the raw field win and reintroducing the old contradictory prompt (C10).
    /// </summary>
    public static void ValidateCoherentOverride(ContinuationOverride? ov)
    {
        if (ov is null || !ov.Tempo.HasValue)
            return;

        var (bundlePacing, bundleTimeShift, bundleGranularity) = SceneDirection.TempoBundle(ov.Tempo.Value);
        if (ov.Pacing.HasValue && ov.Pacing.Value != bundlePacing)
            throw new InvalidOperationException(
                $"ContinuationOverrideConflict: Tempo={ov.Tempo.Value} is a coherent bundle (Pacing={bundlePacing}, TimeShift={bundleTimeShift}, Granularity={bundleGranularity}) but Pacing={ov.Pacing.Value} was also set. Pick Tempo alone or clear the conflicting raw field — the prompt would otherwise emit a contradictory directive.");
        if (ov.TimeShift.HasValue && ov.TimeShift.Value != bundleTimeShift)
            throw new InvalidOperationException(
                $"ContinuationOverrideConflict: Tempo={ov.Tempo.Value} is a coherent bundle (Pacing={bundlePacing}, TimeShift={bundleTimeShift}, Granularity={bundleGranularity}) but TimeShift={ov.TimeShift.Value} was also set. Pick Tempo alone or clear the conflicting raw field — the prompt would otherwise emit a contradictory directive.");
        if (ov.Granularity.HasValue && ov.Granularity.Value != bundleGranularity)
            throw new InvalidOperationException(
                $"ContinuationOverrideConflict: Tempo={ov.Tempo.Value} is a coherent bundle (Pacing={bundlePacing}, TimeShift={bundleTimeShift}, Granularity={bundleGranularity}) but Granularity={ov.Granularity.Value} was also set. Pick Tempo alone or clear the conflicting raw field — the prompt would otherwise emit a contradictory directive.");
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
