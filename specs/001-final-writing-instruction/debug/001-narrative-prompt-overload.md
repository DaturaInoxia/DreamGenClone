# Debug Record 001: Narrative Prompt Overload

**Created:** 2026-07-29
**Related spec:** `specs/001-final-writing-instruction/spec.md` (US1/US2)

---

## Report

**Symptoms:** The Narrative variant prompt contains unnecessary sections that confuse or distract from its core purpose. The narrative prompt should primarily consist of "Write as omniscient narrator — synthesize all character perspectives" (from `ActorAssignmentSlot`), but it currently includes:

1. **SystemPrimerSlot (Slot 0)** — Emits character-focused primer text including "Write in the assigned character's voice" and "User Direction — your immediate task for this response." These labels are meaningless for an omniscient narrator.
2. **InteractionHistorySlot (Slot 9)** — Emits all recent interactions across multiple turns. The narrative variant only needs current-turn interactions for context.
3. **UserDirectionSlot (Slot 16)** — Emits "User Direction:" followed by user prompt text like "Describe what happens after Ken's action. Include scene details, other characters' reactions, internal thoughts, and sensory details." This adds noise for the narrative variant.

**No specific session ID** — this is a general structural issue affecting all Narrative-variant prompts.

---

## Analysis

### Code paths traced

**File:** `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/SystemPrimerSlot.cs`
- `ShouldWrite()` always returns `true` — no variant awareness
- `WriteAsync()` emits fixed text with character-centric language and "User Direction" priority section
- The text "User Direction — your immediate task for this response. This is what you must do right now." appears unconditionally

**File:** `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/InteractionHistorySlot.cs`
- `ShouldWrite()` checks only for `RecentInteractions.Count > 0` — no variant awareness
- `WriteAsync()` renders all recent interactions grouped by turn
- No filtering logic for Narrative variant

**File:** `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/UserDirectionSlot.cs`
- `ShouldWrite()` checks only for non-generic prompt text — no variant awareness
- `WriteAsync()` always emits "User Direction:" header + prompt text

**File:** `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/ActorAssignmentSlot.cs` (reference)
- Already variant-aware — emits "Write as omniscient narrator — synthesize all character perspectives." for Narrative variant (this is the desired core content)

**File:** `DreamGenClone.Web/Application/RolePlay/Prompts/RolePlayPromptBuilder.cs`
- Builds all slots regardless of variant — each slot decides its own `ShouldWrite`
- No variant-level filtering

### Spec artifacts consulted

- `specs/001-final-writing-instruction/spec.md` — US1 (writer-standard terminology), US2 (single authoritative writing instruction)
- `specs/001-final-writing-instruction/contracts/slot-17-output-contract.md`
- `specs/001-rp-prompt-redesign/spec.md` — 17-slot architecture (frozen)

---

## Plan

### Change 1: Suppress SystemPrimerSlot for Narrative variant
**File:** `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/SystemPrimerSlot.cs`

Modify `ShouldWrite` to return `false` when `context.Variant == PromptVariant.Narrative`:
- The primer text is character-centric ("Write in the assigned character's voice... Never break character...")
- The "User Direction" priority section is confusing for narrative (there is no character-level user direction)
- The Narrative variant's section priority is simpler: the ActorAssignment core instruction + FinalInstruction constraints

### Change 2: Limit InteractionHistorySlot to current turn for Narrative variant
**File:** `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/InteractionHistorySlot.cs`

Modify the interaction grouping logic to only render the current (last) turn's interactions when `context.Variant == PromptVariant.Narrative`:
- Narrative synthesis only needs to know what happened in the current turn
- Prior turns are already summarized by the narrative close of each previous turn
- Reduces token waste and focuses the model on the current narrative task

### Change 3: Suppress UserDirectionSlot for Narrative variant
**File:** `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/UserDirectionSlot.cs`

Modify `ShouldWrite` to return `false` when `context.Variant == PromptVariant.Narrative`:
- User direction text like "Describe what happens after Ken's action..." directs character action, not narrative synthesis
- The Narrative variant's operational directives come from `FinalInstructionSlot` (Slot 17)
- User direction is meant for character actors, not the omniscient narrator

### Blast radius
- Changes are confined to 3 slot files in `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/`
- No data model changes, no DB changes, no new dependencies
- No impact on Character variant (all changes are gated on `PromptVariant.Narrative`)
- Contract tests in `SlotContractTests.cs` may need updates for Narrative-variant expected output
- Existing Character-variant prompts are completely unaffected

---

## Resolution

**Implemented 2026-07-29** — 3 files changed:

1. **`SystemPrimerSlot.cs`** — `ShouldWrite` returns `false` for `PromptVariant.Narrative`. The character-centric primer (character voice, User Direction priority, section labels) is irrelevant for omniscient narration. Build: 1 line changed.

2. **`InteractionHistorySlot.cs`** — For `PromptVariant.Narrative`, only the last turn group is rendered (using collection expression `[turnGroups[^1]]` when variant is Narrative and turns exist). Prior turns are already captured by each previous turn's narrative close. Build: 1 ternary branch added.

3. **`UserDirectionSlot.cs`** — `ShouldWrite` returns `false` for `PromptVariant.Narrative`. User direction text ("Describe what happens after Ken's action...") is character-focused. The Narrative variant's operational directives come from `FinalInstructionSlot` (Slot 17). Build: early-return guard added before existing checks.

**Verified:**
- Build: `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore` → succeeded (0 warnings from changed files)
- Slot contract tests: All 7 failures are pre-existing (unrelated slots: SceneAnchor, ThemeContract, FinalInstruction, CurrentLocation, SceneContinuityAnchor — documented in pre-existing-test-failures.md)

---

## Validated

- [ ] Pending — awaiting user confirmation (code changes implemented, needs runtime validation with a fresh Narrative-variant RP session)
