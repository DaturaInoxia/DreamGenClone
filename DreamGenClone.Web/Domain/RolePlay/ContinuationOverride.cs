using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Domain.RolePlay;

/// <summary>
/// Sticky, session-persisted override of the theme phase-guidance markers and word-count
/// target (B-082). Nullable fields mean "no override" — the theme marker (then phase
/// default) decides. A set value wins over the theme marker.
///
/// B-089: Tempo/Span are the primary scene-direction controls (coherent bundles); the raw
/// dimensions (Pacing, BeatScope, TimeShift, Granularity, Deepening) remain for power-user
/// "Advanced" disclosure. Prompt-only dimensions are read at prompt build by scene-direction
/// resolution. Engine markers (ForceMultiEncounterClimax, ForceAftermathHusbandContrast) are
/// read between prompts by RolePlayEngineService / the semantic job at the encounter-boundary
/// and time-skip decision points.
/// </summary>
public sealed class ContinuationOverride
{
    // ── B-089 primary scene-direction controls (Tempo + Span) ──
    // Tempo = density bundle (Linger/Steady/Push/Leap → coherent Pacing+TimeShift+Granularity).
    // Span = duration (Moment/Scene/ExtendedArc → BeatScope budget).
    // When set, they win over the raw fields below; the raw fields remain for power-user
    // "Advanced" disclosure and backward compatibility.
    public SceneTempo? Tempo { get; set; }
    public SceneSpan? Span { get; set; }

    // ── Scene-direction dimensions (read at prompt build) ──
    public ScenePacing? Pacing { get; set; }
    public BeatScope? BeatScope { get; set; }
    public TimeShiftPolicy? TimeShift { get; set; }
    public NarrativeGranularity? Granularity { get; set; }
    public DeepeningPolicy? Deepening { get; set; }

    // ── Engine markers (read between prompts) ──
    // null = theme marker decides; true = force on; false = force off.
    public bool? ForceMultiEncounterClimax { get; set; }
    public bool? ForceAftermathHusbandContrast { get; set; }

    // ── Word count ──
    public int? WordTargetMin { get; set; }
    public int? WordTargetMax { get; set; }

    public bool HasSceneDirectionOverride => Tempo.HasValue || Span.HasValue
        || Pacing.HasValue || BeatScope.HasValue
        || TimeShift.HasValue || Granularity.HasValue || Deepening.HasValue;

    /// <summary>Dimensions that no other slot renders (Beat Style, Time Shift, Granularity).</summary>
    public bool HasUnconsumedDimensionOverride => BeatScope.HasValue || TimeShift.HasValue
        || Granularity.HasValue;

    public bool HasWordCountOverride => WordTargetMin.HasValue || WordTargetMax.HasValue;

    public bool HasAny => HasSceneDirectionOverride
        || ForceMultiEncounterClimax.HasValue || ForceAftermathHusbandContrast.HasValue
        || HasWordCountOverride;
}
