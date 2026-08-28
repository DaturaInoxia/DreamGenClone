# Contract: Prompt Slot (`IPromptSlot`)

**Branch**: `001-rp-prompt-redesign` | **Frozen per spec contract**

This contract defines the interface every prompt slot implements and the frozen 17-slot registry. Implementation MUST NOT add, remove, reorder, or re-zone slots without a spec amendment.

---

## Interface

```csharp
namespace DreamGenClone.Web.Application.RolePlay.Prompts;

public interface IPromptSlot
{
    /// <summary>Unique slot identifier (matches PromptSlotId enum).</summary>
    PromptSlotId Id { get; }

    /// <summary>Attention zone: A (Primacy), B (Context), or C (Recency).</summary>
    PromptZone Zone { get; }

    /// <summary>Order within zone (1-based). Builder sorts by Zone then Order.</summary>
    int Order { get; }

    /// <summary>True if this slot's text can be trimmed when over budget. FR-029.</summary>
    bool IsTrimEligible { get; }

    /// <summary>
    /// Pure predicate. Returns true if this slot should emit text for the given context.
    /// Idempotent for identical context, no side effects.
    /// </summary>
    bool ShouldWrite(PromptBuildContext context);

    /// <summary>
    /// Produces the slot's text. MUST NOT throw for a context where ShouldWrite returned true.
    /// Result MUST NOT contain leading/trailing newlines — builder handles spacing.
    /// Exceptions propagate per fail-fast contract.
    /// </summary>
    Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct);

    /// <summary>
    /// Trims the slot's text to fit the remaining budget. MUST be idempotent.
    /// MUST NOT produce empty output from non-empty input.
    /// </summary>
    string Trim(string text, int maxChars);
}
```

---

## Frozen 17-Slot Registry

### Zone A — Primacy (never trimmed)

| Order | Slot ID | FR | Trim Eligible | Conditional |
|-------|---------|----|---------------|-------------|
| 1 | `SceneAnchor` | FR-005 | No | No |
| 2 | `ActorAssignment` | FR-006 | No | No |
| 3 | `TurnContext` | FR-007 | No | No |
| 4 | `SceneLocationLock` | FR-008 | No | No |
| 4a | `WorldState` | FR-009 | No | Yes — fires only when B-062 data available; silently omitted otherwise. NOT counted in the 17. |

### Zone B — Context (trimmable per FR-029 priority)

| Order | Slot ID | FR | Trim Eligible | Notes |
|-------|---------|----|---------------|-------|
| 5 | `CharacterData` | FR-010, FR-011 | Yes (priority 2) | Non-present char data trimmed before present |
| 6 | `ScenarioContext` | FR-012 | Yes (priority 3) | Progressive compression after threshold |
| 7 | `CurrentLocation` | FR-013 | Yes (low) | Current scene full; occupied one-line; others omitted |
| 8 | `WritingStyle` | FR-014 | Yes (last resort) | Timeless desc/example always kept; phase RoT trimmed only under extreme pressure |
| 9 | `InteractionHistory` | FR-015 | Yes (priority 1) | Oldest trimmed first; tiered compression |
| 10 | `SessionMemory` | FR-016 | Yes (priority 4) | Three tiers |
| 11 | `SceneContinuityAnchor` | FR-017 | Yes (low) | Cross-perceptions only |

### Zone C — Recency (never trimmed except Slot 13 non-present frames)

| Order | Slot ID | FR | Trim Eligible | Notes |
|-------|---------|----|---------------|-------|
| 12 | `ThemeContract` | FR-018 | No | Appears exactly once |
| 13 | `BehavioralFrames` | FR-019 | Yes (non-present frames only) | Appears exactly once; filtered by actor |
| 14 | `ScenarioGuidance` | FR-020 | Yes (low) | Resistance band suppressed when threshold crossed |
| 15 | `IntensityPacing` | FR-021 | No | Merged escalation + scene-time-direction |
| 16 | `UserDirection` | FR-022 | No (when present) | Only fires when user provided real direction |
| 17 | `FinalInstruction` | FR-023 | No | Last content before generation |

---

## Trim Priority Order (FR-029)

When the prompt exceeds `MaxPromptChars`, trim in this order:

1. **Slot 9** (InteractionHistory) — oldest turns first, Layer 1 → Layer 2 → Layer 3
2. **Slot 5** (CharacterData) — non-present character data first
3. **Slot 6** (ScenarioContext) — compress to summary
4. **Slot 10** (SessionMemory) — most recent encounters kept
5. **Slot 7** (CurrentLocation) — drop occupied-location summaries
6. **Slot 11** (SceneContinuityAnchor) — drop cross-perceptions
7. **Slot 8** (WritingStyle) — trim phase Rule-of-Thumb (last resort)

**Never trimmed**: Slots 1-4, 4a, 12, 15, 16 (when present), 17. Slot 13 trims only non-present frames, never the actor's own frame.

---

## Startup Validation

`RolePlayPromptBuilder` constructor asserts:
- Exactly 17 distinct mandatory slots registered (WorldState is conditional, not counted)
- Each slot's `Zone`/`Order` matches the frozen registry above
- No duplicate `Id` values

Fail fast with explicit diagnostic on mismatch.
