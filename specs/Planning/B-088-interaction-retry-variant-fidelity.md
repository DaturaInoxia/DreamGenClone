# B-088: BUG — Interaction retry uses character variant, not narrative prompt

**State**: `done`
**Priority**: high
**Scope**: small
**Related**: B-024 (narrative prompt issues), B-053 (prompt viewer / `PromptText`), B-045/B-046 (narrative anchoring)

---

## Report

When a user clicks **Retry** (or **Make longer**, **Make shorter**, **Ask to rewrite**) on a *narrative* interaction, the regenerated alternative is produced by the **character-rewrite** prompt path instead of replaying / rebuilding the original **narrative** prompt. The retried narrative loses the Narrative variant's omniscient third-person framing and bypasses the narrative validation pipeline (`GenerateNarrativeWithValidationAsync`), so it re-narrates "as a character" rather than as the original narrative.

## Root Cause (verified code trace)

`InteractionRetryService` (all six commands: `RetryAsync`, `RetryWithModelAsync`, `RetryAsAsync`, `MakeLongerAsync`, `MakeShorterAsync`, `AskToRewriteAsync`) is a **standalone** prompt builder:

1. `ResolveOriginal` → `session.ResolveActiveAlternative(original)` resolves the target interaction.
2. `BuildRetryPromptAsync` builds a **generic character-style rewrite prompt** that always:
   - appends `RolePlayPerspectivePromptBuilder.AppendInteractionInstruction(sb, perspectiveMode, target.ActorName, …)` — a POV/perspective instruction (Character-variant);
   - never reads the interaction's stored `PromptText` (the full built prompt persisted per B-053);
   - never consults a prompt-variant signal; and
   - never routes through the 17-slot builder / `PromptIntent.Narrative` / `GenerateNarrativeWithValidationAsync`.
3. Generates via `_completionClient.GenerateWithReasoningAsync` directly — bypassing the narrative validation/retry pipeline.
4. `CreateAlternative` copies `active.InteractionType` / `active.ActorName` onto the alternative — so a narrative retry *looks* correct (`System` / `"Narrative"`) but the underlying prompt used is the character rewrite.

**Evidence — live DB (session `aa89a8c2`, `Sessions.PayloadJson.interactions`):**

| interactionType | actorName | generatedByCommand | promptText length |
|---|---|---|---|
| 4 (System) | Narrative | Narrative | ~17–29 KB |
| 1/2 (User/Npc) | Becky / Ken | Continue | ~27–35 KB |

Narrative interactions are unambiguously distinguishable by `GeneratedByCommand == "Narrative"` (contract also documented in `specs/024-narrative-prompt-fix/tasks.md` T019). The variant, however, is **not persisted as a typed field** on `RolePlayInteraction`, so the retry service has no explicit variant to restore.

## Design Decisions

### D1 — Persist the variant on the interaction (source of truth)

Add to `RolePlayInteraction` (following the existing fully-qualified pattern used by `NarrativePhaseAtCreation`):

```csharp
/// <summary>
/// The prompt variant (Character vs Narrative) used to generate this interaction.
/// Null for interactions persisted before B-088.
/// </summary>
public DreamGenClone.Domain.RolePlay.PromptVariant? GeneratedVariant { get; set; }
```

Set at every creation site:
- `RolePlayContinuationService.ContinueAsync` (character path) → `PromptVariant.Character`
- `RolePlayContinuationService.ContinueNarrativeAsync` + batch-narrative → `PromptVariant.Narrative`
- `RolePlayEngineService` multi-actor / overflow / opening narrative creation sites → per path (`Narrative` for narrative outputs, `Character` for actor outputs)

No DB migration needed — interactions live in the `Sessions.PayloadJson` blob; the new key serializes automatically as `generatedVariant`.

### D2 — Variant resolution (typed field, then legacy heuristic)

Add a resolver (e.g. `ResolveGeneratedVariant(original/active)`):
1. Prefer typed `GeneratedVariant` on the active alternative (fall back to the original).
2. Legacy heuristic when null: `GeneratedByCommand == "Narrative"` → `Narrative`; otherwise `Character`.

This keeps old sessions (no typed field) correct without backfilling.

### D3 — Narrative retry routes through the narrative pipeline

When effective variant == `Narrative`, all default retry commands rebuild via the **narrative prompt builder** with `PromptIntent.Narrative` and run through `GenerateNarrativeWithValidationAsync` — not the standalone character rewrite.

Implementation approach: inject `IRolePlayContinuationService` into `InteractionRetryService` (already DI-registered; retry service currently injects `_completionClient` directly for the bypass). Then:
- **Plain Retry / Retry with model** — rebuild the narrative prompt from the original's stored `PromptText` (the faithful "original prompt context") OR rebuild fresh via the narrative builder from current session state. **Recommendation: rebuild via `ContinueNarrativeAsync(session, actorName, directiveText, …)` using the original `PromptText` as the directive**, preserving the Narrative variant and validation pipeline. (See Open Decision O1.)
- **Make longer / Make shorter / Ask to rewrite** — same narrative rebuild with the rewrite instruction appended to the directive text (mirrors how the character path appends rewrite instructions today).
- **Model resolution** — keep `RetryWithModelAsync`'s model override; route the resolved model into the narrative call.
- **Alternative contract unchanged** — the result must still be linked as an alternative (`ParentInteractionId`, `AlternativeIndex`, update `ActiveAlternativeIndex`) with `InteractionType.System` + original `ActorName` + `GeneratedByCommand = "Narrative"`. This requires either (a) a new `ContinueNarrativeAsAlternativeAsync` method on the continuation service, or (b) an overload of `ContinueNarrativeAsync` accepting alternative metadata. Recommend (a) to keep the narrative path cohesive.

### D4 — Character retry unchanged

Effective variant == `Character` → keep the current `BuildRetryPromptAsync` path (it is already a character rewrite). Only addition: set `GeneratedVariant = Character` on retry-created alternatives for consistency.

### D5 — Retry-as is an explicit variant override

`RetryAsAsync(actor, customActorName)` is a deliberate user override and keeps current behavior. Improvement in scope: when the user picks **"Narrative"** via `RetryAsAsync(ContinueAsActor.Npc, "Narrative")`, route through the real narrative builder instead of the character-style prompt with actor name "Narrative".

### D6 — UI

No UI change required to close the bug — the fix is service-side. Optional nice-to-have (defer, out of scope): a small variant badge on interactions in the workspace so narrative vs character is visible. Open decision O2.

---

## Implementation Plan

### Phase 1 — Persist variant at creation (D1)

| File | Change |
|---|---|
| `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs` | Add `GeneratedVariant` property (nullable `DreamGenClone.Domain.RolePlay.PromptVariant?`). |
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | Set `GeneratedVariant` in `ContinueAsync` (Character), `ContinueNarrativeAsync` (Narrative), batch-narrative (Narrative). |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Set `GeneratedVariant` at the multi-actor / overflow / opening-narrative creation sites (lines ~1311, ~1471, ~1692, ~1777 and narrative wrap). |

### Phase 2 — Variant resolver + narrative routing in retry service (D2, D3, D4, D5)

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs` | Add `ResolveGeneratedVariant` (typed → legacy heuristic). Inject `IRolePlayContinuationService`. Branch in `BuildRetryPromptAsync` / each command: Narrative → narrative rebuild + validation; Character → current path. Set `GeneratedVariant` on created alternatives. Route `RetryAsAsync("Narrative")` through narrative builder. |
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | Add `ContinueNarrativeAsAlternativeAsync` (or overload) that produces an alternative linked to a parent interaction, running the narrative validation pipeline. |
| `DreamGenClone.Web/Application/RolePlay/IRolePlayContinuationService.cs` | Interface signature for the new narrative-as-alternative method. |

### Phase 3 — Tests (D7)

New file `DreamGenClone.Tests/RolePlay/InteractionRetryServiceTests.cs` (no retry tests exist today), using the existing `RolePlayTestFactory` / `NullRolePlayDebugEventSink` doubles and a prompt-capturing completion client (pattern from `CapturingCompletionClient` / `QueueCompletionClient`):

1. **Narrative `RetryAsync`** — retry prompt is built as Narrative variant (assert captured prompt carries narrative-variant framing, NOT the character POV rewrite block); alternative retains `GeneratedByCommand = "Narrative"`, `InteractionType.System`, original `ActorName`, correct `ParentInteractionId` / `AlternativeIndex` / `ActiveAlternativeIndex`.
2. **Character `RetryAsync`** — existing character-rewrite behavior unchanged.
3. **Narrative `MakeLonger` / `MakeShorter` / `AskToRewrite`** — narrative variant with the rewrite instruction appended.
4. **Legacy heuristic** — interaction with only `GeneratedByCommand = "Narrative"` (no typed variant) routes to Narrative.
5. **`RetryAsAsync(…, "Narrative")`** — routes through the narrative builder.
6. **`RetryWithModelAsync` on a narrative** — respects model override while staying on the narrative path.

---

## Blast Radius

- **Code**: `RolePlayInteraction.cs`, `RolePlayContinuationService.cs` (+ interface), `RolePlayEngineService.cs` (additive field sets only), `InteractionRetryService.cs`. No changes to prompt slots, gate thresholds, or RP config sources.
- **Data**: additive JSON key only (`generatedVariant`) in `Sessions.PayloadJson` — no DB migration, no snapshot impact.
- **Tests**: additive — new `InteractionRetryServiceTests.cs`. No existing test asserts retry-variant behavior (grep confirms none exist), so no expected regressions.
- **Hard-rule compliance**: no fallback thresholds introduced — variant resolution has exactly one active decision path (typed field → legacy heuristic), no config bypass.

## Validation Protocol

- Build: `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore` and `dotnet build DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore` (full solution build before finish).
- Tests: `dotnet test DreamGenClone.Tests --filter "FullyQualifiedName~InteractionRetryService"` then the RolePlay suite (`FullyQualifiedName~RolePlay`). All must pass — no skipped/disabled tests.
- Runtime spot-check (user does the fresh RP session per debug protocol): generate a narrative interaction → Retry → the retried alternative should read as an omniscient narrative (same variant) and its prompt (Interaction Info → LLM Prompt tab, B-053) should carry the Narrative-variant framing.

## Open Decisions

- **O1 — Plain Retry rebuild mode**: rebuild fresh via `ContinueNarrativeAsync` from current session state (recommended — matches "rebuild with the same variant" and gets fresh history) vs replay the stored `PromptText` verbatim (maximum fidelity to the exact original, but reuses stale history).
- **O2 — UI variant badge**: include a Narrative/Character badge on interactions in the workspace, or keep UI out of scope (recommended: out of scope for this bug; track separately if desired).

## Resolution (implemented 2026-08-17)

- **O1 resolved**: plain Retry rebuilds fresh via the narrative builder (the original directive text is not persisted separately, so the narrative default directive is used; the stored `PromptText` remains the faithful record, viewable via B-053).
- **O2 resolved**: UI badge deferred — out of scope, service-side fix only.

**Files changed (all additive):**
- `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs` — new `GeneratedVariant` (`PromptVariant?`) property.
- `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — stamp `GeneratedVariant` on `ContinueAsync` (Character) and `ContinueNarrativeAsync`/batch-narrative (Narrative); new `ContinueNarrativeAsAlternativeAsync` (narrative builder + validation pipeline, caller-resolved model, not committed).
- `DreamGenClone.Web/Application/RolePlay/IRolePlayContinuationService.cs` — new interface method.
- `DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs` — inject continuation service; `ResolveGeneratedVariant` (typed field → legacy `GeneratedByCommand == "Narrative"` heuristic); narrative branch in `RetryAsync`/`RetryWithModelAsync`/`MakeLongerAsync`/`MakeShorterAsync`/`AskToRewriteAsync`/`RetryAsAsync("...Narrative")`; `CreateAlternative` stamps `GeneratedVariant`.
- `DreamGenClone.Tests/RolePlay/InteractionRetryServiceTests.cs` — new (9 tests).
- 5 test doubles updated with the new interface method (`RolePlayTestDoubles`, `RolePlayIntentRoutingTests`, `PersonaInteractionSelectionTests`, `RolePlayBehaviorModeSubmitTests`, `RolePlaySessionLifecycleTests`).

**Validation:**
- Web build: 0 errors. Test build: 0 errors.
- New tests: 9/9 pass.
- Full RolePlay suite: 749/749 pass. Full test project: 1048/1048 pass. No regressions.
- Runtime spot-check confirmed by user 2026-08-18 — marked `done` (functionally stable in dev).

---

*Plan created 2026-08-17. Implemented 2026-08-17. Marked `done` 2026-08-18.*
