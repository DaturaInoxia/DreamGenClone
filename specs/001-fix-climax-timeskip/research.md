# Research: Fix Climax Time-Skip System

## Research Task 1: Verify `GeneratedByCommand` persistence

**Decision**: `GeneratedByCommand` already exists on `RolePlayInteraction` (line 42 of `RolePlayInteraction.cs`) and is persisted by `RolePlayStateRepository`. Used by `ContinueAsync` (`"Continue"`), `ContinueNarrativeAsync` (`"Narrative"`), and `InteractionRetryService` (command name). User-typed Instructions leave it null.

**Rationale**: No schema migration needed. The field is already wired through persistence, UI display, and test doubles.

**Alternatives considered**: Adding a new `Origin` enum field — rejected because `GeneratedByCommand` already serves this purpose and is persisted.

## Research Task 2: Verify `PromptIntent.Instruction` behavior in `BuildPromptAsync`

**Decision**: When `intent == PromptIntent.Instruction`, the prompt text appears as `"Instruction:\n{promptText}"` at the end of the prompt (line 1303-1305 of `RolePlayContinuationService.cs`). The "Active Instruction (persistent)" re-injection block (lines 1272-1292) is SKIPPED when `intent == PromptIntent.Instruction` (line 1271 guard: `if (intent != PromptIntent.Instruction)`).

**Rationale**: Using `PromptIntent.Instruction` for the first actor gives the directive maximum authority via the `"Instruction:"` label AND bypasses the persistent re-injection entirely. No Instruction interaction is created in `session.Interactions`, so there's nothing for future turns to find and re-inject.

**Alternatives considered**: Using `PromptIntent.Message` — rejected because `"Message:"` has less authority than `"Instruction:"` and the user's proven working example used the Instruction label.

## Research Task 3: Verify overflow loop structure

**Decision**: The overflow loop in `ContinueAsAsync` (line ~1545 of `RolePlayEngineService.cs`) iterates `batchSize` actors. The first actor (`i == 0`) currently gets a per-position prompt. The time-skip block (line ~1492) runs BEFORE the loop and injects an Instruction interaction into `session.Interactions`.

**Rationale**: The fix removes the Instruction interaction injection entirely. Instead, when `TimeSkipPending` is true and no user Instruction is active, the first actor's `promptText` is set to the time-skip directive and `PromptIntent.Instruction` is used instead of `PromptIntent.Message`. Subsequent actors (`i > 0`) keep `PromptIntent.Message` with `"Describe this same moment from your character's perspective."`.

**Alternatives considered**: Injecting the directive into ALL actors' prompts — rejected because only the first actor needs to initiate the transition; subsequent actors describe the same moment.

## Research Task 4: Verify user Instruction detection window

**Decision**: The last 3 interactions in `session.Interactions` are checked for `ActorName == "Instruction"` AND `GeneratedByCommand` is null/empty. This window is small enough to be performant and large enough to catch user steers typed earlier in the same turn.

**Rationale**: User steers are typically the most recent interaction before a Continue. A 3-interaction window covers the user's steer plus any Narrative that may have been generated.

**Alternatives considered**: Checking only the last 1 interaction — rejected because the overflow loop may have already added a Narrative before the time-skip check runs.
