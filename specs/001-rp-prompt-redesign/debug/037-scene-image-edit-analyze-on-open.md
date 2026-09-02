# 037 — Scene Image Editor: "Analyze on Open" (What the model sees)

**Status:** Implemented (code + DB + UI); runtime re-check pending
**Date:** 2026-08-27

## Report / Request

User requested: when the scene image editor opens, analyze the source image and show the user what the
vision model sees, to help them word the edit intent (and to spot/correct a false model perception before
compiling). Implemented after the abliterated compiler swap (036).

## Design (approved)

- Reuse the existing compiler AppFunction + model (`RolePlaySceneImageEditPromptCompiler`) for resolution —
  no new Model Manager config.
- Background job (same pattern as compilation) + polling; plain structured description call (the multimodal
  client requires a JSON schema, so a `{ "description": "..." }` schema is used).
- Persist the description on the edit session so it is not re-called on every open; Re-analyze button forces
  a re-describe.

## Resolution

Files changed / added:

- **Domain:** `SceneImageEditCompilationModels.cs` — `SceneImageEditSession.DescriptionText` (nullable).
- **Application:** `ISceneImageEditRepository.SetDescriptionAsync(...)`.
- **Infrastructure:**
  - `SceneImageEditRepository.cs` — `SetDescriptionAsync` impl; `GetSessionAsync` reads `DescriptionText`;
    `EnsureSchemaAsync` adds the column.
  - `SqlitePersistence.cs` — `DescriptionText TEXT NULL` in the create schema + additive ALTER TABLE migration.
- **Web:**
  - `BackgroundJobTypes.SceneImageEditDescription = "scene-image-edit-description"`.
  - `SceneImageEditDescriptionJobPayload.cs` (EditSessionId).
  - `SceneImageDescriptionPromptBuilder.cs` — description system prompt + `scene_image_description` schema.
  - `SceneImageEditDescriptionJobHandler.cs` — resolves model, reads image, calls the multimodal client,
    persists `DescriptionText`, writes debug events (completed/failed).
  - `SceneImageEditCompilationService.EnqueueDescriptionAsync(editSessionId, force)` + interface.
  - `Program.cs` — handler registered as `IBackgroundJobHandler`.
  - `SceneImageEditor.razor` — auto-enqueues a description on load when missing; displays
    "What the model sees" above the intent box with a Re-analyze button; `_descriptionPending` keeps polling
    until the description lands.
- **Tests:** `SceneImageEditRepositoryTests.SetDescriptionAsync_PersistsAndRoundsTrips`.

## Validated

- [x] Web build green (0 errors).
- [x] SceneImageEdit tests green (37/37); full suite green (1383/1383).
- [x] Webapp rebuilt + running (HTTP 200 on :5177); additive DB migration applied on startup.
- [ ] User runtime check: opening the editor shows "What the model sees"; Re-analyze re-describes.

## Notes

- Description uses the same abliterated compiler model as compilation, so it reflects exactly what the
  compiler will perceive.
- A failed description job is non-fatal: it logs a `SceneImageEditDescriptionFailed` debug event and the
  user can hit Re-analyze.
