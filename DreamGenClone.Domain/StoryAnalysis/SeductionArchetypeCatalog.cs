namespace DreamGenClone.Domain.StoryAnalysis;

/// <summary>
/// A named behavioral mode for OtherMan seduction, grounded in erotic fiction genre analysis.
/// Each entry has an identifier, display name, and a prose description of the behavioral pattern.
/// </summary>
public sealed record SeductionArchetype(string Id, string DisplayName, string Description);

/// <summary>
/// Code-defined catalog of 8 seduction archetypes for the OtherMan role.
/// Single source of truth for archetype definitions and prose guidance.
/// Analogous to <see cref="SteerRoleIntentCatalog"/>.
/// </summary>
public static class SeductionArchetypeCatalog
{
    private static readonly IReadOnlyList<SeductionArchetype> _all =
    [
        new("Charmer", "The Charmer / Smooth Talker",
            "Use calibrated compliments, witty banter, and verbal seduction — make her feel uniquely seen and desired. Create intimacy through words before any physical move."),
        new("Competent", "The Competent / Capable Man",
            "Demonstrate physical competence and reliability — fix broken things, perform manual labor, display strength and skill. Let her watch, aroused by competence and physique. Create debt through acts of service."),
        new("Confidante", "The Confidante / Emotional Connection",
            "Build emotional intimacy through attentive listening and understanding her frustrations. Be the shoulder to cry on. Create the 'he actually understands me' realization."),
        new("Tease", "The Tease / Playful Provocateur",
            "Use humor and playfulness to create sexual tension — teasing, banter, and 'accidental' contact. A hand brushing hers, standing too close, a lingering look. Make her laugh, then make her want."),
        new("Protector", "The Protector / Rescuer",
            "Create or leverage damsel-in-distress scenarios — save her from danger, difficulty, or vulnerability. Trigger the gratitude-attraction pathway. Position yourself as the safe harbor in chaos."),
        new("Dominant", "The Dominant / Assertive",
            "Use direct physical presence and confident body language. Know what you want and make it her. Create polarity through certainty — overwhelming attraction as a force she cannot resist."),
        new("Mysterious", "The Mysterious / Dangerous Stranger",
            "Use intrigue through mystery and unpredictability — draw her in by making her want to figure you out. Reveal yourself in controlled doses. Let danger be part of the attraction."),
        new("Situational", "The Situational / Opportunist",
            "Exploit circumstance — being stuck together, heightened emotional states, proximity as catalyst. Let the situation do the work; be present and willing when the moment opens."),
    ];

    /// <summary>All 8 archetype definitions. Read-only, fixed at compile time.</summary>
    public static IReadOnlyList<SeductionArchetype> All => _all;

    /// <summary>
    /// Looks up an archetype by Id (case-insensitive).
    /// Returns null if the id is not recognized.
    /// </summary>
    public static SeductionArchetype? Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _all.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds combined prose guidance for the given archetype IDs.
    /// Format: "{DisplayName}: {Description}" per archetype, joined with " " (space).
    /// Returns null if archetypeIds is null or empty.
    /// Silently skips unrecognized IDs.
    /// </summary>
    public static string? BuildGuidance(IReadOnlyList<string>? archetypeIds)
    {
        if (archetypeIds is null || archetypeIds.Count == 0) return null;

        var parts = new List<string>();
        foreach (var id in archetypeIds)
        {
            var archetype = Get(id);
            if (archetype is not null)
            {
                parts.Add($"{archetype.DisplayName}: {archetype.Description}");
            }
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>
    /// Canonical semantic event id for an archetype: <c>otherman-&lt;id&gt;</c> (lowercase).
    /// Returns null for unknown archetype ids.
    /// </summary>
    public static string? ToEventId(string? archetypeId)
    {
        var archetype = Get(archetypeId);
        return archetype is null ? null : $"otherman-{archetype.Id.ToLowerInvariant()}";
    }

    /// <summary>
    /// True when the event id is one of the canonical OtherMan seduction-trope events
    /// (e.g. <c>otherman-charmer</c>).
    /// </summary>
    public static bool IsOtherManSeductionEvent(string? eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return false;
        return _all.Any(a => string.Equals(
            eventId, $"otherman-{a.Id.ToLowerInvariant()}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds the semantic event description dictionary for the OtherMan seduction archetype
    /// tropes, keyed by canonical event id. Used by the semantic-inference job so the LLM can
    /// detect when the OtherMan performs a specific trope in the narrative and knows that the
    /// event targets the Wife (her willingness to cheat rising in response).
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildSemanticEventDescriptions()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var archetype in _all)
        {
            result[ToEventId(archetype.Id)!] =
                $"The OtherMan performs the '{archetype.DisplayName}' seduction trope in the narrative. {archetype.Description} " +
                "Targets the Wife: this event fires on the OtherMan's turn and applies to the Wife (set targetCharacterName to the Wife) — it represents her willingness to cheat rising in response to this seduction behavior.";
        }
        return result;
    }
}
