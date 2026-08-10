---
applyTo: 'DreamGenClone.Web/Application/RolePlay/SceneDirectionResolver.cs,DreamGenClone.Web/Application/RolePlay/Prompts/**/*.cs,DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs,DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs,DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs,DreamGenClone.Tests/RolePlay/Prompts/**/*.cs,DreamGenClone.Tests/RolePlay/**/*.cs,.github/instructions/rp-prompt-injection-reference.instructions.md,specs/001-rp-prompt-redesign/**'
description: 'Verified pacing-directive findings from session 7763f8a8. When working on pacing, scene tempo, beat advancement, or encounter pacing, read this first — it documents the positional gap in the pacing directive, the correct phase-default table, and why guidance/directive prose can dominate pacing.'
---
# Pacing Directive — Verified Findings (from Session 7763f8a8)

**Created:** 2026-08-09
**Source of truth:** Verified against actual stored `promptText` in `Sessions.PayloadJson.interactions` for session `7763f8a8-4e5b-4502-8528-a7fb94bc1281` (theme `infidelity-public-facade-v3`, Climax phase). Do NOT trust the prose in `rp-prompt-injection-reference.instructions.md` — it describes the pre-redesign injector architecture and its phase-default table is WRONG (see below).

**Read this before editing ANY pacing-related code, prompt slot, or phase-default logic.**

---

## ⛔ CRITICAL: The phase-default table in the reference doc is WRONG

The doc `.github/instructions/rp-prompt-injection-reference.instructions.md` claims Climax defaults to **Fast** and Reset to **Slow**. That is **false** for the current code. The actual hardcoded defaults in `SceneDirectionResolver` are **all Medium**:

```csharp
PhaseDefaultPacingMap = {
    Opening=Medium, BuildUp=Medium, Committed=Medium,
    Approaching=Medium, Climax=Medium, Reset=Medium
}
PhaseDefaultBeatScopeMap = { ... all Short ... }
PhaseDefaultTimeShiftMap = { ... all Medium ... }
```

**Consequence:** A theme with no `[Pacing:*]` marker resolves to **Medium pacing** in *every* phase, including Climax. The Fast branch in `FinalInstructionSlot.cs` only ever fires when a theme explicitly declares `[Pacing:fast]`.

---

## ⛔ CRITICAL: The pacing HARD CONSTRAINT is position-scoped to position 1 only

`FinalInstructionSlot.cs` (Slot 17, Zone C) only emits the pacing HARD CONSTRAINT for the **first character** of a turn:

```csharp
// ── Pacing direction (Character position 1 only, near end of prompt for recency) ──
if (!isNarrative && context.PositionInTurn == 1 && context.Intensity.SceneDirection is not null)
```

**Verified in session data:** In encounter-5's turn (idx 203–209):
- idx 203 Becky (position 1) → prompt contains `Scene Pacing` HC ✅
- idx 204 Dean (position 2) → **no** pacing HC ❌
- idx 207 Dean (position 2) → **no** pacing HC ❌
- idx 208 Ken (position 3) → **no** pacing HC ❌

**Impact (FIXED 2026-08-09, debug 022):** Positions 2 and 3 previously received NO pacing directive (and no deepening constraint unless `[Deepening:subsequent-actors]` is declared), so the response that *completes* an encounter arc (almost always position 2/3) ran unconstrained by pacing. **This was the single most important structural fact about pacing in this codebase.** Fix: `FinalInstructionSlot.cs` now emits the pacing HARD CONSTRAINT for ALL Character positions (`PositionInTurn is not null`), and positions 2+ get an explicit "build on the beat already established this turn; do not restart or jump past it" guard. See `specs/001-rp-prompt-redesign/debug/022-pacing-other-actors.md`.

---

## The actual injected pacing text (verified)

When Medium (the default), the exact prompt text is:

> `HARD CONSTRAINT — Scene Pacing: Medium pacing — advance the scene by one beat. Move the story forward.`

The string `Fast: advance through multiple beats` that appears in stored prompts is ONLY the **System Primer glossary** line (`SystemPrimerSlot.cs`), which is definitional and present in every prompt — it is NOT a pacing directive. The `FinalDirectiveInjector` / fast-pacing `HARD CONSTRAINT` text from the old reference doc no longer exists (that architecture was replaced by the 17-slot `RolePlayPromptBuilder`).

---

## Why a theme can produce a full start→orgasm scene in one turn

Stacked, verified factors for `infidelity-public-facade-v3`:

1. **Positional gap (primary):** The completing response is position 2/3 → no pacing HC, no deepening → nothing constrains it to a single beat.
2. **DirectiveText (Climax) quick-release prose:** `"the encounters are raw and quick both trying to reach climax and release as quick as possible"` — present in ALL climax prompts from idx 117 onward, near the END of the prompt (highest recency). This is the dominant instruction.
3. **GuidanceText (Climax) completion prose:** `"The other man has reached his limit... He needs release NOW"`, `"The arc ends in completion, not interruption."`
4. **No `[ClimaxMode:multi-encounter]`:** the `minIxns=4` minimum-encounter-length guard in `RolePlayEngineService.TryDetectEncounterBoundaryAsync` is OFF → the boundary closes instantly on orgasm evidence (0.95–1.0 confidence). The keyword gate is permissive (`orgasm/climax/come/came/cum/release/spent/afterglow/subside/fade/pulse/spasm`).
5. **No `[Deepening:subsequent-actors]`:** positions 2/3 advance rather than linger.
6. **Supporting:** scenario heat = Hardcore (explicit release written out); word target small 200–400 (default).

So it is NOT "Fast pacing" — the theme had no pacing marker, resolved to Medium, and still produced full scenes per turn because the completing actor had no pacing constraint and the guidance/directive prose commanded completion.

---

## How to get one-turn full scenes on purpose (and how to prevent them)

- **To allow one-turn full scenes:** quick-release prose in Climax `DirectiveText` + no `[ClimaxMode:multi-encounter]` marker + no `[Deepening:subsequent-actors]` marker + Hardcore heat. A pacing marker is NOT required (Medium default works).
- **To spread encounters across turns:** use `[ClimaxMode:multi-encounter]` (enables the 4-turn minimum) and/or `[Pacing:slow]` and/or `[Deepening:subsequent-actors]` so positions 2+ deepen instead of advancing.

---

## Checklist when working on pacing

1. **Never assume the phase-default table in the reference doc is correct** — read `SceneDirectionResolver.PhaseDefaultPacingMap` (it's all Medium).
2. **Remember the pacing HC only reaches position 1** — any change to beat/pacing behavior must account for positions 2/3, which currently have no pacing directive unless a Deepening marker is present.
3. **Check the actual prompt text**, not the code path — query `Sessions.PayloadJson.interactions[].promptText` (canonical), or the `PromptBuilt` debug events, to confirm what was really injected.
4. **Distinguish glossary from directive** — `SystemPrimerSlot` contains definitional text ("Fast: advance through multiple beats") that must not be mistaken for an active pacing directive.
5. **Encounter pacing is gated in `RolePlayEngineService`** (`TryDetectEncounterBoundaryAsync`), separate from prompt pacing — the `minIxns=4` guard only applies when `[ClimaxMode:multi-encounter]` is present.
