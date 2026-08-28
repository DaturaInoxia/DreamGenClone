# 021 — Pacing Directive Is Position-1-Only → Full Sex Scene Per Turn (Session 7763f8a8)

## Report

Session `7763f8a8-4e5b-4502-8528-a7fb94bc1281` (Campground Intimacy, theme `infidelity-public-facade-v3`, Climax phase) produced a **complete start→orgasm sex scene within every single turn**, very consistently. Encounter completion spans from `RolePlayV2EncounterSummaries`:

- Encounter 1: idx 61–169 (drawn out — boundary detection failed 8× with HTTP 400 during this span)
- Encounter 2: idx 181–193
- Encounter 3: idx 195–196 (2 interactions)
- Encounter 4: idx 199–200 (2 interactions)
- Encounter 5: idx 204–204 (1 interaction!)
- Encounter 6: idx 207–208

User reported they had **previously tried to produce this behavior on purpose and could not**, making it a genuinely surprising consistency. Theme has **no markers at all** in any phase (`RPThemePhaseGuidance` — no `[Pacing:*]`, no `[ClimaxMode:*]`, no `[Deepening:*]`, no `[TimeShift:*]`).

## Analysis

### Phase-default table is all Medium (reference doc was wrong)

`SceneDirectionResolver.PhaseDefaultPacingMap` / `PhaseDefaultBeatScopeMap` / `PhaseDefaultTimeShiftMap` are **all Medium / all Short / all Medium** for every phase — **Climax is NOT Fast**. The `.github/instructions/rp-prompt-injection-reference.instructions.md` doc claimed Climax=Fast / Reset=Slow, but that table is wrong for current code (it reflects the pre-redesign injector architecture). Since this theme has no markers, the resolved `SceneDirection.Pacing` = **Medium**.

### The pacing HARD CONSTRAINT is position-1-only

`FinalInstructionSlot.cs` (Slot 17, Zone C) emits the pacing HARD CONSTRAINT only for the **first character** of a turn:

```csharp
if (!isNarrative && context.PositionInTurn == 1 && context.Intensity.SceneDirection is not null)
```

**Verified in the actual stored prompts** (encounter-5 turn, idx 203–209):

| idx | actor | position | `Scene Pacing` in promptText? |
|-----|-------|----------|-------------------------------|
| 203 | Becky | 1 | ✅ HAS (Medium HC) |
| 204 | Dean | 2 | ❌ none |
| 205 | Narrative | — | ❌ |
| 206 | Becky | 1 | ✅ HAS (Medium HC) |
| 207 | Dean | 2 | ❌ none |
| 208 | Ken | 3 | ❌ none |

The response that **completes** the encounter is always position 2 or 3 → it receives **no pacing directive and no deepening constraint**. The Medium HC ("advance the scene by one beat") constrains only position 1.

### What actually drives the completing response

Since positions 2/3 have no pacing directive, the **only** operative instruction for the completing actor is the theme's guidance/directive prose, which explicitly commands completion:

- DirectiveText (Climax): *"the encounters are raw and quick both trying to reach climax and release as quick as possible"* — present in ALL climax prompts from idx 117 onward, near the END of the prompt (highest recency).
- GuidanceText (Climax): *"The other man has reached his limit... He needs release NOW"*, *"The arc ends in completion, not interruption."*

### Boundary closes instantly (no minimum length for non-multi themes)

`RolePlayEngineService.TryDetectEncounterBoundaryAsync` has a `minIxns = 4` minimum-encounter-length guard, but it only applies when `isMulti` (`[ClimaxMode:multi-encounter]` marker). This theme has **no** such marker → the guard is OFF → the boundary closes the moment orgasm evidence appears (0.95–1.0 confidence). Keyword gate is permissive: `orgasm/climax/come/came/cum/release/spent/afterglow/subside/fade/pulse/spasm`.

### Why the user couldn't reproduce it "on purpose"

They likely assumed a **pacing marker** (e.g. `[Pacing:fast]`) was required. It is not — Medium + quick-release prose still produces full scenes per turn. OR they tested themes carrying `[ClimaxMode:multi-encounter]` (e.g. `exhibitionism-v2`, `infidelity-brief-disappearance`), where the 4-turn minimum guard blocks 1-turn encounters.

## Plan / Resolution

Analysis only — **no code or data changed** during investigation (per change-control rule). Findings persisted to:

- **`.github/instructions/pacing-directive-findings.instructions.md`** — new verified-findings file (applyTo covers all pacing-relevant files + the reference doc + prompt-redesign spec).
- **`.github/instructions/rp-prompt-injection-reference.instructions.md`** — added architecture warning banner (pre-redesign injector architecture) and **corrected the phase-default table** to all-Medium; also corrected the TimeShift phase-default claim.
- **`.github/copilot-instructions.md`** — added "Pacing Directive Findings (MANDATORY for pacing work)" section pointing to the findings file.
- **`.github/skills/rp-session-debug/references/codebase-map.md`** — linked the findings file in "See Also".

## Open questions for a future fix (no decision made)

1. Should positions 2/3 receive a pacing/lingering constraint by default (e.g. replicate the Deepening behavior, or scope the Medium HC beyond position 1)? This is the structural lever that would prevent one-turn full scenes.
2. Should the `minIxns` minimum-encounter-length guard apply to non-multi themes too, or be UI-backed (per the no-hardcoded-defaults rule)?
3. Should the phase-default table (all Medium) be intentional, or should Climax default to Fast as the old doc claimed?

## Validated

- [x] Verified against stored `promptText` in `Sessions.PayloadJson.interactions` (session 7763f8a8) — pacing HC presence per position confirmed.
- [x] Verified `SceneDirectionResolver` default maps (all Medium).
- [x] Verified encounter completion spans and boundary events.
- [ ] Fix pending user decision (findings only, no code change).
