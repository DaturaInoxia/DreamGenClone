# 063 — Character Identity uploaded angle source

## Report

- **Requested:** 2026-09-16.
- **Requirement:** Each face angle should accept an existing uploaded image as an alternative source, while retaining the same edit, attempt, acceptance, and promotion flow as generated images.

## Resolution

- Added `UploadAsync` to the angle service contract and implementation.
- Reused `ISceneAssetService.AddUploadedImageAsync`.
- Uploaded images enter the selected angle's candidate batch and attempt gallery.
- Uploaded attempts can be selected, edited through the shared `ImageEditWorkspace`, reviewed, deleted, and promoted with the same view tag.
- Added an upload control to every Panel C angle card.

## Validated

- [x] Touched-file diagnostics clean.
- [x] Web project build succeeds.
- [ ] Runtime upload, edit, acceptance, and promotion test pending.
