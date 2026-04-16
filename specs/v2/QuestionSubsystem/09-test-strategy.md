# Test Strategy

## Unit Tests

- Cadence gate logic with deterministic interaction sequences.
- Skip-reason classification for each gate outcome.
- Actor resolution with persona and non-persona contexts.
- Stat delta application scoped to intended targets.

## Integration Tests

- Decision creation and persistence roundtrip with SQLite.
- Migration startup on legacy database variants.
- UI DTO serialization for ownership/target display.

## Replay/Scenario Tests

- Seeded roleplay sessions with known event timelines.
- Assert decision frequency band and skip-cause distribution.
- Validate progression from `DecisionCount=0` states to expected non-zero decisions.

## Regression Suite

- Keep existing decision mutation tests as baseline.
- Add new tests without weakening current assertions.

## Exit Metrics

- 0 failing tests in decision mutation baseline.
- 0 failing migration integration tests.
- >= 90% branch coverage in cadence/skip-reason logic.
