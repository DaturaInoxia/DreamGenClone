# Contract: Actor Profile

**Branch**: `001-rp-prompt-redesign`

Defines the 5 actor profiles and their resolution rules. Each profile determines content filtering across all 17 slots.

---

## Profile Kinds

```csharp
public enum ActorProfileKind { Player, NpcPresent, NpcNonPresent, Narrative, Custom }
```

---

## Resolution Rules

`ActorProfileResolver.Resolve` takes `(ContinueAsActor actor, string? customActorName, PromptIntent intent, RolePlaySession session, IReadOnlyList<ScenarioCharacter> roster)` and returns `ActorProfile`.

| `intent` | `actor` | Condition | Resolved `Kind` |
|----------|---------|-----------|-----------------|
| `Narrative` | (any) | — | `Narrative` |
| `Message` or `Instruction` | `You` | — | `Player` |
| `Message` or `Instruction` | `Npc` | actor in current scene location | `NpcPresent` |
| `Message` or `Instruction` | `Npc` | actor NOT in current scene location | `NpcNonPresent` |
| `Message` or `Instruction` | `Custom` | — | `Custom` |

**Scene presence** is determined by `RolePlayScenePresenceHelper` using `session.AdaptiveState.CurrentSceneLocation` and the character location truth state.

**Fail-fast**: If `actor == Npc` and the actor name is not found in the session's character roster, throw `InvalidOperationException` with diagnostic (session ID, actor name). Do NOT silently default to a different actor (Edge Case: Actor profile mismatch).

---

## Profile Record

```csharp
public sealed record ActorProfile
{
    public required ActorProfileKind Kind { get; init; }
    public required string ActorName { get; init; }
    public required string ActorRole { get; init; }
    public required IReadOnlyList<string> PresentCharacterIds { get; init; }
    public required IReadOnlyList<string> AllCharacterIds { get; init; }
}
```

---

## Content Filtering Per Profile

| Slot | Player | NpcPresent | NpcNonPresent | Narrative | Custom |
|------|--------|------------|---------------|-----------|--------|
| 2 (Actor Assignment) | "Continue as: {name} ({role})" | "Continue as: {name} ({role})" | "Continue as: {name} ({role})" | "Write as omniscient narrator" | "Continue as: {name}" |
| 5 (Character Data) | Full self + full partners + comparison for non-present | Full self + full player (if present) + comparison for non-present | Full self + comparison-only for present chars | All chars, lighter format, no persona, no intimate self-awareness | Full self + full partners |
| 9 (History) | All recent interactions | Recent interactions with player emphasized | Recent interactions involving self | All interactions (needs to see what it synthesizes) | All recent interactions |
| 13 (Behavioral Frames) | Self + partners | Self + player + other present | Self only | All frames | Self + partners |
| 14 (Scenario Guidance) | All directives | All except resistance band | Pacing + intensity only | All except POV-specific; includes physical detail checklist + zero-dialogue | All |
| 17 (Final Instruction) | 1st person, 100-300 words | 1st person (character voice) | 1st person | 3rd person omniscient, 300-500 words, zero-dialogue, physical detail checklist | Per configuration |

---

## Narrative Variant Specifics (FR-026)

For `Kind == Narrative`:
- **No POV persona** — suppress all `POV Persona: [character]` injection (S-025).
- **Lighter character data** — description + key attributes only, no intimate self-awareness text.
- **All behavioral frames** — Narrative must portray all characters accurately.
- **Physical detail checklist** in final instruction: positions, contact, sensations, sounds, rhythm.
- **Zero-dialogue hard constraint** in final instruction.
- **All interactions in history** — Narrative needs to see what it is synthesizing.

---

## NPC Agency (Clarification 2026-07-17)

NPC Agency is NOT a separate slot or directive. Agency qualities (initiative, internal life, desires) belong inside each character's behavioral frame content in Slot 13. The slot architecture does not introduce an "NPC Agency" slot.
