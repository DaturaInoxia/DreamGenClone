# B-059: Semantic Encounter-Start + Memory Enrichment Contrast

**State**: `designed`
**Priority**: `high`
**Scope**: `medium`

---

## TL;DR

Two changes to make encounter memory vivid and start detection reliable:

1. **Semantic `encounter-started` detection** — Replace the keyword-only heuristic that detects encounter starts with LLM semantic inference (same engine as `encounter-completed`). Symmetric start/end detection. Universal — works in any phase, any scenario, no marker dependency.

2. **EncounterCompletion enrichment prompt rewrite** — The current prompt produces sterile third-person summaries with a `displayName` data bug. Rewrite for vivid first-person prose capturing who, what, orgasms, sensory detail, and emotional impact. Role-agnostic — works for Wife, Husband, OtherMan, Persona. The `HusbandAftermathInjector` contrast framing is separate and unchanged.

**Prerequisite fix**: Reset `CurrentEncounterStartInteractionIndex = 0` after encounter boundary so encounter #2+ start detection works correctly regardless of whether `AdvanceTime → None` is reached.

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

---

## Design

### Part A: Semantic `encounter-started` Detection

**Event ID**: `encounter-started`
**Pre-filter**: `HasSexualActivityContent()` keyword heuristic (keeps LLM calls cheap — same as current)
**LLM inference**: `ISemanticEventInferenceService.InferAsync(...)` — same engine as `encounter-completed`
**Gate**: No phase gate. No marker gate. Universal — fires in any phase for any scenario.
**Re-entry guard**: `CurrentEncounterNumber > 0 && CurrentTimeSkipPhase == None` — only detect start when NOT already in an active encounter.

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
- Set `CurrentEncounterNumber` (if 0)
- Set `CurrentEncounterStartInteractionIndex = session.Interactions.Count`
- Tag `interaction.WasEncounterStart = true`
- Write debug event `EncounterStartDetected`

**Theme mapping**: Add `encounter-started` to `RPThemeSemanticEventMappings` for all themes that have `encounter-completed`. Universal fallback: confidence ≥ 0.70 when no mapping exists.

**Flow**:
```
Interaction added
  → HasSexualActivityContent? (keyword pre-filter)
    → No → skip
    → Yes → CurrentEncounterNumber > 0 AND CurrentTimeSkipPhase == None?
      → Yes → already in encounter → skip
      → No → run semantic inference for encounter-started
        → Detected (conf ≥ threshold) → start new encounter
        → Not detected → skip (was just sexy talk)
```

### Part B: EncounterCompletion Enrichment Prompt Rewrite

**Role-agnostic**: Works for any character (Wife, Husband, OtherMan, Persona). The memory is pure encounter recall — what happened, who was there, what it felt like. Aftermath contrast framing lives in `HusbandAftermathInjector` (unchanged).

**Fix 1 — `displayName` data bug** (line 251):
```csharp
// Before (broken):
var displayName = !string.IsNullOrWhiteSpace(record.DetectionEvidence)
    ? record.DetectionEvidence
    : "(no detection evidence captured)";

// After:
var displayName = session.Characters
    .FirstOrDefault(c => c.Id == record.CharacterId)?.Name
    ?? record.CharacterId;
var detectionEvidenceLine = !string.IsNullOrWhiteSpace(record.DetectionEvidence)
    ? record.DetectionEvidence
    : "(no detection evidence captured)";
```

**Fix 2 — Rewrite prompt** (lines 257–275):

```
You are writing a vivid, first-person memory of a sexual encounter. The character
will recall this internally — what they saw, felt, tasted, heard, and experienced.

Character: {displayName}
Character role: {characterRole}
Encounter number: {encounterNumber} of {totalInArc} in this arc
Location: {sceneLocation}
Detection context: "{detectionEvidenceLine}"

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
```

**Expected output examples by role**:

*Wife (Becky), Husband (Ken) secretly watching:*
> I was on my knees in the bathroom and I took him all the way into my throat. I could feel him hit the back and I didn't gag — I wanted it. He came in my mouth and I swallowed every drop. I can still taste the salt on my tongue. I've never done that before. Ken was standing in the doorway the whole time and I didn't stop — I looked right at him while I did it.

*Husband (Ken), watching:*
> I stood in the hallway and watched my wife suck another man's cock. She was on her knees on the bathroom tile, taking him deep, and she never gagged — not once. I've never seen her like that. When he came in her mouth she swallowed and wiped her lips with the back of her hand and I didn't make a sound. I just stood there and watched and got hard.

*OtherMan (Dean):*
> She was on top of me on the couch, riding me slow at first and then faster, her head thrown back and her hands braced on my chest. I came inside her — I felt her squeeze around me as I finished and she just kept going, milking every last drop. She whispered my name when she came. The husband was in the next room watching TV and she didn't care at all.

### Part C: Reset `CurrentEncounterStartInteractionIndex` After Boundary

**File**: `RolePlayEngineService.cs`
**Location**: After line ~4867 (after `GenerateEncounterCompletionSummariesAsync` call in `TryDetectEncounterBoundaryAsync`)

```csharp
// After EncounterCompletion summary generation and the catch block:
state.CurrentEncounterStartInteractionIndex = 0;
```

This ensures the first-sexual-content guard (`if (CurrentEncounterStartInteractionIndex == 0)`) at line 2568 fires correctly for the next encounter, regardless of whether `AdvanceTime → None` is reached.

---

## Implementation Steps

### Step 1: Prerequisite — Reset Start Index After Boundary

| File | Change |
|------|--------|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | In `TryDetectEncounterBoundaryAsync()`, after the `EncounterCompletion` generation block (after the `catch` at line ~4867), add: `state.CurrentEncounterStartInteractionIndex = 0;` |

**1 line. Zero risk.** The three capture points (Climax entry, first-sexual-content, AdvanceTime→None) all unconditionally overwrite this value before it's read.

### Step 2: Semantic `encounter-started` Detection

| File | Change |
|------|--------|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Add `TryDetectEncounterStartAsync(RolePlaySession, RolePlayInteraction, AdaptiveScenarioState, CancellationToken)` method. Replace the keyword-only `CurrentEncounterNumber == 0` start detection block (lines ~2558–2573) with a call to the new method. Keep `HasSexualActivityContent()` only as pre-filter. |
| `DreamGenClone.Domain/RolePlay/RolePlayInteraction.cs` | Add `bool WasEncounterStart` property (or use existing `WasInSexScene` — evaluate during implementation). |
| `DreamGenClone.Domain/RolePlay/RPThemeSemanticEventMappings.cs` | Add `encounter-started` event ID to seed data / migration for all themes that have `encounter-completed`. Universal fallback handles themes without it. |

**Key design points for `TryDetectEncounterStartAsync`**:
- Same pattern as `TryDetectEncounterBoundaryAsync` — resolve theme, check for `encounter-started` mapping, build context window, call `_semanticEventInferenceService.InferAsync()`
- Re-entry guard: `if (state.CurrentEncounterNumber > 0 && state.CurrentTimeSkipPhase == TimeSkipPhase.None)` → return (already in encounter)
- If `CurrentEncounterNumber == 0`: set it to `state.GlobalEncounterCount + 1` (same as current flow, but only after LLM confirms sex is happening)
- Always set `CurrentEncounterStartInteractionIndex = session.Interactions.Count` on detection
- Universal fallback mapping: confidence ≥ 0.70 when no theme mapping exists
- Write `EncounterStartDetected` debug event on detection

### Step 3: EncounterCompletion Prompt Rewrite

| File | Change |
|------|--------|
| `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` | Fix `displayName` data bug at lines 251–253. Rewrite `BuildEncounterCompletionPrompt` return string (lines 257–275) with the new prompt. |

**~40 lines changed.** The interaction range (`StartInteractionIndex` → `EndInteractionIndex`) and `interactionsText` are already correctly populated — only the prompt text changes.

### Step 4: Theme Mapping Migration

| File | Change |
|------|--------|
| `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | Add `encounter-started` to `RPThemeSemanticEventMappings` for all themes that have `encounter-completed`. Insert with `ConfMin = 0.70, ConfMax = 1.0`. |
| Or: Code-level fallback | Universal default mapping (`ConfMin = 0.70`) when no `encounter-started` mapping exists in the theme. No DB migration needed. |

**Recommendation**: Code-level fallback — simpler, no migration, and the universal default is correct for all themes.

---

## Files Changed (Complete List)

| File | Change | Lines |
|------|--------|-------|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Add `state.CurrentEncounterStartInteractionIndex = 0;` after encounter boundary | 1 |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Add `TryDetectEncounterStartAsync()` method. Replace keyword-only start detection with semantic inference. | ~80 new, ~15 removed |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Add `encounter-started` to `ISemanticEventInferenceService.InferAsync()` call with proper event description | inline in new method |
| `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` | Fix `displayName` data bug | ~6 changed |
| `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` | Rewrite `BuildEncounterCompletionPrompt` return string | ~40 changed |

**5 changes in 2 files. No schema changes. No new files. No new dependencies.**

---

## Edge Cases

### Semantic encounter-started

| Case | Behavior |
|------|----------|
| Two encounters back-to-back (no non-sexual interaction between) | `CurrentEncounterNumber > 0` re-entry guard prevents false start during active encounter |
| Encounter-start in middle of narrative (not at turn start) | Detected on whichever interaction crosses the threshold |
| Encounter-start keyword match but LLM says no | LLM overrides — wasn't real physical contact |
| Encounter-start NOT keyword-matched but IS real sex | Keyword pre-filter misses it. Acceptable risk — the keyword list (`SexualActivityKeywords` + `SubtleSexualActivityKeywords`) is broad. If this becomes an issue, pre-filter can be relaxed. |
| First interaction of session is sexual | Detected normally — `CurrentEncounterNumber == 0`, start set correctly |
| Session has no `encounter-started` theme mapping | Universal fallback: confidence ≥ 0.70 |
| LLM inference fails (network error, timeout) | `try/catch` — log warning, write `EncounterStartDetectionFailed` debug event, fall back to keyword heuristic in catch block |

### Enrichment prompt

| Case | Behavior |
|------|----------|
| Single-interaction encounter (range = 1 interaction) | `interactionsText` has 1 entry. LLM can still produce memory from it. |
| Enrichment job not yet run (async latency) | `ActiveSummary` falls back to `TemplateSummary` (existing fallback chain unchanged). Vivid prose arrives on next aftermath cycle. |
| Character has no name in `session.Characters` | `displayName` falls back to `record.CharacterId` |
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
- ✅ `CurrentEncounterStartInteractionIndex` reset after boundary
- ✅ `displayName` data bug fix
- ✅ EncounterCompletion enrichment prompt rewrite (first-person, vivid, role-agnostic)
- ✅ `encounter-started` universal fallback (confidence ≥ 0.70)

### Deliberately Excluded
- ❌ Phase-specific logic — all detection is phase-agnostic
- ❌ Marker dependency — no `[ClimaxMode:multi-encounter]` or `[Aftermath:husband-contrast]` gates on start detection
- ❌ `HusbandAftermathInjector` changes — injector is unchanged, reads `ActiveSummary` as before
- ❌ DB migration for `encounter-started` theme mapping — code-level fallback handles it
