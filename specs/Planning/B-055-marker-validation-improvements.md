# B-055 — Scene Direction Marker Validation & Improvements

**State**: `new` | **Priority**: `high` | **Scope**: `large`

---

## Summary

Comprehensive end-to-end validation of the Scene Direction injection pipeline — all markers, all 6 phase defaults, all 11 injectors — across 2 RP sessions (~200 interactions). The pipeline is **verified correct** at the injection level. **13 improvements** identified — 2 Critical, 8 High, 2 Medium, 1 Low.

---

## Test Results

### Phase Defaults (T8) — All 6 Phases PASS ✅

| Phase | Pacing | TimeShift | Deepening | Source |
|---|---|---|---|---|
| Opening | Medium | Small | None | Phase default |
| BuildUp | Medium | Small | None | Phase default |
| Committed | Medium | Small | None | Phase default |
| Approaching | Medium | Small | None | Phase default |
| Climax | **Fast** | **Medium** | None | Phase default |
| Reset | **Slow** | **None** | None | Phase default |

### Marker Override Tests

| Test | Marker | Phase | Resolver Output | Output Compliance |
|---|---|---|---|---|
| T1.1 | `[Pacing:slow]` | BuildUp | Pacing=Slow ✅ | Escalation slower, deeper ✅ |
| T1.3 | `[Pacing:fast]` | Committed | Pacing=Fast ✅ | HC fires, beats compressed ✅ |
| T2.1 | `[TimeShift:within-timeframe]` | Climax | TimeShift=Small (overrides Medium) ✅ | Injector text identical to Medium — effectively no-op for non-None phases |
| T3 | `[Deepening:subsequent-actors]` | BuildUp+ | Deepening=SubsequentActors ✅ | Pos2+ deepens from POV. Works with Slow/Medium. Conflicts with Fast (see I11) |
| T4.1 | `[BeatStyle:episodic]` | Climax | BeatScope=Extended ✅ | beat-stage(p90) + position-list(p80) fire. Beat catalog injects. Model ignores hints (I12) |
| T5.1 | `[ClimaxMode:multi-encounter]` | Climax | Engine-level ✅ | Encounter boundary detected: counter increments, interactions reset, time-skip between encounters |

### Injection Pipeline Verified

Coordinator log confirms all 11 injectors fire in correct priority order:
```
turn-context(p5) → time-location(p10) → behavioral-frame(p20) → theme-contract(p30) →
theme-ai-guidance(p40) → intensity-contract(p50) → escalation(p60) → scene-time-direction(p70) →
position-list(p80) → beat-stage(p90) → final-directive(p100)
```

Pacing, TimeShift, and Deepening values confirmed via coordinator log for every turn across all phases.

---

## Identified Improvements (13 Total)

### 🔴 Critical (2)

**I9 — Male cum consumption HC missing**
- Evidence: Dean licked his own cum off Becky's back in Climax-T5  
- Fix: Add engine-level HARD CONSTRAINT to `BehavioralFrameInjector(p20)` or `FinalDirectiveInjector(p100)`: *"Male characters must never consume, taste, or lick their own semen."* Must fire in ALL prompts regardless of theme or phase.  
- Files: `BehavioralFrameInjector.cs` or `FinalDirectiveInjector.cs`

**I5 — Approaching guidance has no visual-only boundary**
- Evidence: Model jumped from visual exposure to "fingers inside me" in one turn (Approaching T4→T5, session 98b5ada1)  
- Fix: Add HARD CONSTRAINT to exhibitionism Approaching guidance: *"Visual Only — bolder flashing: bare ass, bare pussy, bare tits, touching self. Other man watches but does NOT touch. Physical contact begins in Climax when he reveals himself."* Update Climax guidance with corresponding release trigger.  
- Files: `RPThemePhaseGuidance` (GuidanceText for exhibitionism Approaching/Climax)

### High (8)

**I1 — Phase gate awareness**
- Evidence: Masturbation beat skipped in BuildUp; model may assume limited turns  
- Fix: Inject `InteractionCountInPhase` and estimated remaining turns into prompt  
- Files: `RolePlayContinuationService.BuildPromptAsync()`

**I4 — Approaching needs `[Pacing:slow]`**  
- Evidence: Open-robe/shirtless in one turn — escalation jump too steep  
- Fix: Add `[Pacing:slow]` marker to exhibitionism Approaching phase guidance  
- Files: `RPThemePhaseGuidance` (GuidanceText)

**I7 — Fast pacing skips narrative closure**  
- Evidence: Orgasm → campfire jump with no "get dressed, return to husband, lie" transition  
- Fix: Rewrite `FinalDirectiveInjector` Fast Pacing HC to require: *"conclude encounter first — get dressed, return to normal setting, interact with partner — then advance time"*  
- Files: `FinalDirectiveInjector.cs`

**I10 — Climax encounters feel like first time**  
- Evidence: Each new encounter (counter, trail, trailer) felt like discovery, not deepening familiarity  
- Fix: Add continuity guidance to Climax prose: *"Each encounter builds on the last. They know each other's bodies. Start further along the physical arc."*  
- Files: `RPThemePhaseGuidance` (GuidanceText)

**I11 — Fast + Deepening conflict for pos2+**  
- Evidence: Dean at clothesline, Becky at volleyball court — different scenes, Deepening ignored  
- Root cause: `FinalDirectiveInjector(p100)` Fast HC has highest recency; overrides `EscalationInjector(p60)` deepening text  
- Fix: When `Deepening == SubsequentActors`, for position 2+: suppress EscalationInjector(p60), SceneTimeDirectionInjector(p70), and FinalDirectiveInjector(p100) pacing directives. Only inject deepening text. Position 1 gets full pacing injection.  
- Files: `EscalationInjector.cs`, `SceneTimeDirectionInjector.cs`, `FinalDirectiveInjector.cs`, `PromptInjectionContext.cs`

**I12 — Beat sheet hints advisory — model ignores them**  
- Evidence: T1→T3 in 3 turns: baring pussy → cock out + handjob, skipping 30 beats  
- Fix: Move beat injection from `BuildPromptAsync` mid-section to `FinalDirectiveInjector(p100)` area as HARD CONSTRAINT with turns-remaining counter: *"Beat Stage Lock — Stage X, Beat Y. N turns remaining. Do NOT advance."* Hide next-beat info until `turnsInCurrentBeat >= minTurnsBeforeAdvance`  
- Files: `RolePlayContinuationService.BuildPromptAsync()`, `FinalDirectiveInjector.cs` or new injector

**I13 — Beat catalog not theme-scoped**  
- Evidence: 32 universal beats (1a-8g) designed for infidelity-brief-disappearance arc — oral/manual/penetration stages mismatch exhibitionism's visual-only intent  
- Fix: Make `ClimaxBeatEntries` keyed by `ThemeId`. Exhibitionism gets visual-only beat sheet: Teasing Reveal → Full Exposure → Touching Herself → Mutual Acknowledgment → He Reveals Himself  
- Files: `ClimaxBeatRepository.cs`, `ClimaxBeatEntry.cs`, DB schema

### Medium (2)

**I2 — DirectiveText vs GuidanceText priority unclear**  
- Evidence: "must masturbate" treated as suggestion, not requirement  
- Fix: Add explicit `HARD CONSTRAINT` prefix to non-negotiable directive beats; consider numbered sequence  
- Files: `ThemeContractInjector.cs`, guidance formatting

**I6 — Climax default Fast pacing vs original `[Pacing:medium]` intent**  
- Evidence: Original exhibitionism Climax had `[Pacing:medium]`; stripped for T8 baseline, revealing Fast default  
- Fix: Add `[Pacing:medium]` back if uniform pacing preferred; decision pending  
- Files: `RPThemePhaseGuidance`

### Low (1)

**I8 — Risk management prose**  
- Evidence: Hiking trail scene — Ken leading nearby, Dean flashes Becky with no sight-line management  
- Fix: Add guidance: *"They can see him but he cannot see what they are doing"*  
- Files: `RPThemePhaseGuidance` (GuidanceText)

---

## Test Sessions

| Session | Purpose | Interactions |
|---|---|---|
| `98b5ada1-5b18-4d5d-bed7-c09cd74170ad` | T8 phase defaults baseline (all markers stripped, 6 phases) | 87 |
| `ad54ac3c-e4b8-4018-bdbd-0069a3dff861` | Marker tests (Deepening, Pacing, TimeShift, BeatStyle, ClimaxMode) | 105 |

## Test Infrastructure

| Tool | Location |
|---|---|
| Coordinator log capture | `helpers/test-capture-turn.ps1` |
| Interaction output capture | `helpers/capture-interactions.py` |
| Marker setup SQL | `artifacts/tmp/dbquery/queries/test_*.sql` |
| Test plan reference | `specs/debug/scene-direction-marker-tests/test-plan.md` |
| Marker reference doc | `.github/instructions/rp-prompt-injection-reference.instructions.md` |
| Session memory | `/memories/session/debug-session-adc0acfe.md` |

## Agent Handoff Notes

- Current exhibitionism guidance is **modified** from baseline — `[Pacing:slow]` on BuildUp, `[Pacing:fast]` + `[Deepening:subsequent-actors]` on Committed, `[TimeShift:within-timeframe]` on Climax. Restore from baseline before production use. Baseline: markers stripped, prose intact.
- `dbq exec` command has issues with SQLite `char(10)` concatenation — use Python for marker modifications: `python -c "import sqlite3;c=sqlite3.connect('DreamGenClone.Web/data/dreamgenclone.dev.db');c.execute('UPDATE RPThemePhaseGuidance SET GuidanceText=? WHERE ThemeId=? AND Phase=?', (new_text, 'exhibitionism', 'Climax'));c.commit()"`
- The `RolePlayDebugEvents` table exists but had NO records for either test session — prompt capture via debug events is not active in dev. Coordinator logs in `DreamGenClone.Web/logs/dreamgenclone-{date}.log` are the primary evidence source.
- Pacing markers affect injector text correctly but **output compliance is unreliable** — the model's default detailed writing style often overrides "compress" directives. Slow pacing is more reliably followed than Fast.
- `[TimeShift:within-timeframe]` is effectively a no-op when the phase default is already Small or Medium — the `SceneTimeDirectionInjector` text is identical for all non-None TimeShift values.
