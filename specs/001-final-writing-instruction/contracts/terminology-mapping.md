# Prompt Label Terminology Contract

**Feature**: `001-final-writing-instruction`
**Date**: 2026-07-19

---

## Label Mapping (Prompt-Facing Text Only — No Code Renames)

| App Implementation (code/DB) | Old Prompt Label | New Prompt Label | Writer's Term |
|------------------------------|-----------------|------------------|---------------|
| `StyleProfiles` / `SteeringProfile` | `Writing Style:` | `Prose Style:` | Prose Style |
| `ToneProfiles` / `IntensityProfile` | `Intensity level:` | `Heat Level:` | Heat Level |
| `SteeringProfile.RuleOfThumb` | `Profile Default:` | `Voice:` | Voice (authorial voice — narrator's timeless style baseline) |
| `PhaseRuleOfThumb` | `Phase Rule of Thumb:` | `Scene Direction:` | Scene Direction (what this scene should accomplish) |
| `Scenario.Narrative.Tone` | *(buried in "Style Hint")* | `Tone:` | Tone (mood/attitude) |
| `Scenario.Narrative.Register` | *(not present)* | *(appended to Tone as "— {Register}")* | Register (language complexity) |
| `Scenario.Narrative.Focus` | *(not present)* | `Focus:` | Focus (subject emphasis) |
| `SteeringProfile.ImmersionDirective` | *(hardcoded in Slot 17)* | `Immersion:` | Immersion (depth-of-field directive) |
| `SteeringProfile.ActionDirective` | *(hardcoded in Slot 17)* | `Action:` | Action (what to do now) |
| `SteeringProfile.WordTargetMin/Max` | `Target 200-400 words.` | `Word Target: Target {Min}-{Max} words.` | Word Target |
| `SceneDirection.Pacing` | `Scene pacing:` | `Pacing:` | Pacing |

---

## Rules

1. **No code renames**: Table names (`StyleProfiles`, `ToneProfiles`), class names (`SteeringProfile`, `IntensityProfile`), and property names (`RuleOfThumb`, `PhaseRuleOfThumb`) remain unchanged. Only prompt-facing label text changes.
2. **Labels are presentation-only**: The label mapping is enforced in the slot `WriteAsync` methods, not in the data model.
3. **Consistency**: Every slot that emits a labeled section MUST use the new label. No slot may use an old label after the feature goes live.
4. **Contract tests**: `SlotContractTests.cs` MUST be updated to assert the new labels appear in slot output.
