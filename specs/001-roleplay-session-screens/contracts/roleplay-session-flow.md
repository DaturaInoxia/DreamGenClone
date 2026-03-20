# Contract: Role-Play Session Flow

## Scope

Defines the interaction contract between UI pages and application services for role-play create/list/open/continue/delete behavior.

## Route Contract

- `RolePlayCreateRoute`: dedicated create-session page.
- `RolePlaySessionsRoute`: dedicated saved-sessions page.
- `RolePlayInteractionRoute(sessionId)`: dedicated interaction page for one session.

Implemented route paths:

- Create: `/roleplay/create`
- Saved sessions: `/roleplay/sessions`
- Interaction workspace: `/roleplay/workspace/{sessionId}`
- Legacy route `/roleplay` redirects to `/roleplay/sessions`

Navigation invariants:

1. Successful create always routes to `RolePlaySessionsRoute`.
2. Start/Continue always routes to `RolePlayInteractionRoute(sessionId)`.
3. Delete action is available only on `RolePlaySessionsRoute`.
4. Delete requires an explicit confirmation step on the saved-sessions page before command execution.

## Command/Query Contract

### CreateRolePlaySession

Input:

- `Title` (required)
- Optional scenario/context selectors

Output:

- `SessionId`
- `CreatedAtUtc`
- `Status=NotStarted`

Post-conditions:

- Session is persisted.
- Session appears in saved-sessions query results.

### ListSavedRolePlaySessions

Input:

- Optional paging/filter arguments (implementation-defined)

Output list item fields (required):

- `SessionId`
- `Title`
- `Status`
- `InteractionCount`
- `LastUpdatedUtc`

Behavior:

- Includes all non-deleted role-play sessions.

### OpenRolePlaySession

Input:

- `SessionId`
- `RequestedAction` (`Start` or `Continue`)

Rules:

- `Start` is valid only for `Status=NotStarted`.
- `Continue` is valid only for `Status=InProgress`.
- Service resolves status mismatch as explicit error, no partial mutation.

Output:

- Session workspace model bound to `RolePlayInteractionRoute(sessionId)`.

### DeleteRolePlaySession

Input:

- `SessionId`
- `Confirmed=true`

Rules:

- Command is callable only from saved-sessions page flow.
- Delete is hard-delete and irreversible.
- Associated interaction history for the same session is deleted in the same operation boundary.

Output:

- Success/failure result with actionable message.

Post-conditions:

- Deleted session no longer appears in list.
- Deleted session cannot be opened/continued.

## Error Contract

For create/list/open/continue/delete failures:

- Return user-safe actionable message.
- Emit structured log with operation name and session identifier when available.
- Preserve consistency by avoiding partial mutation on failed operations.

## Observability Contract

At minimum, each operation emits Information logs for:

- Request start
- Decision/validation branch (for example status-action mapping)
- Persistence completion or explicit failure

Failures emit Error-level logs with contextual properties including operation and `SessionId` when available.
