# Runtime Observation (HP1 Anchor)

## Evidence Sources

- Workspace logs under `DreamGenClone.Web/logs`.
- HP1 session identity observed as `2958b5fa-0250-4d46-84df-825d3087da54`.

## HP1 Facts from Logs

- Session creation observed on 2026-04-06 with label HP1.
- Multiple continuation and unified prompt operations observed.
- HP1 was later hard-deleted on 2026-04-10.

## Implication of HP1 Deletion

Because HP1 was deleted, direct late-phase RolePlayV2 diagnostics for HP1 are not present in currently available logs. This limits direct replay-level assertions for the final HP1 lifecycle.

## Closest Runtime Analog Signals

A separate active V2 session (`bf71f1be-2d39-40bd-bb1a-d41d2fce2eff`) shows a pattern relevant to user-reported behavior:

- Repeated diagnostics snapshots with `DecisionCount=0` during long stretches.
- Candidate counts increasing while decision count remains flat.
- Later transition to `DecisionCount=1`, then prolonged plateau.

## Preliminary Runtime Conclusions

1. A cadence/eligibility bottleneck can keep decisions from surfacing for extended interaction runs.
2. Existing logs reveal counts but not complete skip-reason causality.
3. Runtime confidence requires structured skip-reason telemetry per attempt.

## Required Observability Action

Before further behavior tuning, implement explicit diagnostics fields per decision attempt:

- cadence gate result
- cooldown gate result
- candidate availability
- prerequisite failures by template/option
- actor targeting resolution result
- persistence success/failure

This closes the loop between implemented logic and runtime behavior claims.

## Q1 Runtime Check (2026-04-16)

- Session: `Q1`
- SessionId: `e44ee51b-e989-4b73-b13e-4b81448b753d`
- User validation context: first default continue completed and visually looked correct.

### What Looked Healthy

1. First-continue pipeline completed cleanly:
	- `PromptBuilt` -> `LlmRequestSent` -> `LlmResponseReceived` -> `InteractionPrepared` -> `AdaptiveStateUpdated` observed in sequence.
2. Adaptive state persisted without runtime/schema faults:
	- `RolePlayV2AdaptiveStates` row present with `CurrentPhase=BuildUp`, `CycleIndex=0`, `ActiveFormulaVersion=rpv2-default`.
3. Scenario candidate evaluation persisted:
	- `RolePlayV2CandidateEvaluations` rows present with explicit rationale (`Candidate blocked by willingness or eligibility pre-gate.`).
4. No unsupported-session error evidence observed for this session.

### Decision/Question Subsystem Findings

1. Prompt diagnostics repeatedly reported `decisions=0` during the observed first-continue window.
2. No decision/question event records were surfaced in the session debug stream for this window.
3. Candidate evaluation and concept injection telemetry exists, but explicit decision-skip causality remains thin at the event level.

### Enhancement Notes Captured

1. Add explicit per-attempt question skip diagnostics event in-session (not only aggregate prompt counters), including:
	- cadence gate result
	- cooldown gate result
	- template eligibility failures
	- actor-targeting resolution
2. Emit a single concise "decision attempt summary" record per interaction window to satisfy determinism/explainability goals in `00-scope-and-goals.md`.
