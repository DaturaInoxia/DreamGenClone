# B-059: Semantic Encounter-Start + Memory Enrichment Contrast

**State**: `designed` (post-analysis review complete)
**Priority**: `high`
**Scope**: `medium`

---

## TL;DR

**Four changes** to make encounter memory vivid and start detection reliable:

1. **Semantic `encounter-started` detection** — Replace the keyword-only heuristic with LLM semantic inference (same engine as `encounter-completed`). Symmetric start/end detection. Universal — works in any phase, any scenario, no marker dependency.

2. **EncounterCompletion enrichment prompt rewrite** — The current prompt produces sterile third-person summaries with a `displayName` data bug. Rewrite for vivid first-person prose capturing who, what, orgasms, sensory detail, and emotional impact. Role-agnostic — works for Wife, Husband, OtherMan, Persona. The `HusbandAftermathInjector` contrast framing is separate and unchanged.

3. **Prerequisite fix**: Reset `CurrentEncounterStartInteractionIndex = 0` after encounter boundary so encounter #2+ start detection works correctly regardless of whether `AdvanceTime → None` is reached.

4. **NEW: Gate Climax-entry start-index capture** — The Climax phase-entry capture at line 3708 unconditionally overwrites `CurrentEncounterStartInteractionIndex` with the Climax entry index. If an encounter already started in BuildUp (sex began before Climax), this clobbers the correct start. Gate it on `CurrentEncounterStartInteractionIndex == 0`.

### Analysis Findings Applied

This plan was reviewed against the live codebase. The following bugs/fixes were discovered and are incorporated below:

| # | Finding | Fix |
|---|---------|-----|
| 🔴 | Re-entry guard `CurrentEncounterNumber > 0 && CurrentTimeSkipPhase == None` is backwards — `CurrentEncounterNumber` is never reset to 0 by boundary detection, so encounter #2+ start would be blocked | Use `InteractionsInCurrentEncounter == 0` as the "not currently in encounter" signal |
| 🔴 | `session.Characters` does not exist — the proposed `displayName` lookup won't compile | `record.CharacterId` **is** the character name already; no lookup needed |
| 🔴 | Universal code-level fallback (conf ≥ 0.70) violates repo hard rules — `encounter-completed` requires a mapping and fails fast | Extend `EnsureEncounterCompletedMappingAsync` to also require `encounter-started`; fail fast; no code-level fallback |
| 🔴 | Climax-entry capture at line 3708 clobbers start index when encounter began in BuildUp | Gate on `CurrentEncounterStartInteractionIndex == 0` |
| 🟡 | `characterRole` referenced in prompt but not available on `EncounterSummaryRecord` | Resolve from `session.AdaptiveState.CharacterStats[record.CharacterId]?.CharacterRole` |
| 🟡 | `WasEncounterStart` field decision left ambiguous | Commit to new `bool WasEncounterStart` — `WasInSexScene` is semantically wrong (set on every sexual interaction) |

---

## Problem Diagnosis

### Problem 1: Encounter start detection is keyword-only (asymmetric)

| Detection | Current mechanism | Quality |
|-----------|-------------------|---------|
| Encounter **end** | LLM semantic inference for `encounter-completed` + keyword hard-gate | Reliable |
| Encounter **start** | Keyword heuristic only (`HasSexualActivityContent`: "cock", "pussy", "thrust", "wet", "finger", "bare", "flash", "undress" etc.) | Cannot distinguish "sexy conversation" from "actual sex" |

The keyword heuristic also gates on `CurrentEncounterNumber == 0` (first encounter only). For encounter #2+, start detection relies entirely on the `AdvanceTime → None` index reset at `RolePlayEngineService.cs:1593` — which only tells us an encounter COULD begin, not WHEN sexual activity actually resumed.

### Problem 2: EncounterCompletion enrichment is sterile

The current prompt at `EncounterSummaryJobHandler.BuildEncounterCompletionPrompt()` (line 257):

```
Write 2-3 concise sentences from {CharacterId}'s perspective describing what happened
during this encounter. Include: who was present, where it happened, what physical acts
occurred (flashing, hands, oral, intercourse), positions used, and orgasm details
(especially male orgasm details). Focus on sensory and emotional impact so the character
can recall this in their internal dialogue and actions. Base your summary on the
interactions above. Write in third person past tense.
```

Three deficiencies:

| # | Problem | Symptom |
|---|---------|---------|
| 1 | `Write in third person past tense` | Produces detached narration. The `HusbandAftermathInjector` then wraps this in "You just experienced: {sterile}... Act normal..." — the contrast falls flat. |
| 2 | `Detection evidence: {displayName}` where `displayName` is actually `record.DetectionEvidence` (raw text like "He held his beer loosely...") — bug at line 251 | Misleads the LLM about who the subject is. |
| 3 | No explicit instruction for physical recall, emotional peak, or sensory detail | Produces generic scene summaries like "Becky and Dean had an encounter in the living room" instead of "I can still feel him inside me — my thighs are wet, my pussy is still pulsing." |

### Problem 3: `CurrentEncounterStartInteractionIndex` not reset after boundary

After `TryDetectEncounterBoundaryAsync` completes, `CurrentEncounterNumber` is incremented and `InteractionsInCurrentEncounter` is reset to 0 — but `CurrentEncounterStartInteractionIndex` stays at the previous encounter's value. For encounter #2+, `AdvanceTime → None` (line 1593) resets it correctly, but if `AdvanceTime` is never reached (aftermath-only without multi-encounter), the index is stale.

### Problem 4 (NEW): Climax-entry capture clobbers correct start index

At `RolePlayEngineService.cs:3708`, the Climax phase-entry transition handler unconditionally sets:

```csharp
v2State.CurrentEncounterStartInteractionIndex = session.Interactions.Count;
session.AdaptiveState.CurrentEncounterStartInteractionIndex = session.Interactions.Count;
```

If sexual activity began in BuildUp (line 2568 set the index correctly to, say, 5), and Climax is entered at interaction 12, this **overwrites** the start index from 5 to 12. The `EncounterCompletion` record then has `StartInteractionIndex = 12`, causing the LLM enrichment prompt to miss the first 7 interactions of the encounter.

Sexual encounters can begin in any phase (BuildUp, Approaching, Committed, Climax) — not only Climax. The Climax-entry capture is only safe when no encounter has started yet.

### Problem 5 (NEW): Original plan's mapping strategy violates repo rules

The original plan proposed a code-level universal fallback (`ConfMin = 0.70`) for `encounter-started` when no theme mapping exists. This violates the repo's non-negotiable rules:

> Do not introduce hardcoded runtime defaults, guessed substitute values, or hidden backup branches.
> Any RP behavior control must be configurable in UI-backed persisted data, not hidden in code-only defaults.

The existing `encounter-completed` path requires an explicit mapping and fails fast via `EnsureEncounterCompletedMappingAsync` (throws `InvalidOperationException` on missing mapping). For consistency and rule compliance, `encounter-started` must follow the same pattern.

---

## Design

### Part A: Semantic `encounter-started` Detection

**Event ID**: `encounter-started`
**Pre-filter**: `HasSexualActivityContent()` keyword heuristic (keeps LLM calls cheap — same as current)
**LLM inference**: `ISemanticEventInferenceService.InferAsync(...)` — same engine as `encounter-completed`
**Gate**: No phase gate. No marker gate. Universal — fires in any phase for any scenario.
**Re-entry guard**: `InteractionsInCurrentEncounter == 0 && CurrentTimeSkipPhase == TimeSkipPhase.None` — only detect start when NOT already in an active encounter.

> **Why `InteractionsInCurrentEncounter` instead of `CurrentEncounterNumber`?**
> `CurrentEncounterNumber` is **never reset to 0** by boundary detection — it's incremented at line 4801. It only resets to 0 when leaving the Climax phase (line 4310). After `AdvanceTime → None`, `CurrentEncounterNumber` still equals 2 (or higher). Using `CurrentEncounterNumber > 0` as the "already in encounter" signal would **block encounter #2+ start detection entirely**.
>
> `InteractionsInCurrentEncounter` is reset to 0 at every boundary (line 4805), and incremented when an encounter is active (line 2590). It correctly signals "between encounters" for all phases.

**LLM prompt for `encounter-started`**:

```
A NEW sexual encounter has just begun in the most recent interaction. The characters have
crossed from tension/flirtation/suggestion into ACTUAL physical sexual activity — touching,
undressing, oral, intercourse, or any physical act with sexual intent. The mere mention of
sex, a sexy comment, or building tension is NOT enough — actual physical contact must have
occurred or be explicitly depicted as beginning right now. An encounter-start follows an
encounter-completed or follows a period of non-sexual interaction. Do NOT detect if the
characters were already in an active sexual encounter — only detect the moment of
transition from non-sexual to sexual activity.
```

**On detection**:
- Set `CurrentEncounterNumber` to `state.GlobalEncounterCount + 1` (if `== 0`)
- Set `CurrentEncounterStartInteractionIndex = session.Interactions.Count`
- Tag `interaction.WasEncounterStart = true` on the new property
- Write debug event `EncounterStartDetected`

**Theme mapping**: `encounter-started` must be explicitly configured in `RPThemeSemanticEventMappings` for any theme that uses it. No code-level universal fallback. Extend `EnsureEncounterCompletedMappingAsync` to also validate `encounter-started` exists — throw `InvalidOperationException` with explicit diagnostic when missing. Themes without `encounter-started` simply skip semantic detection and rely on the keyword heuristic as today.

**Flow**:
```
Interaction added
  → HasSexualActivityContent? (keyword pre-filter)
    → No → skip
    → Yes → InteractionsInCurrentEncounter == 0 AND CurrentTimeSkipPhase == None?
      → No → already in active encounter → skip
      → Yes → run semantic inference for encounter-started
        → Theme has encounter-started mapping? → use its ConfMin/ConfMax
        → No mapping → skip (keyword heuristic remains as fallback)
        → Detected (conf ≥ threshold) → start new encounter
        → Not detected → skip (was just sexy talk)
```

### Part B: EncounterCompletion Enrichment Prompt Rewrite

**Role-agnostic**: Works for any character (Wife, Husband, OtherMan, Persona). The memory is pure encounter recall — what happened, who was there, what it felt like. Aftermath contrast framing lives in `HusbandAftermathInjector` (unchanged).

**Fix 1 — `displayName` data bug** (line 251):

The current code assigns `record.DetectionEvidence` (raw text like "He held his beer loosely...") to a variable named `displayName`, then prints `Character: {displayName}` — misleading the LLM. `record.CharacterId` is already the character name (set from `CharacterSnapshots.CharacterId` at `EncounterSummaryService.cs:165`).

```csharp
// Before (broken):
var displayName = !string.IsNullOrWhiteSpace(record.DetectionEvidence)
    ? record.DetectionEvidence
    : "(no detection evidence captured)";

// After — just use CharacterId directly; it already IS the character name:
var displayName = record.CharacterId;
// No detectionEvidenceLine variable needed — the encounter interactions
// already provide full context (rewritten prompt drops the redundant "Detection context:" line).
```

**Fix 2 — `characterRole` resolution**:

The rewritten prompt includes `Character role: {characterRole}`. The role is stored in `CharacterStatProfileV2.CharacterRole`, accessible from `session.AdaptiveState.CharacterStats`. Add resolution at the top of `BuildEncounterCompletionPrompt`:

```csharp
var characterRole = session.AdaptiveState?.CharacterStats
    .TryGetValue(record.CharacterId, out var statBlock) == true
    ? statBlock.CharacterRole ?? "Unknown"
    : "Unknown";
```

**Fix 3 — Rewrite prompt** (lines 257–275):

The new prompt drops `Detection context:` entirely (the encounter interaction range already provides full context). Include `characterRole`. Write as a C# raw string literal.

```csharp
return $"""
    You are writing a vivid, first-person memory of a sexual encounter. The character
    will recall this internally — what they saw, felt, tasted, heard, and experienced.

    Character: {displayName}
    Character role: {characterRole}
    Encounter number: {encounterNumber} of {totalInArc} in this arc
    Location: {sceneLocation}

    The interactions that occurred during this encounter (in order):
    {interactionsText}

    Write 2-4 sentences in FIRST PERSON ("I...") from {displayName}'s perspective,
    describing what happened during this encounter. This is their private recollection —
    raw, honest, and sensory. Include ALL of the following:

    1. WHO — Name the other person or people involved. What role did {displayName} play?
       What did the other person do to them or with them?

    2. WHAT — What physical acts occurred? Be anatomically explicit. Kissing, touching,
       oral, fingers, intercourse, positions — whatever the interactions show. Describe
       what {displayName} did and what was done to them.

    3. ORGASMS — Who came? How many times? If male orgasm occurred, where did he finish
       (inside her, on her body, in her mouth, elsewhere)? If female orgasm occurred,
       what triggered it and how intense was it? Be explicit about the physical evidence —
       wetness, semen, taste, marks, soreness.

    4. SENSORY & EMOTIONAL — What did {displayName} feel physically right then? What
       sounds, smells, tastes, textures? What was the strongest emotion — desire, guilt,
       thrill, shame, love, power, submission, fear? Be specific. Use phrases like
       "I've never..." or "I couldn't believe I..." or "The way he..." if the
       interactions support it.

    Write in first person present-perfect or immediate past ("I just..." or "I can still
    feel..."). Do not write in third person. Do not summarize. Do not mention what happens
    next or what {displayName} has to do afterward. This is only the memory of the
    encounter itself — what happened, who was there, what it felt like.
    """;
```

**Expected output examples by role**:

*Wife (Becky), Husband (Ken) secretly watching:*
> I was on my knees in the bathroom and I took him all the way into my throat. I could feel him hit the back and I didn't gag — I wanted it. He came in my mouth and I swallowed every drop. I can still taste the salt on my tongue. I've never done that before. Ken was standing in the doorway the whole time and I didn't stop — I looked right at him while I did it.

*Husband (Ken), watching:*
> I stood in the hallway and watched my wife suck another man's cock. She was on her knees on the bathroom tile, taking him deep, and she never gagged — not once. I've never seen her like that. When he came in her mouth she swallowed and wiped her lips with the back of her hand and I didn't make a sound. I just stood there and watched and got hard.

*OtherMan (Dean):*
> She was on top of me on the couch, riding me slow at first and then faster, her head thrown back and her hands braced on my chest. I came inside her — I felt her squeeze around me as I finished and she just kept going, milking every last drop. She whispered my name when she came. The husband was in the next room watching TV and she didn't care at all.

### Part C: Gate Climax-Entry Capture on `CurrentEncounterStartInteractionIndex == 0`

**File**: `RolePlayEngineService.cs`
**Location**: Line ~3708 (in the phase transition handler, `if (lifecycle.TransitionEvent.ToPhase == NarrativePhase.Climax)` block)

**Bug**: When an encounter already started in BuildUp, Climax entry unconditionally overwrites `CurrentEncounterStartInteractionIndex` with the Climax-entry index.

```csharp
// Before (broken — clobbers correct start index when encounter began in BuildUp):
if (lifecycle.TransitionEvent.ToPhase == NarrativePhase.Climax)
{
    v2State.CurrentEncounterStartInteractionIndex = session.Interactions.Count;
    session.AdaptiveState.CurrentEncounterStartInteractionIndex = session.Interactions.Count;
    ...
}

// After:
if (lifecycle.TransitionEvent.ToPhase == NarrativePhase.Climax
    && v2State.CurrentEncounterStartInteractionIndex == 0)  // NEW guard
{
    v2State.CurrentEncounterStartInteractionIndex = session.Interactions.Count;
    session.AdaptiveState.CurrentEncounterStartInteractionIndex = session.Interactions.Count;
    ...
}
```

**Interaction with semantic start detection**: When sex has already started in BuildUp, `CurrentEncounterStartInteractionIndex` is non-zero → Climax-entry guard skips → semantic detection's value is preserved. When sex has NOT started yet (index == 0), Climax-entry acts as an optimistic pre-seed, which is harmless — semantic detection overwrites it with the accurate value when actual sex begins.

**1 line added to an existing `if` condition. Zero behavior change for Case A (no prior encounter). Bug fix for Case B (encounter active from BuildUp).**

### Part D: Reset `CurrentEncounterStartInteractionIndex` After Boundary

**File**: `RolePlayEngineService.cs`
**Location**: After line ~4867 (after `GenerateEncounterCompletionSummariesAsync` call in `TryDetectEncounterBoundaryAsync`)

```csharp
// After EncounterCompletion summary generation and the catch block:
state.CurrentEncounterStartInteractionIndex = 0;
```

This ensures the re-entry guard (`InteractionsInCurrentEncounter == 0 && CurrentTimeSkipPhase == None`) and the first-sexual-content guard (`CurrentEncounterStartInteractionIndex == 0`) fire correctly for the next encounter, regardless of whether `AdvanceTime → None` is reached.

---

## Implementation Steps

### Step 1: Prerequisite — Reset Start Index After Boundary

| File | Change |
|------|--------|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | In `TryDetectEncounterBoundaryAsync()`, after the `EncounterCompletion` generation block (after the `catch` at line ~4867), add: `state.CurrentEncounterStartInteractionIndex = 0;` |

**1 line. Zero risk.** The capture points all unconditionally overwrite this value before it's read.

### Step 2: Gate Climax-Entry Capture (NEW bug fix)

| File | Change |
|------|--------|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | At line ~3708, add `&& v2State.CurrentEncounterStartInteractionIndex == 0` to the `if (ToPhase == Climax)` condition. |

**1 line change to existing condition. Prevents clobbering the start index when an encounter began in BuildUp.**

### Step 3: Semantic `encounter-started` Detection

| File | Change |
|------|--------|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Add `TryDetectEncounterStartAsync(RolePlaySession, RolePlayInteraction, AdaptiveScenarioState, CancellationToken)` method. Replace the keyword-only `CurrentEncounterNumber == 0` start detection block (lines ~2558–2573) with a call to the new method. Keep `HasSexualActivityContent()` only as pre-filter. |
| `DreamGenClone.Domain/RolePlay/RolePlayInteraction.cs` | Add `bool WasEncounterStart` property (new field — NOT reusing `WasInSexScene`). |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Extend `EnsureEncounterCompletedMappingAsync` to also validate `encounter-started` mapping exists. |

**Key design points for `TryDetectEncounterStartAsync`**:
- Same pattern as `TryDetectEncounterBoundaryAsync` — resolve theme, check for `encounter-started` mapping, build context window, call `_semanticEventInferenceService.InferAsync()`
- Re-entry guard: `if (state.InteractionsInCurrentEncounter > 0 || state.CurrentTimeSkipPhase != TimeSkipPhase.None)` → return (already in encounter or pending transition)
- Mapping required: `EnsureEncounterCompletedMappingAsync` extended to also check `encounter-started`. No universal fallback — themes without the mapping simply skip semantic detection (keyword heuristic still runs as today)
- If `CurrentEncounterNumber == 0`: set it to `state.GlobalEncounterCount + 1` (only after LLM confirms sex)
- Always set `CurrentEncounterStartInteractionIndex = session.Interactions.Count` on detection
- Write `EncounterStartDetected` debug event on detection
- LLM failure (network/timeout): catch block → log warning → write `EncounterStartDetectionFailed` debug event → fall back to keyword heuristic (same as current behavior, but with corrected re-entry guard)

### Step 4: EncounterCompletion Prompt Rewrite

| File | Change |
|------|--------|
| `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` | Fix `displayName` data bug at line 251 (use `record.CharacterId`). Add `characterRole` resolution via `session.AdaptiveState.CharacterStats` lookup. Rewrite `BuildEncounterCompletionPrompt` return string (lines 257–275) with the new prompt. |

**~40 lines changed.** The interaction range (`StartInteractionIndex` → `EndInteractionIndex`) and `interactionsText` are already correctly populated — only the prompt text changes. Detection evidence line removed (interactions provide full context).

### Step 5: Theme Mapping — Fail Fast (changed from universal fallback)

| File | Change |
|------|--------|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Extend `EnsureEncounterCompletedMappingAsync` (currently validates `encounter-completed`) to also check for `encounter-started` mapping when the theme has `encounter-completed`. Throw `InvalidOperationException` with explicit diagnostic if `encounter-started` is missing. |

**No DB migration. No code-level fallback. Themes without `encounter-started` skip semantic detection.**

The diagnostic message pattern:
```
MissingEncounterStartMapping: theme '{theme.Id}' has 'encounter-completed' mapping but no
'encounter-started' mapping. Add an encounter-started mapping to the theme for symmetric
start/end semantic detection.
```

---

## Files Changed (Complete List)

| File | Change | Lines |
|------|--------|-------|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Add `state.CurrentEncounterStartInteractionIndex = 0;` after encounter boundary (Part D) | 1 |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Gate Climax-entry capture on `CurrentEncounterStartInteractionIndex == 0` (Part C — NEW bug fix) | 1 |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Add `TryDetectEncounterStartAsync()` method. Replace keyword-only start detection with semantic inference. Corrected re-entry guard uses `InteractionsInCurrentEncounter > 0`. | ~80 new, ~15 removed |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Extend `EnsureEncounterCompletedMappingAsync` to also validate `encounter-started`. No universal fallback. | ~15 changed |
| `DreamGenClone.Domain/RolePlay/RolePlayInteraction.cs` | Add `bool WasEncounterStart` property | 1 |
| `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` | Fix `displayName` data bug (use `record.CharacterId`). Add `characterRole` resolution from `CharacterStats`. | ~12 changed |
| `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` | Rewrite `BuildEncounterCompletionPrompt` return string. Remove `detectionEvidenceLine`. | ~40 changed |

**7 changes in 3 files. No schema changes (new property on existing class only). No new files. No new dependencies.**

---

## Edge Cases

### Semantic encounter-started

| Case | Behavior |
|------|----------|
| Two encounters back-to-back (no non-sexual interaction between) | `InteractionsInCurrentEncounter > 0` re-entry guard prevents false start during active encounter |
| Encounter-start in middle of narrative (not at turn start) | Detected on whichever interaction crosses the threshold |
| Encounter-start keyword match but LLM says no | LLM overrides — wasn't real physical contact |
| Encounter-start NOT keyword-matched but IS real sex | Keyword pre-filter misses it. Acceptable risk — the keyword list (`SexualActivityKeywords` + `SubtleSexualActivityKeywords`) is broad. If this becomes an issue, pre-filter can be relaxed. |
| First interaction of session is sexual | Detected normally — `CurrentEncounterNumber == 0`, `InteractionsInCurrentEncounter == 0`, start set correctly |
| Session has no `encounter-started` theme mapping | **No universal fallback.** Semantic detection skipped. Keyword heuristic still runs as today (same behavior as current code). |
| LLM inference fails (network error, timeout) | `try/catch` — log warning, write `EncounterStartDetectionFailed` debug event, fall back to keyword heuristic in catch block (with corrected re-entry guard) |
| Encounter began in BuildUp, Climax phase entered later | Climax-entry capture **skipped** (`CurrentEncounterStartInteractionIndex != 0`) — correct start index preserved |
| Encounter #2 after timeskip, no new sex yet | `AdvanceTime → None` resets start index to 0 (Part D) and `InteractionsInCurrentEncounter` to 0 (existing boundary logic). Correct re-entry guard allows semantic detection when sex resumes. |

### Enrichment prompt

| Case | Behavior |
|------|----------|
| Single-interaction encounter (range = 1 interaction) | `interactionsText` has 1 entry. LLM can still produce memory from it. |
| Enrichment job not yet run (async latency) | `ActiveSummary` falls back to `TemplateSummary` (existing fallback chain unchanged). Vivid prose arrives on next aftermath cycle. |
| Character has no role in `CharacterStats` | `characterRole` falls back to `"Unknown"` |
| LLM outputs first person ("I was on my knees...") | Correct — the prompt explicitly requests first person. `HusbandAftermathInjector` wraps it: "You just experienced: {I was on my knees...} Now return to your husband." The "I" in the memory becomes her internal recollection referenced in third-person framing. |
| Husband/OtherMan role — "I" is correct | Yes — the prompt is role-agnostic. "I stood in the hallway and watched..." (Husband) and "She was on top of me..." (OtherMan) are both valid first-person memories. |

---

## Integration

### How the pieces fit together

```
Interaction added
  │
  ├─► TryDetectEncounterStartAsync (NEW — semantic)
  │     → Sets CurrentEncounterNumber, CurrentEncounterStartInteractionIndex
  │
  ├─► TryDetectEncounterBoundaryAsync (UNCHANGED)
  │     → Detects encounter-completed
  │     → Writes EncounterCompletion with StartInteractionIndex → EndInteractionIndex
  │     → Resets CurrentEncounterStartInteractionIndex = 0 (NEW prerequisite fix)
  │
  ├─► EncounterSummaryJobHandler (CHANGED — prompt rewrite)
  │     → LLM enrichment with new vivid first-person prompt
  │     → Populates LlmSummary
  │
  └─► HusbandAftermathInjector (UNCHANGED)
        → Reads ActiveSummary (LlmSummary ?? TemplateSummary)
        → Injects: "You just experienced: {vivid memory}. Now return to your husband..."
```

### Separation of concerns

| Component | Responsibility |
|-----------|---------------|
| `TryDetectEncounterStartAsync` | Detect WHEN an encounter begins (LLM semantic) |
| `TryDetectEncounterBoundaryAsync` | Detect WHEN an encounter ends (LLM semantic) |
| `EncounterSummaryJobHandler` | Generate WHAT happened during the encounter (LLM enrichment — memory) |
| `HusbandAftermathInjector` | Frame the memory for the contrast narrative (static template — "You just experienced {memory}. Now return to your husband...") |

---

## Verification

### Build

```powershell
dotnet build DreamGenClone.Web --no-restore
dotnet build DreamGenClone.Tests --no-restore
```

Confirm 0 errors.

### Manual Smoke Test

1. Create session with exhibitionism theme + `[Aftermath:husband-contrast]` marker
2. Play through flirtation → verify NO `EncounterStartDetected` during sexy conversation
3. First actual sexual contact → verify `EncounterStartDetected` fires, `CurrentEncounterNumber = 1`, `CurrentEncounterStartInteractionIndex` set
4. Play through encounter → encounter-completed boundary detected → verify `EncounterCompletion` written with correct `StartInteractionIndex`/`EndInteractionIndex`
5. Aftermath fires → verify `HusbandAftermathInjector` renders vivid first-person memory
6. AdvanceTime → None → verify `CurrentEncounterStartInteractionIndex` reset correctly
7. Encounter #2 → verify `EncounterStartDetected` fires again
8. Verify `EncounterCompletion` for encounter #2 has correct interaction range

### DB Verification

```sql
-- Verify EncounterCompletion records exist with LlmSummary
SELECT EncounterNumber, CycleIndex, TemplateSummary, LlmSummary, 
       StartInteractionIndex, EndInteractionIndex
FROM RolePlayV2EncounterSummaries
WHERE SessionId = '<sessionId>' AND SummaryType = 'EncounterCompletion'
ORDER BY OccurredUtc;
```

Confirm `LlmSummary` contains first-person vivid prose with who, what, orgasms, sensory detail.

---

## Scope Boundaries

### In Scope
- ✅ Semantic `encounter-started` detection (LLM inference)
- ✅ `CurrentEncounterStartInteractionIndex` reset after boundary (Part D)
- ✅ Climax-entry capture gated on `CurrentEncounterStartInteractionIndex == 0` (Part C — new bug fix)
- ✅ `displayName` data bug fix
- ✅ `characterRole` resolution in enrichment prompt
- ✅ `WasEncounterStart` interaction property
- ✅ EncounterCompletion enrichment prompt rewrite (first-person, vivid, role-agnostic)
- ✅ `encounter-started` theme mapping required + fail fast (extends `EnsureEncounterCompletedMappingAsync`)

### Deliberately Excluded
- ❌ Phase-specific logic — all detection is phase-agnostic
- ❌ Marker dependency — no `[ClimaxMode:multi-encounter]` or `[Aftermath:husband-contrast]` gates on start detection
- ❌ `HusbandAftermathInjector` changes — injector is unchanged, reads `ActiveSummary` as before
- ❌ DB migration for `encounter-started` theme mapping — detection is opt-in via explicit theme mapping; no migration needed
- ❌ Code-level fallback for `encounter-started` mapping — violates repo hard rules; themes without the mapping skip semantic detection
- ❌ Removing the AdvanceTime→None start-index capture (line 1593) — kept as safety-net; semantic detection overwrites it when actual sex follows
