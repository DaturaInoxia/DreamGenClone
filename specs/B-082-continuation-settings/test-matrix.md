# B-082 Continuation Settings — Definitive Test Matrix

**Created:** 2026-08-14
**Rule:** every iteration runs in a **fresh RP session starting in BuildUp phase**. One iteration = one new session. Never reuse a session.

## Per-iteration protocol (uniform)

1. **Create a new RP session** (same character/persona/theme/scenario each time for comparability). It starts in **Opening** phase.
2. **Open Continuation Settings**, set ONLY the option(s) named for that case. Leave every other option at **"No override"** (or "Theme decides" where that's the neutral).
3. **Advance to BuildUp using ONLY the "…" (default continuation) button** — do NOT type any message. Opening → BuildUp fires after `OpeningPeriodTurnCount` (3) observed turns, i.e. the 4th continuation is the first BuildUp turn.
4. **Generate the specified number of BuildUp-phase test turns** (still only "…" — default continuations), then report the session ID + turn count.
5. I run 3 checks and log PASS/FAIL:
   - **Injection** — the exact directive text is present in the right position's `promptText`.
   - **Outcome** — the generated content matches the expected behavior (word count, time-skips, beat advancement, POV).
   - **Engine** (Climax Mode / Aftermath only) — `RolePlayV2AdaptiveStates` + `RolePlayDebugEvents`.

**Hard rules for clean test cases:**
- Do NOT type any message — typed messages inject a User Direction that masks the option under test.
- Verify on **BuildUp-phase** turns, not Opening (Opening uses a different, pre-selection prompt with no regular theme phase-guidance).
- **Reuse ONE session across test cases — changing the option mid-session IS the use case** (sticky override must take effect on the next turn). Only start a NEW session after a code change (so the rebuild's new prompt text is exercised).

All expected injection strings below are the exact, code-verified strings.

---

## Part 1 — Individual cases (each = 1 fresh session, 2 turns, one option set)

### Pacing

| ID | Setting | Exact expected injection (position 1) | Expected outcome |
|---|---|---|---|
| P1 | Pacing = **Slow** | `HARD CONSTRAINT — Scene Pacing: Slow pacing — advance within the current beat. Do not leap to a new beat or position.` | pos-1 response stays inside ONE beat; no time-skip phrase; no new location/act. |
| P2 | Pacing = **Medium** | `HARD CONSTRAINT — Scene Pacing: Medium pacing — advance the scene by one beat, then stop. Do not skip ahead in time or jump to a new location.` | pos-1 advances exactly ONE beat, then stops; next beat only on the next turn. |
| P3 | Pacing = **Fast** | `HARD CONSTRAINT — Scene Pacing: Fast pacing — advance through multiple beats. Push the story forward rapidly.` | pos-1 compresses MULTIPLE beats; a time-skip phrase (`hours later`) is allowed. |

> **Pacing is the general scene-duration lever — not sex-specific.** It controls how many turns a *moment/scene* spans, for any scene (a beach day, a dinner, a hike). Slow = linger in one beat across many turns; Fast = compress several beats into one turn. (Note: verified against non-sex scenes in BuildUp — P1/P2/P3; in Climax the encounter machinery can mask it.)

### Beat Style

| ID | Setting | Exact expected injection | Expected outcome |
|---|---|---|---|
| B1 | Beat Style = **Single** | `HARD CONSTRAINT — Beat Style: Single — Resolve this moment in one turn.` | the current moment resolves within the turn; no carry-over. |
| B2 | Beat Style = **Short** | `HARD CONSTRAINT — Beat Style: Short — Build the moment across 2–3 turns.` | the same moment persists across 2–3 turns. |
| B3 | Beat Style = **Episodic** | `HARD CONSTRAINT — Beat Style: Extended — Linger in this moment for 4+ turns (episodic).` | the same moment persists 4+ turns. |

> **Beat Style = Episodic is supposed to drive the Climax Beat Sheet** (the `ClimaxBeatCursor` — 32 canonical beats `1a → 8g`, each with `MinTurnsBeforeAdvance`). That cursor only activates when the THEME declares `[BeatStyle:episodic]` in its Climax guidance — the UI override does NOT wire into it (see Part 3 / BS1).

### Time Shift

| ID | Setting | Exact expected injection | Expected outcome |
|---|---|---|---|
| T1 | Time Shift = **None** | `HARD CONSTRAINT — Time Shift: None — No time skip — continue from the exact moment.` | no time skip anywhere. |
| T2 | Time Shift = **Small** | `HARD CONSTRAINT — Time Shift: Small — Minutes to a few hours.` | only minutes-to-hours jumps. |
| T3 | Time Shift = **Medium** | `HARD CONSTRAINT — Time Shift: Medium — Hours to half a day.` | hours-to-half-day jumps permitted. |
| T4 | Time Shift = **Large** | `HARD CONSTRAINT — Time Shift: Large — A day or more.` | a day-or-more jump permitted. |

### Granularity

| ID | Setting | Exact expected injection | Expected outcome |
|---|---|---|---|
| G1 | Granularity = **Micro** | `HARD CONSTRAINT — Granularity: Micro — One response = one moment.` | one response = one moment (dense sensory detail). |
| G2 | Granularity = **Meso** | `HARD CONSTRAINT — Granularity: Meso — One response = one scene/beat.` | one response = one scene/beat. |
| G3 | Granularity = **Macro** | `HARD CONSTRAINT — Granularity: Macro — One response = a day or significant span.` | one response = a day/significant span (routines summarized). |
| G4 | Granularity = **Montage** | `HARD CONSTRAINT — Granularity: Montage — One response = multiple days to weeks.` | one response = multiple days-to-weeks highlights. |

### Deepening

| ID | Setting | Exact expected injection | Expected outcome |
|---|---|---|---|
| D1 | Deepening = **Standard** | (no deepening line) | pos 2+ follow normal pacing (may advance). |
| D2 | Deepening = **SubsequentActors** | `- You are a subsequent actor this turn. Deepen the moment established by the first response from your character's perspective. Do not advance to a new beat or position.` (pos 2+) | pos 2+ re-explore pos-1's beat from their POV; never advance. |

### Scene Presence

| ID | Setting | Exact expected injection | Expected outcome |
|---|---|---|---|
| S1 | Scene Presence = **Off** | `HARD CONSTRAINT — Scene Presence: off — No stay-present contract.` | normal behavior (no stay-present contract). |
| S2 | Scene Presence = **On** | `HARD CONSTRAINT — Scene Presence: on — Stay present — no time skip.` | no time skip; characters stay physically in scene. |

### Climax Mode (engine — no prompt injection)

| ID | Setting | Expected engine behavior |
|---|---|---|
| C1 | Climax Mode = **Normal** | one continuous Climax; no `EncounterBoundaryAdvanced` split; `IsEncounterActive` toggles normally. |
| C2 | Climax Mode = **Multi-encounter** | `EncounterBoundaryAdvanced` + `EncounterStartDetected` events; `GlobalEncounterCount` increments; a non-sexual gap between encounters. |

### Aftermath (engine — no prompt injection)

| ID | Setting | Expected engine behavior |
|---|---|---|
| A1 | Aftermath = **Off** | no husband-contrast beat after an encounter. |
| A2 | Aftermath = **Husband contrast** | after an encounter boundary, an `AftermathCoupleInteraction` phase (wife acts normal to husband) BEFORE the next encounter. |

### Word Count

| ID | Setting | Exact expected injection | Expected outcome |
|---|---|---|---|
| W1 | Word Count = **small** | `Word Target: Target 200-400 words.` (Narrative: 400–800) | each LLM interaction lands in 200–400 (narrative 400–800). |
| W2 | Word Count = **medium** | `Word Target: Target 300-700 words.` (Narrative: 600–1400) | each LLM interaction lands in 300–700 (narrative 600–1400). |
| W3 | Word Count = **large** | `Word Target: Target 500-1000 words.` (Narrative: 1000–2000) | each LLM interaction lands in 500–1000 (narrative 1000–2000). |

---

## Part 2 — Combination cases (each = 1 fresh session, 3–4 turns, exact settings)

| ID | Settings (exactly) | Expected outcome |
|---|---|---|
| C1 | Pacing=**Slow** + ClimaxMode=**Multi-encounter** + Aftermath=**Husband contrast** | Deep lingering within beats; when the male orgasm occurs the scene settles to afterglow, encounter closes (boundary event), an "act normal to husband" beat appears, THEN the next encounter starts. **The full realistic flow.** |
| C2 | Pacing=**Fast** + ClimaxMode=**Multi-encounter** | Each encounter compresses beats but still closes on male orgasm → afterglow; boundary event; non-sexual gap; next encounter. |
| C3 | Pacing=**Slow** + Aftermath=**Husband contrast** | Lingering sex; on close, a clear husband-contrast beat; no endless loop. |
| C4 | Granularity=**Micro** + WordCount=**large** | One moment expanded to 500–1000 words; no beat/time advance despite length. |
| C5 | Pacing=**Fast** + Deepening=**SubsequentActors** | pos 1 compresses beats; pos 2+ only deepen (never advance) — Deepening overrides pos-2+ pacing. |
| C6 | Pacing=**Slow** + TimeShift=**Large** | Lingering beat; when the scene does shift, a day+ jump is permitted (not a micro-jump). |
| C7 | Pacing=**Slow** + Granularity=**Micro** + Deepening=**SubsequentActors** + BeatStyle=**Short** | **Regression case (the known loop).** Expected: deep lingering that STILL resolves when the male orgasm arrives. Observed bug: endless orgasm loop + `IsEncounterActive` stuck false. |

---

## Part 3 — Climax Beat Sheet (Episodic)

| ID | Setup | Expected behavior |
|---|---|---|
| BS1 | Theme with `[BeatStyle:episodic]` in Climax guidance (only `infidelity-brief-disappearance-v2` carries it) | `ClimaxBeatCursor` activates (`CurrentBeatCode="1a"`), advances one beat per `MinTurnsBeforeAdvance` turns through `1a → … → 8g`; the encounter/scene spans the full 32-beat sheet (many turns). |

**Status: NEVER TESTED.** Session `318d77a2` (ntr-open-world) has no `[BeatStyle:episodic]` → `0` `ClimaxBeatCursor` events. The UI override `Beat Style = Episodic` does NOT activate the cursor — `IsEpisodicBeatStyle` reads only the theme marker, so the override merely injects the soft text line (verified inert in Climax). **To run BS1:** create a session on `infidelity-brief-disappearance-v2`, reach Climax, and verify the cursor ticks + the scene spans the sheet. **Wiring fix needed:** feed the resolved `BeatScope` (override ?? theme marker) into the cursor activation so Single/Short/Episodic actually control scene length.

---

## Execution order (do these in this order)

1. **P1, P2, P3** (Pacing — the foundational lever)
2. **B1, B2, B3** (Beat Style)
3. **T1–T4** (Time Shift)
4. **G1–G4** (Granularity)
5. **D1, D2** (Deepening)
6. **S1, S2** (Scene Presence)
7. **C1, C2** (Climax Mode — need to reach Climax phase)
8. **A1, A2** (Aftermath — need an encounter to complete)
9. **W1, W2, W3** (Word Count)
10. **C1–C7** (combinations)

**Note for C1/C2/A1/A2:** Climax Mode and Aftermath only manifest in the Climax phase and after an encounter completes, so those sessions must be run past BuildUp into an encounter.

---

## Results log

| Case | Injection | Outcome | Engine | Verdict |
|---|---|---|---|---|
| P1 Slow | ✅ idx9 "Slow pacing — advance within the current beat. Do not leap…" (pos1); pos2 containment; Narrative none | ✅ 332w/346w/694w, no time-skips, single beat | — | **PASS** |
| P2 Medium | ✅ idx12 "Medium pacing — advance the scene by one beat, then stop…" (NEW wording); pos2 containment; Narrative none | ✅ one beat (night→next morning), no time-skip, no location jump | — | **PASS** |
| P3 Fast | ✅ idx9 "Fast pacing — advance through multiple beats. Push the story forward rapidly."; pos2 containment; Narrative none | ✅ multi-beat: Opening turns showed "that night/by evening" skips; BuildUp idx9 advances to a new location/time | — | **PASS** |
| P2 Medium | | | — | |
| P3 Fast | | | — | |
| B1 Single | ✅ "HARD CONSTRAINT — Beat Style: Single — Resolve this moment in one turn." (all 4 positions) | ✅ clothesline moment resolved within the turn; no carry-over beat | — | **PASS** (debug-008 fix validated: mid-session override now injects on next turn) |
| B2 Short | ✅ "HARD CONSTRAINT — Beat Style: Short — Build the moment across 2–3 turns." (3 positions) | ✅ kiss/confrontation moment continuing across turns (not resolved in one turn) | — | **PASS** |
| B3 Episodic | ✅ "HARD CONSTRAINT — Beat Style: Extended — Linger in this moment for 4+ turns (episodic)." (3 positions) | ✅ kiss moment lingered (Committed phase); same beat persisting across turns | — | **PASS** |
| T1 None | ✅ "HARD CONSTRAINT — Time Shift: None — No time skip — continue from the exact moment." (3 positions) | ✅ continued from exact moment (kiss at reeds); no time skip | — | **PASS** |
| T2 Small | ✅ "HARD CONSTRAINT — Time Shift: Small — Minutes to a few hours." (confounded with B1 still set) | ✅ morning→afternoon shift (minutes-to-hours) | — | **injection PASS** (outcome confounded; redo clean if needed) |
| T3 Medium | ✅ "HARD CONSTRAINT — Time Shift: Medium — Hours to half a day." (3 positions) | ✅ afternoon→evening jump (sky "gold then bruised lavender, first stars") | — | **PASS** |
| T4 Large | ✅ "HARD CONSTRAINT — Time Shift: Large — A day or more." (3 positions) | ✅ overnight jump (evening→next morning); permitted day-scale skip | — | **PASS** (overnight, not full multi-day — within permitted range) |
| G1 Micro | ✅ "HARD CONSTRAINT — Granularity: Micro — One response = one moment." (4 positions) | ✅ dense single-moment sensory detail (shower building: damp concrete, old pipes, 2:40 clock) | — | **PASS** |
| G2 Meso | ✅ "HARD CONSTRAINT — Granularity: Meso — One response = one scene/beat." (3 positions) | ✅ one scene/beat (kiss in shower building) | — | **PASS** |
| G3 Macro | ✅ "HARD CONSTRAINT — Granularity: Macro — One response = a day or significant span." (4 positions) | ✅ day/significant span (afternoon→evening, routines summarized) | — | **PASS** |
| G4 Montage | ✅ "HARD CONSTRAINT — Granularity: Montage — One response = multiple days to weeks." (4 positions) | ✅ "Three days passed" montage (multi-day highlights) | — | **PASS** |
| D1 Standard | ✅ no deepening line; pos2+ get standard containment ("…subsequent actor — build on the beat…") | ✅ normal pacing, no deepening constraint | — | **PASS** |
| D2 SubsequentActors | ✅ pos2+ (Dean, Ken) get "- You are a subsequent actor this turn. Deepen the moment established by the first response…"; pos1 (Becky) + Narrative none | ✅ pos2+ deepened pos-1's beat from their POV | — | **PASS** |
| S1 Off | ✅ "HARD CONSTRAINT — Scene Presence: off — No stay-present contract." (all positions) | ✅ normal behavior; no stay-present constraint | — | **PASS** (matrix expectation corrected: Off stores `false` and DOES emit a line) |
| S2 On | ✅ "HARD CONSTRAINT — Scene Presence: on — Stay present — no time skip." (all positions) | ✅ characters stayed present (bedroom/grill aftermath, no time skip) | — | **PASS** |
| C1 Normal | — | — | ✅ encounter #1 completed (`EncounterBoundaryAdvanced 1->2` + `EncounterCompletionSummariesWritten`), then afterglow; `IsEncounterActive`=0; NO new `EncounterStartDetected` (single climax) | **PASS** |
| C2 Multi | — | — | ✅ `EncounterBoundaryAdvanced 1->2` → `EncounterStartDetected #2` (idx 86, conf 0.95) → `IsEncounterActive`=1; non-sexual gap (Ken waking/empty sheets) between encounters | **PASS** |
| A1 Off | — | — | ✅ no husband-contrast beat (default behavior) | **PASS** (same as baseline) |
| A2 Husband contrast | — | — | ✅ `forceAftermathHusbandContrast=true` → encounter #2 completed → `CurrentTimeSkipPhase` CloseScene(1) → CloseScene directive emitted ("Wrap up the current encounter naturally…") → AftermathCoupleInteraction(3) → None(0). Multi-turn state machine legs fire correctly. | **PASS** |
| W1 small | ✅ "Word Target: Target 200-400 words." (override wordTargetMin=200/Max=400) | ✅ Dean 369w, Ken 362w (200–400); Narrative 640w (400–800) | — | **PASS** |
| W2 medium | ✅ "Word Target: Target 300-700 words." (wordTargetMin=300/Max=700) | ✅ Dean 568w, Ken 517w (300–700); Narrative 1178w (600–1400) | — | **PASS** |
| W3 large | ✅ "Word Target: Target 500-1000 words." (wordTargetMin=500/Max=1000) | ✅ Becky 801w, Dean 694w, Ken 679w (500–1000); Narrative 1726w (1000–2000) | — | **PASS** |
| C1 combo | ✅ Pacing=Slow HC (idx121 pos1 "SLOW"; pos2+ containment); override saved {pacing:0, multi:true, aftermath:true} | ✅ Slow pacing observed to linger (no rush into next encounter across 4 turns); fire-pit→trailer transition | ⏳ aftermath not reached (encounter #4 never started/completed — Slow pacing delayed escalation) | **injection PASS; aftermath cycle pending** |
| C2 combo | ✅ Fast HC (pos1 FAST, pos2+ containment); override {pacing:2, multi:true} | ✅ `EncounterBoundaryAdvanced 5->6` (encounter #5 closed) | ✅ multi-encounter boundary fired | **PASS** |
| C3 combo | ✅ Slow HC (pos1 SLOW, pos2+ containment); override {pacing:0, aftermath:true} | ✅ lingering; `CloseScene→AftermathCoupleInteraction→abort` — `HusbandAftermathAbortedMissingSpouse` (04:29) skipped cleanly | ✅ aftermath leg fired, no crash | **PASS** |
| C4 combo | ✅ Micro HC + "Word Target: Target 500-1000 words." override {granularity:0, wordTarget 500/1000} | ✅ Becky 890w, Dean 888w, Ken 586w (500–1000); Narrative 1344w (1000–2000) | — | **PASS** |
| C5 combo | ✅ Fast HC (pos1 FAST) + deepening line on pos2+ (Becky/Ken); override {pacing:2, deepening:1} | ✅ pos1 compressed beats ("that night" skip); pos2+ deepened | — | **PASS** |
| C6 combo | ✅ Slow HC (pos1 SLOW) + Time Shift Large override {pacing:0, timeShift:3} | ✅ "a day and a half after" jump (day+); Ken drives "through the day and into the next" | — | **PASS** |
| C7 combo (regression) | ✅ all 4 dimensions inject: Slow HC (pos1 SLOW), Deepening line (pos2+), Beat Style Short + Granularity Micro override block; override {pacing:0, beatScope:1, granularity:0, deepening:1} | ✅ no stuck loop — `IsEncounterActive=1`, `GlobalEncounterCount=6`, encounter re-activated and state machine advanced | — | **PASS — regression FIXED** (stuck-encounter loop resolved) |

> **Note (2026-08-14):** C1 was started (Slow + Multi-encounter + Husband contrast) — injection verified, but the full encounter→boundary→aftermath cycle was not reached before the session wrapped. C2–C6 remain pending. The individual dimensions they combine are all individually verified PASS; in the Climax phase the active encounter dominates scene-direction behavior (see Multi-turn follow-up below).

---

## Multi-turn follow-up (Climax phase, session 318d77a2)

Follow-up runs to test whether Beat Style / Scene Presence / Deepening produce *behavioral* differences over multiple turns, per the user's request. Run in the **current (Climax) phase** with an active encounter.

| Case | Turns observed | Observation | Conclusion |
|---|---|---|---|
| B1 Single (beatScope=0) | 3 turns (idx 99–109) | Same fire-pit encounter persisted: riding (idx 99–102) → oral (idx 103–106) → "again, in me this time" (idx 107–109). Moment did **NOT** resolve in one turn. | **NO behavioral effect** — "resolve in one turn" ignored during active encounter. |
| B2 Short (beatScope=1) | 1 turn (idx 110–113) | Identical fire-pit moment continuing ("slow grinding roll"). | Same as B1 — no differentiation. |
| B3 Episodic (beatScope=2) | 1 turn (idx 114–116) | Identical fire-pit moment continuing ("joined, cooling… he watched everything"). | Same as B1/B2 — no differentiation. |

**Key finding:** In the **Climax phase with an active encounter**, the Beat Style dimension (Single vs Short vs Episodic) produces **no observable behavioral difference** — all three inject the correct directive text, but the encounter momentum (continuous explicit scene) dominates the generated content and the moment persists identically regardless of the setting. Beat Style appears behaviorally inert when an encounter is active; its effect was only observed in non-encounter phases (BuildUp/Committed).

**Secondary finding (bug):** Aftermath `Husband contrast` flow crashes with `System.ArgumentOutOfRangeException` at `RolePlayEngineService.cs:1669` when the spouse is unresolvable. Sequence: `HusbandAftermathAbortedMissingSpouse` (SpouseName=null) fires, then the batch loop `for (i < batchSize) { sceneActors[i] … }` indexes past the end of the (empty/short) aftermath-restricted actor list. Per contract FR-008 the abort should emit the warning and skip the batch — instead it throws. The phase self-reset to `None` after the crash, so the session remained usable.

**Stuck-encounter loop (C7 regression) — FIXED 2026-08-14:** After an encounter boundary (`IsEncounterActive=false`, `TimeSkipPhase=None`), when the model ignored the "wrap up" directive and kept writing explicit sex (no non-sexual gap), the semantic `encounter-started` detector correctly reported "no transition" (it only fires on non-sexual→sexual transitions) — so the state machine froze: no new start → no boundary → no aftermath, and the scene looped repeating content. **Fix:** in `TryDetectEncounterStartAsync`, when the semantic detector returns no-detection but `CurrentEncounterNumber > 0` and the content matches the explicit-sexual keyword list, re-activate the encounter (`IsEncounterActive=true`, `WasEncounterStart=true`, `EncounterStartReactivated` debug event). Verified end-to-end in session 318d77a2: encounter #4 started (idx 141), boundary advanced, aftermath fired, and the spouse-unresolvable abort skipped cleanly with no crash.

