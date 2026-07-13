# Debug Findings — Session 5902dc74-5ed3-4968-8f18-1949f208f55e

## Summary

Session in Climax phase, exhibitionism scenario, 64 interactions, 3 completed encounters (1→2→3→4), encounter-start detection working, StartIdx fix confirmed for encounter 4. But several quality issues found.

---

## Finding 1: Multi-Encounter Time-Skip Injecting CloseScene + AdvanceTime Together

### Evidence
Dean at si=37 writes encounter ending + orgasm, then `---` separator, then jumps to "The next morning..." in the SAME response. The LLM executed both CloseScene (wrap up) and AdvanceTime (next morning) in one turn.

### Design Expectation (B-057 Phase 2)
The B-057 plan defines separate sequential phases: CloseScene → AftermathCoupleInteraction → AdvanceTime → None. They should NOT execute together in one turn batch.

### Root Cause
Investigation needed — the multi-encounter overflow injection code may have changed. The CloseScene directive and the AdvanceTime directive may be getting injected into the same prompt.

### Status
Open — needs code review of `RolePlayEngineService.cs` overflow injection block (~lines 1556-1573).

---

## Finding 2: Boundary-to-Aftermath Delay

### Evidence
- si=37: Dean BOUNDARY (encounter ended at 22:41:55)
- si=38-40: Next turn starts (Becky, Ken, Narrative)
- si=41: Instruction (CloseScene fires HERE, not at si=37)

The aftermath injection happens 3 interactions after the boundary, breaking the timeline between the encounter end and the wrap-up directive.

### Design Expectation
Per B-057, the aftermath should fire as `AftermathCoupleInteraction` phase immediately after CloseScene, not 3 interactions later.

### Status
Open — needs code review of overflow transition sequencing.

---

## Finding 3: Enrichment Prompts Not Logged

### Evidence
Only one enrichment event logged: `"Encounter summary LLM enhancement complete: ... charId=Ken"`. No other enrichment completions found in logs, and NO enrichment prompt content is logged anywhere.

The semantic inference prompts ARE logged with `--- RAW OUTPUT ---` / `--- SYSTEM ---` / `--- USER ---` sections, but the `EnhanceRecordAsync` method does not log the prompt sent for memory generation.

### Impact
Cannot debug POV confusion, reasoning leaks, or factuality errors in enrichment memories without seeing the actual prompt sent.

### Fix
Add prompt logging to `EncounterSummaryJobHandler.EnhanceRecordAsync` — log the prompt before calling `_completionClient.GenerateAsync`.

**File**: `EncounterSummaryJobHandler.cs`  
**Estimate**: 1 line — `_logger.LogInformation("Enrichment prompt ({Len} chars): {Prompt}", prompt.Length, prompt)`

---

## Finding 4: Memory Quality Degradation — Reasoning Leaks

### Evidence
- Dean Enc2: "We need to write 2-4 sentences in first person from Dean's perspective..."
- Ken Enc3: "I can't write this response. Throughout the entire narrative, Ken was asleep..."
- Ken Enc4: "Okay, this is a detailed creative writing request with very specific constraints..."

### Root Cause
DeepSeek model outputs `reasoning_content` that bleeds into the main response. No post-processing strips it.

### Impact
All 3 reasoning-contaminated memories are unusable — they show the LLM's internal deliberation instead of the memory.

### Fix
In `EnhanceRecordAsync`, detect and strip reasoning patterns before saving: lines starting with "Okay,", "Hmm,", "We need to", "The user", "I need to", etc.

---

## Finding 5: POV Confusion in Dean Memories

### Evidence
- Dean Enc3: "I can still feel the way his sweat tasted on my tongue... my jaw aching as I took him deep" — written from Becky's oral-sex perspective, NOT Dean's
- Dean Enc4: "I knelt between his thighs... my own cock throbbing" — confused POV mixing giver/receiver

### Root Cause
Cannot determine without seeing the enrichment prompt. Likely causes:
- Wrong interaction range feeding the prompt
- Model confusing Deans identity
- Prompt not clearly specifying whose POV

### Fix
Needs prompt inspection first (Finding 3).

## Finding 7: Enrichment Memory Prompt — Three-Path Character Handling

### Current Problem
`BuildEncounterCompletionPrompt` filters to character-only interactions (prevents POV confusion), but this produces impoverished memories:
- **Dean/Becky** (sexual participants): Memory is just their own actions without full context
- **Ken** (absent): Prompt asks for "vivid memory of a sexual encounter" but character was asleep — LLM fabricates
- **Ken-as-voyeur** (future themes): Would need a witnessing memory, but absent-only path would say "no memory"

### Three-Path Design

| Character's filtered interactions | Path | Prompt |
|------------------------------------|------|--------|
| Sexual activity present (oral, intercourse, etc.) | **Participating** | Current vivid first-person memory prompt — "I can still feel..." |
| Non-sexual but in scene (watching, nearby) | **Witnessing** | What they perceived through sight/sound/proximity — "I heard sounds through the wall..." or "I watched from the window..." |
| Absent (sleeping, walking, elsewhere) | **Absent** | Simple factual memory — "I was asleep when it happened. I didn't know." |

### Implementation

**File**: `EncounterSummaryJobHandler.cs` — `BuildEncounterCompletionPrompt`

1. Keep per-character interaction filtering (prevents POV confusion)
2. Check for sexual activity keywords in filtered interactions (already done)
3. Add presence check: was the character physically near the encounter? (check if any narrative/system interactions reference the character being in the location)
4. Three prompt branches:

```
Participating path (hasSexualContent = true):
  → Current vivid first-person memory prompt (existing)
  → "I can still feel..." sensory/emotional memory

Witnessing path (no sexual content, but character present in scene):
  → "You are writing a first-person memory for {CharacterId}. This character was present
     during the encounter but not directly participating in the sexual activity. Write their
     recollection of what they perceived — what they saw, heard, and felt being near the
     scene. Write in first person. Be specific about what they observed and their emotional
     reaction."

Absent path (no sexual content, character not in scene):
  → "Write a brief factual memory for {CharacterId}. They were not present during the
     sexual encounter and have zero awareness it occurred. Write ONLY what they experienced
     in the interactions below — their ordinary, mundane actions. Do NOT reference, imply,
     or hint at any sexual activity, other people's encounters, or anything unusual.
     The character simply lived through this time period doing whatever they were doing.
     Write in first person. Example: 'I fell asleep early that night, the ceiling fan
     rattling above me, and woke to sunlight through the blinds.'"}
```

### Presence Detection Logic

To determine if a character was "witnessing" vs "absent":
- Check if any Narrative/System interactions in the encounter range mention the character being in the location (e.g., "Ken lay on the bed" = present but not participating)
- Check character's own interactions for location clues (nearby, watching, hearing)
- If uncertain, default to "absent" path — safer than fabricating

### Files Changed
- `EncounterSummaryJobHandler.cs`: Modify `BuildEncounterCompletionPrompt` — add presence detection + witness prompt branch

---

## Finding 8: DeepSeek `reasoning_content` Fallback Returns Reasoning as Content

### Problem
`ParseContent` in `CompletionClient.cs` has a fallback: when `Content` is empty, it returns `ReasoningContent` as the content. DeepSeek flash models return reasoning chains in `reasoning_content` and sometimes empty `content`, causing the reasoning to be accepted as valid output.

### Fix
**File**: `CompletionClient.cs` — `ParseContent`
- When `Content` is empty and `ReasoningContent` is present, return empty string (not reasoning)
- This is correct behavior — callers that need reasoning use `GenerateWithReasoningAsync`
- Add logging when this happens so we can track it

### Alternative Fix (belt-and-suspenders)
**File**: `EncounterSummaryJobHandler.cs` — `EnhanceRecordAsync`
- Strip reasoning patterns from output before saving to DB
- Detect lines starting with: "Okay,", "Hmm,", "We need to", "The user", "I need to", "So, I need"
- Truncate to first 4 sentences after stripping

## Finding 6: POV Confusion — All Actors' Text in Per-Character Prompt

### Evidence
`BuildEncounterCompletionPrompt` selects interactions from the full session list, not filtered by character:
```csharp
var encounterInteractions = session.Interactions
    .Where(x => !x.IsExcluded)
    .Skip(record.StartInteractionIndex)
    .Take(rangeCount)
    .Select(x => $"[{x.InteractionType}] {x.ActorName}: {x.Content}")
    .ToList();
```
This means **Dean's memory prompt includes Becky's first-person interactions** like *"My fingers wrapped around him and I couldn't breathe"* and *"The stretch of him sinking into me stole every thought from my head."* The LLM reads Becky's POV text and gets confused about whose memory to write — resulting in Dean memories like "I knelt between his thighs" (from Becky's oral sex POV).

### Fix
In `BuildEncounterCompletionPrompt`, filter interactions to only include the target character's own lines when building `interactionsText`. Use the per-character text already available in the template generation path.

**File**: `EncounterSummaryJobHandler.cs` (line ~240)
