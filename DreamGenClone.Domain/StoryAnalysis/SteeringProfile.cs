namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class SteeringProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Example { get; set; } = string.Empty;

    public string RuleOfThumb { get; set; } = string.Empty;

    public Dictionary<string, int> ThemeAffinities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> EscalatingThemeIds { get; set; } = [];

    public Dictionary<string, int> StatBias { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    // ── Writing Instruction fields (FR-005, FR-006) ──────────────────
    // Fail-fast at prompt build time if empty/zero (no hardcoded fallbacks).

    /// <summary>Immersion rule for Character variant (e.g., "Stay inside this character's perceptions... Show, don't tell.")</summary>
    public string ImmersionDirective { get; set; } = string.Empty;

    /// <summary>Action rule for Character variant (e.g., "Respond to the scene naturally.")</summary>
    public string ActionDirective { get; set; } = string.Empty;

    /// <summary>Minimum word count for Character variant.</summary>
    public int WordTargetMin { get; set; }

    /// <summary>Maximum word count for Character variant.</summary>
    public int WordTargetMax { get; set; }

    /// <summary>Minimum word count for Narrative variant (intentionally longer than Character).</summary>
    public int NarrativeWordTargetMin { get; set; }

    /// <summary>Maximum word count for Narrative variant (intentionally longer than Character).</summary>
    public int NarrativeWordTargetMax { get; set; }
}