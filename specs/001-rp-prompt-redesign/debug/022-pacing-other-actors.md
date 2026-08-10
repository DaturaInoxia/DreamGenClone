# 022 — Pacing Directive Is Position-1-Only → Extend Pacing to All Actors

**Feature**: `001-rp-prompt-redesign` / `001-final-writing-instruction`
**Date**: 2026-08-09
**Status**: Implemented (pending user validation)

## Report

Sex encounter scenes begin and end within a single turn — effectively fast-paced even though the resolved `ScenePacing` is `Medium`. A scene that should span multiple turns (kissing → oral → penetration → release across turns) collapses into one start→orgasm turn. This reproduces the behavior documented in debug `021` and `pacing-directive-findings.instructions.md`.

The user's framing: "a sex scene start to end in 1 turn is fast paced." The label in the prompt says "Medium," but the *effective* tempo is fast — the outcome is what matters.

## Analysis — Root Cause

`FinalInstructionSlot.cs` (Slot 17, Zone C) emits the pacing HARD CONSTRAINT **only for position 1**:

```csharp
// ── Pacing direction (Character position 1 only, near end of prompt for recency) ──
if (!isNarrative && context.PositionInTurn == 1 && context.Intensity.SceneDirection is not null)
```

Verified in stored prompts (session `493c9602`, Climax):
- Becky (position 1) → prompt contains `HARD CONSTRAINT — Scene Pacing: Medium`
- Dean (position 2) → **no** pacing HC
- Narrative → **no** pacing HC

The response that **completes** an encounter is almost always position 2 or 3 (or the narrative close). Those actors receive **no pacing directive and no deepening constraint**, so they run unconstrained — effectively unbounded tempo (faster than Fast). This is the documented primary cause of one-turn full scenes (debug 021, pacing-directive-findings §CRITICAL-2).

Additional contributing factors (unchanged here, documented in 021):
- No `[ClimaxMode:multi-encounter]` → `minIxns=4` encounter-length guard is OFF.
- No `[Deepening:subsequent-actors]` → positions 2/3 advance rather than linger.
- Phase-default pacing is all Medium (`SceneDirectionResolver.PhaseDefaultPacingMap`); the theme has no `[Pacing:*]` marker.

## Plan — Proposed Fix

**File:** `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/FinalInstructionSlot.cs`

Emit a **position-aware pacing directive for every Character position** (not just position 1):
- Condition changes from `context.PositionInTurn == 1` to `context.PositionInTurn is not null`.
- Position 1: same pacing HC as today (Slow/Medium/Fast).
- Positions 2+ (subsequent actors): same pacing HC **plus** an explicit guard — build on the beat already established this turn, do not restart or jump past it.

**Rationale:** closes the position-2/3 gap so every actor receives the same pacing constraint. This is Option A from the analysis — a universal fix (applies to all themes), rather than marker-scoped.

**Blast radius:** low–medium. Slot 17 is never trimmed; change adds a pacing line + a short guard line for positions 2+ only. No fallback/default logic added — text is still derived from resolved `SceneDirection.Pacing`. Applies to every Character-variant prompt for all themes.

## Resolution — What Was Changed (iteration 2, final wording)

`FinalInstructionSlot.cs` (Slot 17): the pacing block now fires for **any Character position** (`PositionInTurn is not null`).

**Position 1 (unchanged semantics):**
```
HARD CONSTRAINT — Scene Pacing: Medium pacing — advance the scene by one beat. Move the story forward.
```
(uses the resolved Slow/Fast/Medium wording as before)

**Positions 2+ (new — combined containment directive):**
```
HARD CONSTRAINT — Scene Pacing: Medium pacing — You are a subsequent actor — build on the beat already established this turn. Do not restart or jump past it.
```

### Why this wording (iteration 2)
The first iteration emitted the raw pacing HC ("advance the scene by one beat. Move the story forward.") **plus** a separate "build on the established beat" line for positions 2+. In a **distributed scene** (e.g. Ken in bed while Becky/Dean are on the porch), the raw "advance the scene / move the story forward" imperative pushed the position-2/3 actor to advance the shared beat — and the simplest way to "move forward" was to relocate a character into their scene (Ken wrote Becky returning to bed). Session `d763ecb6` (interaction `fcf7c480` onwards) showed this regression: Ken kept writing Becky back to bed while she was still on the porch.

The fix: for positions 2+, replace the raw "advance the scene by one beat / move the story forward" wording with a **containment phrasing** — the same Medium pacing label but framed as "build on the established beat, do not restart or jump past it." This keeps a pacing constraint on positions 2+ (anti one-turn-collapse) while removing the "move the story forward" license that caused cross-location teleports.

## Validated

- [x] Web project builds (0 errors)
- [ ] Prompt-slot / RP tests pass — **blocked by pre-existing test-project compile failures** (`ISqlitePersistence.GetLatestSteeringGenerationRecordAsync` missing from test doubles; documented in `/memories/repo/pre-existing-test-failures.md`). The Web change itself compiles clean.
- [ ] User confirms one-turn scenes spread across turns (pending — needs fresh session after rebuild)
- [ ] User confirms Ken no longer teleports Becky to bed in distributed scenes (pending)
