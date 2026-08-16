using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Domain.RolePlay;

/// <summary>
/// Sticky, session-persisted override of the theme phase-guidance markers and word-count
/// target (B-082). Nullable fields mean "no override" — the theme marker (then phase
/// default) decides. A set value wins over the theme marker.
///
/// Prompt-only dimensions (Pacing, BeatScope, TimeShift, Granularity, Deepening,
/// RequireScenePresence) are read at prompt build by scene-direction resolution.
/// Engine markers (ForceMultiEncounterClimax, ForceAftermathHusbandContrast) are read
/// between prompts by RolePlayEngineService / the semantic job at the encounter-boundary
/// and time-skip decision points.
/// </summary>
public sealed class ContinuationOverride
{
    // ── Scene-direction dimensions (read at prompt build) ──
    public ScenePacing? Pacing { get; set; }
    public BeatScope? BeatScope { get; set; }
    public TimeShiftPolicy? TimeShift { get; set; }
    public NarrativeGranularity? Granularity { get; set; }
    public DeepeningPolicy? Deepening { get; set; }
    public bool? RequireScenePresence { get; set; }

    // ── Engine markers (read between prompts) ──
    // null = theme marker decides; true = force on; false = force off.
    public bool? ForceMultiEncounterClimax { get; set; }
    public bool? ForceAftermathHusbandContrast { get; set; }

    // ── Word count ──
    public int? WordTargetMin { get; set; }
    public int? WordTargetMax { get; set; }

    public bool HasSceneDirectionOverride => Pacing.HasValue || BeatScope.HasValue
        || TimeShift.HasValue || Granularity.HasValue || Deepening.HasValue
        || RequireScenePresence.HasValue;

    /// <summary>Dimensions that no other slot renders (Beat Style, Time Shift, Granularity, Scene Presence).</summary>
    public bool HasUnconsumedDimensionOverride => BeatScope.HasValue || TimeShift.HasValue
        || Granularity.HasValue || RequireScenePresence.HasValue;

    public bool HasWordCountOverride => WordTargetMin.HasValue || WordTargetMax.HasValue;

    public bool HasAny => HasSceneDirectionOverride
        || ForceMultiEncounterClimax.HasValue || ForceAftermathHusbandContrast.HasValue
        || HasWordCountOverride;
}
