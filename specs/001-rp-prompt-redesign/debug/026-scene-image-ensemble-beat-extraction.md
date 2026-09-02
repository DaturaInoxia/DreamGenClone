# 026 - Scene Image Ensemble Beat Extraction

## Report

On 2026-08-21, Studio beat extraction for session `6e836089-0505-4b7b-b7d0-53e1ee81f15b`, turn 19, split concurrent character viewpoints into separate beats. Becky changing, Dean observing through the bedroom door, and Ken observing from the shed were represented as separate chronological events even though the Narrative interaction synthesizes them as one shared timeline. The output also introduced unsupported clothing.

## Analysis

The extraction prompt describes different people per beat but does not declare Narrative as the authoritative synthesis or distinguish active involvement from observation. The character contract stores only name, profile ID, and clothing. It cannot preserve position, action/observation, sightline, or directional visibility. The downstream POV framer consequently uses a generic character-foreground instruction rather than the selected character's actual vantage.

The authoritative Studio full-turn display confirms that the Narrative interaction is the synthesis of all character interactions in the turn. This is a generic role-play invariant, not a rule specific to the cited scenario.

## Plan

Implement the approved plan at `specs/Planning/B-032-scene-image-generator/design/ensemble-beat-render-brief-plan.md`: versioned ensemble beats, strict active/observer parsing, Narrative-led extraction, geometry-aware POV framing, deterministic long-form render briefs, explicit legacy regeneration, and focused regression coverage.

## Resolution

- Added schema-v2 ensemble beats with required visual description, atmosphere, involvement, physical position, action or observation, sightline, visibility, and clothing fields.
- Made Narrative the authoritative chronology and supporting character interactions parallel evidence.
- Defined Narrative-first event grouping: camera/viewpoint changes and observer-only locations cannot create standalone beats; remote observers attach to the simultaneous active event.
- Kept parsing strict. Invalid JSON, unsupported schemas, missing geometry, and invalid visibility fail explicitly; no feature-specific JSON repair was added.
- Added geometry-aware character POV framing and a deterministic authoritative render brief appended after raw turn context.
- Added current-analysis/schema/POV validation and explicit Studio regeneration for legacy analyses.
- Updated the Studio to display rich ensemble geometry and offer Omniscient plus every character associated with the selected beat.

## Validated

- [x] All 92 focused scene-image tests passed before the final prompt-contract refinement.
- [x] Full suite passed after the final implementation: 1,198 passed, 0 failed.
- [x] Full solution build passed. Reported warnings were pre-existing package advisory, obsolete API, nullability, and analyzer warnings.
- [x] Live turn 19 regeneration produced eight chronological event beats. Becky action beats include Dean and Ken as observers with separate positions, sightlines, and visibility; no observer-only viewpoint beat remains.
- [x] Beat 2 exposes independent `Omniscient`, `Becky`, `Dean`, and `Ken` POV options.
- [x] Live Dean prompt generation stored an authoritative render brief after full-turn context, placed the camera at Dean's kitchen position, identified Becky as visible, and kept Dean behind the camera.
- [x] A live malformed JSON response failed strict parsing; the output contract was strengthened to forbid literal control characters, and regeneration then completed without adding parser recovery.