# Debug Findings: B-059 Encounter-Start Detection

**Session**: `7d6c7ea9-24b0-40f2-841d-1943b01415b3`  
**Date**: 2026-07-08  
**Status**: Open

---

## Finding 1: `WasInSexScene` False Positive via "skin" Keyword

### Interaction
`d7e5f477-b19c-4ef4-9efb-32dfee76caf4` — Ken's emotional/romantic response in a Campground Intimacy session.

### Content Excerpt
```
"I felt the weight of Becky's heels on my thigh, the warmth of her skin through the worn denim."
"I reached down and rested my hand on her ankle..."
```

### Cause
Keyword `"skin"` in `SubtleSexualActivityKeywords` matched `"the warmth of her skin through the worn denim"`.

**File**: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (line ~200)

### Impact
| Field | Value | Verdict |
|-------|-------|---------|
| `WasInSexScene` | `true` | ❌ False positive — romantic conversation, not sexual activity |
| `WasEncounterStart` | `null` | ✅ Correct — semantic inference didn't detect encounter start |
| `CurrentEncounterNumber` | `0` | ✅ Correct — no encounter started |
| `CurrentEncounterStartInteractionIndex` | `0` | ✅ Correct |

### Proposed Fix Options

| Option | Description | Risk |
|--------|-------------|------|
| A | Remove `"skin"` from `SubtleSexualActivityKeywords` | May miss subtle erotic writing that uses "skin" sexually |
| B | Gate `WasInSexScene` behind semantic inference (only set when LLM confirms sexual content) | Changes `WasInSexScene` semantics; increases LLM dependency |
| C | Leave as-is — `WasInSexScene` is documented as "keyword-based, intentionally broad" | False positives persist but `WasEncounterStart` is the authoritative field |

### Status
Pending user decision. Not yet resolved.

---

## Finding 2: Exhibitionism HARD CONSTRAINT Violated by LLM

### Interaction
`c22375fa-c715-47d7-993d-1b73661e304e` — Dean's POV at the beach, Becky goes topless on a public beach with strangers present.

### Content Excerpt
```
"A few other campers dotted the shore—an older couple reading paperbacks under an umbrella,
a young family splashing in the shallows"
"She pulled the shirt over her head in one smooth motion, and beneath it she wore nothing."
"The full weight of her breasts swung free, pale against the tan lines..."
```

### Prompt Context

The HARD CONSTRAINT was present in the prompt — injected twice:
1. In phase guidance section (char 59339 of 75679 — 79% through prompt)
2. At the very end with `— enforce in this response:` prefix:
   ```
   HARD CONSTRAINT — enforce in this response: The wife is an exhibitionist, not a nudist. 
   She exposes skin, flashes, positions herself to be glimpsed, and creates deliberate 
   sightlines — but she does not strip fully naked in public or open spaces where strangers 
   could see. Private or semi-private spaces with only the other man watching are the 
   exception. The exposure is in the controlled reveal, not in public nudity. It is not 
   about being watched in public, it is about purposely flashing another man.
   ```

### Violation Description
The HC explicitly says *"does not strip fully naked in public or open spaces where strangers could see"* and *"Private or semi-private spaces with only the other man watching are the exception."* The LLM produced output where Becky removes her top on a public beach with an older couple and a young family present — a direct violation.

The model's reasoning content acknowledged the constraint:
> *"The theme hard constraints say no pubic nudity, and the exposure is for the other man, hidden from the husband."*

...then subverted it:
> *"So, on the beach, she needs to expose her breasts fully..."*

### Root Cause
**Not a code issue** — the prompt infrastructure correctly injects the HC. The `deepseek-v4-pro` model acknowledged the constraint in its chain-of-thought reasoning and then produced output violating it. This is a **model compliance failure**.

### Impact
| Aspect | Detail |
|--------|--------|
| HC injector | ✅ Working — HC present with `— enforce` prefix |
| Prompt placement | ✅ Near end of prompt (last 500 chars) |
| Model reasoning | ⚠️ Acknowledged constraint, then ignored it |
| Output | ❌ Public topless scene with strangers present |

### Proposed Fix Options

| Option | Description | Risk |
|--------|-------------|------|
| A | Escalate to model provider — model ignoring explicit HCs | No immediate fix |
| B | Strengthen prompt language (e.g., "CRITICAL: Violating this constraint will break the character.") | May still be ignored |
| C | Add narrative validation layer that checks output against active HCs before accepting | Engineering cost |
| D | Re-roll the violating interaction | Temporary fix |

### Status
Pending user decision. Not yet resolved.

---

## Finding 3: Persona Location Contradiction — Ken Writes Becky's Dialogue While She's in Shed

### Interaction
`79036ebc-c862-4035-933d-3dfa16214bdf` — Ken's first-person POV in bed, writing Becky's goodnight dialogue while she's actually in the shed with Dean.

### Surrounding Context (same turn batch)

| # | Actor | Location | Content |
|---|-------|----------|---------|
| [24] | Becky | **Shed** | *"The darkness inside the shed was absolute—sawdust and old grease"* |
| [25] | Ken | **Trailer bed** | *"Becky settling back into bed... 'Couldn't sleep. Too hot. Just sat on the deck.'"* |
| [26] | Narrative | **Shed** | *"The maintenance shed held the darkness like a held breath"* |
| [27] | Dean | Shed | *"The heat of her mouth drew a raw groan from Dean's throat"* |

### Description
Ken (persona/player character) writes Becky's dialogue and actions in first person, placing her in the trailer bed claiming she "sat on the deck." But Becky's actual location and actions in the same turn batch are in the shed with Dean. The persona system allows the LLM to invent dialogue for other characters when writing from Ken's perspective, but this dialogue is unconstrained by actual character state — creating a timeline contradiction where Becky is in two places simultaneously.

### Root Cause
The persona prompt instructs Ken to write from first-person perspective and refer to others in third person, implicitly allowing writing other characters' dialogue. No explicit instruction prevents Ken from writing Becky's lines. No mechanism validates Ken's written dialogue against Becky's actual character state/location. Ken's prompt also leaks `BEHAVIORAL CONSTRAINT — Becky's perspective on Ken` data into Ken's perspective.

### Prompt Evidence
- `Write the next interaction as Ken in FIRST PERSON. Use "I" throughout. Include Ken's dialogue, actions, physical sensations, and internal thoughts. Refer to all other characters by name in third person.`
- `Continue from your character's perspective — only what they can see, hear, smell, or otherwise perceive.`
- No explicit "Ken is currently at: bedroom" location state
- Ken's prompt includes Becky-specific behavioral constraints

### Proposed Fix Options

| Option | Description | Risk |
|--------|-------------|------|
| A | Add per-character location state to persona prompts (e.g., "You are in: bedroom. Becky is in: shed.") | Requires location tracking infra |
| B | Instruct persona not to write other characters' dialogue or actions | Changes persona behavior significantly |
| C | Add "Ken does not see Becky return" constraint to prevent bed scene | May limit narrative possibilities |
| D | Gate — prevent persona from writing NPC dialogue when NPC is in a different scene/location | Complex engineering |

### Status
Pending user decision. Not yet resolved.

## Resolution Log

| Date | Finding | Decision | Implemented? |
|------|---------|----------|-------------|
| 2026-07-08 | Finding 1 — "skin" false positive | Awaiting user decision (Option A/B/C) | No |
| 2026-07-08 | Finding 2 — HC violation by LLM | Awaiting user decision (Option A/B/C/D) | No |
| 2026-07-08 | Finding 3 — Persona location contradiction | Awaiting user decision (Option A/B/C/D) | No |
