# Risk Register

## R-01: Cadence Overcorrection

Risk:

Cadence fixes could make questions too frequent.

Impact:

Question fatigue and lower narrative flow quality.

Mitigation:

Use replay frequency bands and adjustable thresholds.

## R-02: Diagnostic Noise

Risk:

Instrumentation could flood logs.

Impact:

Harder debugging and larger log storage footprint.

Mitigation:

Structured compact events and one summary per attempt.

## R-03: Actor Targeting Regressions

Risk:

Multi-actor changes can accidentally reintroduce persona bias.

Impact:

Incorrect ownership/target in gameplay.

Mitigation:

Dedicated actor-resolution integration tests.

## R-04: Migration Drift

Risk:

Legacy databases may still miss expected columns.

Impact:

Runtime failures in persisted sessions.

Mitigation:

Idempotent startup verification plus pre-migration fixtures.

## R-05: UI/Engine Semantic Drift

Risk:

Engine and UI may display different transparency semantics.

Impact:

User confusion and trust erosion.

Mitigation:

Single DTO contract and snapshot tests for rendered labels.
