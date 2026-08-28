# B-082 Continuation Settings — Test Case Specification

**Created:** 2026-08-14
**Purpose:** Define expected outcomes for each continuation-settings option individually, then for the combinations that matter, so behavior can be validated against intent rather than against "whatever the model happened to write."

**Two-layer model (important):**
- **Injection (deterministic):** does the correct directive text appear in the prompt? Verifiable mechanically from `Sessions.PayloadJson.interactions[].promptText` or `PromptBuilt` debug events.
- **Outcome (probabilistic):** does the generated content honor the directive? Verified by reading full outputs against the expected behavior below.

Position scoping (verified):
- **Pacing HC → position 1 only.** Positions 2+ always get the fixed containment line `…subsequent actor — build on the beat already established this turn. Do not restart or jump past it.`
- **Deepening → positions 2+** (via `TurnContextSlot`), plus position 1 unaffected.
- **Beat Style / Time Shift / Granularity → all Character positions** (via `ContinuationOverrideSlot`), also Narrative.
- **Scene Presence → only when `On`** (Off stores as `null`, no injection).
- **Climax Mode / Aftermath → engine-side**, not in the prompt.
- **Word Count → all positions** (`Word Target: Target {min}-{max} words.`); Narrative derives `min*2`/`min(max*2,1500)`.

---

## Part 1 — Individual option cases (all other options = "No override / default")

### T1.1 Pacing = Slow
- **Expected injection (pos 1):** `HARD CONSTRAINT — Scene Pacing: Slow pacing — advance within the current beat. Do not leap to a new beat or position.`
- **Expected outcome:** position-1 response stays inside one beat; deep sensory/emotional detail; no time skip, no location jump, no new beat introduced. Position-2/3 deepen the same beat.
- **Pass example (session f1787868 t6):** Becky stays seated on the towel, one extended dialogue, zero movement.
- **Fail signal:** any time-skip phrase (`hours later`, `the next morning`), or a new location/act in position 1.

### T1.2 Pacing = Medium
- **Expected injection (pos 1):** `HARD CONSTRAINT — Scene Pacing: Medium pacing — advance the scene by one beat, then stop. Do not skip ahead in time or jump to a new location.` (new wording post-005)
- **Expected outcome:** position-1 response advances exactly one beat, then stops; no time skip, no multi-beat compression.
- **Pass example (session 4c676f02 t72/t76):** one act (doorway prelude) then a distinct next beat (penetration) on the *next* turn.
- **Fail signal:** covering multiple beats or a time skip in one position-1 response (this was the pre-fix failure, session f1787868 t9).

### T1.3 Pacing = Fast
- **Expected injection (pos 1):** `HARD CONSTRAINT — Scene Pacing: Fast pacing — advance through multiple beats. Push the story forward rapidly.`
- **Expected outcome:** position-1 response compresses multiple beats and may include a time skip ("hours later").
- **Pass example (session 4c676f02 idx 68):** cedar act → "hours later" → fire pit in one response.

### T1.4 Beat Style = Single
- **Expected injection:** `HARD CONSTRAINT — Beat Style: Single — Resolve this moment in one turn.`
- **Expected outcome:** the current beat wraps up this turn; next turn may shift.

### T1.5 Beat Style = Short
- **Expected injection:** `HARD CONSTRAINT — Beat Style: Short — Build the moment across 2–3 turns.`
- **Expected outcome:** the moment is spread over 2–3 turns (not resolved in one, not dragged past 3).
- **⚠ Note:** this is a **soft** nudge only — there is no engine "close after N turns" rule. Encounter end is still event-driven (see Part 3).

### T1.6 Beat Style = Episodic (Extended)
- **Expected injection:** `HARD CONSTRAINT — Beat Style: Extended — Linger in this moment for 4+ turns (episodic).`
- **Expected outcome:** stays in the moment for many turns.

### T1.7 Time Shift = None / Small / Medium / Large
- **Expected injection:** `HARD CONSTRAINT — Time Shift: {value} — {description}` with the matching description.
- **Expected outcome:** the permitted jump size is respected. `None` → continue from the exact moment. `Large` → a day or more is allowed.
- **⚠ Note:** Time Shift had **no consumer** historically; with the B-082 override slot it is injected, but there is no engine enforcement — treat outcome as soft guidance.

### T1.8 Granularity = Micro / Meso / Macro / Montage
- **Expected injection:** `HARD CONSTRAINT — Granularity: {value} — {description}`.
- **Expected outcome:** response density matches — Micro = one moment; Montage = many days compressed.
- **⚠ Note:** same as Time Shift — soft guidance, no enforcement.

### T1.9 Deepening = Standard vs SubsequentActors
- **Standard:** no deepening line for positions 2+.
- **SubsequentActors:** positions 2+ get `- You are a subsequent actor this turn. Deepen the moment established by the first response from your character's perspective. Do not advance to a new beat or position.`
- **Expected outcome (SubsequentActors):** positions 2+ never advance; they re-explore the same beat from their own POV.

### T1.10 Scene Presence = Off vs On
- **Off:** stored as `null`; no injection (correct).
- **On:** `HARD CONSTRAINT — Scene Presence: on — …` stay-present contract.
- **Expected outcome (On):** no time skip; stay physically present in the scene.

### T1.11 Climax Mode = normal vs multi-encounter
- **Engine-side.** multi-encounter enables the `minIxns=4` interaction floor in `TryDetectEncounterBoundaryAsync` and splits the Climax into discrete encounters with time-skips between them.
- **Expected outcome:** distinct `EncounterBoundaryAdvanced` + `EncounterStartDetected` events, `GlobalEncounterCount` increments, and a non-sexual gap between encounters.

### T1.12 Aftermath = off vs husband-contrast
- **Engine-side.** husband-contrast inserts an `AftermathCoupleInteraction` time-skip phase after an encounter (wife acts normal to husband).
- **Expected outcome:** after an encounter boundary, a non-sexual "act normal" beat before the next encounter.

### T1.13 Word Count = small / medium / large
- **Expected injection:** `Word Target: Target {min}-{max} words.` (small 200–400, medium 300–700, large 500–1000). Narrative doubles the range.
- **Expected outcome:** each interaction's word count lands in range.

---

## Part 2 — Combination cases (expected outcomes)

### T2.1 Slow + Micro + Deepening(SubsequentActors) + Short — **the current failure**
- **Intent:** linger deeply in the moment, but still resolve the encounter when the male climax arrives.
- **Expected outcome:** deep one-moment exploration; when the male orgasm occurs, the scene settles into afterglow and the encounter closes (→ aftermath → next encounter).
- **Observed (FAIL):** endless "he comes again and again" loop; `IsEncounterActive` stuck `false`; no boundary event since 15:55:55. See debug `007`.
- **Two bugs surfaced:**
  1. Engine: continuous sex across a boundary never re-arms `IsEncounterActive`, so `encounter-completed` is gated off forever.
  2. Prompt: "don't advance / one moment / deepen" directives suppress the afterglow the detector needs.

### T2.2 Fast + multi-encounter
- **Intent:** compress beats per response, but still split the Climax into encounters.
- **Expected outcome:** each encounter resolves quickly (male orgasm → afterglow), boundary fires, time-skip, next encounter.

### T2.3 Slow + Aftermath(husband-contrast)
- **Intent:** lingering sex, then a clear "act normal to husband" beat.
- **Expected outcome:** after the encounter closes, a non-sexual husband-contrast beat appears before any further sex.

### T2.4 Micro + Word Count(large)
- **Intent:** one moment, but written at length.
- **Expected outcome:** a single moment expanded to 500–1000 words (no beat/time advance despite the length).

### T2.5 Fast + Deepening(SubsequentActors)
- **Intent:** position 1 compresses beats; positions 2+ still only deepen (never advance).
- **Expected outcome:** position-1 fast compression; positions 2+ re-explore position 1's beats without adding new ones. (Deepening overrides position 2+ behavior regardless of pacing — documented orthogonality.)

### T2.6 Slow + Time Shift(Large)
- **Intent:** linger now, but permit a large jump when the scene does shift.
- **Expected outcome:** lingering beat, then a large allowed jump (a day+) rather than an illegal micro-jump. (Time Shift is soft — expect inconsistency.)

---

## Part 3 — Encounter-resolution invariants (the core gap)

These are the rules the current system does **not** enforce and should:

- **R1.** After a multi-encounter boundary advance, the next `encounter-started` evidence MUST re-arm `IsEncounterActive=true` even when the new encounter began continuously (no non-sexual gap). Currently it does not → detection is gated off (debug 007, Factor 1).
- **R2.** Slow / Micro / Deepening directives MUST still allow the encounter to *resolve* (male ejaculation → afterglow). Lingering ≠ never-ending. Currently the wording pushes the model to keep going (debug 007, Factor 2).
- **R3.** The male-orgasm-afterglow signal is the only close trigger; there is no maximum-turn or max-interaction safeguard. Consider a hard cap (e.g. force `encounter-completed` after N interactions in an encounter, or degrade lingering directives once the male has orgasmed).

---

## Verification recipe (per test case)

1. **Injection check** — extract the directive line from the position-scoped `promptText`; assert the exact expected string.
2. **Outcome check** — read the full outputs; check the pass/fail signals listed.
3. **Engine check (Climax Mode / Aftermath)** — query `RolePlayV2AdaptiveStates` (`IsEncounterActive`, `CurrentTimeSkipPhase`, `GlobalEncounterCount`) and `RolePlayDebugEvents` (`EncounterBoundaryAdvanced`, `EncounterStartDetected`).
