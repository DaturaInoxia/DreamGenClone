# Gap Analysis

## Gap Matrix

| Area | Intended Behavior | Observed/Current Behavior | Gap | Priority |
| --- | --- | --- | --- | --- |
| Cadence | Predictable question cadence with bounded intervals | Long stretches with zero decisions in some sessions | Missing causal visibility and possible gate interactions | P0 |
| Observability | Explain why question appeared or skipped | Snapshot counts available, skip reasons mostly opaque | Insufficient diagnostics for confidence | P0 |
| Multi-actor targeting | Questions can be generated for context-appropriate actors | Core logic supports it, UX/runtime confidence still limited | Behavior verification gap | P1 |
| Transparency semantics | Stable user-facing stat effect model | Directional mapping changed across iterations | Consistency and expectation gap | P1 |
| Persistence robustness | Metadata columns always available after startup | Prior runtime column-missing issue occurred, later patched | Need regression guardrails | P1 |

## Root-Cause Themes

- Gate layering complexity without full skip telemetry.
- Runtime verification lagging behind implementation speed.
- Evolving UX semantics causing user expectation drift.

## Required Closure Criteria

- Every decision attempt emits a structured reason trail.
- Cadence invariants validated via deterministic tests and replay.
- Actor ownership and target display validated in runtime sampling.
- Migration checks run at startup and in integration tests.
