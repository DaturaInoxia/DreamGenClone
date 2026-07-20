# Slot 17 (Final Instruction) Output Contract

**Feature**: `001-final-writing-instruction`
**Date**: 2026-07-19

---

## Character Variant

```
Scene Direction:
  {PhaseGuidanceLine1}
  {PhaseGuidanceLine2}
  ...

Writing Instruction:
  Prose Style: {SteeringProfile.Name} — {SteeringProfile.Description}
  Voice: {SteeringProfile.RuleOfThumb}
  Tone: {NarrativeSettings.Tone}[ — {NarrativeSettings.Register}]
  Focus: {NarrativeSettings.Focus}
  Heat Level: {IntensityProfile.ResolvedLabel} — {IntensityProfile.Description}
  Pacing: {PacingText derived from SceneDirection.Pacing}
  POV: Write in first-person from {ActorProfile.ActorName}'s point of view.
  Immersion: {SteeringProfile.ImmersionDirective}
  Word Target: Target {SteeringProfile.WordTargetMin}-{SteeringProfile.WordTargetMax} words.
  Action: {SteeringProfile.ActionDirective}
```

### Conditional Lines

| Line | Condition |
|------|-----------|
| `Scene Direction:` block | Only when `Theme.PhaseGuidanceLines.Count > 0` |
| `Tone:` line | Only when resolved Tone is non-empty |
| `Focus:` line | Only when resolved Focus is non-empty |
| `— {Register}` suffix on Tone | Only when resolved Register is non-empty |

### Pacing Text Mapping

| `SceneDirection.Pacing` | Text |
|-------------------------|------|
| `Slow` | "Slow pace — linger on sensory detail, internal reflection, and atmosphere. Let moments stretch." |
| `Fast` | "Fast pace — drive toward the next beat. Keep actions crisp and dialogue forward-moving." |
| `Medium` (default) | "Medium pace — advance the scene naturally, not rushed, not stalled. Let moments breathe without dragging." |

---

## Narrative Variant

```
Writing Instruction:
  Prose Style: {SteeringProfile.Name} — {SteeringProfile.Description}
  Voice: {SteeringProfile.RuleOfThumb}
  Tone: {NarrativeSettings.Tone}[ — {NarrativeSettings.Register}]
  Focus: {NarrativeSettings.Focus}
  Heat Level: {IntensityProfile.ResolvedLabel} — {IntensityProfile.Description}
  Pacing: {PacingText derived from SceneDirection.Pacing}
  POV: Write in third-person omniscient point of view.
  HARD CONSTRAINT: Zero dialogue. No character speech, no thoughts quoted, no inner monologue.
  Synthesize only what the characters have already expressed in this turn.
  Do not introduce new events, advance the plot, or have characters take new actions.
  Word Target: Target {SteeringProfile.NarrativeWordTargetMin}-{SteeringProfile.NarrativeWordTargetMax} words of scene synthesis.
  Physical Detail Checklist (MUST cover from what was described):
    - Body positions and spatial arrangement
    - Physical contact points and pressure
    - Sensory details (touch, smell, sound, taste)
    - Rhythm and pacing of movement
    - Environmental atmosphere and ambient details
```

### Narrative Variant Differences from Character

| Aspect | Character | Narrative |
|--------|-----------|-----------|
| Scene Direction block | Present (if phase active) | Absent (Narrative synthesizes, doesn't drive story) |
| POV | First-person from actor | Third-person omniscient |
| Immersion directive | Present | Absent |
| Action directive | Present | Replaced by narrative constraints (zero dialogue, no new events, synthesis-only) |
| Word Target source | `WordTargetMin/Max` | `NarrativeWordTargetMin/Max` |
| Word Target label | "words" | "words of scene synthesis" |
| Physical Detail Checklist | Absent | Present |

---

## Fail-Fast Errors

### Missing SteeringProfile Field (FR-006)

```
MissingPromptConfig: SteeringProfile '{profileName}' (Id={profileId}) is missing required field '{fieldName}'. FR-006 requires this field to be populated. No hardcoded fallback is permitted. Populate the field via the Style Profile management UI or a DB update.
```

**Required fields**: `ImmersionDirective`, `ActionDirective`, `WordTargetMin`, `WordTargetMax`, `NarrativeWordTargetMin`, `NarrativeWordTargetMax`

### Missing Intensity Profile (Edge Case)

```
MissingPromptConfig: No IntensityProfile resolved for session '{sessionId}'. Heat Level is a required component of Slot 17. Ensure the scenario has a DefaultIntensityProfileId and the referenced ToneProfile exists.
```

---

## Slot 8 (WritingStyle) Contract — After Consolidation

**After change**: Slot 8 emits NO writing direction. It becomes purely contextual/structural.

**Options** (to be finalized in implementation):
- **Option A**: Slot 8 emits nothing (removed from pipeline if spec amended to allow 16 slots)
- **Option B**: Slot 8 emits a single reference line: `"Writing direction: see Writing Instruction below."`

**Recommended**: Option B for backward compatibility — keeps the 17-slot architecture intact and provides a pointer for readers inspecting the prompt.

---

## Slot 15 (IntensityPacing) Contract — After Consolidation

**After change**: Slot 15 emits only structural data (available positions). No heat level, no contract, no pacing.

```
Available positions:
  • {Position1}
  • {Position2}
  ...
```

If no available positions, Slot 15 emits nothing.

---

## Slot 12 (ThemeContract) Contract — After Consolidation

**After change**: Slot 12 stops emitting phase guidance prose. Retains:

```
Theme Contract: {ActiveTheme.Label}
{ActiveTheme.Description}

Theme Directives:
  {directive1}
  {directive2}

AI Guidance Notes:
  [{section}] {note1}

Hard Constraints:
  - {constraint1}
```

Phase guidance prose is removed (moved to Slot 17 as Scene Direction).
