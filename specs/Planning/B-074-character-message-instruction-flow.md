# B-074: Fix character message & instruction flow — staged + button / … button workflow

**State**: `designed`
**Priority**: high
**Scope**: medium

---

## TL;DR

Split the single morphing send button in the RP workspace into two distinct controls:

- **`+` button** — stages a character message / instruction / narrative-direction row into the session with **no continuation, no turn side-effects, and no `ObservedTurnCount++`**.
- **`…` button** — the only path that runs a continuation (starts a turn, generates the assistant response, increments turn counters).

Add net-new **pinned-interaction injection** via a dedicated prompt slot so pinned character messages/instructions are injected into **every** future continuation prompt regardless of context-window position, bypassing the `UserDirectionSlot` generic-default suppression for pinned instructions.

Close-out scope: B-071 (turn double-counting) and B-072 (instruction disappearance) collapse into this design.

**Related items**: B-071 (instructions/messages count as turns, `new`), B-072 (instructions disappear / need entering twice, `new`), B-053 (prompt viewer tab, `new` — companion for verifying the pinned block).

---

## Discovery Summary

### Current State (verified in code)

| Component | Detail |
|---|---|
| Intent enum | `PromptIntent` (`DreamGenClone.Web/Domain/RolePlay/PromptIntent.cs`): `Message=1`, `Narrative=2`, `Instruction=3`. No `Intent` field persisted on `RolePlayInteraction` — intent is inferred from `InteractionType` + `ActorName == "Instruction"` + `GeneratedByCommand`. |
| Submission source | `SubmissionSource` (`DreamGenClone.Web/Domain/RolePlay/SubmissionSource.cs`): `SendButton=1`, `PlusButton=2`, `ContinueAsPopupContinue=3`, `MainOverflowContinue=4`, `AutoComplete=5`. |
| `+` button | `RolePlayWorkspace.razor` L9131–9134 → `AddPromptEntryAsync` (L4574–4623) → `SubmitPromptAsync` with `SubmittedVia = PlusButton`. Already skips `ContinueAsync` for non-instruction intents (engine L1132–1189). |
| Morphing send button | L9135–9153 — renders `↑` arrow when `HasPromptText` (→ `SubmitPromptWithContinuationAsync`, L4643), `…` when empty (→ `SubmitOrOverflowContinueAsync`, L4625 → `ExecuteContinueAsync` → `ContinueAsAsync`). |
| Turn accounting | `RolePlayEngineService.SubmitPromptAsync` L1075–1086: `if (!isInstruction) { StartTurnAsync(...); ObservedTurnCount++; }`. **Instructions skip the turn already**; Message/Narrative via `+` still start a turn and increment `ObservedTurnCount`. `TurnCountInPhase` is computed by the V2 pipeline from *generated* interactions, not user submissions. |
| Instruction persistence | Engine L1100–1108: `System` interaction, `ActorName = "Instruction"`, added to `session.Interactions`, flushed synchronously. |
| `UserDirectionSlot` | Slot 16 (`DreamGenClone.Web/Application/RolePlay/Prompts/Slots/UserDirectionSlot.cs`). Reads only `context.PromptText` (per-turn), suppressed for `Variant == Narrative` and when text matches `GenericDefaults` (`continue`, `continue naturally`, empty). This is the B-072 disappearance surface. |
| Pin Interaction | `RolePlayInteraction.IsPinned` (L27) toggled via `InteractionCommandService.ToggleFlagAsync` (`InteractionFlag.Pinned`). **No prompt injection exists today** — `InteractionHistorySlot` filters only `!IsExcluded`, `PromptBuildContext` has no pin field, no pin slot registered in `Program.cs`. |
| Reference idiom | `AssistantContextManager.AddPinnedContext` (`[Pinned]` prefix, deterministic retention in `GetContext` L101–128) — the established pattern for persistent pinned context. |
| Continuation paths | `ContinueAsAsync` / `ContinueAsync` / `ContinueNarrativeAsync` all start a turn + increment `ObservedTurnCount++`. `SubmitPromptWithContinuationAsync` (send/↑) registers with `_tracker` and supports resubmit (L4703, L4759). |

### Key Gaps

1. **`+` still counts as a turn for Message/Narrative** — the L1075 guard only excludes `Instruction`. This inflates `ObservedTurnCount` and starts an empty turn record on every staged message (B-071).
2. **No batched staging UX** — multiple `+` clicks append rows one at a time; the workflow works but there is no queued-entries affordance and each add costs a turn count today.
3. **`↑`/`…` morphing button conflates "send text + continue" with "continue only"** — the design decision removes the `↑` path entirely.
4. **Pinned interactions are never injected into prompts** — net-new feature: no `PinnedContextSlot`, no `PromptBuildContext.PinnedInteractions`, no pin-aware retention.
5. **Pinned instructions hitting `GenericDefaults` are suppressed** — a pinned instruction whose text is `continue` is dropped by `UserDirectionSlot` (B-072 surface for pinned content).

---

## Design Decisions

1. **Two distinct buttons, no `↑` shortcut.** The morphing send button is replaced by a constant `+` (stage) and `…` (continue). The `SubmitPromptWithContinuationAsync` path and `SubmissionSource.SendButton` are removed entirely, along with the `ConfirmResubmitAsync` retry banner. [user-confirmed]
2. **`+` never counts as a turn.** The `!isInstruction` guard in `SubmitPromptAsync` is widened to `Intent == Instruction || SubmittedVia == PlusButton`. `StartTurnAsync` + `ObservedTurnCount++` happen only on continuation (`…`). This closes B-071. [user-confirmed]
3. **Pinned interactions inject into every future continuation prompt** until unpinned, regardless of context-window position — via a net-new `PinnedContextSlot` (Order 8) + `PromptBuildContext.PinnedInteractions`. [user-confirmed]
4. **Pinned instructions bypass `UserDirectionSlot.GenericDefaults` suppression.** Handled implicitly by the dedicated `PinnedContextSlot` (its `ShouldWrite` is independent of `UserDirectionSlot`'s rules). No change to `UserDirectionSlot` required. [user-confirmed]
5. **Analytics deferred to continuation.** `+`-staged messages should not trigger semantic encounter detection / V2 pipeline on add; analytics run on the next `…`.
6. **Deferred / surfaced open decisions:**
   - Pinned items also retained in `InteractionHistorySlot` (duplicate injection) vs dropped from `RecentInteractions`. **Recommended: drop pinned from `RecentInteractions`, keep them only via `PinnedContextSlot`** — avoids token duplication. (User to confirm.)
   - `BuildContinuationPromptText` becomes dead code once the `↑` path is removed. **Recommended: delete it.** (User to confirm.)
   - Non-pinned instruction re-injection across turns (B-072's full fix) is deferred; the pin feature offers users a guaranteed-reinject workaround.

---

## Implementation Steps

### Phase A — Engine: split add-only path (no turn side-effects)

1. In `RolePlayEngineService.SubmitPromptAsync` (`DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` L1017–1175), widen the turn-creation guard at **L1075**:
   ```csharp
   var isAddOnly = submission.Intent == PromptIntent.Instruction
                   || submission.SubmittedVia == SubmissionSource.PlusButton;
   if (!isAddOnly) { /* StartTurnAsync(...); ObservedTurnCount++; */ }
   ```
   Keep `persistedTurn = null` semantics so downstream `CompleteTurnAsync` calls (L1239, L1377, L1820) — already guarded by `persistedTurn is not null` — auto-skip.

2. Extend the existing `SubmissionSource.PlusButton` branch (L1132) to also short-circuit `BuildContinuationPromptText` and the `UpdateStateAndDetectEncounterAsync` semantic-analytics invocation for non-instruction intents. Staged messages must not trigger semantic encounter detection / V2 pipeline on add; analytics run on the next continuation. Audit `UpdateStateAndDetectEncounterAsync` callers to find the "on-continue" hook.

### Phase B — UI: two distinct buttons

*Parallel with Phase A — engine public contract unchanged; tests stay green.*

1. In `RolePlayWorkspace.razor` (L9131–9153), replace the morphing single button with two adjacent constant buttons:
   - **`+`** keeps `@onclick="AddPromptEntryAsync"`, `disabled="@(!CanAddPromptEntry)"` — unchanged handler, `SubmittedVia = PlusButton`.
   - **`…`** new constant button, `@onclick="EmptyPromptContinueAsync"` — thin wrapper forcing the continue path regardless of `HasPromptText`.
2. Simplify `SubmitOrOverflowContinueAsync` (L4625) — delete the `if (CanSendPrompt) { await SubmitPromptWithContinuationAsync(); }` branch; keep only the empty-text continue path (`BuildContinueRequest(SubmissionSource.MainOverflowContinue, false)` → `ExecuteContinueAsync`).
3. Remove dead code: `SubmitPromptWithContinuationAsync` (L4643), `SubmissionSource.SendButton` enum value, `ConfirmResubmitAsync` (L4730), retry banner UI (L8722), and `_tracker.TryBeginSubmission` for the send flow (keep tracker for continue path, L6754). Audit all `SubmissionSource.SendButton` usages across the workspace after removal.
4. Repurpose `CanSendPrompt` (L4496) as `CanContinue` (session-not-null, not submitting, no background submission). `CanAddPromptEntry` (L4489) stays.
5. Rewrite `.rw-send-btn` CSS (in `RolePlayWorkspace.razor.css`) — no more `:has-text` keying; add `.rw-add-btn` + `.rw-continue-btn` pair styling.

### Phase C — Pin Interaction: net-new prompt injection

*Depends on Phase A so pinning a staged message doesn't wrong-count a turn.*

1. Add `PinnedInteractions` (`IReadOnlyList<RolePlayInteraction>`) to `PromptBuildContext` (`DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs`). Populate in `RolePlayContinuationService.BuildPromptViaBuilderAsync` (L620–705) **before** the `.TakeLast(ContextWindowSize)` filter:
   ```csharp
   context.PinnedInteractions = session.Interactions
       .Where(i => i.IsPinned && !i.IsExcluded)
       .OrderBy(i => i.SessionInteractionIndex)
       .ToList();
   ```
2. New `IPromptSlot`: `PinnedContextSlot` (`DreamGenClone.Web/Application/RolePlay/Prompts/Slots/PinnedContextSlot.cs`):
   - `Id = PromptSlotId.PinnedContext`, `Zone = PromptZone.C`, `Order = 8` (between `CurrentLocation` 7 and `InteractionHistory` 9).
   - `IsTrimEligible = false`.
   - `ShouldWrite` → `context.Variant == PromptVariant.Narrative ? false : context.PinnedInteractions.Count > 0`.
   - `WriteAsync` → deterministic block, one entry per pinned interaction, labeled by origin type:
     ```
     [Pinned Context]
     Character Message — <ActorName>: <Content>
     Instruction: <Content>
     ```
   - Reference idiom: `AssistantContextManager.AddPinnedContext` + `[Pinned]` header (`DreamGenClone.Web/Application/Assistants/AssistantContextManager.cs` L80, L101–128).
3. Add `PinnedContext` to the `PromptSlotId` enum (`DreamGenClone.Web/Application/RolePlay/Prompts/PromptSlotId.cs`); register the slot in `Program.cs` at ~L165 alongside `UserDirectionSlot`.
4. **Bypass generic-default suppression for pinned instructions** — handled implicitly by `PinnedContextSlot` (independent `ShouldWrite`). **No change to `UserDirectionSlot` required** unless the user prefers overlap.

### Phase D — Tests & verification

1. New `DreamGenClone.Tests/RolePlay/Prompts/PinnedContextSlotTests.cs`:
   - `WriteAsync_OutputsPinnedMessagesAndInstructions`.
   - `ShouldWrite_FalseWhenNoPinned`.
   - `ShouldWrite_FalseForNarrativeVariant`.
   - `ShouldWrite_True_WhenPinnedInstructionIsGenericDefault` — pins bypass `GenericDefaults`.
2. `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs` (~L1214): add `PinnedContextSlot_ShouldWrite_WhenPinnedExists`, `PinnedContextSlot_Order_Is_8`.
3. Update existing intent-routing tests (`RolePlayIntentRoutingTests.cs`, `RolePlaySessionLifecycleTests.cs` L785/L829): remove `SendButton`-specific cases; add `PlusButton_Message_DoesNotStartTurn`, `PlusButton_Message_DoesNotInvokeContinuationService`, `PlusButton_Narrative_DoesNotIncrementTurnCountInPhase`.
4. Build the solution + run `dotnet test --filter "FullyQualifiedName~RolePlay"`.

---

## Relevant Files

- **Engine:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` — `SubmitPromptAsync` (L1017–1175); widen `!isInstruction` guard at L1075; PlusButton branch L1132.
- **UI:** `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` — `AddPromptEntryAsync` (L4574), `SubmitOrOverflowContinueAsync` (L4625), button markup L9131–9153, `SubmitPromptWithContinuationAsync` (L4643) [delete], `ConfirmResubmitAsync` (L4730) [delete], retry banner L8722 [delete].
- **Domain/enums:** `DreamGenClone.Web/Domain/RolePlay/SubmissionSource.cs` — remove `SendButton`.
- **Pin injection:** `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs` — new `PinnedInteractions` field; `DreamGenClone.Web/Application/RolePlay/Prompts/PromptSlotId.cs` — new `PinnedContext` value; `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/PinnedContextSlot.cs` — NEW; `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — populate field in `BuildPromptViaBuilderAsync` (L620–705); `DreamGenClone.Web/Program.cs` (~L165) — register slot.
- **InteractionHistorySlot:** `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/InteractionHistorySlot.cs` — pin-ignorance; open decision on dropping pinned from `RecentInteractions`.
- **Reference idiom:** `DreamGenClone.Web/Application/Assistants/AssistantContextManager.cs` (`AddPinnedContext` L80, `GetContext` L101–128).
- **Tests:** `DreamGenClone.Tests/RolePlay/RolePlayIntentRoutingTests.cs`, `RolePlaySessionLifecycleTests.cs` (L785, L829), NEW `PinnedContextSlotTests.cs`, `SlotContractTests.cs` (L1214, L1226).

---

## Verification

1. `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore 2>&1 | Select-String -Pattern "error CS"` — must be empty.
2. `dotnet build DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore` — must succeed.
3. `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~RolePlay"` — all green; specifically new pin-slot tests and updated intent-routing tests.
4. Manual (RP session): type a character message, click `+` — row appears, prompt box clears, **no assistant generation, no `ObservedTurnCount` change** in the adaptive panel. Repeat 2–3×. Click `…` — assistant generates ONE continuation, turn counters advance by exactly 1.
5. Manual pin: kebab menu → Pinned → next `…` continuation contains the pinned text as a `[Pinned Context]` block; pin an instruction whose text is `continue` (a `GenericDefault`) → next `…` continuation STILL includes the instruction via `PinnedContextSlot`.
6. Inspect the recorded prompt in the adaptive panel / future prompt viewer (B-053) — confirm `[Pinned Context]` block ordering (after `CurrentLocation`, before `InteractionHistory`).
7. Backlog state update: B-071 → `done`, B-072 → `done` (mitigated for pinned content), B-074 → `implemented`.

---

## Excluded Scope

- Out-of-session prompt queue persistence (already B-027).
- Retry/resubmit banner UI (removed with the `↑` path).
- TakeTurns user-turn blocking (B-045, separate).
- B-072's standalone fix for NON-pinned instruction disappearance (deferred; pinning offers a guaranteed-reinject workaround).
- B-053 prompt viewer (companion, separate backlog item).
