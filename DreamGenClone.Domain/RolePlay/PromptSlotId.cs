namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Frozen 17-slot architecture identifiers. Zone/order mapping is normative per spec contract.
/// WorldState (5) is a conditional sub-slot — not counted in the 17 mandatory slots.
/// </summary>
public enum PromptSlotId
{
    /// <summary>Zone A, order 0 — prompt primer explaining sections and priority.</summary>
    SystemPrimer = 0,

    /// <summary>Zone A, order 1 — location + phase one-liner (FR-005).</summary>
    SceneAnchor = 1,

    /// <summary>Zone A, order 2 — "Continue as: {name} ({role})" (FR-006).</summary>
    ActorAssignment = 2,

    /// <summary>Zone A, order 3 — turn number, response position (FR-007).</summary>
    TurnContext = 3,

    /// <summary>Zone A, order 4 — current location hard constraint (FR-008).</summary>
    SceneLocationLock = 4,

    /// <summary>Zone A, order 4a — conditional world state (FR-009, B-062).</summary>
    WorldState = 5,

    /// <summary>Zone B, order 5 — actor-aware character data (FR-010, FR-011).</summary>
    CharacterData = 6,

    /// <summary>Zone B, order 6 — progressive scenario compression (FR-012).</summary>
    ScenarioContext = 7,

    /// <summary>Zone B, order 7 — current location details (FR-013).</summary>
    CurrentLocation = 8,

    /// <summary>Zone C, order 18 — style guide moved to end of prompt (recency position).</summary>
    WritingStyle = 9,

    /// <summary>Zone B, order 9 — tiered interaction history (FR-015).</summary>
    InteractionHistory = 10,

    /// <summary>Zone B, order 10 — 3-tier session memory (FR-016).</summary>
    SessionMemory = 11,

    /// <summary>Zone B, order 11 — cross-perception continuity anchor (FR-017).</summary>
    SceneContinuityAnchor = 12,

    /// <summary>Zone C, order 12 — theme contract, appears exactly once (FR-018).</summary>
    ThemeContract = 13,

    /// <summary>Zone C, order 13 — actor-filtered behavioral frames (FR-019).</summary>
    BehavioralFrames = 14,

    /// <summary>Zone C, order 14 — scenario guidance directives (FR-020).</summary>
    ScenarioGuidance = 15,

    /// <summary>Zone C, order 15 — intensity pacing + escalation (FR-021).</summary>
    IntensityPacing = 16,

    /// <summary>Zone C, order 16 — conditional user direction (FR-022).</summary>
    UserDirection = 17,

    /// <summary>Zone C, order 17 — final instruction before generation (FR-023).</summary>
    FinalInstruction = 18,

    /// <summary>Zone C, order 8 — pinned interactions injected at deterministic position (FR-024).</summary>
    PinnedContext = 19,

    /// <summary>Zone C, order 9 — transient batch scene directions queue, one-shot on next continuation (FR-025). Renders after PinnedContext (8) so persistent constraints precede the one-shot staged plan.</summary>
    StagedDirections = 20
}
