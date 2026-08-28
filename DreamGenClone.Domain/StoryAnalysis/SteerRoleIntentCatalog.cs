using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Domain.StoryAnalysis;

/// <summary>
/// Per-role steering intent text for option generation and prompt injection (B-075 D1b).
/// Each entry maps a (role, direction) pair to a prose description of what that direction
/// means for that role's narrative job. Used by BuildSteerOptionsPromptAsync and
/// StagedDirectionsSlot.WriteAsync.
/// </summary>
public sealed record SteerRoleIntent(string Role, string Direction, string IntentText);

/// <summary>
/// Code-defined catalog of per-role steering intents (3 roles × 4 directions = 12 entries).
/// Single source of truth for role-aware steering guidance in both option generation and
/// prompt injection. Roles: Wife, Husband, OtherMan.
/// </summary>
public static class SteerRoleIntentCatalog
{
    private static readonly IReadOnlyList<SteerRoleIntent> AllEntries =
    [
        // ── Wife ─────────────────────────────────────────────────────────────────
        new("Wife", "Away",
            "She should reinforce her boundaries: pull back, resist advances, let guilt or loyalty override temptation. Focus on her commitment to her husband and the cost of crossing the line."),
        new("Wife", "Neutral",
            "She stays in her current headspace without committing either way — neither retreating nor advancing. The tension is unresolved and she is letting the moment sit."),
        new("Wife", "Towards",
            "She should lower her guard and engage more openly: consider crossing a line, respond to seduction or emotional pull, let desire override restraint by degrees."),
        new("Wife", "Hard",
            "She should actively pursue the encounter: initiate escalation, set aside restraint and guilt, take control of the dynamic, and push past any remaining hesitation."),

        // ── Husband ──────────────────────────────────────────────────────────────
        new("Husband", "Away",
            "He should block or interfere with the dynamic: confront, withdraw in a way that shuts things down, assert his presence as an obstacle, or voice suspicion that halts the momentum."),
        new("Husband", "Neutral",
            "He remains in his current posture — neither facilitating nor obstructing. His presence may be felt but he takes no action that changes the trajectory."),
        new("Husband", "Towards",
            "He should create space for the Wife-OtherMan dynamic: step aside, announce he is leaving or retiring, encourage the connection knowingly or unknowingly, or remove an obstacle that was preventing escalation."),
        new("Husband", "Hard",
            "He should actively enable the encounter: push them together directly, voice approval or encouragement, remove every obstacle, or participate in orchestrating the situation."),

        // ── OtherMan ─────────────────────────────────────────────────────────────
        new("OtherMan", "Away",
            "He should back off: break eye contact, cool the pursuit, redirect attention elsewhere, give her space. He reads the moment as not right and steps back."),
        new("OtherMan", "Neutral",
            "He maintains his presence without pressing — stays in her orbit, attentive but not advancing. Waits for a signal before making his next move."),
        new("OtherMan", "Towards",
            "He should actively seduce her using genre-grounded seduction behaviors — calibrate his compliments and conversation to make her feel uniquely seen (Charmer), display physical competence and capability (Competent), build emotional intimacy through attentive listening (Confidante), create playful tension and teasing (Tease), leverage protection and rescue (Protector), project confident physical presence and certainty (Dominant), use mystery and controlled revelation (Mysterious), or exploit proximity and circumstance (Situational). Read which approach works for HER and pursue it with singular focus — the seduction should feel earned and natural, not mechanical."),
        new("OtherMan", "Hard",
            "He should push aggressively: close distance decisively, make his intent unambiguous, bypass subtlety entirely. Physical escalation with direct, unmistakable purpose."),
    ];

    private static readonly IReadOnlyDictionary<(string Role, string Direction), string> Lookup = AllEntries
        .ToDictionary(e => (e.Role, e.Direction), e => e.IntentText);

    /// <summary>
    /// Returns the role-steering intent text for the given role and direction.
    /// Falls back to a generic description if no catalog entry is found for the role.
    /// </summary>
    public static string GetIntent(string? role, SteerDirection direction)
    {
        var dirLabel = direction.ToString();

        if (!string.IsNullOrWhiteSpace(role)
            && Lookup.TryGetValue((role.Trim(), dirLabel), out var intent))
        {
            return intent;
        }

        // Fallback: generic direction-only intent for unknown roles (e.g. persona, custom character).
        return direction switch
        {
            SteerDirection.Away => "Steer this character away from the current trajectory — resist, pull back, refuse escalation.",
            SteerDirection.Neutral => "Steer this character to hold the current state — neither escalate nor retreat.",
            SteerDirection.Towards => "Steer this character toward the active theme's direction — align, engage, move forward.",
            SteerDirection.Hard => "Steer this character to push extreme — jump fully into the escalation with active initiative.",
            _ => "Steer this character in a scene-appropriate direction."
        };
    }

    /// <summary>
    /// Returns the role-overview text for option-generation context (the character's narrative job).
    /// </summary>
    public static string GetRoleContext(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return "No specific narrative role — steer based on recent scene context and character disposition.";

        return role.Trim() switch
        {
            "Wife" => "Her narrative job: decide whether to cheat, how far to go, and how she feels about it afterward. Core conflict: commitment to her husband versus desire for exciting, satisfying encounters. Every direction choice is about crossing or defending this line.",
            "Husband" => "His narrative job: enable or block the encounter between Wife and OtherMan, knowingly or unknowingly. Core conflict: his presence, choices, and emotional state either create opportunity or close it. He may be aware or oblivious, turned on or threatened, interfering on purpose or inadvertently.",
            "OtherMan" => "His narrative job: pursue the Wife with singular focus, adapting his seduction approach to what works for her. He employs genre-grounded seduction behaviors — verbal charm, physical competence, emotional connection, playful tension, protection, confident presence, mystery, or situational opportunism — whichever blend her state and the moment call for. Core conflict: find the method that works right now and commit to it.",
            _ => "No specific narrative role — steer based on recent scene context and character disposition."
        };
    }
}
