# B-128 — Qwen-Image-2.1 app integration (generation + multi-reference editing) and Image Composer UI rework plan

**State:** `implemented` (engine/plumbing) — UI rework `planned`
**Created:** 2026-09-23
**Related:** B-032 (scene image generator), B-121 (character identity studio), B-125 (Qwen source-image editor + editor LoRA), B-126 (multi-character scene composition), B-003 (pose/controlnet render)

---

## 1. What this item is

Wire **Qwen-Image-2.1** (local ComfyUI, WOOD-GAME-MAIN RTX 5080 16 GB) into the application as a **full replacement for the Flux / Juggernaut image models**:

1. **Generation** — prompt-only renders.
2. **Generation WITH references** — character faces and/or approved scene assets (location) condition the render in **one call**, no identity mechanism and no repair pass.
3. **Editing** — the model must be selectable in the **Edit Paths**, including edits that include reference images.

Measured on the host 2026-09-23 (see `helpers/local-comfyui-host/README.md` for the raw run log):

| Cell | Result |
| --- | --- |
| t2i 1024² | 55.1 s cold |
| rgba 1024² | 15.1 s, **genuine native alpha** (35.4 % pixels alpha = 0) |
| edit (1 ref) | 106 s / 80.5 s |
| generation with 2 identity refs | 30.5 s / 40.2 s — both identities correct in a **new** scene, one pass |
| location + 2 faces in ONE call | 100.6 s / 120.4 s — works; slot order sets placement (`image_1` anchors left) |
| wide-frame identity edit | **fails** — reference head angle dominates; a tight head crop transfers identity reliably |

Envelope: `cfg = 1`, `euler/simple`, 25–50 steps, up to 16 autogrow reference slots, `resolution` is a **pixel budget, not a dimension**.

---

## 2. What shipped

### 2.1 Model rows (dev DB `dreamgenclone.dev.db`)

Two rows are required because the generation path and the edit path resolve through different resolvers, and both need a **fully configured** row (the selectors list only rows that resolve end-to-end).

| Row | Id | ModelIdentifier | Purpose |
| --- | --- | --- | --- |
| Qwen-Image-2.1 (Local ComfyUI) | `3f1c9a52-7d4e-4c8b-9a21-6b0e5d2c8f41` | `qwen_image_2.1_int8_convrot.safetensors` | Generation (`RolePlaySceneImage` default / pinned render model) |
| Qwen-Image-2.1 Editor (Local ComfyUI) | `8b2e4d16-3a5f-4c7e-9d10-5f6a7c8b9d20` | `qwen_image_2.1_editor` | Editing + Finish (`RolePlaySceneImageEditor`) |

Both use provider `80262aaa-b069-4be3-b6e2-8fb25c9f7520` (Local ComfyUI WOOD-GAME-MAIN 5080, `ImageProtocol = ComfyUi`, `ContentPolicy = AdultAllowed`).

The editor row keeps the **real artifact names** in `ImageEditorDiffusionModel` / `ImageEditorTextEncoder` / `ImageEditorVae` and uses a **slug `ModelIdentifier`** — matching the convention of every pre-existing editor row (`qwen_edit_local_remix_aio_v20`, …). The slug is also required by the `UNIQUE (ProviderId, ModelIdentifier)` constraint, because the generation row already owns the artifact filename.

`CapabilityQualificationsJson` (both rows) carries the single source of truth for the 2.1 artifacts and envelope:

```json
[{"Strategy":"NativeMultiReference","Qualified":true,"ProofId":"qwen-image-2-1-local-2026-09-23",
  "UnetName":"qwen_image_2.1_int8_convrot.safetensors",
  "TextEncoderName":"qwen3vl_8b_int8_convrot.safetensors",
  "VaeName":"qwen_image_2.1_vae_bf16.safetensors",
  "Resolution":1024,"MaxReferences":16}]
```

`QwenImage21ModelSettings.Resolve` reads it and **fails fast** (`missing_qwen_image_21_qualification`) when it is absent or not qualified — there is no fallback artifact set.

### 2.2 Code

| File | Change |
| --- | --- |
| `DreamGenClone.Domain/ModelManager/SceneImageModelFamily.cs` | `QwenImage21 = 5` + `(QwenImage21, NaturalLanguage)` compatibility |
| `DreamGenClone.Domain/ModelManager/QwenImage21Refs.cs` | *(new)* artifact + budget record |
| `DreamGenClone.Web/Application/ModelManager/QwenImage21ModelSettings.cs` | *(new)* qualification/artifact resolution |
| `DreamGenClone.Web/Application/RolePlay/SceneImagePromptCompilers.cs` | `QwenImage21SceneImagePromptCompiler` (fam 5 / dialect 3) + scene-asset compiler arm |
| `DreamGenClone.Application/Abstractions/IReferenceConditionedImageClient.cs` | *(new)* reference-conditioned generation seam (`GenerateWithReferencesAsync`) |
| `DreamGenClone.Infrastructure/Models/ReferenceConditionedImageClientDispatcher.cs` | *(new)* routes `ComfyUi`; anything else throws `unsupported_reference_image_protocol` |
| `DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs` | `BuildQwenImage21Workflow`; `GenerateWithReferencesAsync`; shared `ComfyUiWorkflowTransport` upload |
| `DreamGenClone.Infrastructure/Models/ComfyUIImageEditingClient.cs` | `BuildQwenImage21EditWorkflow` (`QwenImage21Native`) |
| `DreamGenClone.Domain/ModelManager/ImageEditorGraphKind.cs` | `QwenImage21Native = 2` |
| `DreamGenClone.Web/Application/ModelManager/ImageEditorModelResolver.cs` | resolves `ResolutionBudget` for that graph kind only |
| `DreamGenClone.Domain/RolePlay/SceneImageRecord.cs` | `SceneImageRenderMode.NativeReference = 2` |
| `DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs` | `NativeReference` branch + ordered reference-set builder |
| `DreamGenClone.Web/Program.cs` | DI for the compiler + the reference-conditioned client |
| `ModelManager.razor`, `ModelDetailsEditor.razor` | family + editor-graph options |

**Graph contract (critical):** the reference inputs of `TextEncodeQwenImage21` are an **autogrow group addressed with flat dotted ids** — `images.image_1`, `images.image_2`, … A hand-built `{"images": {...}}` dict is **silently ignored** (the job still succeeds with zero references) and a flat `image_1` kwarg raises `TypeError`. Both silent-failure shapes are asserted ABSENT by tests. The source image of an edit is always `image_1`; reference *n* is `image_{n+1}`.

### 2.3 Proof

- **Host E2E**: the app's own emitted graph was submitted unchanged through `run-local-proof.ps1 -ComfyUiUrl http://192.168.0.11:8188` → prompt `039c2a57-be71-4078-bda6-9bbb76bc3633`, all 8 node classes present, clean photoreal 1024² image (visually verified).
- **Tests**: 191/191 green across every touched area (`QwenImage21*`, `ComfyUIImageClient*`, `ComfyUIImageEditingClient*`, `SceneImageModelFamily`, `SceneImagePromptCompiler*`, `SceneImageRenderingJobHandler*`, `SceneImageServiceJob`, `ImageEditor*`, `MediaEdit*`, contract tests).

---

## 3. Path capability matrix

| Path | Executor | Model-dependent? | 2.1 status |
| --- | --- | --- | --- |
| Generate (prompt only) | `ComfyUIImageClient.GenerateAsync` → `BuildQwenImage21Workflow` | yes | **works** |
| Generate with references | `SceneImageRenderingJobHandler` `NativeReference` → `IReferenceConditionedImageClient` | yes | **works** (code complete, fail-fast on empty ref set) |
| Edit (Studio / composer) | `SceneImageEditingJobHandler` → `ResolveByIdAsync(EditorModelId)` → `EditWithReferencesAsync` | yes | **works** (select the 2.1 editor row; refs supported) |
| Finish | `SceneImageEditingJobHandler.ExecuteFinishAsync` → `ResolveAsync` (configured editor) → `EditWithReferencesAsync` | yes | **works** — same graph kind |
| Crop | `SceneImageService` (`Operation = Crop`) → deterministic crop window | **no** — no image model is involved | model-agnostic by design |
| Enhance | `SceneImageService` (`Operation = Enhance`) → `IImageUpscaleClient` (UpscaleModelLoader + ImageScaleBy/Lanczos) | **no** — the upscaler, not the editor | model-agnostic by design |
| Pose | `RenderPoseControlledAsync` → `IPoseConditionedImageClient` (ControlNet skeleton) | yes (different client) | **DEFERRED** — no 2.1 ControlNet weights exist (HF search for `Qwen-Image-2.1 controlnet` returns zero results; the host has only SDXL depth/canny + `thibaud-openpose-xl2`). Combining pose with a native-reference render **fails fast today** with an explicit message instead of silently dropping the skeleton. |

---

## 4. Pose — SOLVED on 2.1 by skeleton-as-reference (measured 2026-09-23)

**Earlier assumption corrected.** This document previously said pose was blocked on 2.1 until upstream
ControlNet weights existed, on the reasoning that a skeleton is a ControlNet conditioning map and native
reference conditioning only transfers appearance. A five-cell proof on the local host shows that is
**wrong**: 2.1 reads an OpenPose skeleton in a reference slot as pose guidance.

| Cell (832×1216, seed 20260922) | `<image1>` | Result | Verdict |
| --- | --- | --- | --- |
| `posePlate` | none | clean photoreal frontal standing figure | plate produced |
| `poseTextRef` | Becky face (stance in **words**) | standing, arms at sides, feet apart | pose **pass** |
| `poseRef` | **photoreal plate** + Becky face | reproduced the plate's man wholesale; face reference had no effect | **fail** as a pose donor |
| `poseSkel` | **standing skeleton** + Becky face | figure adopted the skeleton's splayed limbs and wide stance | **pass** |
| `poseSkelKneel` | **kneeling skeleton** + Becky face — identical wording, same seed, prompt says "standing" | **kneeling with both arms raised overhead**, exactly the skeleton's geometry | **pass (decisive)** |

`poseSkelKneel` is decisive: with the same seed and wording, and the word "standing" in the prompt, the
output follows the *kneeling* skeleton instead. The pose came from the skeleton image.

Two rules follow:

1. **Skeletons in a reference slot are pose guidance on 2.1** — the existing library
   (`DreamGenClone.Web/wwwroot/pose-library/{standing,squatting,kneeling}.png`, `BodyStanceSkeletons`,
   and the 472-pose OpenPose pack) is directly usable. **No 2.1 ControlNet is required for pose, and no
   photoreal pose plates are needed.**
2. **A photoreal person in slot 1 is the BASE IMAGE to reproduce, not a pose donor** (`poseRef` copied
   the plate's man and ignored the face reference). Photos and skeletons behave differently in the same
   slot, so "pass a photo to convey a pose" is not a safe mechanism.

Not yet measured: precision of adherence (limb angles are approximate), ambiguous stances (lying /
all-fours — ControlNet itself fails on those), and a skeleton composed with a location reference plus
several faces in one call.

**Consequences:**

- `Pose` becomes a reference **kind** with a per-model mechanism — skeleton-as-reference (2.1) vs the
  `PoseControlNet` graph (SDXL/FLUX, already qualified) — selected by capability, never silently switched.
  A future 2.1 ControlNet would be a drop-in third option, not a prerequisite.
- The handler guards that refuse "pose + native reference" are now **wrong** and are replaced by
  "pose travels as a skeleton reference".
- The ControlNet workstream stays, but only for SDXL/FLUX (and for *location* structure, which a skeleton
  does not provide).

### 4.1 Wired into the surfaces

| Surface | Change |
| --- | --- |
| `SceneImageRenderingJobHandler` / `SceneAssetGenerationJobHandler` | the mechanism is decided ONCE per render by `ReferenceStrategyResolver.ResolvePoseAsync` (qualified `PoseControlNet` graph first, then the model's own reference slots); a skeleton reference is appended **after** the identity face and the approved scene assets, so slot order is deterministic |
| `CompositionComposer.razor` | the pose switch is offered when the pose and identity mechanisms are compatible, and the identity checkbox is no longer disabled for a model whose identity travels as references |
| `CharacterIdentityBodyService.ResolvePoseAvailabilityAsync` | the same `ResolvePoseAsync` decision, so an offered pose switch is never one the render refuses; on the reference route the answer carries `DefaultStrength = null` because there is no ControlNet to weight |
| `BodyViewsPanel.razor` | the pose switch is withheld only for the genuinely impossible mix (identity as a reference image **plus** pose through ControlNet); a pose asked for on a model that cannot carry one now **stops the submit with the resolver's reason** instead of being dropped from the request (`PoseRequestedButNotCarried`); the ControlNet strength box is shown only when the route has a strength to set |

The `Pose` addition is done **once** across all four create surfaces rather than retrofitted per surface:
"integrate the Pose now, rather than retrofit it into the workflow later."

---

## 5. Image Composer UI rework plan (create + edit accepting multiple image references)

### 5.1 Current state (verified)

- `Components/Shared/ReferenceApplyPanel.razor` renders **one card per `ElementKey`** (`Identity`, `Body`, `Wardrobe`, `Location`) and offers a **hardcoded** strategy list:

  ```csharp
  "Identity" => ["TextOnly", "ReferenceConditioning", "NativeMultiReference", "Lora"],
  "Body"     => ["TextOnly", "Lora"],
  "Wardrobe" => ["TextOnly", "WardrobeTryOn"],
  "Location" => ["TextOnly", "ControlNet"],     // ← no NativeMultiReference
  _ => throw new InvalidOperationException(...) // ← throws on any new element
  ```

- The panel filters that list against an `ExecutableStrategies` parameter passed by the host page, so the **model's** capability set is already plumbed in — but the per-element hardcoding means a Location reference can never be rendered natively even on a model that supports it.
- **Who offers what today** (verified; the two pages below were then fixed, see §5.1.1):

  | Host | `ExecutableStrategies` | Net effect for 2.1 |
  | --- | --- | --- |
  | `Components/Editing/ImageEditWorkspace.razor` | `["TextOnly", "NativeMultiReference"]` | Identity references already work natively on the Edit path |
  | `Components/Pages/SceneImageStudio.razor` (`FinishReferenceStrategies`) | was `["TextOnly", "ReferenceConditioning"]` | native references were **not** offered on the production/Finish surface |
  | `Components/Pages/CompositionComposer.razor` | was `["TextOnly"]` | references were effectively disabled |
  | `Components/Pages/SceneImageCompose.razor` | *(no panel at all)* | no references on Create |
  | `Components/Assets/PromptAssetCreator.razor` | `["TextOnly"]` | references disabled on the asset-create path (still open) |

  The combination of the two hardcoded layers (element kind → strategy, plus the page's executable list) means the Edit path can already send identity references to 2.1, while Location references cannot be sent natively from **any** surface.

### 5.1.1 Defects found and fixed 2026-09-24 (operator report)

Two independent defects, both reported as "with 2.1 the capabilities are not refreshed / the Pony prompt lands in the SDXL prompt":

1. **The page-level strategy lists were frozen.** Because `ExecutableStrategies` was a per-page constant, selecting Qwen-Image-2.1 left the Identity row offering only `TextOnly` — identity could not be requested at all ("the identity is not available to allow but it should be"). Fixed by carrying the model's real capability set on the model choice:
   - `ReferenceStrategyResolver.ListAvailableStrategies(model, provider)` — `TextOnly` plus every declared-and-qualified graph strategy, answered through the same `Resolve` the render calls, so the offered set can never exceed the executable set.
   - `SceneImageModelChoice.QualifiedStrategies` carries it; `ModelResolutionService.ListSceneImageModelsAsync` populates it.
   - `CompositionComposer.razor` passes `SelectedModelStrategies`, `SceneImageStudio.razor` passes `ProductionModelStrategies` — both read from the selected choice and **fail fast** if the id is not in the enabled list.
   - A model change re-validates every reference row: a strategy the new model cannot execute is reset to `TextOnly` **and named in the status line**, never left in place to be dropped silently.
   - `SceneImageStudio.razor`'s identity card now resolves and displays the mechanism (`NativeMultiReference` = reference image, `ReferenceConditioning` = IP-Adapter/PuLID) via `IReferenceStrategyResolver`, re-resolves on every picker change, and `RenderIdentityImageAsync` **refuses** when the selected model cannot carry identity.

2. **The 2.1 prompt compiler was wired to the Pony tag builder.** `QwenImage21SceneImagePromptCompiler` (and `ApiSceneImagePromptCompiler`) injected `ISceneImageLLMPromptBuilder`; both the Pony tag builder and the natural-language builder implement that interface, and DI was bound to the **Pony** one. Result: with 2.1 selected, "Generate Prompt" returned `score_9, score_8_up, … rating_explicit, 1girl, …` while the record's style was `NaturalLanguage`, so the Pony text appeared in the "SDXL / natural language" draft (confirmed in `SceneImagePrompts` + the `SceneImagePromptProjected` debug events, which showed `imageModelIdentifier = qwen_image_2.1_int8_convrot.safetensors` with the Pony system prompt). Fixed by injecting the **concrete** natural-language builder (`SdxlSceneImagePromptBuilder`, the same posture as FLUX/API), removing the ambiguous DI registration, and pinning the rule with a structural test (no compiler may depend on `ISceneImageLLMPromptBuilder`) plus a dialect test.
- There is **no ordering control** anywhere: the reference order that actually reaches the model is decided in code (`BuildNativeReferencesAsync` — identity faces first, then assets), which is correct but invisible and unadjustable.
- `Pages/SceneImageCompose.razor` (the Create flow) has **no reference UI at all**; it only chooses `PromptOnly` vs `IdentityControlled` (`_applyIdentityOnCreate`). A `NativeReference` render therefore cannot be requested from the Create page yet.
- The reference panel is hosted by `SceneImageStudio.razor`, `CompositionComposer.razor`, `ImageEditWorkspace.razor`, `PromptAssetCreator.razor`.

### 5.2 Target model

One **ordered list of reference bindings** replaces the implicit per-element map. Each binding:

| Field | Meaning |
| --- | --- |
| `Ordinal` | 1-based position in the list; position 1 → `images.image_1` |
| `Kind` | `IdentityFace` \| `Location` \| `Pose` \| `Wardrobe` \| `Prop` |
| `ActorKey` | which character the face belongs to (identity faces only) |
| `AssetType` / `SceneAssetId` / `SceneAssetImageId` / `SceneAssetSha256` | the approved immutable asset selection (existing fields) |
| `Strategy` | `TextOnly` \| `NativeMultiReference` \| … (validated against the model's qualified strategies) |
| `Strength` | existing |

Rules to preserve (already true in the engine, must become visible in the UI):

1. **Identity faces come first**, then scene assets — they anchor composition.
2. **`image_1` anchors the left** of the frame; order is request data, not a detail.
3. The **reference head angle dominates the requested pose** — the picker must surface which pack view (front / three-quarter / profile) is being used, because that is what the render will follow.
4. Empty reference set on a native-reference render = **explicit failure**, never a prompt-only fallback.
5. Every strategy offered must exist in the model's **qualified** strategies (`NativeMultiReference` for 2.1) — an unqualified strategy must be shown as unavailable with the reason, not silently accepted.

### 5.3 Phased UI work

**U1 — data-driven strategies (small, unblocks everything)**
- Replace the hardcoded `StrategiesFor` switch in `ReferenceApplyPanel.razor` with a capability lookup: the host page supplies, per element kind, the strategies the **selected model** is qualified for (already available as `ExecutableStrategies`; extend to a per-kind map so `Location` can offer `NativeMultiReference` where qualified).
- Widen the page-level lists: `FinishReferenceStrategies` in `SceneImageStudio.razor` must include `NativeMultiReference`; `CompositionComposer.razor` currently passes `["TextOnly"]`, so it must take the model's qualified set.
- Remove the `throw` on unknown element keys; unknown kinds must render as "unsupported" rather than crashing the page.

**U2 — multi-reference list with ordering**
- Extend `ReferenceApplicationSelection` with `Ordinal` + `Kind` (additive; persisted bindings stay backwards compatible — missing ordinal means "use the legacy element order").
- Rework the panel into a single ordered list: add / remove / move-up / move-down, with the ordinal shown on each card and a colour/`badge` per kind.
- Keep the existing per-element cards as the *default populated rows* so nothing regresses for models that only support `TextOnly`.
- Show the resolved order that will actually be sent (faces first) and warn when the user's manual order was normalised.

**U3 — Compose (create) gains references + render mode**
- `SceneImageCompose.razor`: add the reference panel and a render-mode choice (`PromptOnly` / `IdentityControlled` / `NativeReference`), defaulting to the current behaviour.
- The compose page must set `RenderMode = NativeReference` when any binding uses `NativeMultiReference`, and must block submit with an explicit reason when the selected model cannot serve the requested bindings.
- Surface the reference **head-angle** warning in the compose flow (see 5.2 rule 3).

**U4 — Pose as a reference**
- Once 2.1 ControlNet weights exist (or immediately, as a reference-image path): add `Pose` as a first-class kind whose asset comes from the existing pose store, routed as an additional reference image instead of a ControlNet skeleton.
- Until then the handler's explicit "pose cannot be combined with a native-reference render" failure is the contract; the UI must show that as a *disabled/explained* state, not a crash.

**U5 — capability surfacing**
- Show, on the model selector and on the resolved-render badge: family, the qualified strategies, the reference budget (`Resolution` / `MaxReferences`), and the audit `ProofId`.

### 5.4 Non-negotiables for this UI work

- No silent strategy downgrade: a binding whose strategy the model is not qualified for must **fail with an explicit message** before submission (matching `roleplay-engine-no-fallback` and the RP hard rules).
- No reference is ever dropped to make a render succeed.
- Ordering is user data; the engine normalises but reports.
- `.razor` edits follow `.github/instructions/razor-editing.instructions.md` (full-context reads, micro-step diffs).

---

## 6. Test plan

Already green (191 tests across the touched areas). Still to add with U1–U5:

1. `ReferenceApplyPanel` strategy source: Location + `NativeMultiReference` offered when the model is qualified, hidden otherwise.
2. Ordering: a manual order is preserved when it is already valid and reported when normalised.
3. Compose page sets `NativeReference` when a native binding exists, and refuses to submit otherwise.
4. Fail-fast: unqualified strategy → explicit error, no submission.
5. Pose-as-reference: routed as a reference image (not a skeleton) once U4 lands.

---

## 7. Open questions

1. **Enhance on 2.1** — Enhance is model-independent today (upscaler + Lanczos). It stays that way unless the user wants a 2.1 refiner pass. No change planned.
2. **Identity mechanism vs native references** — 2.1 renders references natively; the older `IdentityControlled` (IP-Adapter / LoRA) path remains for other families. Both must remain selectable per model.
3. **Resolution budget** — currently the qualified `Resolution` (1024). A per-render budget control is possible but should not be guessed; the model's qualification stays the source.
4. **Accessory leakage** — measured: a reference pack's accessories (e.g. a necklace) leak into the render. If this becomes a problem, the fix belongs in the reference *selection* guidance (choose a clean view), not in a negative prompt.
