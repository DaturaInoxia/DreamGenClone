# Quickstart: Theme State Machine Continuity

**Feature Branch**: `007-theme-state-machine`  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md) | **Data Model**: [data-model.md](data-model.md)

---

## What This Feature Delivers

Adds a reusable, deterministic theme state machine framework to RP V2 so continuity is enforced by persisted machine transitions rather than ad hoc prompt behavior.

First production machine: `infidelity-brief-disappearance`

Transition path:
`PublicBaseline -> EncounterInProgress -> ReturnBeatRequired -> ReintegrationCooldown -> NextDisappearanceEligible`

---

## Prerequisites

1. .NET 9 SDK installed.
2. Existing local SQLite database initialized by app startup.
3. Admin-capable test actor for machine definition and migration actions.

---

## Implementation Sequence

### Phase 0 - Research and Design Baseline

1. Confirm single active machine resolution path and no-fallback contract.
2. Finalize versioning, transition priority, cooldown gates, and authorization model.
3. Lock persistence strategy for machine definitions and runtime snapshot.

### Phase 1 - Data and Contracts

1. Add domain models for machine definition/state/transition and session snapshot.
2. Extend repository and diagnostics contracts.
3. Define service contracts for evaluator, admin mutations, and migration.
4. Add SQLite schema/migrations for machine definition and diagnostics persistence.

### Phase 2 - Runtime Integration

1. Resolve active machine from `ActiveScenario -> RPTheme -> active definition`.
2. Evaluate transitions in `RunRolePlayV2PipelinesAsync` with deterministic priority selection.
3. Persist updated machine snapshot in adaptive state.
4. Apply machine directives to candidate selection and prompt assembly.

### Phase 3 - UI and Authorization

1. Extend RP theme pages for machine editing, validation, and activation.
2. Enforce admin-only mutation/migration actions.
3. Surface machine state and blocked-path reasons in debug/diagnostics UI.

### Phase 4 - Verification

1. Add/extend RolePlay tests for transition correctness, directives, persistence, and fail-fast paths.
2. Validate migration and pinned-version behavior.
3. Run manual lifecycle walkthrough for disappearance -> return -> cooldown -> next eligible cycle.

---

## Developer Commands

```powershell
# Build
 dotnet build DreamGenClone.sln -v minimal

# Run RolePlay-focused tests
 dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter FullyQualifiedName~RolePlay

# Run application
 dotnet watch --project DreamGenClone.Web/DreamGenClone.csproj
```

---

## Manual Verification Flow

1. Open RP theme management and configure/activate `infidelity-brief-disappearance` machine definition.
2. Start a session that resolves to that theme.
3. Drive state to `EncounterInProgress`, then `ReturnBeatRequired`.
4. Verify disappearance candidates are blocked while return beat is pending.
5. Complete return beat and enter `ReintegrationCooldown`.
6. Verify `NextDisappearanceEligible` is blocked until both conditions are met:
   - configured minimum cooldown interactions reached
   - return-beat completion flag true
7. Verify transition to `NextDisappearanceEligible` after both conditions pass.
8. Attempt non-admin machine mutate/migrate action and confirm explicit authorization failure.
9. Force missing required machine config and confirm explicit runtime failure with diagnostics.

---

## Migration and Operations Notes

1. Machine version changes do not affect in-progress sessions automatically.
2. Activate the new definition version in `RPThemes` (theme-level active definition).
3. Use explicit admin migrate action from `RPThemeDetail` to move a specific session snapshot to the active version.
4. Non-admin mutation/migration attempts are denied with explicit authorization failure and warning-level logs.
5. If machine config is missing/ambiguous for a theme that has machine definitions, runtime fails fast and persists a machine failure diagnostic event.

---

## Verification Results (2026-05-09)

### Targeted US3 tests

- Command:
   `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~ThemeMachineResolution_|FullyQualifiedName~ThemeMachinePersistenceTests|FullyQualifiedName~RolePlayDiagnosticsRepositoryTests" -v minimal`
- Result: Passed (`total: 5, failed: 0, succeeded: 5, skipped: 0`)

### Targeted runtime/evaluator regression

- Command:
   `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~ThemeMachineEvaluatorTests|FullyQualifiedName~RolePlaySessionLifecycleTests.ThemeMachine|FullyQualifiedName~RolePlayContinueAsSelectionTests.ThemeMachine|FullyQualifiedName~RolePlayContinuationScenarioGuidanceTests.ThemeMachine|FullyQualifiedName~PhaseLifecycleTransitionTests.ThemeMachine" -v minimal`
- Result: Passed (`total: 6, failed: 0, succeeded: 6, skipped: 0`)

### RolePlay regression sweep

- Command:
   `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~RolePlay" -v minimal`
- Result: Failed (`total: 291, failed: 37, succeeded: 254, skipped: 0`)
- Dominant failure clusters:
   - Existing disposal/SQLite file-lock cleanup issues in RP finish seed tests.
   - Existing expectation mismatches in lifecycle/guidance/selection tests outside this feature scope.

### Solution build verification

- Command:
   `dotnet build DreamGenClone.sln -v minimal`
- Result: Passed.

---

## Expected Evidence of Success

1. Deterministic transition and selection behavior with no hidden fallback path.
2. Pinned machine version remains stable for in-progress sessions unless explicit migrate action occurs.
3. Prompt output includes machine directives when active.
4. Diagnostics show machine init/transition/block/failure events with reason codes.
