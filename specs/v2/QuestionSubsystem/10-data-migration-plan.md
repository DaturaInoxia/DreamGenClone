# Data and Migration Plan

## Data Requirements

Decision persistence must include:

- context summary
- asking actor name
- target actor id
- transparency mode data needed for replay and diagnostics

## Migration Principles

1. Additive first.
2. Idempotent startup checks.
3. Safe for partially migrated legacy stores.
4. Backfill defaults where nulls are possible.

## Migration Steps

- Step M1: Ensure columns exist in decision point table.
- Step M2: Backfill missing values for existing rows.
- Step M3: Add runtime schema verification at startup.
- Step M4: Add integration test fixtures for pre-migration DB snapshots.

## Failure Handling

- On missing column detection, fail fast with explicit migration error.
- Emit actionable diagnostic indicating missing migration step.

## Validation

- Startup migration logs must show verified schema state.
- No runtime "no such column" errors under replay tests.
