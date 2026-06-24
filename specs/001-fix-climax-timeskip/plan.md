# Implementation Plan: Fix Climax Time-Skip System

**Branch**: `001-fix-climax-timeskip` | **Date**: 2026-06-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-fix-climax-timeskip/spec.md`

## Summary

Three fixes to the multi-encounter Climax time-skip system: (1) replace persistent Instruction interaction injection with one-shot `PromptIntent.Instruction` on the first overflow actor, (2) remove stale encounter number from directive text, (3) skip engine injection when a user-typed Instruction is active in the recent interaction window. The `GeneratedByCommand` field already exists on `RolePlayInteraction` and is persisted — no schema migration needed.

## Technical Context

**Language/Version**: C# / .NET 9
**Primary Dependencies**: Blazor Server, Microsoft.Data.Sqlite, Serilog
**Storage**: SQLite (`DreamGenClone.Web/data/dreamgenclone.dev.db`)
**Testing**: xUnit (`DreamGenClone.Tests`)
**Target Platform**: Windows (local-first Blazor Server)
**Project Type**: Web application (layered .NET solution)
**Performance Goals**: No additional LLM calls; time-skip directive is text-only
**Constraints**: Must not regress existing encounter boundary detection; must not break existing sessions with persisted Instruction interactions
**Scale/Scope**: 2 source files modified, 1 new test file, ~50 lines changed

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [x] Module boundaries and adapter seams are explicit and swappable
- [x] .NET layered architecture uses separate projects with enforced dependency direction
- [x] Deterministic state transitions and JSON contract validation are test-covered
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes

## Project Structure

### Documentation (this feature)

```text
specs/001-fix-climax-timeskip/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── contracts/           # Phase 1 output
```

### Source Code (repository root)

```text
DreamGenClone.Web/
├── Application/
│   └── RolePlay/
│       ├── RolePlayEngineService.cs       # Time-skip injection logic (overflow loop)
│       └── RolePlayContinuationService.cs # BuildPromptAsync re-injection (no changes needed)
└── Domain/
    └── RolePlay/
        └── RolePlayInteraction.cs         # GeneratedByCommand field (already exists)

DreamGenClone.Tests/
└── RolePlay/
    └── MultiEncounterTimeSkipTests.cs     # New test file
```

**Structure Decision**: Minimal change — 2 source files modified, 1 new test file. The `GeneratedByCommand` field already exists on `RolePlayInteraction` (line 42) and is persisted in the SQLite repository. No schema migration, no new projects, no new dependencies.

## Complexity Tracking

No constitution violations. No complexity justifications needed.

---

## Phase 0: Research

### Research Task 1: Verify `GeneratedByCommand` persistence

**Decision**: `GeneratedByCommand` already exists on `RolePlayInteraction` (line 42) and is persisted by `RolePlayStateRepository`. Used by `ContinueAsync` (`"Continue"`), `ContinueNarrativeAsync` (`"Narrative"`), and `InteractionRetryService` (command name). User-typed Instructions leave it null.

**Rationale**: No schema migration needed. The field is already wired through persistence, UI display, and test doubles.

**Alternatives considered**: Adding a new `Origin` enum field — rejected because `GeneratedByCommand` already serves this purpose and is persisted.

### Research Task 2: Verify `PromptIntent.Instruction` behavior in `BuildPromptAsync`

**Decision**: When `intent == PromptIntent.Instruction`, the prompt text appears as `"Instruction:\n{promptText}"` at the end of the prompt (line 1303-1305 of `RolePlayContinuationService.cs`). The "Active Instruction (persistent)" re-injection block (lines 1272-1292) is SKIPPED when `intent == PromptIntent.Instruction` (line 1271 guard: `if (intent != PromptIntent.Instruction)`).

**Rationale**: Using `PromptIntent.Instruction` for the first actor gives the directive maximum authority via the `"Instruction:"` label AND bypasses the persistent re-injection entirely. No Instruction interaction is created in `session.Interactions`, so there's nothing for future turns to find and re-inject.

**Alternatives considered**: Using `PromptIntent.Message` — rejected because `"Message:"` has less authority than `"Instruction:"` and the user's proven working example used the Instruction label.

### Research Task 3: Verify overflow loop structure

**Decision**: The overflow loop in `ContinueAsAsync` (line ~1545 of `RolePlayEngineService.cs`) iterates `batchSize` actors. The first actor (`i == 0`) currently gets a per-position prompt. The time-skip block (line ~1492) runs BEFORE the loop and injects an Instruction interaction into `session.Interactions`.

**Rationale**: The fix removes the Instruction interaction injection entirely. Instead, when `TimeSkipPending` is true and no user Instruction is active, the first actor's `promptText` is set to the time-skip directive and `PromptIntent.Instruction` is used instead of `PromptIntent.Message`. Subsequent actors (`i > 0`) keep `PromptIntent.Message` with `"Describe this same moment from your character's perspective."`.

**Alternatives considered**: Injecting the directive into ALL actors' prompts — rejected because only the first actor needs to initiate the transition; subsequent actors describe the same moment.

### Research Task 4: Verify user Instruction detection window

**Decision**: The last 3 interactions in `session.Interactions` are checked for `ActorName == "Instruction"` AND `GeneratedByCommand` is null/empty. This window is small enough to be performant and large enough to catch user steers typed earlier in the same turn.

**Rationale**: User steers are typically the most recent interaction before a Continue. A 3-interaction window covers the user's steer plus any Narrative that may have been generated.

**Alternatives considered**: Checking only the last 1 interaction — rejected because the overflow loop may have already added a Narrative before the time-skip check runs.

---

## Phase 1: Design & Contracts

### Data Model

No new entities. The existing `RolePlayInteraction.GeneratedByCommand` field (already persisted) is used to distinguish engine-generated time-skip Instructions (`"MultiEncounterTimeSkip"`) from user-typed Instructions (null).

### Contracts

#### Time-Skip Directive Injection Contract

```text
WHEN: TimeSkipPending == true AND CurrentEncounterNumber > 1 AND CurrentPhase == Climax
CHECK: Last 3 interactions for user-typed Instruction (ActorName="Instruction" AND GeneratedByCommand is null)
IF user Instruction found:
  - Skip injection for this turn
  - Keep TimeSkipPending = true (retry next turn)
  - Log informational event "MultiEncounterTimeSkipSkippedDueToUserInstruction"
ELSE:
  - Set first actor's promptText to time-skip directive
  - Set first actor's PromptIntent to Instruction
  - Clear TimeSkipPending
  - Log informational event "MultiEncounterTimeSkipDirectiveInjected"
```

#### Directive Text Contract

```text
"Close the current encounter naturally. Then advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life."
```

No encounter number. No "before encounter #N begins" language.

### Quickstart

```bash
# Build
dotnet build DreamGenClone.sln

# Run tests
dotnet test DreamGenClone.Tests --filter "MultiEncounterTimeSkip"

# Manual test
# 1. Start web app
# 2. Load a multi-encounter Climax session
# 3. Continue until encounter boundary fires
# 4. Verify: first actor gets "Instruction:" labeled time-skip directive
# 5. Verify: subsequent turns do NOT contain the directive
# 6. Verify: typing a user steer before boundary skips engine injection
```

---

## Implementation Changes

### File 1: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

**Change A (Fix 1 + Fix 2): Replace Instruction interaction injection with first-actor promptText**

Remove the entire time-skip block (lines ~1492-1540) that creates and injects a `RolePlayInteraction` with `ActorName="Instruction"`. Replace with a flag check in the overflow loop's first-actor branch.

Before (current):
```csharp
// Time-skip block before overflow loop
if (isClimaxPhase && CurrentEncounterNumber > 1 && InteractionsInCurrentEncounter == 0)
{
    // ... load theme, build directive, create RolePlayInteraction, add to session ...
}

// Overflow loop
for (var i = 0; i < batchSize; i++)
{
    if (isClimaxPhase && i == 0)
    {
        promptText = isNewEncounterStart
            ? "Continue the scene naturally."
            : "Continue the current encounter naturally from where it left off.";
    }
}
```

After:
```csharp
// No time-skip block before overflow loop

// Overflow loop
var timeSkipActive = isClimaxPhase
    && session.AdaptiveState.CurrentEncounterNumber > 1
    && session.AdaptiveState.InteractionsInCurrentEncounter == 0
    && session.AdaptiveState.TimeSkipPending
    && !HasRecentUserInstruction(session, windowSize: 3);

if (timeSkipActive)
{
    session.AdaptiveState.TimeSkipPending = false;
    // Log: MultiEncounterTimeSkipDirectiveInjected
}

for (var i = 0; i < batchSize; i++)
{
    if (isClimaxPhase && i == 0)
    {
        if (timeSkipActive)
        {
            promptText = "Close the current encounter naturally. Then advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life.";
            intent = PromptIntent.Instruction;  // override for first actor only
        }
        else
        {
            promptText = isNewEncounterStart
                ? "Continue the scene naturally."
                : "Continue the current encounter naturally from where it left off.";
            intent = PromptIntent.Message;
        }
    }
    // ... rest of loop unchanged ...
}
```

**Change B (Fix 3): Add `HasRecentUserInstruction` helper**

```csharp
private static bool HasRecentUserInstruction(RolePlaySession session, int windowSize)
{
    return session.Interactions
        .TakeLast(windowSize)
        .Any(x => string.Equals(x.ActorName, "Instruction", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(x.GeneratedByCommand));
}
```

**Change C: Remove stale `injectedTimeSkipInstruction` variable and exclusion block**

Remove the `RolePlayInteraction? injectedTimeSkipInstruction = null;` declaration and the post-loop `IsExcluded = true` block (lines ~1591-1595).

### File 2: `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`

**No changes needed.** The "Active Instruction (persistent)" re-injection block (lines 1272-1292) is already guarded by `if (intent != PromptIntent.Instruction)`. Since the first actor now uses `PromptIntent.Instruction`, the re-injection is automatically bypassed. No Instruction interaction is created in `session.Interactions`, so future turns have nothing to find.

### File 3: `DreamGenClone.Tests/RolePlay/MultiEncounterTimeSkipTests.cs` (new)

Test cases:
1. `TimeSkipPending_True_FirstActorGetsInstructionIntent` — verify first actor prompt uses `PromptIntent.Instruction` with directive text
2. `TimeSkipPending_True_SubsequentActorsGetMessageIntent` — verify actors 2+ use `PromptIntent.Message`
3. `TimeSkipPending_True_DirectiveHasNoEncounterNumber` — verify directive text contains no `#N`
4. `TimeSkipPending_True_SecondTurnDoesNotReinject` — verify directive does not appear in second turn
5. `UserInstructionPresent_TimeSkipSkipped` — verify injection skipped when user Instruction in last 3
6. `UserInstructionPresent_TimeSkipPendingRemainsTrue` — verify flag persists for retry
7. `EncounterNumber1_TimeSkipDoesNotFire` — verify `CurrentEncounterNumber > 1` gate

---

## Re-evaluate Constitution Check (Post-Design)

- [x] Local-first runtime preserved — no new cloud dependencies
- [x] Module boundaries preserved — changes in Web/Application and Web/Domain only
- [x] .NET layered architecture — no new project references
- [x] Deterministic state — `TimeSkipPending` cleared atomically with directive injection
- [x] SQLite persistence — `GeneratedByCommand` already persisted, no migration
- [x] Serilog logging — new informational events for injection/skip
- [x] Log level configurability — uses existing `_logger` infrastructure
