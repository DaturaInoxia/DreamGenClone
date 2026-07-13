# Plan: Enrichment Memory Prompt Quality

**TL;DR**: Replaced fragile keyword-based `hasSexualContent` branching with a single unified prompt. Zero interactions → skip. Pacing directive restoration applied (EscalationInjector + SceneTimeDirectionInjector).

---

## Decisions Made

| Decision | Choice | Rationale |
|---|---|---|
| **Sexual content detection** | No keyword matching | Fragile — false positives (`"skin"`, `"wet"`, `"hard"`), false negatives (novel terms), maintenance burden |
| **Prompt branching** | None — single unified prompt | LLM can determine from interactions whether content is sexual. "If sexual, describe WHO/WHAT/ORGASMS/SENSORY; if not, describe ordinary experience." |
| **Zero interactions** | Skip entirely — no memory generated | Already implemented. If a character has no interactions in the encounter range, there's nothing to summarize. |
| **Non-sexual memory** | Interactions-driven | Let Ken's actual interactions determine if he's suspicious, oblivious, or observing — not hardcoded "elsewhere/sleeping" assumptions |
| **Reasoning leakage fix** | Model config change (max tokens) + model switch | Already done outside these changes. Post-processing helpers removed as they were applied prematurely. |

---

## A: Enrichment Memory Prompt — Unified (✅ Implemented)

`BuildEncounterCompletionPrompt` now:
- Returns `null` when `encounterInteractions.Count == 0` (skip, no memory)
- Single unified prompt — no keyword detection, no branching

### Final Prompt (Live)

```
Write a first-person memory for {CharacterId}.
Describe what they actually experienced, saw, heard, felt, noticed.

If interactions involve sexual activity, be explicit and vivid:
- WHO was involved and their roles
- WHAT physical acts occurred
- ORGASMS — who came, how many, physical evidence
- SENSORY & EMOTIONAL details

If nothing notable occurred, describe their ordinary experience.
For unusual observations (sounds, absences, changes in others),
include those naturally.

Character: {CharacterId}
Character role: {characterRole}
Encounter number: {record.EncounterNumber} of {totalInArc} in this arc
Location: {record.SceneLocation}

The interactions involving {CharacterId} during this encounter (in order):
{interactionsText}

Write 2-4 sentences in FIRST PERSON ("I...").
Base the memory ONLY on the interactions above.
```

Non-sexual instructions listed first (reduces priming bias).

### Removed

- `SexualActivityKeywords` array (stays in `RolePlayEngineService.cs` for encounter detection)
- `SubtleSexualActivityKeywords` array (same)
- `hasSexualContent` keyword check variable
- `if (!hasSexualContent)` / `else` branching
- The old "vivid sexual memory" prompt with numbered WHO/WHAT/ORGASMS/SENSORY breakdown

### Risk Accepted

- **Priming bias**: sexual instructions come after "describe what they experienced" — lower risk
- **DeepSeek conditional compliance**: minor risk of hybrid responses but no keyword false positives
- **Fallback**: if Ken hallucinates, revisit with encounter-detection-based gating (see Future Consideration)

### Verification

1. ✅ Build passes (0 errors)
2. ☐ Run debug session 2252c0bc — check Ken's non-sexual memory
3. ☐ Run debug session 2252c0bc — check Dean's sexual memory has WHO/WHAT/ORGASMS/SENSORY

---

## B: Pacing Directive Restoration (Escalation + SceneTime)

### Current State (already applied)

**EscalationInjector.cs** — all three pacing branches rewritten:

| Pacing | Old (broken) | New |
|--------|-------------|-----|
| **Slow** | "Advance within same beat — deepen, do not leap. Do not describe a new beat." | "Cover exactly one beat — richly detailed. Advance to new beat next response. Do not repeat." |
| **Medium** | "Avoid repeating only hesitant or reset beats." | "Each response should advance to new beats — do not repeat previous beats." |
| **Fast** | "Pack maximum density into this moment. Expand each beat." | "Move through full arc — initiation, act, climax, conclusion — within this and next response. Do not linger. Brief and urgent." |

**SceneTimeDirectionInjector.cs** — Slow (!hasTimeShift), Slow (hasTimeShift), Fast (hasTimeShift), Medium (hasTimeShift) rewritten:

| Section | Old (broken) | New |
|---------|-------------|-----|
| Slow (!hasTimeShift) | "Stay in current moment. Do not skip forward." | "Cover one beat per response. Advance to new beat each response. Do not repeat." |
| Slow (hasTimeShift) | "Stay in this moment. Do not jump forward in time." | "Cover one beat per response. Move to new beat each response. Keep advancing." |
| Fast (hasTimeShift) | "Stay in this moment and exhaust it. Expand each beat." | "Cover full arc rapidly. Do not linger. Compress into efficient urgent prose." |
| Medium (hasTimeShift) | "Do not skip forward." | "Advance to new beats each response." |

**Design principle**: Every turn advances to new beats. Pacing controls beats-per-turn and detail-per-beat, not whether to advance.

| Pacing | Beats/Turn | Detail/Beat | Encounter Duration |
|--------|-----------|-------------|-------------------|
| Slow | 1 | Rich, deep | ~8-9 turns |
| Medium | 1-2 | Moderate | ~4-6 turns |
| Fast | Full arc (3+) | Minimal | ~2-3 turns |

Unchanged: `SceneTimeDirectionInjector` Fast `!hasTimeShift` and Medium `!hasTimeShift` (already correct). `FinalDirectiveInjector` Fast HC (already correct).

Reference: `specs/028-encounter-start-detection/pacing-directive-changes.md`

### Verification

1. Build passes
2. Run a Fast session — encounter should complete in 2-3 turns ("quickie" style)
3. Run a Slow session — no repetition, each turn advances
4. Run a Medium session — 1-2 beats, forward momentum

---

## Files

| File | Changes |
|------|---------|
| `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` | Unified prompt (remove keyword branching, remove arrays, single prompt) |
| `DreamGenClone.Web/Application/RolePlay/Injectors/EscalationInjector.cs` | Already changed (3 pacing branches) |
| `DreamGenClone.Web/Application/RolePlay/Injectors/SceneTimeDirectionInjector.cs` | Already changed (4 sections) |

---

## Future Consideration: Deterministic Gating

If the unified prompt causes Ken to hallucinate sexual encounters (the original pain point), revisit with **encounter-detection-based gating**:

- Check if the encounter was detected as sexual via `WasEncounterStart` on interactions in range
- Use the semantic encounter detector result (already proven accurate) instead of keywords
- This preserves the vivid WHO/WHAT/ORGASMS/SENSORY prompt for non-participating characters without keyword fragility

Not implemented yet — only needed if the unified prompt approach fails.
