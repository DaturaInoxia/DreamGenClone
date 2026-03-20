# Quickstart: Role-Play Session Screen Separation

## Prerequisites

- .NET 9 SDK installed
- LM Studio available for runtime parity (not required for basic session navigation checks)
- Repository root: `D:\src\DreamGenClone`

## Run Application

1. `dotnet build DreamGenClone.sln`
2. `dotnet run --project DreamGenClone.Web\DreamGenClone.csproj`
3. Open app in browser and navigate to role-play flow.

## Validation Walkthrough

### Route Baseline

Use these implemented routes during validation:

- Create page: `/roleplay/create`
- Saved sessions page: `/roleplay/sessions`
- Interaction workspace page: `/roleplay/workspace/{sessionId}`
- Legacy route check: `/roleplay` should redirect to `/roleplay/sessions`

### Flow A: Create -> Saved Sessions

1. Open `/roleplay/create`.
2. Enter valid required fields and submit.
3. Verify redirect to saved-sessions page.
4. Verify new row exists with:
   - Title
   - Status
   - Interaction Count
   - Last Updated

Expected:

- Status is `NotStarted`.
- Interaction Count is `0`.

### Flow B: Start/Continue Mapping

1. On `/roleplay/sessions`, select a `NotStarted` session.
2. Verify primary action label is `Start` and opens dedicated interaction page.
3. Produce at least one interaction, then return to saved-sessions page.
4. Verify same session now shows status consistent with in-progress behavior and primary action `Continue`.
5. Select `Continue` and confirm interaction page opens with prior context.

Expected:

- Action labels are status-driven, not inferred from UI state.

### Flow C: Delete on Saved-Sessions Only

1. On `/roleplay/sessions`, select delete on a session and cancel confirmation.
2. Verify session remains available.
3. Select delete again and confirm.
4. Verify row is removed from saved-sessions list.
5. Attempt to open deleted session route directly (if route is known/bookmarked).

Expected:

- Delete action exists only on saved-sessions page.
- Confirmed delete hard-deletes session and associated interaction history.
- Deleted session cannot be opened or continued.

### Flow D: Error Handling

1. Simulate stale list state (for example delete session in one tab, attempt open in another).
2. Verify actionable error and list refresh behavior.

Expected:

- No partial mutation.
- User receives clear recovery message.

## Logging Checks

Confirm logs for create/list/open/continue/delete include:

- Information logs on major call paths
- Error logs with contextual properties for failures
- Log levels configurable through app settings (including Verbose)
