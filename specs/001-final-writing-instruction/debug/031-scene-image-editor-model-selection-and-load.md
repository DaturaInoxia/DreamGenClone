# Scene Image Editor: Load, Model Selection, and Queued Reference Editing

## Report

On 2026-09-06, opening `/roleplay/image-editor/{sessionId}/{interactionId}/{sourceImageId}` failed immediately with:

```
Failed to open the image editor: Function 'RolePlaySceneImageEditPromptCompiler' requires configured inference credential.
```

The user also requires the editor to select only capable configured image-edit models, expose identity/reference selection when the selected endpoint supports it, enqueue edits using the same durable workflow as image creation, attach every completed edit to the Moment image list, and permit repeated edits.

## Analysis

`SceneImageEditor.razor` creates the edit session and then unconditionally calls `SceneImageEditCompilationService.EnqueueDescriptionAsync`. Description analysis resolves `AppFunction.RolePlaySceneImageEditPromptCompiler`, which is a separate multimodal inference credential from the Qwen source-image editor. Its missing credential therefore prevents the edit page from opening before the user can choose an editor model or enter an edit intent.

The edit session, compilation attempts, prompt revisions, and `SceneImageService.EnqueueEditAsync` already preserve a multi-edit lineage. `SceneImageEditingJobHandler` runs queued edits and completed records remain in `ListImagesByInteractionAsync`, so results are included in the Moment's image list. However, the scene edit request and durable payload do not contain a chosen `EditorModelId` or reference selections; the service and handler call `IImageEditorModelResolver.ResolveAsync`, which chooses the function default. This differs from the existing `SceneAssetImageEditCompilationService.EnqueueEditAsync` path, which persists an exact editor model in its durable job payload and serializes reference applications.

`IImageEditorModelResolver.ListImageEditorModelsAsync` already filters its choices through `ResolveByIdAsync`, so it is the correct single source for enabled editor endpoints with all required Qwen settings. `ReferenceApplyPanel` delegates strategy availability to `StrategySelector`, which uses the selected registered model and its qualified capabilities. This can represent text-only operation for endpoints without an executable reference strategy and expose identity conditioning only when it is actually qualified.

## Plan

1. Update `SceneImageEditor.razor` so opening an edit session does not enqueue optional vision description analysis. Keep analysis user-triggered through the existing Re-analyze action, making unavailable compiler credentials a scoped preparation error rather than a page-load failure. Add a required selector supplied by `IImageEditorModelResolver.ListImageEditorModelsAsync` and reuse `ReferenceApplyPanel` for the selected editor model.
2. Extend `SceneImageEditRequest` and `SceneImageEditingJobPayload` to persist the exact chosen editor model and immutable reference applications. Validate a non-empty exact model ID and selection payload in `SceneImageService.EnqueueEditAsync`, then enqueue the durable image-edit job on the existing `ImageEdit` lane.
3. Change `SceneImageEditingJobHandler` to resolve the exact payload model with `ResolveByIdAsync`. Apply approved, qualified reference images only for a selected executable reference strategy; use the ordinary source-image edit operation when all selections are text-only. Preserve the current explicit errors for unqualified/missing references and never fall back to a function default.
4. Add narrow regression coverage for page load behavior, exact model persistence/dispatch, and reference selection behavior. Run Razor diagnostics and a Release Web build. Automated tests remain disabled until the user re-enables them.

## Blast Radius

The change is limited to the source-image editor UI, its request/job contracts, the existing `SceneImageService` enqueue boundary, and `SceneImageEditingJobHandler`. It does not alter Composition creation, Model Manager data, the compiler’s fail-fast credential contract, or the existing Image/Finish stage flows. Existing editing jobs without an exact model ID will fail explicitly rather than silently choosing a new default.

## Resolution

`SceneImageEditor.razor` no longer queues source-description analysis while opening. It now loads the configured callable editor-model choices from `IImageEditorModelResolver`, requires a selected exact model before Run edit is enabled, and supplies that selected model to the existing capability-aware `ReferenceApplyPanel`. The optional Re-analyze action still uses the Qwen2.5-VL image compiler and reports its configured error only when the user requests analysis.

The page creates a new persisted edit session before a new preparation only after a prior edit session completed. This keeps each completed edit immutable while allowing repeated queued edits from the original source image. Existing edit-result navigation continues to support iterative edits from any completed descendant. All results continue to use the current interaction image query, so they appear in the Moment image list.

`SceneImageEditRequest` and `SceneImageEditingJobPayload` now carry `EditorModelId`; the request also carries immutable reference applications. `SceneImageService.EnqueueEditAsync` rejects a missing editor model, resolves that exact configured endpoint before record creation, snapshots the selection on the `SceneImageRecord`, and puts its exact protocol on the durable `ImageEdit` queue.

`SceneImageEditingJobHandler` resolves normal scene edits with `ResolveByIdAsync(payload.EditorModelId)`. It sends approved reference images only when the selected strategy is not `TextOnly`, and validates that each selected strategy is qualified for the exact endpoint. An unavailable/unsupported strategy fails explicitly; there is no fallback to a global image-editor default.

The live Model Manager function default for `RolePlaySceneImageEditPromptCompiler` is assigned to enabled `qwen2.5-vl-7b-instruct-abliterated` (`Qwen2.5-VL 7B abliterated image compiler`, provider `Local`).

## Validated

- [x] Screenshot error traced to eager `EnqueueDescriptionAsync` on editor load.
- [x] Existing durable scene-edit lineage and Moment-list inclusion traced.
- [x] Existing Asset Manager exact-model durable edit pattern traced.
- [x] Razor diagnostics passed for `SceneImageEditor.razor`.
- [x] Release Web build completed successfully with zero errors on 2026-09-06.
- [x] Development-host health checks confirmed `qwen2.5-vl-7b-instruct-abliterated` is reachable and responding through provider `Local` at `https://qwen.kenacwood.net` on 2026-09-06.
- [ ] Automated tests not run because test execution is disabled for this session.
- [ ] Pending user validation with the updated Development host at `http://localhost:5178`.
