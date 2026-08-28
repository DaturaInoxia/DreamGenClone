# B-082 Continuation Settings — Test Plan & Execution Log

**Created:** 2026-08-14
**Session focus:** validate each continuation-settings option and the key combinations against *expected* outcomes (not "whatever the model wrote").

## How a test is executed (uniform protocol)

Every test case follows the same 4-step protocol, and each step has a deterministic check:

| Step | What | Who | Verifiable via |
|---|---|---|---|
| 1. Setup | Pick phase + set option(s) in the popup, generate turns | User (UI) | popup state → `Sessions.PayloadJson.continuationOverride` |
| 2. Injection | Confirm the exact directive text reached the prompt | Agent | `interactions[].promptText` (grep the expected string) |
| 3. Outcome | Confirm generated content honors the intent | Agent | full outputs (word count, time-skip markers, beat advancement, POV) |
| 4. Engine | Confirm engine-side settings fired (Climax Mode / Aftermath) | Agent | `RolePlayV2AdaptiveStates` + `RolePlayDebugEvents` |

**Verdicts:** ✅ PASS · ❌ FAIL · ⚠️ PARTIAL (injection OK, outcome not) · ⏭ NOT RUN (needs a fresh session)

**Position scoping (authoritative):**
- Pacing HC → position 1 only; positions 2+ get the fixed containment line.
- Deepening → positions 2+ (via `TurnContextSlot`).
- Beat Style / Time Shift / Granularity → all positions (via `ContinuationOverrideSlot`).
- Scene Presence → only when On (Off = `null`, no injection).
- Climax Mode / Aftermath → engine-side only.
- Word Count → all positions; Narrative range = 2× the character range.

---

## Part 1 — Individual option cases

### T1.1 Pacing = Slow
- **Setup:** any phase; popup Pacing=Slow; generate 1+ turn.
- **Expected injection (pos 1):** `HARD CONSTRAINT — Scene Pacing: Slow pacing — advance within the current beat. Do not leap to a new beat or position.`
- **Expected outcome:** pos-1 stays in one beat; no time skip; no new location/act.
- **Fail signal:** time-skip phrase or new location in pos 1.
- **Result:** `_____`

### T1.2 Pacing = Medium
- **Expected injection (pos 1):** `HARD CONSTRAINT — Scene Pacing: Medium pacing — advance the scene by one beat, then stop. Do not skip ahead in time or jump to a new location.`
- **Expected outcome:** one beat per pos-1 response; next beat on the next turn.
- **Result:** `_____`

### T1.3 Pacing = Fast
- **Expected injection (pos 1):** `HARD CONSTRAINT — Scene Pacing: Fast pacing — advance through multiple beats. Push the story forward rapidly.`
- **Expected outcome:** multiple beats + optional time skip in one pos-1 response.
- **Result:** `_____`

### T1.4 Beat Style = Single
- **Expected injection:** `HARD CONSTRAINT — Beat Style: Single — Resolve this moment in one turn.`
- **Result:** `_____`

### T1.5 Beat Style = Short
- **Expected injection:** `HARD CONSTRAINT — Beat Style: Short — Build the moment across 2–3 turns.`
- **Expected outcome:** moment spread over 2–3 turns.
- **Result:** `_____`

### T1.6 Beat Style = Episodic (Extended)
- **Expected injection:** `HARD CONSTRAINT — Beat Style: Extended — Linger in this moment for 4+ turns (episodic).`
- **Result:** `_____`

### T1.7 Time Shift = None / Small / Medium / Large
- **Expected injection:** `HARD CONSTRAINT — Time Shift: {value} — {description}`.
- **Result:** `_____`

### T1.8 Granularity = Micro / Meso / Macro / Montage
- **Expected injection:** `HARD CONSTRAINT — Granularity: {value} — {description}`.
- **Result:** `_____`

### T1.9 Deepening = SubsequentActors
- **Expected injection (pos 2+):** `- You are a subsequent actor this turn. Deepen the moment established by the first response from your character's perspective. Do not advance to a new beat or position.`
- **Expected outcome:** pos 2+ re-explore the same beat from their POV; never advance.
- **Result:** `_____`

### T1.10 Scene Presence = On
- **Expected injection:** `HARD CONSTRAINT — Scene Presence: on — …`.
- **Result:** `_____`

### T1.11 Climax Mode = multi-encounter (engine)
- **Expected:** `EncounterBoundaryAdvanced` + `EncounterStartDetected` events; `GlobalEncounterCount` increments; non-sexual gap between encounters.
- **Result:** `_____`

### T1.12 Aftermath = husband-contrast (engine)
- **Expected:** after a boundary, an `AftermathCoupleInteraction` phase (wife acts normal to husband).
- **Result:** `_____`

### T1.13 Word Count = small / medium / large
- **Expected injection:** `Word Target: Target {min}-{max} words.` (small 200–400, medium 300–700, large 500–1000; Narrative 2×).
- **Expected outcome:** each interaction word count in range.
- **Result:** `_____`

---

## Part 2 — Combination cases

### T2.1 Slow + Micro + Deepening + Short (the stuck loop)
- **Intent:** deep lingering that still resolves when the male orgasm arrives.
- **Expected outcome:** male orgasm → afterglow → encounter closes → aftermath → next encounter.
- **Observed:** endless "he comes again and again"; `IsEncounterActive` stuck false. See debug `007`.
- **Result:** `_____`

### T2.2 Fast + multi-encounter
### T2.3 Slow + Aftermath
### T2.4 Micro + Word Count(large)
### T2.5 Fast + Deepening(SubsequentActors)
### T2.6 Slow + Time Shift(Large)

---

## Execution log (filled in as run — session 4c676f02, snapshot 2026-08-14 18:42)

| Case | Injection | Outcome | Engine | Verdict |
|---|---|---|---|---|
| T1.1 Pacing=Slow | ✅ exact text | ✅ no time-skips in last 12 ixns | — | **PASS** |
| T1.2 Pacing=Medium | ✅ new wording present (idx 72/76) | ✅ one beat (t72/t76) | — | **PASS** |
| T1.3 Pacing=Fast | ✅ exact text (idx 68) | ✅ multi-beat + "hours later" | — | **PASS** |
| T1.4 BeatStyle=Single | ⏭ not exercised | ⏭ | — | NOT RUN |
| T1.5 BeatStyle=Short | ✅ exact text | ⚠️ soft nudge only (loop, see T2.1) | — | **PARTIAL** |
| T1.6 BeatStyle=Episodic | ⏭ not exercised | ⏭ | — | NOT RUN |
| T1.7 TimeShift=Small | ✅ exact text | ✅ no jumps (Small held) | — | **PASS** |
| T1.8 Granularity=Micro | ✅ exact text | ✅ one-moment responses | — | **PASS** |
| T1.9 Deepening=SubseqActors | ✅ exact text (pos2+) | ✅ pos2/3 deepen, no advance | — | **PASS** |
| T1.10 ScenePresence=On | ⏭ not exercised (Off stored as null) | ⏭ | — | NOT RUN |
| T1.11 ClimaxMode=multi | — | — | ⚠️ 3 advances then #4 stuck (no re-arm) | **PARTIAL** |
| T1.12 Aftermath=husband-contrast | — | ⚠️ no aftermath beat observed | ❌ no Aftermath/TimeSkip events | **FAIL** |
| T1.13 WordCount=300-700 | ✅ exact text | ⚠️ 7/12 in range (5 misses = user inputs/instruction + 1 narr overshoot 1443) | — | **PARTIAL** |
| T2.1 Slow+Micro+Deepening+Short | ✅ all present | ❌ endless orgasm loop | ❌ `IsEncounterActive` stuck false | **FAIL** |

### Key evidence references
- Injection strings verified by scanning all `promptText` in session payload (`testplan_injection.py`).
- Outcome: `testplan_outcome.py` (word count + time-skips), `check_orgasm_loop.py` (orgasm+"not done" markers in every interaction).
- Engine: `RolePlayV2AdaptiveStates` (`IsEncounterActive=0`, `TimeSkipPhase=0`) + `RolePlayDebugEvents` (no boundary/start/aftermath events since 16:13).
- Full root-cause narrative: `debug/007-encounter-stuck-slow-micro-deepening.md`.
