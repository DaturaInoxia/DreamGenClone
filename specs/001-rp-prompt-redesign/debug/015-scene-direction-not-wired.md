# Debug 015 — Scene Direction Not Wired to Prompt Builder

**Created:** 2026-07-17

## Report

Slot 15 outputs bare intensity labels but no pacing/time-shift/deepening directives. Characters teleport between locations and time jumps randomly because the AI has no scene direction.

## Analysis

`SceneDirectionResolver.Resolve()` exists with phase-based defaults but is never called. `IntensityPacingSlot` dumps enum `.ToString()` values (e.g., "medium") that mean nothing to an LLM.

## Plan

**A1:** Call `SceneDirectionResolver.Resolve()` in `ResolveIntensityAsync` and populate `SceneDirection` on the result.

**A2:** Update `IntensityPacingSlot` to output descriptive text per enum value:
- Pacing medium → "Advance the scene naturally — not rushed, not stalled. Let moments breathe without dragging."
- TimeShift small → "Small time shifts allowed (minutes to hours). Use transitions like 'later that evening', 'after supper' when advancing."
- Deepening subsequentActors → "Position 2+ actors: deepen from your POV only. Do NOT advance to a new beat or location."

**Files:** `RolePlayContinuationService.cs`, `IntensityPacingSlot.cs`

## Resolution

[Pending]

## Validated

[ ] Pending
