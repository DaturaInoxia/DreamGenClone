namespace DreamGenClone.Domain.StoryAnalysis;

/// <summary>
/// A single behavioral dimension definition for one character role,
/// with 4 tier descriptions used to generate LLM directive text.
/// Tier thresholds: value ≤20 → Tier1, ≤50 → Tier2, ≤75 → Tier3, >75 → Tier4.
/// </summary>
public sealed record BehavioralDimension(
    string Name,
    string TargetRole,
    string Tier1Text,
    string Tier2Text,
    string Tier3Text,
    string Tier4Text);

/// <summary>
/// Code-defined catalog of all encounter behavioral dimensions and their tier descriptions.
/// Single source of truth for both the UI live preview and the prompt behavioral frame generator.
/// Adding or changing tier text requires only editing this class.
/// </summary>
public static class BehavioralDimensionCatalog
{
    private static readonly IReadOnlyList<BehavioralDimension> AllDimensions =
    [
        // ── Husband ──────────────────────────────────────────────────────────────────────────
        new("Awareness", "Husband",
            "He is completely unaware that anything unusual is happening.",
            "He has vague suspicions but has not connected them; he acts normally.",
            "He suspects or knows something is occurring but chooses not to confront it.",
            "He is fully aware of the encounter and is present with that knowledge."),

        new("Acceptance", "Husband",
            "Any discovery would result in immediate angry confrontation.",
            "He is uncomfortable but would not act decisively if confronted.",
            "He has reluctantly come to terms with it and would not interfere.",
            "He is fully at ease; the situation causes him no distress at all."),

        new("Voyeurism", "Husband",
            "He has no desire to observe; he actively avoids any awareness of it.",
            "He is aware it might be happening but keeps deliberate distance.",
            "He has positioned himself where he might be able to observe if it happens.",
            "He is actively and deliberately watching; he will not interrupt for any reason."),

        new("Participation", "Husband",
            "He will not participate in any form; he would leave or refuse if asked.",
            "He might allow minor indirect involvement if presented carefully.",
            "He participates in a supporting or enabling role when invited.",
            "He is a co-primary participant; he initiates and engages directly."),

        new("Encouragement", "Husband",
            "He shows no sign of approval; no words, gestures, or facilitation.",
            "He is passively complicit — he doesn't stop it but offers nothing.",
            "He quietly approves and may signal approval through small gestures or words.",
            "He openly encourages, facilitates, and verbally praises what is happening."),

        new("RiskTolerance", "Husband",
            "He would shut the encounter down at any sign of exposure risk to others.",
            "He is nervous about risk but would not act unless risk became direct.",
            "He accepts moderate risk; he would manage it rather than stop the encounter.",
            "He is comfortable with significant exposure risk and does not let it interfere."),

        // ── Wife ─────────────────────────────────────────────────────────────────────────────
        new("DiscoveryCaution", "Wife",
            "She makes no effort to conceal this encounter — she may be loud, unconcerned about being heard, and takes no precautions.",
            "She is mildly cautious but is not actively managing discovery risk.",
            "She is careful — she keeps noise down, is aware of time, and would quickly adjust if risk increased.",
            "She is highly vigilant — managing every sensory detail, checking for sounds, and would stop immediately at any sign of detection."),

        new("Exhibitionism", "Wife",
            "She is deeply private — she would be distressed if seen or heard; she minimizes every sign of the encounter.",
            "She doesn't seek visibility but doesn't go out of her way to hide it either.",
            "She is comfortable being seen and heard by appropriate parties; visibility adds to the experience.",
            "She actively enjoys being seen and heard during the encounter — visibility is part of what she wants."),

        new("EmotionalEngagement", "Wife",
            "This is purely transactional — she feels no emotional connection to the other man; it is physical only.",
            "She finds him pleasant but maintains clear emotional detachment.",
            "She has developed some emotional warmth toward him; it shows in how she treats him.",
            "She is developing genuine feelings for him; the emotional component is real and present."),

        new("PostEncounterGuilt", "Wife",
            "She shows no guilt after the encounter — she behaves completely normally with her husband.",
            "She is slightly subdued but recovers quickly and acts normally.",
            "She is noticeably affected — she may be overly affectionate or slightly withdrawn with her husband.",
            "She is overwhelmed — visibly guilty, over-compensating, or emotionally withdrawn after the encounter."),

        // ── OtherMan ─────────────────────────────────────────────────────────────────────────
        new("HusbandAwareness", "OtherMan",
            "He doesn't know the husband exists — he treats the encounter as uncomplicated; the married context is irrelevant to him.",
            "He is vaguely aware she is married but it doesn't enter his actions.",
            "He knows about the husband and is conscious of that fact during the encounter.",
            "He is fully aware of the husband and actively uses that knowledge in his approach and words."),

        new("MarriageContextUse", "OtherMan",
            "He never references the marriage, the husband, or the fact that she is someone's wife.",
            "He may make a passing reference if she brings it up but does not pursue it.",
            "He brings up the married context occasionally as a source of intensity or intimacy.",
            "He actively exploits the married context — he references her husband, her vows, and the forbidden nature as core parts of the encounter."),

        new("DiscoveryRisk", "OtherMan",
            "He shows no concern about being discovered — he is reckless about noise, timing, and evidence.",
            "He is mildly aware of risk but makes no deliberate effort to manage it.",
            "He is careful — he manages obvious risks and would adjust behavior if a threat appeared.",
            "He is highly careful — he actively manages every risk of discovery throughout the encounter."),

        new("PersistencePastLimits", "OtherMan",
            "He respects every stated or implied limit immediately without hesitation.",
            "He may gently probe once but backs off cleanly when met with resistance.",
            "He persists past initial resistance but stops when limits are stated clearly a second time.",
            "He persistently pushes past resistance and stated limits; he treats reluctance as something to overcome rather than a boundary."),
    ];

    // Normalizes role variants so lookups work regardless of legacy spelling.
    // "The Other Man", "Other Man" all map to the catalog key "OtherMan".
    private static string NormalizeRole(string targetRole)
    {
        var t = targetRole.Trim();
        if (t.Equals("OtherMan", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Other Man", StringComparison.OrdinalIgnoreCase)
            || t.Equals("The Other Man", StringComparison.OrdinalIgnoreCase))
        {
            return "OtherMan";
        }
        return targetRole;
    }

    /// <summary>
    /// Returns all behavioral dimensions defined for the given role.
    /// Returns an empty list for "Any" or unrecognized roles.
    /// Accepts "OtherMan", "Other Man", and "The Other Man" as equivalent.
    /// </summary>
    public static IReadOnlyList<BehavioralDimension> GetDimensions(string targetRole) =>
        AllDimensions.Where(d => d.TargetRole.Equals(NormalizeRole(targetRole), StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Returns the single dimension matching the given role and name, or null if not found.
    /// Accepts "OtherMan", "Other Man", and "The Other Man" as equivalent.
    /// </summary>
    public static BehavioralDimension? FindDimension(string targetRole, string name) =>
        AllDimensions.FirstOrDefault(d =>
            d.TargetRole.Equals(NormalizeRole(targetRole), StringComparison.OrdinalIgnoreCase) &&
            d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves the tier text for the given role, dimension name, and value.
    /// Returns an empty string if the dimension is not found.
    /// Tier thresholds: value ≤20 → Tier1, ≤50 → Tier2, ≤75 → Tier3, >75 → Tier4.
    /// </summary>
    public static string ResolveTierText(string targetRole, string name, int value)
    {
        var dim = FindDimension(targetRole, name);
        if (dim is null) return string.Empty;
        return value switch
        {
            <= 20 => dim.Tier1Text,
            <= 50 => dim.Tier2Text,
            <= 75 => dim.Tier3Text,
            _     => dim.Tier4Text,
        };
    }
}
