# B-140 — Image provenance, tags and round-trip (tasks)

**Created:** 2026-10-02
**Order:** phases are sequential; tasks within a phase are dependency-ordered. Each task is done when its acceptance
holds and the affected tests pass.

## Phase 1 — complete the machine record

- [x] **T1.1** Record the full input set in the completed metadata — `SceneAssetGenerationJobHandler` writes
  pose / identity / body / body-angle / character-LoRA ids into `AssociationMetadataJson`.
  *Acceptance:* `SceneAssetImageGenerationDetailsTests.FromImage_ReadsEveryInputTheCompletedMetadataRecorded` passes.
- [x] **T1.2** Typed read-back — `SceneAssetImageGenerationDetails.FromImage` (null = not recorded).
  *Acceptance:* reader tests pass; `GenerationDetails.razor` renders names on `/assets/{id}/images/{img}/review`.
- [x] **T1.3** Scene (Krea 2) LoRAs on the asset path — add `SceneLoras` to `SceneAssetImageGenerationOptions` and
  `SceneAssetGenerationJobPayload`; resolve via `SceneLoraResolver` in the handler and apply to the graph; record
  `sceneLoras` in the metadata.
  *Acceptance:* a render with a scene LoRA records the file name + strength; an unknown/wrong-family file fails by
  name; the review panel shows the selection.
  **Delivered:** options/payload/`SceneAssetService`/handler resolve+apply+record, `SceneLoraResolver.ResolveAsync`
  taking the raw selection list, and the panel's Scene LoRAs rows.
  Also delivered (2026-10-02, B-137 agent): the **catalog-run surface** half — `ImageSuiteRenderRequest.SceneLoras`
  → `SceneAssetImageGenerationOptions.SceneLoras` → the job payload, the report's `SceneLoras` /
  `SceneLoraCount` (counted separately from `LoraCount`: one number for both would let a run that lost its unlock
  still read as "1 LoRA applied"), and the family-filtered `SceneLoraPicker` on the Playground run card (cleared when
  the model changes, because a scene LoRA binds to a model FAMILY). The handler now calls the resolver's raw-selection
  overload directly, so validation has one owner.
  *Evidence:* `ImageSuiteRenderDriverTests.RenderAsync_CarriesTheSceneLorasTheRunWasGiven` +
  `..._WithNoSceneLoras_AppliesNoneRatherThanAnEmptyChain` (every image in the set, chain order preserved, null not
  empty when nothing is selected); combined regression 374/374 green.
  *Also fixed here:* 3 `SceneImageService.DispatchEditAsync` call sites left without the new `outpaint` argument by the
  outpainting work — all three are whole-frame passes, so `outpaint: null` is correct.
- [x] **T1.4** Applied presets — add `AppliedPresetsJson` (axis → key + clause) to options + payload; write it from
  `PromptAssetCreator` when a lighting/expression preset is applied; record it; show it in the review panel.
  *Acceptance:* an image made with an expression preset shows the preset key + clause; `GenerationDetails` reads it.
  **Delivered:** `AppliedImagePreset(Axis, Key, Clause)` (both halves recorded: the clause is what rendered and what a
  re-apply replaces, the key is what the picker reselects), `options.AppliedPresets`, payload, model mapping, handler
  `appliedPresets` in the metadata, composer `_appliedPresetKeys` + `AppliedPresetRecords()`, panel rows.
- [x] **T1.5** Extend `SceneAssetImageGenerationDetails` to read `sceneLoras` + `appliedPresetsJson`; extend
  `GenerationDetails.razor` rows. Tests for both new fields and for their absence.
  *Acceptance:* `SceneAssetImageGenerationDetailsTests` covers both fields present AND absent.
  **Delivered:** `SceneLoras` (fileName @ strength — purpose) and `AppliedPresets` rows; 3 new tests.

## Phase 2 — tags

- [x] **T2.1** `ImageTagCatalog` — one static vocabulary owner for the axes (character, stance, direction, camera,
  rating, sex position, wardrobe, location, LoRA character, scene-LoRA purpose), reusing `PoseMetadataLabels` so
  image tags equal pose keywords.
  *Acceptance:* catalog is a single class with no duplicated strings; unit test pins the axes.
  **Delivered:** `DreamGenClone.Web/Application/RolePlay/ImageTagCatalog.cs` with 20 prefixes (the plan's axes plus
  `pose`, `category`, `library`, `position`, `variant`, `model`, `reference`, `source`), `Normalize`/`Tag`/`Parse`/
  `Serialize`/`NormalizeAll`, the four enum-typed tag helpers delegating to `PoseMetadataLabels`, `StanceFromName` for
  a payload stance, and `FromPosePreset`. `ImageTagCatalogTests` (13 tests).
  **Deviation (layer):** it lives in the Web layer, not Domain — it reuses `PoseMetadataLabels`, which is a Web class,
  and the storage layer was kept vocabulary-free (it stores a list and matches a caller-normalized query).
- [x] **T2.2** `SceneAssetImage.TagsJson` column — idempotent `pragma_table_info`-guarded ALTER + repository
  read/write (write path only; no new table).
  *Acceptance:* repository round-trips the column; schema is idempotent on an old DB.
  **Delivered:** column appended LAST in `ImageSelectSql` (the reader is ordinal), read at index 36, written on INSERT
  but deliberately absent from the ON CONFLICT list so an ordinary upsert cannot erase hand-added tags; narrow writers
  `SetImageTagsAsync` (replace) and `AddImageTagsAsync` (union, used by the completion step) + `SearchImagesByTagAsync`
  (`json_each`, matched on the tag's VALUE). 6 new repository tests.
- [x] **T2.3** `RenderTagBuilder` — builds the tag list from payload + options + cell/position context
  (catalog cell → sex position + actors + rating; pose cell → stance/direction/camera/rating + character/LoRA). Written
  at enqueue in `AddGeneratedImageAsync` so tags exist even if the render fails.
  *Acceptance:* builder tests for a catalog cell and a pose cell; tags recorded on the image.
  **Delivered:** `RenderTagBuilder` (pure) over a `RenderTagRequest`; declared tags travel options → payload → handler
  (`ImageSuiteRenderDriver` declares position + variant + suite source + character; `PromptAssetCreator` declares
  character + `source:composer`); derived tags are read by the handler (pose preset metadata + library, payload stance,
  applied presets by key word, scene LoRAs by catalog purpose, model, owner by asset type). 12 builder tests.
  **Deviation (ordering):** tags are written at COMPLETION, not enqueue — a tag is a claim about a picture, and a render
  that failed has no picture. The write ADDS rather than replaces, so a tag added by hand while the render ran survives.
- [x] **T2.4** Asset page tag surface — read-only chips + manual add/remove on the asset image.
  *Acceptance:* tags shown; add/remove persists.
  **Delivered:** `Components/Assets/ImageTags.razor` (chips; axis pick list from the catalog; live `prefix:value`
  preview; service refusals surfaced), editable on the completed image's card in `AssetStudioView` and on the review
  page, read-only in tag-search results.

## Phase 3 — exact round-trip

- [x] **T3.1** `ImageGenerationDraft.FromImage` reads `posePresetId`, `poseSkeletonRelativePath`, `characterLoras`,
  `sceneLoras`, `appliedPresets`.
  *Acceptance:* draft tests cover each new field, including their absence.
- [x] **T3.2** `PromptAssetCreator` reselects the pose (as a `PoseLibrarySkeleton` binding), the character LoRAs, the
  scene LoRAs and the applied preset(s).
  *Acceptance:* loading a completed image into the panel restores every recorded selection.
  **Delivered:** `WithPoseBinding` (adds the binding a handler-resolved pose never recorded, using the composer's own
  strategy), `_characterLoras`/`_sceneLoras` restore, `RestoreAppliedPresets` (seeds the clause map WITHOUT re-applying
  the clause, since the restored prompt already contains it), a scene-LoRA picker in the panel, and a notice that names
  what was restored rather than counting it.
- [x] **T3.3** "Load into composer" on the asset review page (seeds the Generate panel from the same draft).
  *Acceptance:* one click opens the generator with the exact inputs.
  **Delivered:** review-page "Reproduce this render" → `/asset-studio/{AssetId}?loadImageId={ImageId}` → the asset
  page's existing `LoadIntoGenerator`, applied once (so the page's polling cannot stomp later edits).

## Phase 4 — search

- [x] **T4.1** Asset Manager search matches `TagsJson`; each matched tag rendered as a chip.
  *Acceptance:* "Becky", "kneeling", "nsfw" each return the expected images.
  **Delivered:** a "Find reference images by tag" panel on the Asset Manager calling
  `ISceneAssetImageTagService.SearchImagesByTagAsync` (not a client-side filter over the loaded tree), with each result
  showing its owner and its own chips.
  **Deviation (surface):** the existing search box filters the ASSET tree; tags live on IMAGES, so the tag search is its
  own panel returning image cards rather than a second meaning for the asset search box.
- [x] **T4.2** Reference-image filter — scope the search to reference-capable images.
  *Acceptance:* the filter narrows results to references only.
  **Delivered:** an "Only images approved as references" scope, filtered on `ProductionApprovalStatus.Approved` — the
  same bar the reference pickers read — applied as a display scope over the returned rows.
- [x] **T4.3** UI contract test for the search + filter wiring (source-level, matching the repo's Razor contract
  tests).
  **Delivered:** `ImageTagUiContractTests` (4 tests): tag component contract, detail-page completion guard, manager
  search + filter wiring, review-page tag editor + reproduce route.

