# Phase 0 Research: Role-Play Session Screen Separation

## Decision 1: Role-play page flow is split into three dedicated screens

- Decision: Use a three-screen flow: `Create Session` -> `Saved Sessions` -> `Role-Play Interaction`.
- Rationale: This directly satisfies the spec requirement to separate creation and list management from the interaction workspace so commands/interactions have more room.
- Alternatives considered:
  - Keep create/list controls embedded in interaction page: rejected because it violates the explicit separation goal.
  - Two-screen flow (create + interaction with inline list drawer): rejected because session list/delete still compete with interaction space.

## Decision 2: Start vs Continue is status-driven, not inferred

- Decision: Persist explicit role-play session status values `NotStarted` and `InProgress`; drive button label/action from status.
- Rationale: Deterministic action labeling avoids ambiguity from inferred signals (for example interaction count edge cases, migration states, or partially saved sessions).
- Alternatives considered:
  - Infer from interaction history count: rejected as brittle for repaired/imported/stale data.
  - Always show both actions: rejected as confusing and unnecessary.

## Decision 3: Saved-session row metadata is fixed and explicit

- Decision: Every session row exposes `Title`, `Status`, `InteractionCount`, and `LastUpdated`.
- Rationale: This metadata set gives enough context to pick the right session quickly while preserving compact list rendering.
- Alternatives considered:
  - Title only: rejected as insufficient disambiguation.
  - Title plus created timestamp only: rejected; does not indicate active progress.
  - Include many extra fields by default: rejected to avoid visual noise.

## Decision 4: Delete is hard-delete and only from saved-sessions page

- Decision: Support irreversible hard-delete of a selected role-play session and associated interaction history only from the saved-sessions page, gated by confirmation.
- Rationale: Matches clarified user intent and enforces a single, predictable place for destructive actions.
- Alternatives considered:
  - Soft-delete/trash: rejected because not requested and adds lifecycle complexity.
  - Delete from interaction page: rejected due to explicit requirement that delete lives on the list page only.

## Decision 5: Persistence and logging remain on existing defaults

- Decision: Keep SQLite persistence path and Serilog structured logging with Information logs on create/list/open/continue/delete call paths.
- Rationale: Aligns with constitution and existing solution setup; no new storage or logging stack is required.
- Alternatives considered:
  - Alternate store (local JSON/session storage) for this feature: rejected because no approved exception is documented.
  - Minimal logging only on errors: rejected because spec requires major call path coverage at Information level.

## Decision 6: Validation strategy for this feature

- Decision: Validate by acceptance-scenario-driven manual checks now and schedule automated test coverage in tasks phase (service-level unit tests and integration tests for list/open/delete behavior).
- Rationale: Repository currently has no dedicated test project; planning output must still preserve constitution gate visibility and define concrete test work.
- Alternatives considered:
  - Defer validation details entirely: rejected because constitution requires testability and deterministic behavior coverage.
