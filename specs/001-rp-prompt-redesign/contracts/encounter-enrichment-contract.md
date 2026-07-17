# Contract: Encounter Enrichment

**Branch**: `001-rp-prompt-redesign`

Defines the input/output contract for the rewritten encounter summary enrichment prompt in `EncounterSummaryJobHandler`. Implements FR-033, FR-035, S-027.

---

## Input

The enrichment LLM receives:

1. **Narrative response text** (primary source, FR-035) — the omniscient 3rd-person synthesis of the encounter, already produced by the Narrative variant of the prompt builder. Contains physical scene, positions, sensations, atmosphere.
2. **Character responses** for the encounter — per-character 1st-person responses providing emotional/POV detail.
3. **Context metadata**:
   - Character name (whose perspective this memory is from)
   - Encounter number (1-based, within session)
   - Scene location
   - Previous encounter summaries (for comparison anchors — FR-033 dimension 6)

---

## Output

A 3-5 sentence first-person memory from the character's perspective, stored in `EncounterSummaryRecord.LlmSummary`. MUST capture 6 dimensions (FR-033):

| # | Dimension | Description |
|---|-----------|-------------|
| 1 | What happened | Key physical and emotional beats of the encounter (plot) |
| 2 | What they felt | Dominant emotional texture (guilt, thrill, shame, desire, satisfaction) |
| 3 | What they learned | Sexual self-knowledge gained (what felt good, what surprised them, what they want again) |
| 4 | What changed | How this encounter shifted the relationship dynamic or self-image |
| 5 | What risk was taken | Near-miss, discovery risk, or boundary crossed |
| 6 | What the other character now knows | What the other character now knows/suspects |

**Validation** (SC-009): At least 4 of 6 dimensions must be present in the output. The enrichment prompt explicitly requests all 6.

---

## Enrichment Prompt Template

```
You are writing a sexual encounter memory for {CharacterName} in an ongoing role-play.

Encounter {EncounterNumber} at {SceneLocation}.

Narrative account (omniscient):
{NarrativeResponseText}

{CharacterName}'s responses during this encounter:
{CharacterResponseTexts}

{PreviousEncounterContextIfAny}

Write a 3-5 sentence first-person memory from {CharacterName}'s perspective that captures:
1. What happened — the key physical and emotional beats of this encounter
2. What they felt — the dominant emotional texture (guilt, thrill, shame, desire, satisfaction)
3. What they learned — any sexual self-knowledge gained (what felt good, what surprised them, what they want again)
4. What changed — how this encounter shifted the relationship dynamic or their self-image
5. What risk was taken — any near-miss, discovery risk, or boundary crossed
6. Sexual comparison — if this is not the first encounter, how it compared to previous ones (more confident? more guilty? more physically intense?)

Write in {CharacterName}'s voice. Be specific and sensory. This memory will be injected into future prompts to maintain continuity across encounters.
```

---

## Persistence

- **Table**: `RolePlayV2EncounterSummaries` (existing — no schema migration).
- **Field**: `LlmSummary` (existing `TEXT NULL` column).
- **Status field**: `LlmEnhancedUtc` set to `DateTime.UtcNow` on write.
- **Fallback**: `ActiveSummary` property returns `LlmSummary ?? TemplateSummary` — but this is a read-time convenience for consumers, NOT a prompt-builder fallback. The prompt builder uses `LlmSummary` directly when present; if null, the encounter summary is omitted from Slot 10 (SessionMemory) rather than falling back to template text.

---

## Encounter Detection (FR-034)

Secondary signals evaluated in `TryDetectEncounterBoundaryAsync`:

1. **Scene change after intimacy** — `CurrentSceneLocation` changes within N turns of `WasInSexScene=true` interactions.
2. **Significant time passage** — narrative response contains time-skip markers ("later that evening", "the next morning", "after a while") following sexual activity.
3. **Explicit encounter boundary language** — narrative contains "when it was over", "after they dressed", "once they had separated".
4. **Phase transition Climax → Reset** — always fires encounter summary write.

Each signal logs at Debug when evaluated, Information when fired. Every detection writes `RolePlayDebugEventRecord` with `EventKind="EncounterBoundaryDetected"` and `MetadataJson.Signal` indicating which signal fired.
