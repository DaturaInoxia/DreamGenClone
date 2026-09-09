# B-116 — Local img2img "re-skin" re-render from a source scene image

**Status:** designed (pending implementation by an agent). **State:** `designed`.
**Priority:** medium · **Scope:** medium. Additive — never repoints the RolePlaySceneImage default and does not modify Generate/Edit/Compose/identity paths.
**Related:** B-112 (local FLUX host + app integration this extends), B-100, B-111, B-032.

---

## 1. Context & problem

- The local ComfyUI render models (FLUX.1-dev fp8, BigLust v1.6, Juggernaut XL, Pony) generate **text-to-image only**. Every `ComfyUIImageClient` workflow builder hardcodes `denoise = 1.0` and starts from `EmptyLatentImage` (`DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs` — denoise at lines ~76, ~146, ~237). There is **no source-image / img2img path** for local generation models.
- Every existing **source-image** operation in the app (scene image edits in `SceneImageEditingJobHandler`, asset edits in `SceneAssetEditingJobHandler`) routes to `IImageEditingClient` = the **cloud Qwen editor**. Local FLUX/SDXL cannot be used to re-render an existing scene image onto a new scene.
- User pain (2026-09-08, verified): a dictated multi-figure layout (e.g. "man kneeling on both knees facing camera, woman lying in front of him") is **fragile in pure text-T2I** on every local model — SDXL does "whatever", and even FLUX drifts when scene clauses are added to the same render. But **img2img over a pose base preserves the geometry and transplants the environment** at the right denoise. This is the missing "set this composition into a location / regrade" capability (the "location plate / img2img anchor" idea from earlier planning).

## 2. Verified evidence (2026-09-08, local FLUX.1-dev fp8 on WOOD-GAME-MAIN)

Runs performed by hand via `helpers/runpod/generate-one.ps1` against `http://192.168.0.16:8188`. Outputs (git-ignored): `artifacts/tmp/images/flux-pose-reskin/`. Workflows (temp, git-ignored): `artifacts/tmp/dbquery/workflows/flux-reskin-{045,060,075}.json`.

| Test | Pose held? | Environment reached? | Verdict |
|---|---|---|---|
| T2I base — minimal pose, no scene | ✅ (man kneeling over woman lying in front) | studio wall (default) | expected; geometry holds bare |
| T2I base + "on grass outdoors" (one anchor) | ✅ | grassy pitch | single anchor keeps pose + plants "outdoor" |
| img2img re-skin @ denoise 0.45 / 0.6 | ✅ | ❌ environment unchanged | denoise too low to move a strong background (studio wall / uniform grass) |
| img2img re-skin @ denoise **0.75** | ✅ | ✅ **wooded, dappled sunlight** | **environment transplant works; pose survives** |

Key facts to not relitigate:
- **denoise ~0.75 is the empirical sweet spot** for FLUX i2i: coarse layout (pose, bodies) is decided early in the schedule and survives; the tail of the schedule re-draws toward the target environment. 0.45–0.6 preserves geometry but cannot overrule a stable, low-entropy background.
- The FLUX i2i recipe = `LoadImage → VAEEncode(ae.safetensors) → KSampler(cfg 1.0, euler/simple, FluxGuidance 3.5, ~24 steps, denoise < 1)`, empty negative — same FLUX settings as the T2I path, differing only in the latent source and denoise.
- Caveat found: a "woman lies" base with no awake/engaged cue drifts semantically to **first-aid/rescue** (eyes closed, concerned). This is a mood problem in the *base* prompt, not the i2i mechanism — base prompts for intimate beats need an awake/engaged cue. Not a B-116 deliverable, but document it in the compiler-standards note (§10).

## 3. Goals / non-goals

**Goals**
1. Re-render an existing scene image onto a target scene/prompt using a **local** ComfyUI generation model (i2i, user-set denoise), preserving layout/pose while changing the environment/details.
2. Surface it in the Studio / image-editor (legacy flow + production stills) next to the existing render/edit actions.
3. **Capability-gate** it: only models that declare `LocalImg2Img` **and** are `ImageProtocol.ComfyUi` can run it; otherwise fail fast with an explicit diagnostic (no fallback to T2I).
4. Additive and isolated — never repoint the default; do not modify Generate / Edit / Compose / identity code paths.

**Non-goals**
- No new model downloads, no ControlNet, no FLUX-Kontext/Redux.
- SDXL (BigLust/Juggernaut) i2i on a dictated multi-figure pose is **unproven** — ship FLUX first; flag SDXL rows capable but mark them experimental in the UI until an SDXL i2i proof passes.
- Identity faces and explicit anatomy still go through the existing Qwen edit step afterward; B-116 does not touch that.

## 4. Decisions (open — confirm before/during implementation)

- **D1 — operation/job naming.** Proposal: `SceneImageOperation.Reskin = 3`, job `scene-image-reskin` (`BackgroundJobTypes.SceneImageReskin`), request type `SceneReskinRequest`. Wording to users: "Reskin onto a scene" / "Re-render onto a location".
- **D2 — entry point.** A card/action on a selected **Complete** rendered image in the image-editor / Studio (pattern: the Compose Stage-1 card, but acting on an existing image). Controls: model dropdown (filtered to `LocalImg2Img` models; auto-select FLUX if present, never silently substitute), denoise slider (default **0.75**, range ~0.4–0.85), editable prompt prefilled with the target scene text.
- **D3 — model scope for v1.** Ship FLUX.1-dev fp8 (local) as the proven path. BigLust/Juggernaut rows get the capability token but are shown as "experimental (unproven pose fidelity)" until an SDXL i2i proof passes.

## 5. Data model / domain

- Add `SceneImageOperation.Reskin = 3` to the **live** scene-image operation enum (the type that already carries `ComposeBase` — this lives with the scene-image record used by the repository/UI, NOT the `DreamGenClone.Domain/RolePlay/SceneImageRecord.cs` Generate/Edit-only enum). Locate the enum that gained `ComposeBase` in the compose slice (B-104/B-105) and add the member there.
- `SceneImageRecord`: reuse `SourceImageId` (edit-parent semantics) for the reskin source image; add a `Denoise REAL` column (nullable) and store `SourceSha256` for staleness (mirror edit sessions).
- Repository (`SceneImageRepository.cs`) + `SqlitePersistence` startup migration: add the column in CREATE TABLE / INSERT / all SELECTs / ReadImage and in the in-repo `EnsureImageProductionColumnsAsync`, all idempotent via `pragma_table_info` (exact pattern used for `RequestedModelId` in B-103 and `ComposeBase` in B-104/B-105).
- `ValidateImage`: a `Reskin` record requires `SourceImageId` and `PromptRecordId`; relax/guard mirroring the ComposeBase handling (compose skips PromptRecordId — reskin still has a prompt, so it requires it).

## 6. Model Manager capability gate

- New visual-strategy token **`LocalImg2Img`** in `RegisteredModels.SupportedVisualStrategiesJson` (existing vocabulary: `ReferenceConditioning`, `NativeMultiReference`).
- New **idempotent, allowlisted dbq named command** `local-img2img-configure`: appends `LocalImg2Img` to `SupportedVisualStrategiesJson` on the local FLUX + BigLust + Juggernaut rows of the **dev.db** (`DreamGenClone.Web/data/dreamgenclone.dev.db`). Snapshot stays the sanitized base — config propagates by running the command per host (same model as `b100-analyzer-configure`, `local-comfyui-configure`, `api-image-catalog`). Never hand-INSERT rows.
- Resolver gating mirrors the identity resolver: **fail-fast, exactly one decision path, no fallback**. `EnqueueReskinAsync` requires the resolved model (by `RequestedModelId` or default) to declare `LocalImg2Img` **and** `ImageProtocol == ComfyUi`; any miss throws an explicit diagnostic. No silent degrade to T2I.
- UI: add `LocalImg2Img` to the visual-strategies multi-select in `ModelManager.razor` / `ModelDetailsEditor.razor`.

## 7. Client + workflow (`DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs`)

- Extend the generation surface with a source-image render method, e.g. `GenerateFromImageAsync(ResolvedImageModel, ReskinRenderInput { SourceBytes, SourceFileName, Denoise, Prompt, Size, Seed }, ct)`. Implement on the ComfyUI client; `RunPodServerlessImageClient` throws `NotSupportedException` (local `ComfyUi` only) — fail fast.
- New workflow builders mirroring today's hand-run proof:
  - `BuildFluxImg2ImgWorkflow`: `LoadImage(1) → VAEEncode → KSampler(denoise, euler/simple, cfg 1.0, FluxGuidance 3.5, ~24 steps)`, empty negative, `ae.safetensors` VAE (mirrors `flux-reskin-075.json`).
  - `BuildSdxlImg2ImgWorkflow` (BigLust/Juggernaut recipe: `dpmpp_2m_sde`, empty negative) — secondary/experimental.
- Reuse the existing `/upload/image` upload + `ISceneImageStorageService` patterns already used by identity/edit paths.

## 8. Jobs / service (`DreamGenClone.Web/Application/RolePlay`)

- Add `BackgroundJobTypes.SceneImageReskin = "scene-image-reskin"` to `BackgroundJobs/BackgroundJobTypes.cs`.
- New `SceneImageReskinJobHandler` (+ payload) mirroring `SceneImageComposeBaseJobHandler` (compose precedent): resolve the model by ID fail-fast; load the source image bytes + verify sha; call the i2i client; save via storage; persist the record with `Operation=Reskin`, `SourceImageId`, `Denoise`, prompt snapshot. Register the handler in `Program.cs`.
- `ISceneImageService.EnqueueReskinAsync(SceneReskinRequest)` with dedupe key `scene-image-reskin:{recordId}` (mirror `EnqueueRenderAsync` / `EnqueueComposeBaseAsync`).

## 9. UI

- `SceneImageStudio.razor` / the image-editor page: on a selected **Complete** image, a "Reskin onto a scene (local img2img)" card — model dropdown (filtered to `LocalImg2Img`), denoise slider (default 0.75, 0.4–0.85), editable prompt. Disabled with an explanatory note when the selected image/model cannot reskin (non-ComfyUi model, missing capability, or source not Complete).
- Follow `.github/instructions/razor-editing.instructions.md` (full-context reads, diff-only / micro-step edits) for every `.razor` change.

## 10. Docs / helpers

- Commit a reusable proof runner under `helpers/flux-local-host/run-flux-reskin-proof.ps1` (+ prompt cells and the i2i workflow JSON templates) so the mechanism is reproducible from scratch.
- Add a "re-skin / img2img" note to `.github/instructions/scene-image-prompt-compiler-standards.instructions.md`:
  - Hard rule: a dictated multi-figure pose must come from a T2I base first; do not try to fuse a rich scene into the same T2I render.
  - img2img denoise ≤ ~0.75 preserves a dictated multi-figure pose; environment transplant needs ~0.75. Base prompts for intimate beats need an awake/engaged cue (avoid the first-aid/unconscious drift).
- Register B-116 in `specs/Planning/backlog.md` (state `designed`). This file + `tasks.md` are the implementation contract for the next agent.

## 11. Test plan (suite must stay green; no skipped/disabled tests)

- Workflow builders: Flux + SDXL i2i JSON — denoise passthrough, `VAEEncode`/`LoadImage` present, seed, target resolution. (`DreamGenClone.Tests/RolePlay/ComfyUIImageClientTests.cs` pattern.)
- Resolver gating: missing `LocalImg2Img` / non-ComfyUi → explicit error; assert no fallback branch (≥5 no-fallback tests, mirror identity resolver tests).
- Repository: `Reskin` operation + `Denoise` + source round-trip; `ValidateImage` arms (source required; PromptRecordId required for Reskin).
- Service: `EnqueueReskinAsync` validation (bad model, missing capability, stale sha) + dedupe key.
- Handler: stub/integration test (mirror `SceneImageRenderingJobHandlerTests`).
- UI contract test for the reskin card visibility/disable logic.
- After implementation, run the full suite; fix forward (never `git restore`).

## 12. Blast radius & sequencing

Additive. Does not modify Generate/Edit/Compose/identity code paths; does not repoint defaults. Recommended order:
1. Capability token + `local-img2img-configure` dbq command + UI strategy option.
2. Client i2i workflow builders + tests.
3. Domain/repo column (`Denoise`, `Reskin`) + migrations + `ValidateImage`.
4. Service `EnqueueReskinAsync` + job handler + DI + tests.
5. Studio/editor UI card + contract tests.
6. Full suite + live FLUX reskin through the app on a real beat image.

## 13. File inventory (primary touch points)

| File | Change |
|---|---|
| `DreamGenClone.Web/Domain/RolePlay/SceneImageRecord.cs` (live op enum) | add `SceneImageOperation.Reskin = 3`, `Denoise` field |
| `DreamGenClone.Infrastructure/RolePlay/SceneImageRepository.cs` | column + SELECTs + `ValidateImage` + `EnsureImageProductionColumnsAsync` |
| `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | idempotent startup migration |
| `DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs` | `GenerateFromImageAsync` + `BuildFluxImg2ImgWorkflow` + `BuildSdxlImg2ImgWorkflow` |
| `DreamGenClone.Web/Application/BackgroundJobs/BackgroundJobTypes.cs` | `SceneImageReskin = "scene-image-reskin"` |
| `DreamGenClone.Web/Application/RolePlay/SceneImageService.cs` + `ISceneImageService.cs` | `EnqueueReskinAsync` |
| `DreamGenClone.Web/Application/RolePlay/SceneImageReskinJobHandler.cs` (new) | job handler + payload |
| `DreamGenClone.Web/Application/RolePlay/Models/SceneRenderRequest.cs` | `SceneReskinRequest` |
| `DreamGenClone.Web/Application/RolePlay/ReferenceStrategyResolver.cs` or model-resolution gating | `LocalImg2Img` capability check (fail-fast) |
| `DreamGenClone.DbQuery/Program.cs` | `local-img2img-configure` named command |
| `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` (+ editor) | reskin card |
| `DreamGenClone.Web/Components/Shared/ModelDetailsEditor.razor` / `ModelManager.razor` | `LocalImg2Img` strategy option |
| `DreamGenClone.Web/Program.cs` | register handler |
| `helpers/flux-local-host/run-flux-reskin-proof.ps1` (new) | reproducible proof runner |
| `.github/instructions/scene-image-prompt-compiler-standards.instructions.md` | re-skin / img2img rules note |
