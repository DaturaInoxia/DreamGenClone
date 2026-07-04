# Scene Direction Marker — Comprehensive Test Plan

**Created**: 2026-07-03
**Session under test**: `adc0acfe` (Campground Intimacy, exhibitionism theme, Climax phase)
**Reference doc**: `.github/instructions/rp-prompt-injection-reference.instructions.md`

---

## Test Methodology

For each test case:
1. **Set up**: Configure theme phase guidance with the marker(s) under test, ensure session is in correct phase
2. **Execute**: Run a ContinueAs turn (multi-actor for position-dependent tests)
3. **Capture**: Extract coordinator log from `logs/dreamgenclone-{date}.log`
4. **Validate**: Compare resolved values and firing sequence against expected

### Evidence Sources
| Source | Capture method |
|---|---|
| Coordinator log | `Select-String -Path logs/*.log -Pattern "Coordinator built prompt.*{sessionId}"` |
| Resolved SceneDirection | Log includes: `Pacing="X" TimeShift="Y" Deepening="Z"` |
| Firing sequence | Log includes: `FiringSequence=injector1(p5) -> injector2(p10) -> ...` |
| Adaptive state | `dbq sql debug_session_state_snapshot.sql {sessionId}` |
| Theme guidance | `dbq sql debug_all_themes_markers.sql` |

---

## Test Matrix

### T1 — Pacing Markers

| ID | Phase | Marker | Expected Pacing | Injectors firing |
|---|---|---|---|---|
| T1.1 | Climax | `[Pacing:slow]` | Slow | escalation(p60): "Advance within the same beat — deepen, do not leap"; scene-time-direction(p70): "Stay in the current moment. Do not skip forward."; final-directive(p100): NO fast-pacing HC |
| T1.2 | Climax | `[Pacing:medium]` | Medium | escalation(p60): "Advance the scene with forward momentum. Cover one to two beats"; scene-time-direction(p70): "Let the scene breathe without dragging"; final-directive(p100): NO fast-pacing HC |
| T1.3 | Climax | `[Pacing:fast]` | Fast | escalation(p60): "Compress multiple beats...advance through the full arc"; scene-time-direction(p70): "Compress multiple beats"; final-directive(p100): "HARD CONSTRAINT — Fast Pacing Directive" |
| T1.4 | Climax | *(none)* | Fast (phase default) | Same as T1.3 — verifies phase default fallback |
| T1.5 | Opening | *(none)* | Medium (phase default) | escalation(p60): Medium text; scene-time-direction(p70): Medium text |
| T1.6 | Reset | *(none)* | Slow (phase default) | escalation(p60): Slow text; scene-time-direction(p70): Slow text |

**Validation**: Compare `Pacing=` value in coordinator log. Check final-directive fires (p100) ONLY for Fast.

### T2 — TimeShift Markers

| ID | Phase | Marker | Expected TimeShift | Key effect |
|---|---|---|---|---|
| T2.1 | Climax | `[TimeShift:within-timeframe]` | Small | scene-time-direction(p70): time-shift variant fires; time-location(p10, pos2+): "Time Shift Permission" appears |
| T2.2 | Climax | *(none)* | Medium (phase default) | No TimeShift marker → phase default Medium |
| T2.3 | Reset | *(none)* | None (phase default) | No time shift allowed |
| T2.4 | BuildUp | `[TimeShift:within-timeframe]` | Small | Marker overrides phase default (Small→Small, same value but marker path) |

**Validation**: Compare `TimeShift=` value. For pos2+, verify time-location injector includes "Time Shift Permission" when TimeShift!=None or Pacing==Fast.

### T3 — Deepening Markers

| ID | Position | Marker | Expected Deepening | EscalationInjector output |
|---|---|---|---|---|
| T3.1 | 1 | `[Deepening:subsequent-actors]` | SubsequentActors | **Standard escalation** (NOT deepening — position 1 is exempt) |
| T3.2 | 2 | `[Deepening:subsequent-actors]` | SubsequentActors | "Scene Deepening (Subsequent Actor): Deepen the current scene beat from your character's POV only. Do NOT advance to a new beat or position." |
| T3.3 | 3 | `[Deepening:subsequent-actors]` | SubsequentActors | Same deepening as T3.2 |
| T3.4 | 2 | *(none)* | None | Standard escalation (no deepening path) |

**Validation**: For pos=1, verify `escalation(p60)` fires standard path. For pos>=2, verify `escalation(p60)` fires deepening path. Compare `Deepening=` value.

### T4 — BeatStyle Markers

| ID | Marker | Expected BeatScope | BeatStageInjector (p90) fires? |
|---|---|---|---|
| T4.1 | `[BeatStyle:episodic]` | Extended | ✅ YES — "Beat Stage Context: Current beat scope: Extended. Stay present in the current moment — deepen sensory and emotional detail." |
| T4.2 | `[BeatStyle:short]` | Short | ❌ NO (only fires for Extended) |
| T4.3 | `[BeatStyle:single]` | Single | ❌ NO (only fires for Extended) |
| T4.4 | *(none)* | Short (Climax default) | ❌ NO |

**Validation**: Verify `beat-stage(p90)` appears in firing sequence ONLY for T4.1. For T4.2-T4.4, verify it's absent.

### T5 — ClimaxMode Markers

| ID | Marker | Expected behavior |
|---|---|---|
| T5.1 | `[ClimaxMode:multi-encounter]` | Raw text in theme-contract(p30) Phase Guidance; engine: encounter boundary detection active, `IsMultiEncounterClimax()` returns true |
| T5.2 | `[ClimaxMode:quick-finish]` | Raw text in theme-contract(p30) Phase Guidance; marker parsed but no dedicated injector (retired) |
| T5.3 | Both in same phase | `EnsureClimaxModeMutualExclusion()` throws `InvalidOperationException` with "ClimaxModeConflict" |

**Validation**: T5.1 — verify `ActiveThemeId=exhibitionism` and phase guidance injected. T5.3 — verify exception thrown on session load.

### T6 — ScenePresence Marker

| ID | Marker | Expected |
|---|---|---|
| T6.1 | `[ScenePresence]` | SceneDirection.RequireScenePresence=true; scene-presence(p75) fires: "Scene Presence Contract: Any intimate physical encounter...must be described in full..." |
| T6.2 | *(none)* | scene-presence(p75) does NOT fire |

**Validation**: Verify `scene-presence(p75)` in firing sequence for T6.1, absent for T6.2.

### T7 — Profile DirectorNote Override (Tier 1)

| ID | Condition | Expected |
|---|---|---|
| T7.1 | Profile has DirectorNote | escalation(p60) and scene-time-direction(p70) are SUPPRESSED; director-note(p65) fires instead |
| T7.2 | Profile has NO DirectorNote | escalation(p60) and scene-time-direction(p70) fire normally; director-note(p65) does NOT fire |

**Validation**: Check firing sequence for presence/absence of `escalation(p60)`, `scene-time-direction(p70)`, `director-note(p65)`.

### T8 — Phase Transition Defaults

| ID | Phase | Expected Pacing | Expected TimeShift | Expected BeatScope |
|---|---|---|---|---|
| T8.1 | Opening | Medium | Small | Short |
| T8.2 | BuildUp | Medium | Small | Short |
| T8.3 | Committed | Medium | Small | Short |
| T8.4 | Approaching | Medium | Small | Short |
| T8.5 | Climax | Fast | Medium | Short |
| T8.6 | Reset | Slow | None | Single |

**Validation**: For each phase with no theme markers, verify coordinator log values match.

### T9 — Multi-Actor Turn Flow (Integration)

| ID | Turn structure | Expected |
|---|---|---|
| T9.1 | Dean(pos1)→Becky(pos2)→Ken(pos3)→Narrative | Dean: Fast escalation; Becky: Deepening; Ken: Deepening; Narrative: omniscient close |
| T9.2 | Single actor (pos1)→Narrative | Fast escalation; Narrative: omniscient close |
| T9.3 | Dean(pos1)→Becky(pos2)→Narrative | Dean: Fast escalation; Becky: Deepening; Narrative: omniscient close |

**Validation**: Capture all 3-4 coordinator entries per turn. Verify position-dependent behavior for each.

---

## Test Themes Required

We need to create or modify themes to cover untested markers:

| Theme | Markers to add | Purpose |
|---|---|---|
| `test-all-markers` | All markers across all phases | Comprehensive single-session testing |
| `test-pacing-slow` | `[Pacing:slow]` in Climax | T1.1 (no existing theme has this) |
| `test-scene-presence` | `[ScenePresence]` in Climax | T6.1 (no existing theme has this) |
| `test-beatstyle-short` | `[BeatStyle:short]` in Climax | T4.2 |
| `test-beatstyle-single` | `[BeatStyle:single]` in Climax | T4.3 |

Alternatively, use existing themes:
- `exhibitionism` Climax: has `[Pacing:medium]`, `[ClimaxMode:multi-encounter]`, `[Deepening:subsequent-actors]` → covers T1.2, T3, T5.1
- `exhibitionism-v2` Climax: has `[Pacing:fast]`, `[TimeShift:within-timeframe]`, `[ClimaxMode:multi-encounter]` → covers T1.3, T2.1, T5.1
- `infidelity-brief-disappearance` Climax: has `[BeatStyle:episodic]` → covers T4.1
- `infidelity-public-facade` Climax: has `[ClimaxMode:quick-finish]`, `[TimeShift:within-timeframe]` → covers T5.2

---

## Execution Workflow

```
Phase 1: SETUP
  1. Create/modify test themes with required markers
  2. Create test scenario (simple, minimal chars)
  3. Start web app in dev mode

Phase 2: PER-TEST EXECUTION
  For each test case:
    1. Create session with correct theme in correct phase
    2. Run ContinueAs (single or multi-actor)
    3. Extract coordinator log
    4. Validate against expected values
    5. Record result (PASS/FAIL)

Phase 3: CLEANUP
  1. Aggregate results
  2. File issues for any failures
  3. Clean up test sessions
```

### Quick single-test execution:
```powershell
# 1. Modify adaptive state to target phase
python -c "
import sqlite3, json
c = sqlite3.connect('DreamGenClone.Web/data/dreamgenclone.dev.db')
p = json.loads(c.execute('SELECT PayloadJson FROM Sessions WHERE Id=?',('SID',)).fetchone()[0])
p['adaptiveState']['currentPhase'] = 4  # Climax
p['adaptiveState']['activeScenarioId'] = 'exhibitionism'
c.execute('UPDATE Sessions SET PayloadJson=? WHERE Id=?',(json.dumps(p),'SID'))
c.commit(); c.close()
"

# 2. Run continuation in the app (via browser or API)

# 3. Extract coordinator log
Select-String -Path logs/dreamgenclone-*.log -Pattern "Coordinator built prompt.*SID" | 
    Select-Object -Last 5 | ForEach-Object { $_.Line }
```
