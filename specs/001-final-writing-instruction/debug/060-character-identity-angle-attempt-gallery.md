# 060 — Character Identity angle attempts require visual curation

## Report

- **Reported:** 2026-09-14.
- **Symptom:** Panel C displayed attempt numbers and statuses but not the actual generated images, so the user could not know which attempt to select.

## Resolution

- Panel C now resolves each `CharacterIdentityAngleAttempt.OutputArtifactId` to its `SceneAssetImage`.
- The selected angle displays an attempt count and visual thumbnail for every attempt.
- Completed images can be opened in the existing attempt viewer.
- `Use this` is disabled for attempts whose output image is not complete.
- The exact selected attempt is passed to `AcceptAttemptAsync`.
- The gallery refreshes after selection, result completion, and acceptance.

## Validated

- [x] Razor diagnostics clean.
- [x] Web project build succeeds.
- [ ] Runtime confirmation pending: generated left/right attempts are visible and the selected image is accepted exactly.
