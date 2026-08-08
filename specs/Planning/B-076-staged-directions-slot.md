# B-076: Staged Directions slot — one-shot batch queue injected on next continuation

**State**: `implemented`
**Priority**: high
**Scope**: medium

---

## TL;DR

Add a new `IsStagedDirection` boolean flag on `RolePlayInteraction` and a new `StagedDirectionsSlot` that injects all `+`-staged character messages/instructions as a single `[Staged Scene Directions — Execute This Turn]` batch block on the **next** `…` continuation, then **graduates** the staged rows (flips `IsStagedDirection = false`). Every `+` submission is always staged — no toggle, no opt-in. Graduation runs from both `SubmitPromptAsync` (try/finally) and `ContinueAsAsync` (before return), guaranteeing one-shot semantics for both `…` with text and pure-continue `…` without text. The `SystemPrimerSlot` introduces the Staged Directions section to the model. Distinct from `PinnedContextSlot` (B-074): pinned = persistent across many turns; staged = one-shot, consumed on next `…`, then graduated.

**Related items**: B-074 (`+`/`…` two-button workflow + `PinnedContextSlot`), B-075 (per-character steering).

---

## Problem Statement (User-Facing)

After B-074, the `+` button stages a character message or instruction into `session.Interactions` (history). The model sees these as prior context in `InteractionHistorySlot`, not as "what happens next, now write it." If a user stages 3 entries — `Becky heads to shower`, `Ken turns on TV`, `Dean waits then follows Becky` — the continuation only acts on the *most recent* text in the prompt box (or none if the box is empty). The earlier staged entries are treated as past narrative, not as scene directions for the next beat.

Users want a way to queue multiple actor-attributed beats (including a closing instruction), hit `…`, and have the model write the continuation *from* that staged scene plan — as a batch directive, not as three independent historical turns.

---

## Design Decisions

1. **Staged entries live in `session.Interactions` with an `IsStagedDirection` flag (NOT a separate list).** Staged entries are real `RolePlayInteraction` rows — added to `session.Interactions` immediately when staged — carrying a new boolean flag `IsStagedDirection`. This unlocks all existing per-interaction commands (Retry / Rewrite / Expand / MakeLonger / MakeShorter / AskToRewrite / Pin / Exclude / Hide) "for free" because the workspace already renders and operates on `session.Interactions`. No parallel queue, no separate persistence path, no dedicated `StagedDirectionEntry` type. The flag tells the prompt builder and history-rendering slots to treat these differently:

    - **Prompt injection** — `StagedDirectionsSlot` reads only `session.Interactions.Where(i => i.IsStagedDirection && !i.IsExcluded)` and renders them as a batch `[Staged Scene Directions — Execute This Turn]` block.
    - **History slot** — `InteractionHistorySlot` reads only `!i.IsStagedDirection && !i.IsExcluded` interactions. Staged entries do NOT appear in the `Interaction History` zone of the prompt (they appear via `StagedDirectionsSlot` instead). This prevents them from polluting the "what already happened" narrative context.
    - **UI interaction list** — staged entries render in the workspace interaction list with a distinct "staged" badge/tint so the user can see them and apply commands (Retry, Expand, etc.). After the staged queue is consumed (next `…`), all staged rows from the prior batch have their `IsStagedDirection` flag flipped to `false` (they become normal history rows) — and a new `RolePlayInteraction` for the continuation result is appended as usual.

2. **One-shot, consumed on `…`.** When the next continuation builds its prompt, the new `StagedDirectionsSlot` (`PromptSlotId.StagedDirections = 20`, `Zone = C`, `Order = 9` — immediately after `PinnedContext` 8) writes the staged block, then the engine **graduates** the staged rows (sets `IsStagedDirection = false` on each) AFTER the prompt is built but BEFORE the LLM call returns. This guarantees one-shot semantics even if the LLM call fails — a failed continuation should still consume the staged batch, otherwise a retry would re-inject stale directions. (Open Decision C.) "Graduated" rows then appear in `InteractionHistory` as normal past context on the *next* turn after consumption.

3. **Actor-attributed block format.** The slot renders an agent-attributed block the model can parse as scene directions, not as completed turn history:
    ```
    [Staged Scene Directions — Execute This Turn]
    Character Message: Becky — Heads to shower
    Character Message: Ken — Turns on TV
    Character Message: Dean — Waits a bit then goes to shower
    Instruction: Becky leaves shower stall door open while she changes, Dean watches...
    ```
    Entries labeled by interaction type / actor name: Instruction interactions (existing `ActorName = "Instruction"` from B-074) → `Instruction:`; Message/Narrative → `Character Message: <Actor> —`. Same convention as `PinnedContextSlot`.

4. **Every `+` submission is always staged — no toggle, no opt-in.** The user-requested workflow removes the B-074 "stage to history" behavior for `+` entirely. All `PlusButton` submissions (both Instruction and Message/Narrative intents) unconditionally set `IsStagedDirection = true`. The `+` button now has exactly one purpose: stage a character message or instruction for the next `…` continuation's batch block. There is no `StageToQueue` flag on `UnifiedPromptSubmission`, no UI toggle, and no per-session persistent mode. Sessions that previously relied on B-074's `+` for "log a normal message in history before continuing" now see that logged message in the `[Staged Scene Directions]` block instead of `InteractionHistory` — graduation after `…` moves it to history.

5. **SystemPrimerSlot introduces the Staged Directions section.** Slot 0 now includes a paragraph explaining `Staged Scene Directions` to the model — positioned between `User Direction` and `Scene Context` in the primer — so the model understands the batch block as a scene plan to execute this turn, not as already-happened context.

6. **No turn counter side-effects.** Staging with `IsStagedDirection = true` (like B-074's `PlusButton`) does NOT call `StartTurnAsync`, does NOT increment `ObservedTurnCount`, does NOT trigger `UpdateStateAndDetectEncounterAsync`, and does NOT increment `TurnCountInPhase` (same `!isAddOnly` guard B-074 already established). Analytics run only on the resulting generated `RolePlayInteraction`(s) after `…`.

7. **Graduate from both `SubmitPromptAsync` and `ContinueAsAsync`.** Graduation happens in two code paths to cover both `…` scenarios:
    - **`…` with text** — `ContinuePromptAsync` creates a `MainOverflowContinue` submission → `SubmitPromptAsync` non-PlusButton path → try/finally wraps `ContinueAsync` and calls `GraduateStagedDirections(session)` in the finally block.
    - **`…` without text** (pure continue) — `ContinuePromptAsync` → `ExecuteContinueAsync` → `ContinueAsAsync` → calls `GraduateStagedDirections(session)` immediately before `return result`.
    Both paths call the same shared helper `GraduateStagedDirections(session)` which flips `IsStagedDirection = false` on all staged rows and queues a save. The flag is sticky through per-interaction commands (Retry / Rewrite / Expand etc.) — only a continuation graduates it.

8. **Composes with `PinnedContextSlot` (B-074) — persistent precedes transient.** Both slots can fire in the same continuation when a user has pinned a persistent constraint (e.g., a character message pinned "I can't hear what is happening in the neighbor's trailer" or a pinned instruction "Ken cannot hear what is happening in the neighbor's trailer") — note this is a *separate* `RolePlayInteraction` with `IsPinned = true` and `IsStagedDirection = false`. The same user can ALSO stage a one-shot plan (e.g., `Becky heads to shower` / `Ken turns on TV` / `Dean waits then follows Becky` / `Instruction: Becky leaves door open, Dean watches`) — these are separate `RolePlayInteraction` rows with `IsStagedDirection = true`. The new `StagedDirectionsSlot` (Zone C, Order 9) renders AFTER `PinnedContextSlot` (Zone C, Order 8) — see Phase C note — so the model reads: persistent constraints first ("here's what's always true"), then this beat's plan ("here's what to execute this turn"). This prevents the staged plan from unintentionally contradicting a pinned constraint (e.g., Ken hearing the shower encounter despite the pinned "I can't hear" constraint).

    Concrete example — both slots fire in the prompt:
    ```
    [Pinned Context]
    Character Message — Ken: I can't hear what is happening in the neighbor's trailer.
    Instruction: Ken cannot hear what is happening in the neighbor's trailer — any sounds from the shower or Dean remain inaudible to Ken.

    [Staged Scene Directions — Execute This Turn]
    Character Message: Becky — Heads to shower
    Character Message: Ken — Turns on TV
    Character Message: Dean — Waits a bit then goes to shower
    Instruction: Becky leaves shower stall door open while she changes, Dean watches...
    ```

    The model receives the persistent constraint (Ken is deaf to the trailer) before the one-shot staged plan (the shower scene), so it knows to keep Ken oblivious to the shower encounter even while generating the staged beat. Both channels are independent `RolePlayInteraction` rows — they share the same persistence path, the same per-interaction command UI (Retry / Expand / Pin / etc.), and survive reloads the same way (B-074 already proved this for pinned interactions). An interaction can be BOTH `IsPinned = true` AND `IsStagedDirection = true` (a pinned one-shot — would fire in both slots on next `…`, then graduate out of staged but stay pinned; the pinned-only channel continues on subsequent turns). Users who want *either* channel alone (only pinned, or only staged) simply don't use the other. `ShouldWrite` for each slot is independent.

9. **Persistence & cross-session lifecycle.** Staged entries are `RolePlayInteraction` rows in `session.Interactions` — they serialize as part of the existing session JSON blob (no schema change, no new field beyond `IsStagedDirection` itself). Consequences:

    - **User can stage entries, close the browser/tab, return later, and still execute `…`** — `LoadSessionAsync()` reloads `session.Interactions` from DB (already happens today), and the `StagedDirections` UI preview chip + the `StagedDirectionsSlot` both filter on the persisted `IsStagedDirection` flag. The next `…` consumes them as if the user had never left. Matches exactly how pinned interactions already survive reloads (B-074).
    - **Cross-machine / DB snapshot**: a `git pull` updates `snapshot.db` only; the live `dev.db` (where staged interactions live) is git-ignored, so staged interactions do NOT travel to other machines via git. A user who wants the same staged plan on another machine must re-stage it there. (Matches the existing dev/snapshot DB model — `db-snapshot-workflow.instructions.md`.)
    - **Consume-once is per-session, not per-load**: the `IsStagedDirection` flags are graduated (flipped to `false`) only when a continuation successfully snapshots them into the prompt. Reloading the page does NOT consume them — only an actual `…` continuation does.
    - **Stale-queue safety**: if a user stages entries, leaves, returns weeks later, and the characters/scenario have changed substantively, the staged text may be stale. Because staged entries are normal `RolePlayInteraction` rows with a flag, the user can review, edit (Retry / Rewrite), exclude, or delete individual rows before hitting `…`. (Same per-row command surface as any other interaction.)

10. **Interaction commands apply to staged entries (BY DESIGN — this was the user's main request).** Because staged entries are real `RolePlayInteraction` rows in `session.Interactions`, all existing per-interaction commands work on them out of the box — no separate "in-queue edit" command surface needs to be built:

    - **Pre-consumption editing**: user can run Retry / Rewrite / Expand / MakeLonger / MakeShorter / AskToRewrite / Pin / Exclude / Hide / Delete on any staged row before hitting `…`. The flag survives these commands (they don't clear `IsStagedDirection`). The command-specific behavior (e.g., AskToRewrite replaces content; Retry regenerates the LLM-generated interaction in place) applies as usual. This means **the "write a sentence and have the LLM rewrite or expand it" workflow is supported BEFORE consuming the staged batch** — exactly the user-requested feature.
    - **If a staged entry is Excluded (or Hidden)**: the `StagedDirectionsSlot` filters `IsStagedDirection && !IsExcluded` (per Decision 1), so excluded entries are omitted from the batch block. Hidden entries (used where appropriate in the existing interaction list to suppress a row from rendering) — staged rows are NOT hidden by default; they render with the staged badge. If a user explicitly hides a staged row, it stays in DB but is skipped by the slot (local rule: slot filters `!IsExcluded`; hidden staged rows are still staged but do not render in the batch block — see Open Decision F).
    - **Pin a staged entry**: a staged row can also have `IsPinned = true` (per Decision 8). It fires in BOTH slots on next `…` — pinned for persistent context, staged for this beat's plan — then graduates out of staged but stays pinned for future turns.
    - **Post-consumption editing**: after `…` consumes the staged batch, all staged rows flip to `IsStagedDirection = false` (graduate to normal history). The resulting generated `RolePlayInteraction`(s) from the continuation are also normal rows. Retry / Rewrite / Expand / etc. all work on graduates and new rows alike — same as today.

---

## Implementation Phases

### Phase A — Domain: `IsStagedDirection` flag on `RolePlayInteraction`

1. Extend `RolePlayInteraction` (`DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs`) with a new boolean flag:
   ```csharp
   /// <summary>
   /// True when this interaction is part of a staged scene-directions batch (B-076).
   /// Staged rows are injected via StagedDirectionsSlot on the next … continuation,
   /// then graduate (flag flips to false) so their content shows up in InteractionHistory
   /// as past context for subsequent turns. Pre-consumption staged rows are real
   /// RolePlayInteraction rows — all per-interaction commands (Retry, Expand, Pin, etc.)
   /// apply to them like any other row.
   /// </summary>
   public bool IsStagedDirection { get; set; }
   ```
   No new collection field on `RolePlaySession`, no new `StagedDirectionEntry` type, no schema migration (the existing session JSON blob serializer picks up the new boolean automatically — verify in Phase E).

2. The engine reads `session.Interactions.Where(i => i.IsStagedDirection && !i.IsExcluded)` wherever it needs the staged set. The UI preview uses the same filter.

### Phase B — Engine: always stage + graduate from both paths

1. In `RolePlayEngineService.SubmitPromptAsync`, the PlusButton branch unconditionally sets `IsStagedDirection = true`:
   - Message/Narrative path: `userPromptInteraction.IsStagedDirection = true` before adding to `session.Interactions`.
   - Instruction path: `interaction.IsStagedDirection = true` (when `SubmittedVia == SubmissionSource.PlusButton`).
   - No `StageToQueue` flag — all `+` submissions are always staged.
   - Synchronous flush (same `isAddOnly` guard B-074 introduced) so the staged row is visible immediately.

2. Shared helper `GraduateStagedDirections(RolePlaySession session)`:
   ```csharp
   private void GraduateStagedDirections(RolePlaySession session)
   {
       var stagedRows = session.Interactions.Where(i => i.IsStagedDirection).ToList();
       if (stagedRows.Count == 0) return;
       foreach (var staged in stagedRows)
           staged.IsStagedDirection = false;
       _autoSaveCoordinator.QueueRolePlaySessionSave(session, "staged-directions-graduated");
       _logger.LogInformation("Graduated {Count} staged direction(s) for session {SessionId}",
           stagedRows.Count, session.Id);
   }
   ```
   Idempotent; safe to call when no staged rows exist.

3. Called from `SubmitPromptAsync`: inside the try/finally that wraps `_continuationService.ContinueAsync(...)`, in the `finally` block, for one-shot guarantee even on failed continuation.

4. Called from `ContinueAsAsync`: immediately after `CompleteTurnAsync` and before `return result`, so pure-continue `…` (no text) also graduates.

5. No `ObservedTurnCount++`, no `StartTurnAsync`, no `TurnCountInPhase` increment for staging (covered by existing `isAddOnly` guard).

### Phase C — Prompt slot: `StagedDirectionsSlot`

1. `DreamGenClone.Web/Domain/RolePlay/PromptSlotId.cs` — add:
   ```csharp
   /// <summary>Zone C, order 9 — transient batch scene directions queue, one-shot on next continuation (FR-025). Renders after PinnedContext (8) so persistent constraints precede the one-shot staged plan.</summary>
   StagedDirections = 20
   ```
   (`= 20` — appended, no renumbering.)

2. `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs` — add `required IReadOnlyList<RolePlayInteraction> StagedInteractions { get; init; }` (note: reuses `RolePlayInteraction` — no separate `StagedDirectionEntry` type).

3. New `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/StagedDirectionsSlot.cs`:
   - `Id = PromptSlotId.StagedDirections`, `Zone = PromptZone.C`, `Order = 9`, `IsTrimEligible = false`.
   - `ShouldWrite` → `context.StagedInteractions.Count > 0` (fires for both Character and Narrative variants — staged directions are operational, not variant-specific).
   - `WriteAsync`:
     ```
     [Staged Scene Directions — Execute This Turn]
     Character Message: Becky — Heads to shower
     Character Message: Ken — Turns on TV
     Character Message: Dean — Waits a bit then goes to shower
     Instruction: Becky leaves shower stall door open while she changes, Dean watches...
     ```
     Final header explicitly tells the model to treat these as the scene's plan for this turn, not as already-happened context. Entries labeled by `InteractionType`/`ActorName`: `ActorName == "Instruction"` (System type) → `Instruction:`; otherwise → `Character Message: <Actor> —`. Same convention as `PinnedContextSlot`.

4. `RolePlayPromptBuilder.GetExpectedZone`/`GetExpectedOrder` — add `PromptSlotId.StagedDirections => 9` to order map; zone falls through `_ => PromptZone.C` default (correct since `StagedDirections = 20 > SceneContinuityAnchor = 12`).

5. `RolePlayContinuationService.BuildPromptViaBuilderAsync` — populate `StagedInteractions = session.Interactions.Where(i => i.IsStagedDirection && !i.IsExcluded).OrderBy(i => i.SessionInteractionIndex).ToList()` (snapshot; graduation happens in the engine after build, not here — the builder is read-only and does not mutate flags).

6. `DreamGenClone.Web/Program.cs` — register `AddScoped<IPromptSlot, StagedDirectionsSlot>()` alongside `PinnedContextSlot`.

7. `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/InteractionHistorySlot.cs` — add `&& !i.IsStagedDirection` to its current `Where(i => !i.IsExcluded)` filter so staged rows are NOT double-counted in `Interaction History`. After graduation (`IsStagedDirection = false`), the rows automatically fall back into `InteractionHistorySlot`.

8. `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/SystemPrimerSlot.cs` — add a `Staged Scene Directions` paragraph between `User Direction` and `Scene Context`:
   ```
   Staged Scene Directions — character messages and instructions staged via the + button.
   These describe what each character is about to do in THIS turn. Execute them
   as a batch scene plan, with each character acting in the order listed.
   The instruction at the end is a scene constraint — follow it.
   ```

### Phase D — UI: no toggle (always staged)

1. The `+` button in `RolePlayWorkspace.razor` is unchanged from B-074 — `AddPromptEntryAsync` creates a `PlusButton` submission. No `StageToQueue` flag, no toggle, no per-session mode.
2. Staged rows render in the existing interaction list. The `IsStagedDirection` flag can drive a future visual badge but is not yet rendered distinctively.
3. The `…` button is unchanged — `ContinuePromptAsync` handles both text + continuation and pure-continue, and the engine graduates staged rows automatically.

### Phase E — Tests & verification

1. New `DreamGenClone.Tests/RolePlay/Prompts/StagedDirectionsSlotTests.cs`:
   - `WriteAsync_OutputsActorAttributedBlock_WhenStagedNonEmpty`
   - `WriteAsync_LabelsInstructionEntriesSeparately`
   - `WriteAsync_SkipsExcludedEntries`
   - `ShouldWrite_True_WhenStagedNonEmpty`
   - `ShouldWrite_False_WhenStagedEmpty`
   - `ShouldWrite_True_ForNarrativeVariant` (unlike `PinnedContextSlot`, staged directions fire in narrative too — they're operational, not variant-specific)
   - `HasCorrectIdentity` — `PromptSlotId.StagedDirections`, `PromptZone.C`, `Order = 9`, `IsTrimEligible = false`

2. Extend `SlotContractTests.cs::CreateContext` to set `StagedInteractions = []` (required field).

3. Extend `SlotContractTests.cs` InteractionHistorySlot tests to verify `IsStagedDirection` rows are excluded from `InteractionHistory` output (new assertion in `InteractionHistorySlot_ThreeTierCompression_FullDetailForRecent`).

4. New engine tests in `RolePlaySessionLifecycleTests.cs`:
   - `SubmitPromptAsync_StageToQueue_AddsStagedInteractionRowWithFlag` — verifies the row is in `session.Interactions` with `IsStagedDirection = true`.
   - `SubmitPromptAsync_StageToQueue_DoesNotIncrementTurnCountInPhase` (covered by existing B-074 `isAddOnly` guard, but explicit assertion for staged mode helps).
   - `SubmitPromptAsync_Continue_WithStagedBatch_BuildsStagedSlotAndGraduatesFlag`
   - `SubmitPromptAsync_FailedContinue_StillGraduatesStagedFlag` (one-shot guarantee)
   - `StagedRow_SurvivesRetryCommand` — applying Retry on a staged row does NOT flip `IsStagedDirection` to false.
   - `InteractionHistorySlot_OmitsStagedRows` — staged rows are absent from `Interaction History` output while staged.

5. `dotnet build` both projects; `dotnet test --filter "FullyQualifiedName~RolePlay"` all green (modulo documented pre-existing DB-schema failures).

6. Manual: queue 3 staged entries + hit `…`; confirm the recorded prompt (B-053 viewer or debug event) contains a `[Staged Scene Directions — Execute This Turn]` block listing all 3, and all 3 rows have `IsStagedDirection = false` after the continuation returns (visible in DB or by their badge disappearing in the UI).
7. Manual command-on-staged: stage 1 row, run Retry on it, confirm the row still has the staged badge (`IsStagedDirection = true`). Then hit `…`, confirm the batch fires with the retry-edited content and graduates.

---

## Open Decisions (User To Confirm)

**Open Decision C — Graduation on failed continuation**: ✅ RESOLVED — C1 (clear unconditionally). Implemented via try/finally in `SubmitPromptAsync` and unconditional call in `ContinueAsAsync`.

**Open Decision D — Consume the queue independently of `…` text**: ✅ RESOLVED — D1 (inject both). The `StagedDirectionsSlot` fires independently of `UserDirectionSlot`; a prompt-box text and a staged block coexist in the prompt (text via UserDirection, batch via StagedDirections).

**Open Decision E — Persistence of `IsStagedDirection` JSON field**: ✅ RESOLVED. The existing session JSON blob serializer picks up the new boolean automatically (same path as `IsPinned`, `IsExcluded`, `IsHidden`). No migration needed.

**Open Decision F — Hidden staged rows** (`IsHidden` + `IsStagedDirection`): Pending. `StagedDirectionsSlot` currently filters ONLY `!IsExcluded` (not `!IsHidden`). A hidden staged row would still appear in the batch block. If the user wants hidden rows suppressed, extend the filter to `!IsExcluded && !IsHidden`. (F1 recommended.)

**Open Decision G — "Clear all staged" toolbar button behavior**: Pending. No clear-all button exists yet. G1 (graduate) recommended when implemented.

---

## Verification

1. `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore 2>&1 | Select-String -Pattern "error CS"` — must be empty.
2. `dotnet build DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore` — must succeed.
3. `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~RolePlay"` — all green; specifically the new `StagedDirectionsSlotTests` and `SubmitPromptAsync_StageToQueue_*` tests.
4. Manual (RP session): stage `Becky — heads to shower` / `Ken — turns on TV` / `Dean — waits then follows Becky` / `Instruction — Becky leaves door open, Dean watches`, hit `…` with empty prompt box → recorded prompt contains a `[Staged Scene Directions — Execute This Turn]` block listing all 4 entries, continuation generated, entries graduate.
5. Manual failure case: stage 2 entries + execute `…` while temporarily breaking the LLM call (or use an invalid model) → staged rows are still graduated after the failed continuation returns.
6. Manual persistence: stage 3 entries, **close the browser tab**, reopen the session → staged rows reappear (`IsStagedDirection` persisted in JSON blob). Hit `…` → block fires, entries graduate.
7. Manual interaction-command: stage 1 row, run Retry on it, confirm the row is still staged. Hit `…`, confirm batch fires with retry-edited content and graduates.

---

## Excluded Scope

- **B-074 `PinnedContextSlot` override** — pinned items remain persistent. `StagedDirectionsSlot` is a separate one-shot channel; both can fire in the same continuation (pinned first at Order 8, staged at Order 9 — persistent constraints precede the one-shot staged plan so the model reads "here's what's always true" before "here's the plan for this beat"). A row can be BOTH `IsPinned = true` AND `IsStagedDirection = true` if the user wants a pinned one-shot (Decision 8).
- **Per-character steering UI** (B-075) — orthogonal; staged directions do not affect stats directly. If a staged entry's actor matches B-075 steering, the LLM response may cause stat drift in the normal post-continuation analytics path, but the staged-directions slot itself is stat-neutral.
- **Queue reordering UI** — initial scope only includes the existing interaction-list kebab (Retry/Expand/Pin/Exclude/Delete) for in-place editing; reorder is out of scope (staged rows read in `SessionInteractionIndex` order). If the user wants order control, they can Delete + re-add in a different order.
- **Cross-session staged transfer** — staged rows live on their own `RolePlaySession`; no copy-to-another-session (would violate the session's single-thread narrative context).
- **Separate StagedDirectionEntry type** — abandoned in favor of the user-requested `IsStagedDirection` flag on `RolePlayInteraction` (see Design Decision 1). This is the cleaner design: no duplicate persistence path, all per-interaction commands free.

---

## Relevant Files

- **Domain**: `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs` — add `IsStagedDirection` flag; `DreamGenClone.Domain/RolePlay/PromptSlotId.cs` — `StagedDirections = 20`.
- **Engine**: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — `SubmitPromptAsync` PlusButton path always sets `IsStagedDirection`; try/finally graduation; `ContinueAsAsync` graduation; shared `GraduateStagedDirections` helper.
- **Continuation**: `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — `BuildPromptViaBuilderAsync` populate `context.StagedInteractions`.
- **Slot**: `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/StagedDirectionsSlot.cs` (NEW); `SystemPrimerSlot.cs` — add Staged Directions paragraph; `RolePlayPromptBuilder.cs` — `GetExpectedOrder` `StagedDirections => 9`; `PromptBuildContext.cs` — `required IReadOnlyList<RolePlayInteraction> StagedInteractions`.
- **History**: `InteractionHistorySlot.cs` — filter `!i.IsStagedDirection`.
- **Registration**: `DreamGenClone.Web/Program.cs` — `AddScoped<IPromptSlot, StagedDirectionsSlot>()`.
- **UI**: `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` — `AddPromptEntryAsync` submission (no toggle); `DreamGenClone.Web/wwwroot/css/roleplay-workspace.css` — removed `.rw-staged-toggle` (no longer needed).