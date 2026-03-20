# Data Model: Role-Play Session Screen Separation

## Entity: RolePlaySession

Purpose: Canonical persisted session record used by create, list, open/continue, and delete flows.

Fields:

- `SessionId` (string/uuid): Unique identifier.
- `Title` (string): User-facing session title.
- `Status` (enum): `NotStarted` or `InProgress`.
- `CreatedAtUtc` (datetime): Session creation timestamp.
- `LastUpdatedUtc` (datetime): Last modification timestamp.
- `InteractionCount` (integer >= 0): Count of persisted interactions.
- `ScenarioId` (string, optional): Link to scenario context when present.

Validation Rules:

- `Title` is required and trimmed; empty/whitespace values are invalid.
- `Status` must be one of the defined enum values.
- `InteractionCount` must be non-negative.
- `LastUpdatedUtc` must be greater than or equal to `CreatedAtUtc`.

State Transitions:

- Create session: initializes record with `Status=NotStarted`, `InteractionCount=0`.
- Start session: transitions `NotStarted -> InProgress` and opens interaction workspace.
- Continue session: retains `InProgress` and opens interaction workspace.
- Add interaction: increments `InteractionCount`, updates `LastUpdatedUtc`.
- Delete session: terminal transition to non-existent state (hard delete).

## Entity: SessionListItem

Purpose: Read-model shown on saved-sessions page.

Fields:

- `SessionId`
- `Title`
- `Status`
- `InteractionCount`
- `LastUpdatedUtc`

Rules:

- Must be derivable directly from `RolePlaySession`.
- Represents only persisted sessions not yet deleted.
- Drives action label resolution:
  - `NotStarted` -> primary action label `Start`
  - `InProgress` -> primary action label `Continue`

## Entity: SessionDeletionRequest

Purpose: Command payload representing explicit user-confirmed deletion intent.

Fields:

- `SessionId` (required)
- `Confirmed` (boolean, required and true to proceed)
- `RequestedAtUtc` (datetime)

Rules:

- Deletion executes only if `Confirmed=true`.
- Deletion is irreversible and removes associated interaction history for `SessionId`.
- Request can only be initiated from saved-sessions page flow.

## Derived Behaviors

- Saved-sessions list reflects create/delete/open mutations on next load/refresh.
- Attempting operations on stale or already-deleted sessions returns explicit user-facing recovery error and refresh trigger.
