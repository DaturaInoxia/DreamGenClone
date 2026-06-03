namespace DreamGenClone.Domain.StoryAnalysis;

/// <summary>
/// A single stat band text definition for one stat × role combination.
/// Band thresholds: value ≤ 20 → Band1, ≤ 50 → Band2, ≤ 75 → Band3, > 75 → Band4.
/// </summary>
public sealed record CharacterStatBand(
    string StatName,
    string TargetRole,
    string Band1Text,
    string Band2Text,
    string Band3Text,
    string Band4Text);

/// <summary>
/// Code-defined catalog of all stat × role text entries (15 entries: 5 stats × 3 roles).
/// Single source of truth for stat state text injection into continuation prompts.
/// </summary>
public static class CharacterStatTextCatalog
{
    private static readonly IReadOnlyList<CharacterStatBand> AllEntries =
    [
        // ── Desire ──────────────────────────────────────────────────────────────────────────
        new("Desire", "Wife",
            "she is largely indifferent to physical intimacy; arousal requires sustained effort and explicit encouragement",
            "she has mild interest and responds to gentle encouragement but is not seeking intensity on her own",
            "she is noticeably engaged and responds eagerly; she welcomes escalation and shows clear arousal signals",
            "she craves physical intensity with urgency; she will initiate, escalate, and pursue without hesitation"),

        new("Desire", "Husband",
            "he has little interest in physical intensity; he is unlikely to initiate or seek escalation",
            "he has moderate interest and will engage when invited but does not drive escalation",
            "he is actively interested and engaged; he participates readily and may gently push for more",
            "he is intensely driven; he initiates strongly, presses for escalation, and sustains high energy throughout"),

        new("Desire", "OtherMan",
            "he shows minimal urgency or drive; his approach is casual and low-energy",
            "he is present and interested but not pressing; he responds to invitation without pushing",
            "he is focused and persistent; he pursues with clear intent and does not let momentum drop",
            "he is single-minded in pursuit; he applies steady, forceful pressure and does not accept easy deflection"),

        // ── Restraint ────────────────────────────────────────────────────────────────────────
        new("Restraint", "Wife",
            "she has almost no capacity to hold back; inhibition is functionally absent; she acts on impulse without internal resistance",
            "she can delay or moderate her responses with effort, but her resistance is fragile and gives way under sustained pressure",
            "she holds herself in check firmly; she requires significant pressure or trust before lowering her guard",
            "she is rigidly self-contained; her inhibition is strong and resistant to erosion under any normal pressure"),

        new("Restraint", "Husband",
            "he exercises almost no self-restraint; he reacts immediately to impulses and does not moderate his responses",
            "he applies moderate self-restraint but it bends under pressure; he can be pushed past his usual boundaries",
            "he exercises clear, sustained restraint; he does not let himself be pushed easily",
            "he is tightly controlled and does not break discipline; he exits or deflects rather than lowering his guard"),

        new("Restraint", "OtherMan",
            "he has no impulse control in this context; he says and does what comes to mind without filtering",
            "he maintains loose self-control but it yields under sustained or clever pressure",
            "he maintains deliberate self-control; he does not allow himself to be rushed or manipulated into acting",
            "he is disciplined and careful; he will not take risks or act impulsively regardless of provocation"),

        // ── Dominance ────────────────────────────────────────────────────────────────────────
        new("Dominance", "Wife",
            "she feels powerless and reactive; she does not direct, steer, or assert — she defers to whatever is placed before her",
            "she has a modest sense of agency but yields the lead readily; she participates without asserting direction",
            "she has clear personal agency; she expresses preferences, sets the tone, and redirects when she chooses",
            "she is fully in command of this encounter; she decides its direction, pace, and terms"),

        new("Dominance", "Husband",
            "he is passive and deferential; he follows any lead, does not assert his own preferences, and makes no effort to control outcomes",
            "he participates willingly but is not asserting direction; he can be led without resistance",
            "he is assertive about his role; he shapes the dynamic and does not simply follow",
            "he is decisive and assertive; he directs the encounter, controls its pace, and does not yield unless he chooses to"),

        new("Dominance", "OtherMan",
            "he is tentative and accommodating; he adjusts to her signals and does not assert pressure",
            "he takes a collaborative stance; he matches her energy and does not try to dominate the pace",
            "he directs the dynamic confidently; he sets pace and framing and expects compliance",
            "he is dominant and controlling; he frames the encounter on his terms and redirects any resistance"),

        // ── Loyalty ──────────────────────────────────────────────────────────────────────────
        new("Loyalty", "Wife",
            "her commitment to her marriage is effectively absent; she feels no guilt and faces no internal resistance to transgression",
            "her loyalty is present but not strong; guilt and hesitation surface occasionally but do not reliably stop her",
            "she retains meaningful loyalty; she requires sustained pressure and a compelling situation before she will act against her commitment",
            "her commitment is strong and active; she will break off or redirect any interaction moving toward transgression and will not be talked back into it"),

        new("Loyalty", "Husband",
            "his emotional investment in the relationship is minimal; he is indifferent to its preservation",
            "his commitment is present but soft; it does not exert strong pressure against the current dynamic",
            "he maintains a real sense of commitment; he does not easily dismiss the significance of the relationship",
            "he is fully committed; he is alert to anything that threatens the relationship and would confront it directly"),

        new("Loyalty", "OtherMan",
            "his awareness of her committed relationship does not constrain him; he treats her as fully available",
            "he is aware she is married and occasionally acknowledges it, but it does not significantly change his approach",
            "he acknowledges her situation and does not press her to act against it",
            "he will not pursue an encounter she is not willing to have; he reads reluctance and backs off"),

        // ── SelfRespect ───────────────────────────────────────────────────────────────────────
        new("SelfRespect", "Wife",
            "her self-valuing has eroded; she accepts degrading or compromising acts without resistance and places little value on her own dignity",
            "her self-worth is uncertain; she may accept acts that compromise her but shows some unease or reluctance",
            "she has clear self-worth and expects to be treated accordingly; she will push back on acts that demean or diminish her",
            "she has strong, unwavering self-regard; she maintains firm personal standards and will refuse anything that compromises her dignity"),

        new("SelfRespect", "Husband",
            "his self-regard is diminished; he accepts humiliation and does not defend his own worth or standing",
            "his self-esteem is inconsistent; he sometimes accepts slights or indignity without pushback",
            "he has solid self-respect; he does not accept humiliation and pushes back against diminishment",
            "he has unshakeable self-respect; he defines clear boundaries around his worth and enforces them without hesitation"),

        new("SelfRespect", "OtherMan",
            "he treats her as someone with no meaningful personal limits; he does not feel bound to preserve her dignity",
            "he shows some awareness of her worth but does not strongly prioritise protecting it",
            "he treats her with respect and does not push her toward acts she would find degrading",
            "he reads her limits; he does not attempt acts she has signaled are outside them and adjusts when she pulls back"),
    ];

    /// <summary>
    /// Returns the band text for the given stat/role/value combination.
    /// Returns null when the stat or role is not in the catalog.
    /// Band thresholds: value ≤ 20 → Band1, ≤ 50 → Band2, ≤ 75 → Band3, > 75 → Band4.
    /// </summary>
    public static string? ResolveText(string statName, string targetRole, int value)
    {
        var entry = AllEntries.FirstOrDefault(e =>
            e.StatName.Equals(statName, StringComparison.OrdinalIgnoreCase) &&
            e.TargetRole.Equals(targetRole, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return null;
        }

        return value switch
        {
            <= 20 => entry.Band1Text,
            <= 50 => entry.Band2Text,
            <= 75 => entry.Band3Text,
            _     => entry.Band4Text
        };
    }

    /// <summary>
    /// Returns true when the value is in the neutral band (35 ≤ value ≤ 65).
    /// No stat state text is injected for neutral-band stats.
    /// </summary>
    public static bool IsNeutralBand(int value) => value is >= 35 and <= 65;
}
