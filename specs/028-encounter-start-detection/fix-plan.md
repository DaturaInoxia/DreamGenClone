# Fix Plan: B-059 Debug Findings — Handoff

**Source**: `specs/028-encounter-start-detection/debug-findings.md`  
**Session**: `7d6c7ea9-24b0-40f2-841d-1943b01415b3`  
**Date**: 2026-07-08  
**Handoff For**: `/speckit.implement`

---

## Issue 1: `CurrentEncounterStartInteractionIndex` Not Persisting (v1 Sessions)

### Root Cause
For v1 sessions, the adaptive state payload doesn't include `CurrentEncounterStartInteractionIndex`. The field IS set in-memory on `AdaptiveScenarioState`, but each turn batch reloads state from the v1 `PayloadJson`, which lacks encounter tracking fields. This causes:
- Start index resets to 0 between turn batches
- EncounterCompletion records get `StartInteractionIndex=0` (entire session, not just encounter)
- Enrichment prompt receives 46 interactions instead of ~10

### Fix
In `RolePlayEngineService.cs`, add encounter tracking fields (`CurrentEncounterNumber`, `InteractionsInCurrentEncounter`, `CurrentEncounterStartInteractionIndex`) to the v1 payload adaptive state serialization. When hydrating v1 state, read these fields from the v1 payload if present, or from the v2 `RolePlayV2AdaptiveStates` table as fallback.

**Files**: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`  
**Estimate**: ~20 lines — add field serialization in the v1 state save path, add deserialization in the v1 state load path.

---

## Issue 2: `WasInSexScene` False Positives — "skin" Keyword

### Root Cause
Keyword `"skin"` in `SubtleSexualActivityKeywords` matches common non-sexual content like *"the warmth of her skin through the worn denim."*

### Fix
Remove `"skin"` from `SubtleSexualActivityKeywords` array. The word appears in romantic and atmospheric prose frequently; it's not a reliable sexual activity signal. Other subtle keywords like `"cleavage"`, `"curve"`, `"flash"` remain.

**Files**: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (line ~200)  
**Estimate**: 1 line — delete `"skin"` from the array.

---

## Issue 3: `RolePlaySummaryEnhancement` Model Not Configured

### Root Cause
`AppFunction.RolePlaySummaryEnhancement` has no model registered in the Model Manager. All `EncounterSummaryEnhancement` background jobs fail with:
```
ModelResolutionException: No model configured for function 'RolePlaySummaryEnhancement'
```

### Fix
In the Model Manager UI (`/model-manager`), register a model for the `RolePlaySummaryEnhancement` function. The `RolePlayMemoryOptions.SummaryEnhancementModelSlot` (default: `"roleplay-summary-enhancement"`) needs a model slot with a valid model assigned. Use the same model as `RolePlaySemanticAnalysis` if no dedicated slot is available.

**Files**: No code changes — UI configuration only.  
**Estimate**: UI action in Model Manager.

---

## Issue 4: Encounter Completion Memory Factually Wrong (Orgasm Hallucination)

### Root Cause
The enrichment prompt receives `StartIdx=0 EndIdx=46` (Issue 1), giving the LLM the entire session instead of just the encounter's interactions. The LLM conflates events from different encounters when reconstructing the memory. Additionally, the prompt says *"if male orgasm occurred, where did he finish"* but doesn't instruct the LLM to verify against the provided interactions.

### Fix
1. **Fix Issue 1 first** — correct interaction ranges will constrain the LLM to the actual encounter.
2. **Strengthen the prompt** — add instruction: *"Only describe what you can confirm from the interactions above. Do not invent details not present."* to the `BuildEncounterCompletionPrompt` in `EncounterSummaryJobHandler.cs`.

**Files**: `EncounterSummaryJobHandler.cs` (line ~230, `BuildEncounterCompletionPrompt`)  
**Estimate**: 1 line added to the prompt string.

---

## Issue 5: Memory Duplication and Corruption

### Root Cause
- **Dean duplication**: The LLM repeated its output verbatim (model behavior, not code)
- **Ken corruption**: The LLM leaked its chain-of-thought reasoning into the output — *"We need to continue from where the assistant left off..."*

Both are DeepSeek model behaviors. The corruption is a known DeepSeek issue where reasoning content leaks into the response. The duplication is a model hallucination/loop.

### Fix
In `EncounterSummaryJobHandler.EnhanceRecordAsync`, add post-processing:
1. **Truncate** to 2-4 sentences after receiving LLM output
2. **Strip meta-text** — detect and remove phrases like "We need to continue", "The user says", etc.
3. **Deduplicate** — detect repeated paragraphs and keep only the first

**Files**: `EncounterSummaryJobHandler.cs`  
**Estimate**: ~10 lines in `EnhanceRecordAsync` — sentence truncation + meta-text detection.

---

## Issue 6: Memory Not Concise (Per Spec: 2-4 Sentences)

### Root Cause
The prompt says "Write 2-4 sentences" but the DeepSeek model often produces 5-10 sentences. No post-generation enforcement of sentence count.

### Fix
Same as Issue 5: add sentence truncation in `EnhanceRecordAsync`. After receiving LLM output, split on sentence boundaries (`.`, `!`, `?`) and keep the first 2-4 sentences.

**Files**: `EncounterSummaryJobHandler.cs`  
**Estimate**: Same change as Issue 5.

---

## Issue 7: Memory Not Used in Aftermath Contrast

### Root Cause
The enrichment job is async — it may complete AFTER the next turn's prompt is built. The `husband-aftermath(p85)` injector reads from `ActiveSummary` (which falls back to `TemplateSummary` if `LlmSummary` is null). When the enrichment job hasn't completed yet, only the short template is available.

### Fix
**Acceptable** — this is a timing issue inherent to async enrichment. The vivid memory arrives on the NEXT aftermath cycle after the job completes. No code fix needed. If immediate injection is required, change the enrichment to synchronous (but this would block turn processing).

**Status**: Won't fix — by design.

---

## Implementation Order

| Step | Issue | Depends On |
|------|-------|-----------|
| 1 | **Issue 3** — Configure model | None |
| 2 | **Issue 1** — Fix v1 start-index persistence | None |
| 3 | **Issue 2** — Remove "skin" keyword | None |
| 4 | **Issue 4** — Strengthen prompt accuracy instruction | Issue 1, 3 |
| 5 | **Issue 5/6** — Post-process LLM output (truncate + dedupe + strip meta-text) | Issue 3 |

**Total estimated**: ~35 lines of code + 1 UI configuration change.

---

## Files Changed

| File | Changes | Lines |
|------|---------|-------|
| `RolePlayEngineService.cs` | v1 state serialization for encounter tracking fields | ~20 |
| `RolePlayEngineService.cs` | Remove `"skin"` from `SubtleSexualActivityKeywords` | 1 |
| `EncounterSummaryJobHandler.cs` | Add accuracy instruction to prompt | 1 |
| `EncounterSummaryJobHandler.cs` | Post-process output: truncate + dedupe + strip meta | ~10 |
| Model Manager (UI) | Register model for `RolePlaySummaryEnhancement` | 0 (UI) |

**No new files. No schema changes.**
