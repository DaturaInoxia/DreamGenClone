# Plan — Fix Encounter Memory Capture: Enrichment Model Routing + Double-Start

**Status:** PLANNED — awaiting implementation approval (do not implement without a go-ahead)
**Created:** 2026-08-06
**Session evidence:** `7235f5b4-862c-4932-9a13-fa9a67f8b1c7` (exhibitionism)
**Related debug records:** `specs/028-encounter-start-detection/debug/001-*`, `002-*`, `003-*`

---

## 1. Problem Summary

"Not all memories are captured correctly; still have issues with encounter start and ending."

Observed in session `7235f5b4`:
1. Encounter #1 started **twice** — `EncounterStartDetected Encounter=1 StartIdx=54` (00:49) and `StartIdx=73` (01:11). The EncounterCompletion memory uses `StartIdx=73`, so interactions 54–72 are **missing**.
2. Encounter #2 is degenerate — `StartIdx=82, EndIdx=82` (single interaction); enrichment skipped (`no interactions in range [82-82]`), no `LlmSummary`.
3. 6 PhaseMilestone records have `LlmSummary=null` — enrichment job logs `LLM call failed on second attempt ... Abandoning`.

---

## 2. Root Causes (with evidence)

### RC-1: Enrichment runs on the wrong model → 400 → abandoned

**File:** `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` (~L106-112)

```csharp
var appFunction = recordsToEnhance.Any(r => r.SummaryType == EncounterSummaryType.PhaseMilestone
                                          && r.SummaryType != EncounterSummaryType.EncounterCompletion)
    && recordsToEnhance.All(r => r.SummaryType == EncounterSummaryType.PhaseMilestone)
    ? AppFunction.RolePlaySemanticAnalysis     // ← PhaseMilestone routes to LOCAL qwen (16,384 ctx)
    : AppFunction.RolePlaySummaryEnhancement;   // ← Encounter/ArcCompletion route to deepseek (large ctx)
```

- PhaseMilestone enrichment is routed to `RolePlaySemanticAnalysis` = `qwen2.5-14b-instruct-1m` (Local).
- The milestone prompt is built from up to **30 recent interactions** (`session.Interactions...TakeLast(30)`) → **79,292 chars ≈ 17,593 tokens**.
- Local model loaded at `n_ctx: 16384` → 400: `n_keep: 17593 >= n_ctx: 16384` on both attempts → abandoned → `LlmSummary` stays null.
- The interaction-phase semantic analysis (same slot) works because its per-interaction prompts are small.

**Function reference**
- `AppFunction.RolePlaySemanticAnalysis` (RP Semantic Analysis Background) → `SemanticEventInferenceService.InferAsync` → per-interaction theme-scoring + universal `encounter-started`/`encounter-completed` detection. Model: local qwen. **Keep as-is.**
- `AppFunction.RolePlaySummaryEnhancement` (RP Summary Enhancement Background) → `EncounterSummaryJobHandler.EnhanceRecordAsync` → rewrites memory records (PhaseMilestone/ArcCompletion/EncounterCompletion) into prose. Model: `deepseek-v4-flash` (DeepSeek). **All enrichment should go here.**

### RC-2: Encounter double-start from over-broad "clear on leaving Climax" branch

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (~L4941), multi-encounter Climax lifecycle

```csharp
else if (finalPhase != Climax && v2State.CurrentEncounterNumber != 0)
{
    v2State.CurrentEncounterNumber = 0;
    v2State.IsEncounterActive = false;   // wipes universal encounter state
    v2State.TurnsInCurrentEncounter = 0;
    Log("MultiEncounterClimax cleared: ... (left Climax phase)");
}
```

- Intended to clear state when a **multi-encounter theme leaves Climax**, but it does **not** check `isMultiEncounterClimax` and fires in **any non-Climax phase** whenever `CurrentEncounterNumber != 0`.
- Proof: log `MultiEncounterClimax cleared ... (left Climax phase)` at **00:52:23 during Approaching**, right after start #1 (00:49). It wipes `IsEncounterActive=false, CurrentEncounterNumber=0` → the start-detection re-entry guard (`if (state.IsEncounterActive || timeSkip != None) return;`) is bypassed → start fires again at index 73 → memory truncates to the second start.

### RC-3: Degenerate encounter #2 (partially downstream, partially independent)
- Partly caused by RC-2 (wiped state + re-start). With RC-2 fixed, encounter 2 would start correctly.
- **Independent residual risk:** the boundary min-length guard (`minIxns = 4`, `RolePlayEngineService.cs` ~L5396) only applies to `[ClimaxMode:multi-encounter]` themes. Non-multi themes can still have a boundary fire on a near-zero-length encounter. This may warrant its own fix (e.g. apply a minimum-length guard to all themes) and is tracked as a residual risk, NOT assumed resolved by RC-2.

---

## 3. Planned Changes

### Change 1 — Route ALL enrichment to `RolePlaySummaryEnhancement`

**File:** `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` (~L103-112)

Replace the conditional model selection with a single function:

```csharp
// All memory enrichment (PhaseMilestone, ArcCompletion, EncounterCompletion) uses the
// dedicated summary-enhancement slot. PhaseMilestone prompts include up to 30 recent
// interactions and can exceed the RolePlaySemanticAnalysis model's context, so they must
// not share that slot. Interaction-phase semantic analysis (SemanticEventInferenceService)
// stays on RolePlaySemanticAnalysis.
var appFunction = AppFunction.RolePlaySummaryEnhancement;
resolvedModel = await _modelResolutionService.ResolveAsync(appFunction, cancellationToken: cancellationToken);
```

- Keeps `RolePlaySemanticAnalysis` = local qwen for interaction-phase analysis (working well).
- Enrichment moves to `deepseek-v4-flash` (adequate context for 79k-char prompts).
- Verified: no test pins the PhaseMilestone→RolePlaySemanticAnalysis routing (`grep` over `DreamGenClone.Tests/**`).

### Change 2 — Gate the encounter-clear branch on a genuine Climax exit

**File:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (~L4941)

```csharp
// Only clear the universal encounter state when actually leaving Climax (priorPhase == Climax).
// Without this guard, the branch fires in BuildUp/Committed/Approaching whenever the universal
// start detection has set CurrentEncounterNumber, wiping IsEncounterActive and causing the
// start detection to re-fire (double/multiple starts) and truncating encounter memory.
else if (priorPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
         && finalPhase != DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
         && v2State.CurrentEncounterNumber != 0)
{
    v2State.CurrentEncounterNumber = 0;
    v2State.IsEncounterActive = false;
    v2State.TurnsInCurrentEncounter = 0;
    Log("MultiEncounterClimax cleared: ... (left Climax phase)");
}
```

`priorPhase` is already in scope (defined as `previousV2State?.CurrentPhase` earlier in the method) and is `NarrativePhase?`. On the first pipeline run (`previousV2State == null`) the `priorPhase == Climax` comparison is simply false, so the branch is skipped — no fallback needed.

IMPORTANT — do NOT add `isMultiEncounterClimax` to this branch. It is intentionally gated only on the Climax-exit transition: non-multi-encounter themes (e.g. exhibitionism) must also reset `CurrentEncounterNumber`/`IsEncounterActive` when leaving Climax → Reset so the next arc's universal start detection can assign a fresh encounter number (`GlobalEncounterCount + 1`). If this branch were scoped to multi-encounter themes only, non-multi themes would never clear and `TryDetectEncounterStartAsync`'s `if (state.CurrentEncounterNumber == 0)` guard would never fire, producing stale encounter numbers across arcs.

---

## 4. Blast Radius

- **Change 1:** Only `EncounterSummaryJobHandler` model routing. PhaseMilestone enrichments now use DeepSeek instead of local qwen → no more 400s on large prompts. Interaction-phase semantic analysis unaffected. Requires `RolePlaySummaryEnhancement` function default to point at a capable model (currently `deepseek-v4-flash`, DeepSeek — verify in `FunctionModelDefaults`). If that function default is missing or its model is disabled, `ResolveAsync` throws `ModelResolutionException` → the handler logs a warning and returns (no crash, but no enrichment — same observable outcome as the current 400). Confirm the default exists before rollout.
- **Change 2:** Only the multi-encounter Climax lifecycle clear branch. Universal encounter state is no longer wiped during non-Climax phases. Expected: encounter starts fire once, EncounterCompletion memories cover the full interaction range, and degenerate single-interaction encounters stop occurring. Climax-exit clearing for multi-encounter themes is preserved (still fires when `priorPhase == Climax`).

## 5. Tests

- Build: `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore` (stop web app first to release DLL locks).
- Run: `dotnet test DreamGenClone.Tests --no-build --filter "FullyQualifiedName~Encounter|FullyQualifiedName~MultiEncounter"` (currently 84/84 pass).
- No existing test asserts `MissingEncounterCompletedMapping` throw or the PhaseMilestone→SemanticAnalysis routing (verified by grep).
- Known pre-existing failures (~75 in RolePlay suite) are unrelated (PromptBuilder/SlotContract/PhaseLifecycle/AdaptiveState/Lifecycle + FunctionDefaultRepository MaxConcurrentJobs DB gap) — do not "fix" those.

## 6. Verification (runtime)

1. Restart web app on `http://localhost:5177` with the new build.
2. Play a fresh session to: flirtation → actual sexual contact → encounter start → completion.
3. Confirm in logs:
   - `EncounterStartDetected` fires **once** per encounter (no duplicate `StartIdx`).
   - `EncounterCompletion` memory `StartInteractionIndex` matches the true start (not a later re-start).
   - PhaseMilestone + EncounterCompletion `LlmSummary` populated (`EncounterSummaryJobHandler: LLM call ... ` success, no `n_keep >= n_ctx`).
4. Query `RolePlayV2EncounterSummaries` for the session: no empty ranges (`StartInteractionIndex == EndInteractionIndex`), all records have `LlmSummary`.

## 7. Open Questions / Follow-ups

- **Prompt size:** PhaseMilestone prompt uses `TakeLast(30)` interactions (79k chars). DeepSeek handles it, but consider whether 30 is excessive for milestone enrichment (separate tuning item).
- **Boundary min-length guard:** consider applying the `minIxns = 4` guard to non-multi-encounter themes too, to prevent near-zero-length encounters (separate item; likely resolves automatically with Change 2).
- **RC-3 validation:** re-check after Change 2 whether encounter 2-style degenerate slices still occur.

## 7.5 Review Notes (post-analysis, incorporated)

1. **Do NOT gate Change 2 on `isMultiEncounterClimax`** — non-multi themes must also clear on Climax→Reset for fresh encounter numbers next arc (see Change 2 note).
2. **Climax-entry branch (L4923) is multi-encounter-scoped** and does NOT fire for non-multi themes — so for exhibitionism the universal start detection's `CurrentEncounterNumber` carries into Climax correctly. This is expected; do not "fix" it.
3. **Universal start in Reset is expected, not a regression** — after Climax→Reset clears the state, a sexual interaction in Reset can legitimately start a new encounter with `CurrentEncounterNumber = GlobalEncounterCount + 1`.
4. **RC-3 min-length guard gap** is a separate residual risk (boundary can fire on near-zero-length encounters for non-multi themes) — track as its own fix, do not assume RC-2 resolves it.
5. **Config prerequisite** — `RolePlaySummaryEnhancement` must point at a working model or enrichment silently no-ops (see Change 1 blast radius).

## 8. References

- `specs/028-encounter-start-detection/debug/001-encounter-start-context-window.md` (context window fix — already applied)
- `specs/028-encounter-start-detection/debug/002-encounter-completed-universal.md` (universal end detection — already applied)
- `specs/028-encounter-start-detection/debug/003-encounter-memory-capture-enrichment.md` (this diagnosis)
- `specs/001-rp-prompt-redesign/debug/018-encounter-fields-rebuild-loss.md` (related: encounter fields lost on state rebuild)
- `specs/001-rp-prompt-redesign/debug/019-encounter-boundary-over-detection.md` (related: boundary IsEncounterActive guard)
