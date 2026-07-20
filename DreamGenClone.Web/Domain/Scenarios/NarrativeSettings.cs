namespace DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Prose-focused narrative defaults for a scenario.
/// Includes narrative tone, prose style preferences, and presentation constraints.
/// </summary>
public class NarrativeSettings
{
    // ── New decomposed fields (FR-007, FR-008) ──────────────────────
    // These take precedence over the deprecated NarrativeTone field.
    // Resolution: Tone → NarrativeTone (fallback) → null (silent omit).

    /// <summary>Mood/attitude (e.g., "Erotic, conversational, playful").</summary>
    public string? Tone { get; set; }

    /// <summary>Language complexity (e.g., "Low to moderate language complexity").</summary>
    public string? Register { get; set; }

    /// <summary>Subject emphasis (e.g., "Physical pleasure").</summary>
    public string? Focus { get; set; }

    // ── Legacy field (deprecated, retained for backward compat) ─────

    /// <summary>[DEPRECATED] Legacy combined tone string. Use <see cref="Tone"/>, <see cref="Register"/>, <see cref="Focus"/> instead.</summary>
    public string? NarrativeTone { get; set; }

    // ── Existing fields ─────────────────────────────────────────────

    public string? ProseStyle { get; set; }
    public string? PointOfView { get; set; }
    public List<string> NarrativeGuidelines { get; set; } = [];
}