# 017 — Writing Terminology + Configurable Final Instruction Plan

**Created:** 2026-07-19
**Last updated:** 2026-07-19 (analysis fixes applied)
**Status:** Planned

---

## 1. Overview

Three related problems:
1. The app's writing-related terminology is confused — table names, model names, and prompt labels don't align with writer-standard terms
2. Some `ToneProfiles` (Intensity) contain prose-style language; one profile (`Atmospheric`) is entirely miscategorized
3. The Final Writing Instruction (Slot 17) is hardcoded with no POV awareness, no component breakdown, no configuration

---

## 2. Research & Architectural Decision

### Evidence

Models exhibit **U-shaped attention** (Liu et al. 2023, "Lost in the Middle"): strong at start AND end, weak in middle. Recency is real but not absolute — primacy also matters.

| Source | Finding |
|--------|---------|
| Anthropic (Claude) | System prompt (start) sets behavioral frame; user message (end) sets task. Both matter. |
| OpenAI (GPT-4) | System-level context at beginning; task-level direction at end. Both are effective. |
| Meta (Llama 3) | Instruction tuning emphasizes both system and user turns. |
| **This project** | Phase Guidance was ignored in Slot 12 (Zone C middle). Moved to Slot 17 (end) with imperative phrasing ("incorporate into this response") — immediately followed. Compliance improvement may be from position, imperative phrasing, or both. |

### Risk: Instruction Saturation

Consolidating 9 components into Slot 17 creates a ~1,500+ char instruction block. Research on instruction-following (Wei et al. 2022) shows too many instructions at once → dilution. Each additional directive slightly reduces compliance with all others.

**Mitigation:** Do NOT duplicate content between Slots 8/15 and 17. If Slot 17 has the authoritative Prose Style, Slot 8 must not emit Prose Style at all — it becomes purely contextual (character data, scenario context). Same for Slot 15 and Heat contract.

### Decision

**All writing direction consolidates into Slot 17 at the end of the prompt.** Slots 8 and 15 stop emitting writing direction entirely — they become purely contextual/structural. Slot 17 is the sole authoritative writing instruction. No duplication.

---

## 3. Data Model Changes (Foundation — Must Come First)

### 3.1 Scenario `NarrativeSettings`

Current `NarrativeTone` contains 5 concepts in one string:
```
Erotic, conversational, playful, and focused on physical pleasure. First-person limited perspective with fast pacing and low to moderate language complexity.
```

**New fields:**

```csharp
public class NarrativeSettings
{
    public string? Tone { get; set; }        // "Erotic, conversational, playful"
    public string? Register { get; set; }     // "Low to moderate language complexity"
    public string? Focus { get; set; }        // "Physical pleasure"
    public string? ProseStyle { get; set; }   // Unchanged
    public string? NarrativeTone { get; set; } // DEPRECATED — kept for backward compat
    public List<string> NarrativeGuidelines { get; set; } = [];
}
```

- POV and pacing removed from narrative text (handled elsewhere)
- `NarrativeTone` deprecated but retained; new reads prefer `Tone`/`Register`/`Focus`

### 3.2 Style Profile `SteeringProfile`

New fields for instruction directives:

```csharp
public class SteeringProfile
{
    // Existing fields unchanged
    public string? ImmersionDirective { get; set; }  // "Stay inside this character's perceptions..."
    public string? ActionDirective { get; set; }      // "Respond to the scene naturally."
    public int WordTargetMin { get; set; } = 200;
    public int WordTargetMax { get; set; } = 400;
}
```

### 3.3 Intensity Profile `IntensityProfile`

No changes needed. Existing fields are correct.

### 3.4 DB Migration

**`StyleProfiles` table:**
```sql
ALTER TABLE StyleProfiles ADD COLUMN ImmersionDirective TEXT NULL;
ALTER TABLE StyleProfiles ADD COLUMN ActionDirective TEXT NULL;
ALTER TABLE StyleProfiles ADD COLUMN WordTargetMin INTEGER DEFAULT 200;
ALTER TABLE StyleProfiles ADD COLUMN WordTargetMax INTEGER DEFAULT 400;
```

**`Scenarios` table:** No schema migration — new fields live in PayloadJson.

---

## 4. Profile Content Analysis & Cleanup

### 4.1 Current State

Six `ToneProfiles` exist. Analysis of their Description content:

**✅ Correctly categorized (pure Heat Level):**
- Hardcore, Erotic, Suggestive

**⚠️ Mixed content (Heat Level + Prose Style language):**
- Sensual: contains "mature, evocative language" and "deliberate pacing"
- Emotional: contains "meaningful dialogue", "conversation", "emotional disclosure"

**❌ Miscategorized (should be Style Profile):**
- Atmospheric: entirely prose craft — "environmental details, lighting, atmosphere, slow pacing, rich descriptive language"

### 4.2 Overlap: Atmospheric ↔ Sultry

Sultry Style Profile: "Atmospheric settings that hint at danger..."
Atmospheric Intensity Profile: "Prioritize environmental details, lighting, sounds, and atmosphere..."

Same concept in two systems.

### 4.3 Cleanup Actions

| Profile | Action | How |
|---------|--------|-----|
| Atmospheric | Move to StyleProfiles | Create new StyleProfile "Atmospheric" with its Description as the style text |
| Sensual | Clean description | Strip "mature, evocative language" / "deliberate pacing" |
| Emotional | Clean description | Strip "meaningful dialogue" / "conversation" / "emotional disclosure" |
| Hardcore, Erotic, Suggestive | Keep | Correctly categorized |

---

## 5. Terminology Mapping (Prompt Labels Only)

No code renames. Only prompt-facing text changes.

| App Implementation | Old Prompt Label | New Prompt Label | Writer's Term |
|-------------------|-----------------|------------------|---------------|
| `StyleProfiles` / `SteeringProfile` | `Writing Style:` | `Prose Style:` | Prose Style |
| `ToneProfiles` / `IntensityProfile` | `Intensity level:` | `Heat Level:` | Heat Level |
| `Scenario.Narrative.NarrativeTone` | *(buried in "Style Hint")* | `Tone:` | Tone |
| `PhaseRuleOfThumb` | `Phase Rule of Thumb:` | `Scene Direction:` | Scene Direction (what this phase should accomplish) |
| `StyleProfiles.RuleOfThumb` | `Profile Default:` | `Voice:` | Voice (authorial voice — the narrator's timeless style baseline) |

---

## 6. Slot Changes

### 6.1 Slot 8 (`WritingStyleSlot`) — No Writing Direction

**After change:** Slot 8 stops emitting writing direction entirely. It becomes purely contextual — character data and scenario context live in Zone B. Writing direction (Prose Style, Tone, Voice, Scene Direction) moves exclusively to Slot 17.

**Slot 8 emits nothing** (or is removed from the pipeline if the spec is amended to allow 16 slots). If kept for backward compat, it emits a single reference line: `"Writing direction: see Writing Instruction below."`

This avoids duplication and instruction saturation.

### 6.2 Slot 15 (`IntensityPacingSlot`) — No Writing Direction

**After change:** Slot 15 stops emitting Heat contract text. It retains only structural elements (available positions list) if any. Heat Level label + contract moves exclusively to Slot 17.

If kept, it emits only: `"Available positions: [list]"` — the positions are structural data, not writing direction.

### 6.3 Slot 17 (`FinalInstructionSlot`) — Authoritative Writing Instruction

This is the primary change. Slot 17 now consolidates ALL writing direction.

#### Character variant:

```
Writing Instruction:
  Prose Style: Sultry — Evocative and moody, layered with ambiguity, seductive undertones, lush descriptions, slow-burn tension. Atmospheric settings that hint at danger...
  Voice: Favor atmosphere, tension, and sensory detail over speed. Let desire accumulate before anything explicit happens.
  Tone: Erotic, conversational, playful, and focused on physical pleasure. Low to moderate language complexity.
  Heat Level: Hardcore — Pure explicit hardcore erotica. Include graphic, detailed descriptions of sexual acts, penetration, oral sex, orgasms, cum shots, and all physical interactions...
  Pacing: Medium pace — advance the scene naturally, not rushed, not stalled. Let moments breathe without dragging.
  POV: Write in first-person from {actorName}'s point of view.
  Show, don't tell. Stay inside this character's perceptions, thoughts, feelings, and physical sensations.
  Target 200-400 words.
  Respond to the scene naturally.
```

Components and sources:

| Order | Component | Source | Fail-fast? |
|-------|-----------|--------|------------|
| 1 | Prose Style name + description | `SteeringProfile.Name` + `.Description` | Fail-fast if profile missing |
| 2 | Voice | `SteeringProfile.RuleOfThumb` | Fail-fast if missing (FR-014) |
| 3 | Tone + Register | `Scenario.Narrative.Tone` + `.Register`; fallback to deprecated `.NarrativeTone` | Silent omit if all empty |
| 4 | Heat Level label + contract | `IntensityProfile` via `ResolvedIntensityData` | Fail-fast if profile missing |
| 5 | Pacing | `SceneDirection` via `ResolvedIntensityData` | Always present (has defaults) |
| 6 | POV directive | `character.perspectiveMode` (FirstPerson/ThirdPerson) | Always present |
| 7 | Immersion directive | `SteeringProfile.ImmersionDirective` | Fail-fast if missing (Hard Rule: no hardcoded fallbacks) |
| 8 | Word target | `SteeringProfile.WordTargetMin` / `WordTargetMax` | Fail-fast if missing (Hard Rule: no hardcoded fallbacks) |
| 9 | Action directive | `SteeringProfile.ActionDirective` | Fail-fast if missing (Hard Rule: no hardcoded fallbacks) |

#### Narrative variant:

```
Writing Instruction:
  Prose Style: Sultry — Evocative and moody, layered with ambiguity...
  Voice: Favor atmosphere, tension, and sensory detail over speed...
  Tone: Erotic, conversational, playful...
  Heat Level: Hardcore — Pure explicit hardcore erotica...
  POV: Write in third-person omniscient point of view.
  HARD CONSTRAINT: Zero dialogue. No character speech, no thoughts quoted, no inner monologue.
  Synthesize only what the characters have already expressed in this turn.
  Do not introduce new events, advance the plot, or have characters take new actions.
  Show, don't tell. Use sensory detail and physical action to convey emotion.
  Target 200-400 words of scene synthesis.
  Physical Detail Checklist:
    - Body positions and spatial arrangement
    - Physical contact points and pressure
    - Sensory details (touch, smell, sound, taste)
    - Rhythm and pacing of movement
    - Environmental atmosphere and ambient details
```

Differences from Character: POV always omniscient, narrative constraints added, no immersion directive, word target says "scene synthesis."

### 6.4 Phase Guidance Placement

Phase Guidance was deliberately moved to Slot 17 (after Writing Instruction) for recency compliance. In the consolidated structure:

```
Writing Instruction:
  [components 1-9 as above]

Phase Directive (incorporate into this response):
  Fully planned deliberate exposure, spread across distinct encounters over time...
```

Phase Directive remains AFTER the Writing Instruction block — it's the absolute last content the model reads, preserving the proven recency pattern.

### 6.5 Slot 12 (ThemeContract) — Phase Guidance Overlap

FR-018 says Theme Contract contains "phase guidance prose." The plan moves phase guidance to Slot 17. To avoid duplication:
- Slot 12 keeps theme name, description, theme directives, AI guidance notes, and steering rank (per FR-018)
- Slot 12 STOPS emitting phase guidance prose — that moves to Slot 17's Phase Directive
- FR-018 must be amended to remove "phase guidance prose" from Slot 12's contract

### 6.6 Slot 16 (UserDirection) — Interaction with Slot 17

Slot 16 (User Direction) fires only when the user provides real direction (FR-022). It stays separate from Slot 17 — it's user input, not writing instruction. If the user says "Make Becky seduce Dean," that appears in Slot 16, and Slot 17's Action Directive can reference it: "Follow the user's direction above."

---

## 7. Token Budget Impact

| Change | Delta |
|--------|-------|
| Slot 8: Foundation text moved out | -~200 chars |
| Slot 15: Contract text moved out | -~200 chars |
| Slot 17: Prose Style description added | +~400 chars |
| Slot 17: Foundation added | +~150 chars |
| Slot 17: Tone/Register added | +~100 chars |
| Slot 17: Heat contract added | +~300 chars |
| Slot 17: Pacing added | +~100 chars |
| **Net** | **~+650 chars** |

At 40,000 budget this is negligible. At 35,000 it may push borderline prompts over. Mitigation: Slots 8 and 15 are Zone B / trimmable — budget enforcer handles overflow.

---

## 8. Backward Compatibility

| Concern | Strategy |
|---------|----------|
| `NarrativeTone` → `Tone`/`Register`/`Focus` | Keep deprecated `NarrativeTone`; new reads prefer new fields; fall back to `NarrativeTone` if new fields null |
| Existing sessions with old data | `ResolvedWritingStyleData` reads both old and new paths |
| `StyleProfiles` without new columns | Migration adds columns with NULL defaults; existing profiles must be updated to populate required fields (fail-fast on null at runtime) |
| Profile rename from DB perspective | No DB renames — only prompt labels change |

### Hard Rule Compliance

The repo Hard Rule (No Fallbacks Across RP Engine) requires: missing RP configuration must fail fast. The following fields on `SteeringProfile` are RP behavior controls and MUST be populated:
- `ImmersionDirective` — fail-fast if null
- `ActionDirective` — fail-fast if null
- `WordTargetMin` / `WordTargetMax` — fail-fast if null or <= 0

Existing `Sultry` profile must be updated to populate these fields before the code change goes live. No hardcoded fallbacks are permitted.

---

## 9. Spec Amendments Required

The 17-slot architecture is frozen per spec contract. This plan requires the following spec amendments:

| FR | Current text | Amendment |
|----|-------------|-----------|
| FR-014 | "The writing style slot MUST include... phase Rule of Thumb... profile default Rule of Thumb as a separate always-present slot element." | Move all writing direction to Slot 17. Slot 8 becomes purely contextual or is removed. |
| FR-018 | "The theme contract MUST... contain... phase guidance prose..." | Remove "phase guidance prose" from Slot 12. Phase guidance moves to Slot 17. |
| FR-021 | "The intensity and pacing slot MUST merge... resolved intensity label, intensity writing contract, pacing directive, and available positions." | Move intensity label + contract + pacing to Slot 17. Slot 15 retains only available positions (structural data). |
| FR-023 | "The final writing instruction MUST be the last content the model reads before generating." | Already amended (debug#12). Phase Directive stays after Writing Instruction. |
| Actor Profile Contract | "Player: 1st person, 100-300 words" / "Narrative: 3rd person, 300-500 words" | Update to match configurable word targets from `SteeringProfile.WordTargetMin/Max`. |

### Rollback Plan

If the consolidated Slot 17 degrades output quality:
1. Revert `FinalInstructionSlot.cs` to only emit POV + Immersion + Word + Action (current state)
2. Restore full Prose Style and Heat contract to Slots 8 and 15
3. Phase Guidance stays in Slot 17 (already proven effective)
4. Data model new fields stay (no rollback needed — they're additive)

---

## 10. Implementation Steps (Dependency-Ordered)

### Phase 1: Data Foundation

| Step | File | Description | Depends on |
|------|------|-------------|------------|
| D1 | `NarrativeSettings.cs` | Add `Tone`, `Register`, `Focus` fields; deprecate `NarrativeTone` | None |
| D2 | `StyleProfiles` (DB) | Add `ImmersionDirective`, `ActionDirective`, `WordTargetMin`, `WordTargetMax` columns | None |
| D3 | `SteeringProfile.cs` | Add matching C# properties | D2 |

### Phase 2: Profile Cleanup

| Step | File | Description | Depends on |
|------|------|-------------|------------|
| P1 | DB data | Create "Atmospheric" StyleProfile; clean Sensual/Emotional descriptions | None |
| P2 | DB data | Update scenario `135a9237` Narrative: populate new `Tone`/`Register`/`Focus` from existing `NarrativeTone` | D1 |

### Phase 3: Slot 8 + 15 Label Changes

| Step | File | Description | Depends on |
|------|------|-------------|------------|
| S1 | `WritingStyleSlot.cs` | Headers: Prose Style, Tone, Phase Direction, Foundation. Split Tone from Style Hint. Lightweight content. | D1 |
| S2 | `IntensityPacingSlot.cs` | Headers: Heat & Pacing, Heat Level. Lightweight content. | None |
| S3 | `SlotContractTests.cs` | Update expected strings for S1, S2 | S1, S2 |

### Phase 4: Slot 17 Consolidation

| Step | File | Description | Depends on |
|------|------|-------------|------------|
| F1 | `PromptBuildContext.cs` | Ensure `ResolvedWritingStyleData` has `ProfileName`, `ProfileDescription`, `Foundation`, `Tone`, `Register` accessible | D1, D3 |
| F2 | `ActorProfileResolver.cs` | Ensure `PerspectiveMode` flows through `ActorProfile` for POV resolution | None |
| F3 | `FinalInstructionSlot.cs` | Consolidated output: Prose Style, Foundation, Tone, Heat, Pacing, POV, Immersion, Word Target, Action | F1, F2, D3 |
| F4 | `SlotContractTests.cs` | Update tests for F3 | F3 |

### Phase 5: Spec & Docs

| Step | File | Description | Depends on |
|------|------|-------------|------------|
| R1 | `spec.md` | Update FR-014, FR-021, FR-023 | All above |
| R2 | `writing-terminology.md` | Create reference mapping document | All above |

---

## 11. Dependency Graph

```
D1, D2 ──→ D3 ──→ F1 ──→ F3 ──→ F4 ──→ R1, R2
                ↗
P1, P2 ──→ S1 ──→ S3
                ↘
S2 ──→ S3         F2 ──→ F3
```

- D1 + D2 can be done in parallel
- P1 + P2 can be done in parallel
- S1 + S2 can be done in parallel
- F1 + F2 can be done in parallel (both depend on D3)
- F3 depends on F1 + F2 + S1
- S3 + F4 can be done in parallel
- R1 + R2 last

---

## 12. Files Affected

| File | Change |
|------|--------|
| `NarrativeSettings.cs` | Add `Tone`, `Register`, `Focus` fields |
| `SteeringProfile.cs` | Add `ImmersionDirective`, `ActionDirective`, `WordTargetMin`, `WordTargetMax` |
| `WritingStyleSlot.cs` | Header renames, Tone block split, lightweight output |
| `IntensityPacingSlot.cs` | Header renames, lightweight output |
| `FinalInstructionSlot.cs` | Consolidated writing instruction, POV from config |
| `PromptBuildContext.cs` | May add instruction data fields |
| `ActorProfileResolver.cs` | Ensure `PerspectiveMode` on `ActorProfile` |
| `SlotContractTests.cs` | Updated expected strings |
| `StyleProfiles` table | Migration: new columns |
| Existing `ToneProfiles` data | Cleanup Sensual/Emotional descriptions |
| Scenario `135a9237` | Populate new `Tone`/`Register`/`Focus` fields |
| `spec.md` | Update FR-014, FR-021, FR-023 |
| NEW: `writing-terminology.md` | Reference mapping document |

---

## 13. Decisions

- No code renames — `ToneProfiles` stays `ToneProfiles`, `StyleProfiles` stays `StyleProfiles`. Prompt labels change.
- POV is per-character (`perspectiveMode`). Final Instruction reads it dynamically.
- Word target sourced from `SteeringProfile.WordTargetMin/Max` — fail-fast if missing (Hard Rule compliance).
- Immersion/Action directives sourced from `SteeringProfile` — fail-fast if missing (Hard Rule compliance).
- Phase Guidance stays at absolute end of prompt (after Writing Instruction).
- `NarrativeTone` deprecated but retained for backward compatibility.
- Atmospheric profile moved to StyleProfiles (data migration, not code change).
- Net token impact ~+650 chars — acceptable within 40K budget.
- **No duplication** between Slots 8/15 and 17 — writing direction lives ONLY in Slot 17.
- **"Show, don't tell"** added to immersion directive — LLMs respond well to this fundamental writing directive.
- **Spec amendments required** for FR-014, FR-018, FR-021, FR-023, and Actor Profile Contract.
- Terminology: `Foundation` → `Voice`, `Phase Direction` → `Scene Direction` (writer-standard terms).

---

## 14. Validated

[ ] pending — implementation not started.
