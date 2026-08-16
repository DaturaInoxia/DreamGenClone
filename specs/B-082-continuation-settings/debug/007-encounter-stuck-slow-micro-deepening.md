# 007 — Encounter Stuck: Continuous Orgasms, Never Advances (Session 4c676f02)

**Created:** 2026-08-14
**Feature:** B-082 continuation settings — combination of Slow + Micro + Deepening + multi-encounter.
**Status:** Root cause identified — two compounding factors (engine gate + directive conflict). No code change yet.

## Report

Session `4c676f02-7bc8-453d-824a-03e0f10f0c62` (Campground Intimacy, theme `ntr-open-world`, Climax phase). User set **all** continuation defaults:

> Pacing=Slow, Beat Style=Short, Time Shift=Small, Granularity=Micro, Deepening=SubsequentActors, Scene Presence=Off, Climax Mode=multi-encounter, Aftermath=husband-contrast, Word Count=300–700.

After interaction idx 113 (16:13), ~6 more turns (idx 114–136) were generated. **Every one stays inside the same sexual act.** The user: *"he just keeps having orgasms, one after the other, and it is never moving forward out of the scene… doing the same thing over and over."*

## Analysis — verified from stored data

### The loop is real (content evidence)

Every new interaction (idx 114–136) contains orgasm words **and** "not-done" markers simultaneously:

| Marker class | Example tokens | Present |
|---|---|---|
| Orgasm | orgasm, come, came, cum, release, spent, climax | in nearly every interaction |
| Non-advance | again, another, more, keep, keeps, not done, not yet, still hard | in **every** interaction |

The model writes "he comes, then is still hard, and goes again" — endlessly. No afterglow, no interruption, no scene exit.

### Factor 1 — Engine gate is stuck OFF (`IsEncounterActive=false`)

`RolePlayV2AdaptiveStates` (18:42):

```
CurrentPhase=Climax  EncounterNum=4  Global=3  IsEncounterActive=0  TimeSkipPhase=0
```

Timeline:
- Encounter #3 ended at 15:55:55 (`EncounterBoundaryAdvanced 3→4`, conf 0.98). That code path sets `IsEncounterActive=false` and `CurrentTimeSkipPhase=CloseScene`.
- **No `EncounterStartDetected` event ever fired for encounter #4** (last one was #3 at 05:17:14).
- `encounter-started` detection requires a *non-sexual → sexual transition*. The content after 15:55 was **continuous sex** (the model never stopped), so no transition existed → encounter #4 never marked active.
- Consequence: `TryDetectEncounterBoundaryAsync` begins with `if (!state.IsEncounterActive) return;` — so `encounter-completed` is **never even checked**. The encounter can never close.

**No `EncounterBoundaryAdvanced`/`EncounterStartDetected`/`AdaptiveCommitGateEvaluated` events fired in the entire 16:13→18:42 window** (verified — event kinds only show PromptBuilt/LlmResponse/SemanticInferredEvidenceApplied/AdaptiveStateUpdateSkipped etc.).

### Factor 2 — The lingering directives are self-reinforcing (prompt-side)

The user's chosen directives all say "stay, don't advance":

- **Slow**: "advance within the current beat. Do not leap to a new beat or position."
- **Micro**: "one response = one moment."
- **Deepening (positions 2+)**: "deepen the moment… Do not advance to a new beat or position."
- **Beat Style Short**: "build the moment across 2–3 turns."

These are doing **exactly what they say** — the model lingers and deepens. But the `encounter-completed` detector needs the model to write a **male ejaculation → bodies spent → afterglow** (i.e., *stop* the act). The "don't advance / one moment / keep deepening" directives actively discourage writing that end state.

So the injections are present and being followed, but they produce the **opposite** of the user's intent: the user wants the encounter to resolve and move to the aftermath, while the directives command the model to never leave the moment.

## Root cause (two compounding factors)

1. **Engine gate bug:** after a multi-encounter boundary advance, if the next encounter begins *continuously* (no non-sexual gap), `encounter-started` never fires, `IsEncounterActive` stays `false`, and encounter-completion detection is permanently gated off.
2. **Directive conflict:** Slow + Micro + Deepening + Short collectively forbid advancement, so the model never writes the afterglow/end state the detector requires — even if the detector were running.

Either alone is bad; together they produce an unrecoverable loop.

## Resolution

None yet (change control). Candidate directions (require user approval):
- **Engine:** re-arm encounter detection when a new sexual act is detected even if `IsEncounterActive` was left false by a continuous transition (or reset `IsEncounterActive=true` on the next `encounter-started`-like evidence).
- **Prompt:** ensure Slow/Micro/Deepening still permit *resolving* the current encounter (e.g. add "when the encounter reaches its natural climax, let it conclude" to the Slow/Micro directive), so lingering ≠ never-ending.
- **Test-case-driven:** encode expected outcomes (see `test-cases.md`) before changing code.

## Validated

- [x] 22+ new interactions all in Climax, all Slow position-1 pacing.
- [x] Content loop confirmed: orgasm + "not done" markers in every interaction.
- [x] `IsEncounterActive=false` stuck; no boundary/start events since 16:13.
- [x] Root cause = engine gate + directive conflict.
- [ ] Fix (pending user approval).
- [ ] Re-validate in a fresh session.
