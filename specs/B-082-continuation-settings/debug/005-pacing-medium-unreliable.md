# 005 — Pacing Directive: Medium Wording Is an Unreliable Middle Lever (Session f1787868)

**Created:** 2026-08-14
**Feature:** B-082 continuation-settings pacing override / `FinalInstructionSlot` pacing directive.
**Status:** Analyzed — fix approved by user, change pending/implemented.

## Report

Session `f1787868-6901-48a9-a2bc-5008031b2aa3` (Campground Intimacy). User set the B-082 continuation-settings pacing override through a sequence of turns (Slow ×2 → Medium ×2 → Fast ×2, ending on Fast for the final turn). Asked: does the generated output actually differ across the 3 pacing settings?

## Analysis — Verified from stored data

### Directives were sent correctly (verified from `PromptBuilt` debug events)

Position-1 (Becky) prompts received these exact `HARD CONSTRAINT — Scene Pacing` lines:

| Turn | Directive text (verified) |
|---|---|
| t4 (03:06:00) | `Medium pacing — advance the scene by one beat. Move the story forward.` |
| t6 (03:10:33) | `Slow pacing — advance within the current beat. Do not leap to a new beat or position.` |
| t7 (03:12:53) | `Slow pacing — advance within the current beat. Do not leap to a new beat or position.` |
| t8 (03:18:02) | `Medium pacing — advance the scene by one beat. Move the story forward.` |
| t9 (03:19:30) | `Medium pacing — advance the scene by one beat. Move the story forward.` |
| t10 (03:23:10) | `Fast pacing — advance through multiple beats. Push the story forward rapidly.` |
| t11 (03:25:35) | `Fast pacing — advance through multiple beats. Push the story forward rapidly.` |

Confirmed: the override reached position-1 prompts with the correct per-turn value. Not a bug in injection.

### Outputs (full text read, from `LlmResponseReceived` debug events + payload)

- **Slow (t6/t7):** single continuous beat expanded in place (extended dialogue on the towel; one unbroken walk home). No time skip. Clearly distinguishable.
- **Medium (t4/t8):** advances one beat (arrive at beach → man arrives; arrive at fire pit). Clean one-beat step.
- **Medium (t9):** spans fire-close → walk home → night → next-morning garden → Dean's greeting. **Advances as much as the Fast outputs.**
- **Fast (t10/t11):** multiple discrete scene fragments (bed → deck → garden → shower → yard; float → swim → settle → arrival).

### Verdict

- Slow vs Fast: **clearly different** in output (single expanded beat vs multi-fragment).
- Slow vs Medium: **mostly different** (t4/t8 one-beat; Slow lingers).
- Medium vs Fast: **NOT reliably different** — t9 (Medium) covered a full evening→morning transition indistinguishable from Fast scope.

## Root cause — why Medium is an unreliable middle lever

1. **"One beat" is not an enforceable unit for an LLM.** Slow works via an explicit negative (`do not leap to a new beat or position`); Fast works via an explicit quantity (`multiple beats`, `rapidly`). Medium's "one beat" has no operational definition.
2. **The tail "Move the story forward" cancels the restraint.** It actively licenses advancement — the opposite of a one-beat hold.
3. **Scene-state handoff (t9):** the prior turn's narrative close left the story at "evening winding down"; continuity pressure pushed the next position-1 response across a night boundary. Directive could not hold against that.
4. **BuildUp phase guidance leans forward** ("escalate toward intimacy/confrontation"), compounding the weak Medium restraint.

## Plan (user-approved)

Change the Medium directive wording in `FinalInstructionSlot.cs` so it behaves like a real one-beat step instead of an advancement license. Remove/replace the forward-leaning "Move the story forward" and add a concrete negative (do not skip time / jump location), mirroring Slow's enforceable form.

### Exact code location

`DreamGenClone.Web/Application/RolePlay/Prompts/Slots/FinalInstructionSlot.cs` (Slot 17, Zone C), pacing switch in the position-1 branch. `ScenePacing` enum: `Slow=0, Medium=1, Fast=2`; Medium is the `_ =>` default branch.

## Resolution — Implemented 2026-08-14

Changed the Medium wording from:

> `HARD CONSTRAINT — Scene Pacing: Medium pacing — advance the scene by one beat. Move the story forward.`

to:

> `HARD CONSTRAINT — Scene Pacing: Medium pacing — advance the scene by one beat, then stop. Do not skip ahead in time or jump to a new location.`

Rationale (mirrors Slow's enforceable negative): "then stop" ends the forward impulse; "do not skip ahead in time or jump to a new location" gives the model concrete prohibitions to follow — the thing "one beat" alone could not convey.

Build: Web project compiles 0 errors. Pacing/SceneDirection tests: 24/24 pass.

Note: the webapp running at the time held the DLL lock; it was stopped for the clean build and restarted afterward (`helpers/start-webapp.ps1`, http://localhost:5177). The running process is preserved.

The 2 `SlotContractTests` failures (`FinalInstructionSlot_CharacterVariant_FirstPersonPOV`, `FinalInstructionSlot_NarrativeVariant_OmniscientZeroDialogue`) are **pre-existing** — documented in `/memories/repo/pre-existing-test-failures.md` (WriteAsync returns empty string in test context due to a resolver throwing; assert on "Writing Instruction:" section header, not pacing). Unrelated to this change.

## Validated

- [x] Directive text per turn verified from `PromptBuilt` debug events (session f1787868).
- [x] Full outputs read (Slow/Medium/Fast) — Slow vs Fast differ; Medium vs Fast unreliable.
- [x] Implement Medium wording change.
- [x] Build: Web 0 errors.
- [x] Pacing/SceneDirection tests: 24/24 pass.
- [x] Re-validated in live session data — see `debug/006-pacing-medium-fix-validated.md` (session 4c676f02, turns after interaction cf3aeba1): the new Medium wording produced clean one-beat steps while the adjacent Fast turn skipped hours. Fix confirmed working.
