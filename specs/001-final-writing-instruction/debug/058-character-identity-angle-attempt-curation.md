# 058 — Character Identity angle attempts need independent curation

## Report

- **Reported:** 2026-09-14, during B-121 Panel C implementation.
- **Symptoms:** Selecting angle cards could retain the prior workspace/prompt; right-angle prompts could contain left-facing text; repeated runs overwrote one angle output, leaving no way to choose among attempts.

## Analysis

- B-121 requires independent per-view reruns and user selection of the accepted view.
- The prototype stored one `OutputArtifactId` per angle and dispatched directly from the angle service.
- The existing shared `ImageEditWorkspace` is the approved prompt-preparation and durable-edit UI/pipeline and must remain separate from Panel B.

## Plan

- Preserve immutable attempts for each angle.
- Keep one accepted output on the angle view record, while retaining all attempts.
- Add a selected-angle attempt list with an explicit `Use this` action.
- Ensure angle selection clears/recreates the dedicated workspace and uses the configured editor model.

## Resolution

- Added `CharacterIdentityAngleAttempt` persistence and repository methods.
- Added attempt creation after angle dispatch/result recording and attempt listing/acceptance service methods.
- Added selected-angle attempt selection controls in Panel C.
- Existing Panel C workspace switching changes selected state before asynchronous preparation and uses an angle-specific component key.

## Validated

- [x] Touched-file diagnostics clean.
- [x] Web project build succeeds with existing warnings.
- [ ] Runtime confirmation pending: run multiple attempts for one angle, select a non-latest attempt, and confirm dependent view unlocks from the selected artifact.
