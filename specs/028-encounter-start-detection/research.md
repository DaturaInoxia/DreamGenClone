# Research: Semantic Encounter-Start Detection & Memory Enrichment

**Feature**: 028-encounter-start-detection  
**Date**: 2026-07-08

## 1. Semantic Inference Pattern Reuse

### Decision
Model `TryDetectEncounterStartAsync` on the existing `TryDetectEncounterBoundaryAsync` pattern at `RolePlayEngineService.cs:4736`.

### Rationale
The existing `encounter-completed` detection is proven in production. Its structure — pre-guards → context window → LLM inference → confidence filter → state mutation → persist — is directly applicable to `encounter-started`. Reusing this pattern minimizes risk and keeps the two detection paths symmetric.

### Key differences from encounter-completed
| Aspect | encounter-completed | encounter-started |
|--------|-------------------|-------------------|
| Theme mapping | Required (`SemanticEventMappings["encounter-completed"]`) | Universal — no mapping needed |
| Confidence threshold | From theme mapping (`ConfMin`/`ConfMax`) | Global configurable default (0.70) from appsettings |
| Phase gate | Returns on `Reset` | No phase gate — universal |
| Marker gate | Requires `isMulti` or `isAftermath` | No marker gate — universal |
| Re-entry guard | `CurrentTimeSkipPhase != None` | `InteractionsInCurrentEncounter > 0 \|\| CurrentTimeSkipPhase != None` |
| Keyword hard-gate | `ContainsEncounterCompletionKeywords` | Not applicable (pre-filter already did keyword check) |
| State mutation | Increment `CurrentEncounterNumber`, set `TimeSkipPhase` | Set `CurrentEncounterNumber` (if 0), set `CurrentEncounterStartInteractionIndex` |

### Alternatives considered
- **Brand-new detection flow**: Rejected — unnecessary complexity, would diverge from proven pattern.
- **Same method for both start/end**: Rejected — different guards, different state mutations, different event IDs.

---

## 2. Global Confidence Threshold Configuration

### Decision
Add a new `EncounterStartConfidenceThreshold` property to `RolePlayMemoryOptions` (default 0.70), configurable via `appsettings.json` under the existing `RolePlayMemory` section.

### Rationale
- `RolePlayMemoryOptions` already holds encounter-related configuration (max summaries to inject, LLM enhancement toggle).
- Adding to an existing options class avoids creating a new config section for a single value.
- The `IOptions<RolePlayMemoryOptions>` is already injected into `EncounterSummaryJobHandler` and accessible in `RolePlayEngineService`.
- Default 0.70 balances precision (filtering flirtation) with recall (catching real sexual activity).

### Configuration entry
```json
{
  "RolePlayMemory": {
    "EncounterStartConfidenceThreshold": 0.70
  }
}
```

### Alternatives considered
- **New `RolePlaySemanticOptions` class**: Rejected — over-engineered for a single threshold. If more semantic config accumulates, extract later.
- **Hardcoded constant**: Rejected — violates FR-007 (must be configurable) and repo "no hardcoded defaults" rule.
- **Reuse encounter-completed mapping thresholds**: Rejected by user — universal detection should not depend on per-theme mappings.

---

## 3. Re-Entry Guard Logic

### Decision
Use `InteractionsInCurrentEncounter > 0` as the "already in encounter" signal, NOT `CurrentEncounterNumber > 0`.

### Rationale
`CurrentEncounterNumber` is never reset to 0 by boundary detection — it's only reset when leaving the Climax phase (line ~4310). After `AdvanceTime → None`, `CurrentEncounterNumber` still equals the previous encounter's number. Using `CurrentEncounterNumber > 0` as the re-entry guard would block encounter #2+ start detection entirely.

`InteractionsInCurrentEncounter` is reset to 0 at every boundary (line ~4805) and incremented when an encounter is active (line ~2590). It correctly signals "between encounters" for all phases.

### Guard expression
```csharp
if (state.InteractionsInCurrentEncounter > 0 || state.CurrentTimeSkipPhase != TimeSkipPhase.None)
    return; // already in active encounter or pending transition
```

### Alternatives considered
- `CurrentEncounterNumber > 0`: Rejected — stale after boundary (never reset to 0 by boundary logic).
- `CurrentEncounterStartInteractionIndex > 0`: Rejected — reset to 0 by Part D fix; but semantically wrong — this tracks the start index, not whether we're in an encounter.

---

## 4. LLM Prompt for encounter-started

### Decision
Use a dedicated prompt instructing the LLM to detect the moment of transition from non-sexual to sexual activity.

### Prompt design
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

### Rationale
- Explicitly distinguishes "tension/flirtation" from "physical contact" — the core problem being solved.
- "Do NOT detect if already in an active encounter" — second layer of defense beyond the code re-entry guard.
- Same `AllowedEventIds = ["encounter-started"]` pattern as `encounter-completed`.

### Alternatives considered
- **Reuse encounter-completed prompt**: Rejected — different detection semantics (end vs start) require different instructions.

---

## 5. Encounter Completion Prompt Rewrite

### Decision
Rewrite `BuildEncounterCompletionPrompt` to produce first-person vivid prose with explicit requirements for who, what acts, orgasms, and sensory/emotional detail.

### Changes from current prompt
| Aspect | Current | New |
|--------|---------|-----|
| Perspective | Third person past tense | First person ("I...") |
| Structure | "2-3 concise sentences" | "2-4 sentences" covering 4 explicit requirements |
| Content | Generic "what happened" | WHO, WHAT (anatomically explicit), ORGASMS (where, how many), SENSORY & EMOTIONAL |
| Character name | Uses `detectionEvidence` as "displayName" (bug) | Uses `record.CharacterId` directly |
| Character role | Not included | Resolved from `CharacterStats` |
| Detection context line | Redundant (interactions already provide context) | Removed |

### Fix: `displayName` data bug
`record.CharacterId` is already the character name (set from `CharacterSnapshots.CharacterId` at `EncounterSummaryService.cs:165`). The current code assigns `record.DetectionEvidence` (raw text like "He held his beer loosely...") to `displayName` — misleading. Fix: use `record.CharacterId` directly.

### Fix: `characterRole` resolution
```csharp
var characterRole = session.AdaptiveState?.CharacterStats
    .TryGetValue(record.CharacterId, out var statBlock) == true
    ? statBlock.CharacterRole ?? "Unknown"
    : "Unknown";
```

### Alternatives considered
- **Keep third person, just add more detail**: Rejected — first person is the core value proposition for vivid aftermath contrast.
- **Role-specific prompt branches**: Rejected — FR-009 requires role-agnostic. The prompt works for any character.

---

## 6. Bug Fix: Climax-Entry Capture Guard

### Decision
Add `&& v2State.CurrentEncounterStartInteractionIndex == 0` to the Climax phase-entry condition at line ~3708.

### Rationale
When an encounter began in BuildUp (index set to, say, 5), Climax entry at interaction 12 unconditionally overwrites it to 12. The `EncounterCompletion` record then misses the first 7 interactions. The guard preserves the correct start index when an encounter is already active.

### Impact
- **Case A (no prior encounter)**: `CurrentEncounterStartInteractionIndex == 0` → guard passes → Climax-entry sets index as optimistic pre-seed. Semantic detection overwrites with accurate value when sex begins. No behavior change.
- **Case B (encounter active from BuildUp)**: `CurrentEncounterStartInteractionIndex != 0` → guard blocks → correct BuildUp index preserved. Bug fixed.

### Alternatives considered
- **Remove Climax-entry capture entirely**: Rejected — still useful as optimistic pre-seed for Case A.
- **Always prefer BuildUp capture**: Rejected — requires tracking which phase set the index, adding complexity.

---

## 7. Bug Fix: Start Index Reset After Boundary

### Decision
Add `state.CurrentEncounterStartInteractionIndex = 0;` after `GenerateEncounterCompletionSummariesAsync` completes in `TryDetectEncounterBoundaryAsync`.

### Rationale
After boundary processing, `CurrentEncounterNumber` is incremented and `InteractionsInCurrentEncounter` is reset — but `CurrentEncounterStartInteractionIndex` stays stale. For encounter #2+, if `AdvanceTime → None` is never reached, the index remains from encounter #1. This fix ensures the re-entry guard and first-sexual-content guard work correctly for every encounter.

### Impact
- Zero risk — all capture points unconditionally overwrite this value before it's read.
- One line of code.

### Alternatives considered
- **Reset only in AdvanceTime→None**: Rejected — doesn't cover the case where AdvanceTime is never reached.
- **Reset at encounter-start detection**: Rejected — would lose the index before boundary processing uses it.
