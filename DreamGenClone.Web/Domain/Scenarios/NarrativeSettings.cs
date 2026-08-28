namespace DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Prose-focused narrative defaults for a scenario.
/// Includes narrative tone, prose style preferences, and presentation constraints.
/// </summary>
public class NarrativeSettings
{
    // ── New decomposed fields (deprecated per plan-amendment 2026-07-22) ──
    // These were scenario-level overrides that now belong to IntensityProfile.
    // Marked [Obsolete] — prompt builder no longer reads them.

    /// <summary>[DEPRECATED] Mood/attitude. Moved to IntensityProfile.ToneDirective.</summary>
    [Obsolete("Moved to IntensityProfile.ToneDirective per plan-amendment 2026-07-22")]
    public string? Tone { get; set; }

    /// <summary>[DEPRECATED] Language complexity. Moved to IntensityProfile.</summary>
    [Obsolete("Moved to IntensityProfile per plan-amendment 2026-07-22")]
    public string? Register { get; set; }

    /// <summary>[DEPRECATED] Subject emphasis. Moved to IntensityProfile.FocusDirective.</summary>
    [Obsolete("Moved to IntensityProfile.FocusDirective per plan-amendment 2026-07-22")]
    public string? Focus { get; set; }

    // ── Legacy field (deprecated) ──

    /// <summary>[DEPRECATED] Legacy combined tone string. Use IntensityProfile.ToneDirective instead.</summary>
    [Obsolete("Moved to IntensityProfile per plan-amendment 2026-07-22")]
    public string? NarrativeTone { get; set; }

    // ── Existing fields ─────────────────────────────────────────────

    public string? ProseStyle { get; set; }
    public string? PointOfView { get; set; }
    public List<string> NarrativeGuidelines { get; set; } = [];
}