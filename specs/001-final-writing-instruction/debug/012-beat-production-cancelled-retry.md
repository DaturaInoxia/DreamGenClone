# Debug Record 012: Cancelled Beat Production has no Retry action

## Report

- Session: `4f2eec18-b190-4beb-ad35-8d520ae5c800`
- Interaction: `bda4f5c0-ce31-4a67-800f-109892d0e0d0`
- Symptom: The Beat Production panel displayed `Production status: Cancelled` without a Retry button after a cancelled production-planning run.
- User request: `there is not retry now`

## Analysis

The owning UI branch is `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`. The panel had a Retry action only for `SceneBeatCatalogueStatus.Failed`; `SceneBeatCatalogueStatus.Cancelled` therefore fell through to the generic status message.

The backend retry path already exists. The Retry confirmation sets `_replaceProductionRequested`, and `GenerateProductionPlanAsync` calls `BeatProductionService.ReplaceAsync` when that flag is set. No service or persistence change is required.

## Plan

- Include `Cancelled` in the existing failed-production action branch.
- Show a cancellation-specific message when no error code is available.
- Preserve the existing confirmation and `ReplaceAsync` flow.
- Build the Web project, run the RolePlay regression suite, and verify the rendered Studio route when the persisted test session is available.

Blast radius: Beat Production status markup only. Existing failure retry, cancellation, version replacement, and completed-plan behavior remain unchanged.

## Resolution

Changed the Beat Production status branch to match both `Failed` and `Cancelled`. Cancelled plans now show `Production planning cancelled` and the existing Retry button, which uses the established new-version flow.

## Validated

- [x] Razor diagnostics: no errors.
- [x] Web build: succeeded with 0 warnings and 0 errors.
- [x] RolePlay regression suite: 1,415 passed, 0 failed.
- [ ] Fresh browser acceptance: the rebuilt host served the route, but the previously used persisted session then reported `Session or interaction not found`, so the button could not be re-asserted in the live tab.
