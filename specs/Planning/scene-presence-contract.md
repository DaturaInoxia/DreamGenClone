# Scene Presence Contract (removed 2026-06-26)

Removed from `RolePlayContinuationService.cs` to resolve conflict with pacing directives.
The third bullet directly contradicts fast pacing ("compress multiple beats into one response" vs "one response covers one beat or scene").

## Original text

```
HARD CONSTRAINT — Scene Presence Contract:
- Any intimate physical encounter — kissing, touching, caressing, or sexual activity —
  occurring in the current moment must be described in full in this response.
  Do not fade to black. Do not summarize what happened with a single sentence.
- Do not write time-skip transitions that bypass an intimate scene in progress:
  e.g. 'the door closed behind her', 'an hour later', 'when it was over'.
  Stay present inside the encounter.
- Do not write time-skip transitions in the middle of a response.
  Write through the full response — one response covers one beat or scene from
  beginning to end. Time-skipping, scene resets, or setting up a new location
  belongs in a subsequent turn.
- The Resolved Intensity controls HOW explicitly you write the encounter
  (vocabulary, anatomical detail), not WHETHER you write it.
- At lower intensity levels: use evocative, sensory, emotionally resonant
  language — describe physical contact, sensation, and reactions without
  graphic anatomy.
```

## Fire conditions

- `resolvedScale >= Emotional` (intensity must be Emotional or higher)
- `currentPhase != "BuildUp"`
- `intent != PromptIntent.Instruction`

## Reasoning for removal

The third bullet ("one response covers one beat or scene") directly conflicts with
the fast pacing directive ("compress multiple beats into one response"). Since the
Scene Presence Contract is a HARD CONSTRAINT and the pacing instruction is a soft
directive, the model always obeys the HC and ignores pacing.

If this is re-added, it should include a carve-out:
  "unless the active pacing directive permits beat compression"
