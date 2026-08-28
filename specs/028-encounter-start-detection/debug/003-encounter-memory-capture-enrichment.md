# 003 — Encounter Memory Capture Broken: Double-Start + Enrichment on Wrong Model

**Created:** 2026-08-06
**Status:** Diagnosis complete; plan drafted, awaiting implementation approval

## Report

Session `7235f5b4` (exhibitionism): "not all memories are captured correctly, still have issues with encounter start and ending."

Evidence:
- Encounter #1 started **twice**: `EncounterStartDetected Encounter=1 StartIdx=54` (00:49) and `EncounterStartDetected Encounter=1 StartIdx=73` (01:11).
- Encounter #1 `EncounterCompletion` memory uses `StartIdx=73` → interactions 54–72 missing.
- Encounter #2 is degenerate: `StartIdx=82, EndIdx=82`; enrichment skipped ("no interactions in range [82-82]"), no `LlmSummary`.
- 6 PhaseMilestone records have `LlmSummary=null`; enrichment job logs `LLM call failed on second attempt ... Abandoning`.

## Analysis

### Issue 1 — Double-start (root cause)
`RolePlayEngineService.cs` ~L4941, multi-encounter Climax lifecycle:
```csharp
else if (finalPhase != Climax && v2State.CurrentEncounterNumber != 0)
{
    v2State.CurrentEncounterNumber = 0;
    v2State.IsEncounterActive = false;   // wipes encounter
    v2State.TurnsInCurrentEncounter = 0;
    Log("MultiEncounterClimax cleared: ... (left Climax phase)");
}
```
Meant to clear state when a multi-encounter theme LEAVES Climax, but it does NOT check `isMultiEncounterClimax` and fires in ANY non-Climax phase whenever `CurrentEncounterNumber != 0`. Log proof: `MultiEncounterClimax cleared ... (left Climax phase)` at 00:52:23 during **Approaching**, right after start #1 (00:49). It wipes `IsEncounterActive=false, CurrentEncounterNumber=0` → re-entry guard bypassed → start fires again (index 73) → EncounterCompletion memory truncates to the second start.

### Issue 2 — Enrichment runs on the wrong model
`EncounterSummaryJobHandler.cs` ~L106-112:
```csharp
var appFunction = recordsToEnhance.Any(r => r.SummaryType == PhaseMilestone && r.SummaryType != EncounterCompletion)
    && recordsToEnhance.All(r => r.SummaryType == PhaseMilestone)
    ? AppFunction.RolePlaySemanticAnalysis     // ← PhaseMilestone uses semantic-analysis slot (local qwen)
    : AppFunction.RolePlaySummaryEnhancement;   // Encounter/Arc completion use summary slot (deepseek)
```
PhaseMilestone enrichment routes to `RolePlaySemanticAnalysis` = `qwen2.5-14b-instruct-1m` (Local). The milestone prompt is huge — `TakeLast(30)` interactions → 79,292 chars ≈ 17,593 tokens — exceeding the model's loaded 16,384 context:
```
Completion request failed: n_keep: 17593 >= n_ctx: 16384
```
→ 400 on both attempts → abandoned → no `LlmSummary`. The interaction-phase semantic analysis (same slot) works because its per-interaction prompts are small; the enrichment prompt is not.

### Issue 3 — Encounter #2 degenerate
Downstream of Issue 1 (wiped state + re-start) plus: the boundary min-length guard (`minIxns = 4`) only applies to `[ClimaxMode:multi-encounter]` themes; exhibitionism is not multi-encounter, so a boundary can fire on a near-zero-length encounter.

## Plan (awaiting "go ahead")

1. **Move enrichment off `RolePlaySemanticAnalysis`** — `EncounterSummaryJobHandler.cs` L106-112: always use `AppFunction.RolePlaySummaryEnhancement` (deepseek-v4-flash, adequate context). Update comment. Interaction-phase semantic analysis (`SemanticEventInferenceService`) unchanged → keeps working on qwen.
2. **Fix double-start** — `RolePlayEngineService.cs` ~L4941: gate the clear branch on genuine Climax exit: `else if (priorPhase == Climax && finalPhase != Climax && v2State.CurrentEncounterNumber != 0)`.
3. **Verify** — after #2, encounter boundaries align with real starts; re-check encounter 1 memory includes 54+, and encounter 2 is no longer a single-interaction slice.

## Function Reference

- **RP Semantic Analysis (Background)** = `AppFunction.RolePlaySemanticAnalysis` → `SemanticEventInferenceService.InferAsync` → theme-scoring events + universal encounter-start/completed detection per interaction. Model: `qwen2.5-14b-instruct-1m` (Local). Keeps working; do NOT route enrichment here.
- **RP Summary Enhancement (Background)** = `AppFunction.RolePlaySummaryEnhancement` → `EncounterSummaryJobHandler.EnhanceRecordAsync` → rewrites memory records (PhaseMilestone/ArcCompletion/EncounterCompletion) into vivid prose. Model: `deepseek-v4-flash` (DeepSeek). This is where ALL enrichment should go.

## Resolution (implemented 2026-08-06, awaiting runtime confirmation)

1. **Change 1 — enrichment off RolePlaySemanticAnalysis** — `EncounterSummaryJobHandler.cs` L103-112: removed the PhaseMilestone→RolePlaySemanticAnalysis conditional; all enrichment now resolves `AppFunction.RolePlaySummaryEnhancement` (deepseek-v4-flash).
2. **Change 2 — gate encounter-clear on Climax exit** — `RolePlayEngineService.cs` ~L4941: `else if (finalPhase != Climax && CurrentEncounterNumber != 0)` → `else if (priorPhase == Climax && finalPhase != Climax && CurrentEncounterNumber != 0)`. Intentionally NOT gated on `isMultiEncounterClimax` (see plan §7.5).
3. Build: Web ✅ 0 errors. Encounter/MultiEncounter tests: 84/84 pass. Web app restarted on http://localhost:5177.
4. Plan artifact updated with review notes: `specs/028-encounter-start-detection/plan-encounter-memory-enrichment-fix.md` (§7.5).

## Validated
- [ ] pending — implement plan, build, run Encounter/MultiEncounter tests, then re-test session memory capture.
- [ ] pending — runtime: play a session to encounter start → completion; confirm no duplicate `EncounterStartDetected`, EncounterCompletion `StartInteractionIndex` matches true start, and PhaseMilestone/EncounterCompletion `LlmSummary` populated (no `n_keep >= n_ctx`).

## Plan artifact
Full implementation plan persisted for further agent analysis at:
`specs/028-encounter-start-detection/plan-encounter-memory-enrichment-fix.md`
