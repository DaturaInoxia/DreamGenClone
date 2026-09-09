# B-116 — Local img2img "re-skin" re-render: tasks

Contract: `specs/Planning/B-116-local-img2img-reskin/plan.md`. Read the plan + §2 verified evidence before starting.
Rules that always apply (repo `copilot-instructions.md`):
- **No `git restore` / `git checkout --` / `git reset --hard`.** Fix forward only.
- **No fallback branches.** All RP/image gating uses configured values only; missing capability fails fast with an explicit diagnostic.
- **Every implementation change must leave the test suite green.** No skipped/disabled tests.
- Razor edits: follow `.github/instructions/razor-editing.instructions.md` (full-context reads, micro-step/diff-only).
- Additive only — never repoint the RolePlaySceneImage default; do not modify Generate/Edit/Compose/identity code paths.

Legend: `[ ]` = not started, `[X]` = done. Order is dependency-ordered within each phase.

---

## Phase 1 — Capability token + config surface

- [ ] **T01** Add `LocalImg2Img` to the visual-strategy vocabulary surfaced in `DreamGenClone.Web/Components/Shared/ModelDetailsEditor.razor` + `ModelManager.razor` (multi-select for `SupportedVisualStrategiesJson`). Acceptance: an agent can mark a model row with `LocalImg2Img` from the UI without hand-editing JSON.
- [ ] **T02** Add allowlisted, idempotent named command `local-img2img-configure` to `DreamGenClone.DbQuery/Program.cs` (mirror `local-comfyui-configure` / `api-image-catalog`). It appends `LocalImg2Img` to `SupportedVisualStrategiesJson` on the dev DB rows whose provider is `Local ComfyUI (WOOD-GAME-MAIN 5080)` and whose `ModelIdentifier` is `flux1-dev-fp8.safetensors`, `bigLust_v16.safetensors`, `juggernautXL_ragnarok.safetensors`. Idempotent (no duplicate token). Acceptance: run twice — second run reports no change; verify via a `SELECT DisplayName, SupportedVisualStrategiesJson ...` query.

## Phase 2 — Client i2i workflow builders (+ tests)

- [ ] **T03** In `DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs`, add `BuildFluxImg2ImgWorkflow(unetName, prompt, sourceImageName, denoise, size, seed)`: `LoadImage` → `VAEEncode(ae.safetensors)` → `KSampler(cfg 1.0, euler/simple, ~24 steps, denoise < 1, FluxGuidance 3.5)` with an empty negative — mirror the verified hand-run `flux-reskin-075.json`. Acceptance: unit test asserts denoise passthrough, `VAEEncode` + `LoadImage` present, correct node wiring, seed.
- [ ] **T04** Add `BuildSdxlImg2ImgWorkflow` (BigLust/Juggernaut: `dpmpp_2m_sde`, empty negative, cfg 5) as a secondary builder. Acceptance: unit test of structure (denoise, `VAEEncode`, `LoadImage`). No pod/UI claim yet (experimental per plan D3).
- [ ] **T05** Extend the generation client surface with a source-image method (e.g. `GenerateFromImageAsync(ResolvedImageModel, ReskinRenderInput { SourceBytes, SourceFileName, Denoise, Prompt, Size, Seed }, ct)`); implement on the ComfyUI client (upload via `/upload/image`, reuse existing helpers) and dispatch to the correct i2i builder by `SceneImageModelFamily`. `RunPodServerlessImageClient` implementation throws `NotSupportedException("...local ComfyUi only...")`. Acceptance: tests for ComfyUI upload + workflow selection; fail-fast test for the serverless client.

## Phase 3 — Domain + repository (+ tests)

- [ ] **T06** Locate the **live** scene-image operation enum (the one carrying `ComposeBase` — B-104/B-105 precedent) and add `Reskin = 3`. Add nullable `Denoise REAL` to the scene-image record type + `SourceSha256`. Acceptance: enum round-trips; non-zero persisted value rule respected.
- [ ] **T07** `DreamGenClone.Infrastructure/RolePlay/SceneImageRepository.cs`: add the column to CREATE TABLE / INSERT / all SELECTs / ReadImage and to `EnsureImageProductionColumnsAsync`; add the matching idempotent startup migration in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` (`pragma_table_info` guard). Acceptance: repo round-trip test for a `Reskin` record incl. `Denoise` + source; migration is idempotent.
- [ ] **T08** `ValidateImage`: `Reskin` requires `SourceImageId` + `PromptRecordId`; mirror the ComposeBase guard handling. Acceptance: `ValidateImage` tests for missing source (throws) and valid reskin (passes).

## Phase 4 — Service + job handler + DI (+ tests)

- [ ] **T09** Add `BackgroundJobTypes.SceneImageReskin = "scene-image-reskin"` in `DreamGenClone.Web/Application/BackgroundJobs/BackgroundJobTypes.cs`. Acceptance: constant referenced by the enqueue path below.
- [ ] **T10** Add `SceneReskinRequest { SessionId, InteractionId, SourceImageId, Denoise, Prompt, ImageSize, RequestedModelId, ProductionGroupId?, BeatId?, Pov? }` in `DreamGenClone.Web/Application/RolePlay/Models/SceneRenderRequest.cs`. Acceptance: compiles; fields documented.
- [ ] **T11** Add `EnqueueReskinAsync` to `ISceneImageService` / `SceneImageService.cs` (mirror `EnqueueComposeBaseAsync`/`EnqueueRenderAsync`): resolve model fail-fast requiring `LocalImg2Img` capability **and** `ImageProtocol.ComfyUi` (no fallback; explicit diagnostic otherwise); load + sha-verify the source; create record (`Operation=Reskin`, `SourceImageId`, `Denoise`); enqueue with dedupe key `scene-image-reskin:{recordId}`. Acceptance: validation tests (missing capability → explicit error, stale source sha, dedupe key asserted).
- [ ] **T12** New `SceneImageReskinJobHandler` (+ payload) mirroring `SceneImageComposeBaseJobHandler`: resolve model by ID fail-fast, load source bytes, call the i2i client, save via `ISceneImageStorageService`, set status Complete. Register in `DreamGenClone.Web/Program.cs`. Acceptance: stub integration test (mirror `SceneImageRenderingJobHandlerTests`); registration present.

## Phase 5 — UI (+ contract test)

- [ ] **T13** Add a "Reskin onto a scene (local img2img)" card on a selected **Complete** image in `SceneImageStudio.razor` (and/or the image-editor page): model dropdown filtered to `LocalImg2Img` models (auto-select FLUX if present, never silently substitute), denoise slider (default 0.75, range 0.4–0.85), editable prompt. Disabled with explanatory note when the selected image/model cannot reskin. Acceptance: UI contract test for visibility/disable logic (mirror `SceneImageStudioUiContractTests`); follow razor-editing rules.

## Phase 6 — Docs + proof runner + verification

- [ ] **T14** Commit `helpers/flux-local-host/run-flux-reskin-proof.ps1` (+ i2i workflow JSON templates + a small prompt set) so the mechanism is reproducible from scratch against a local ComfyUI. Acceptance: dry-run/help path; documented invocation; outputs to a git-ignored `artifacts/tmp/` path.
- [ ] **T15** Add the re-skin / img2img rules note to `.github/instructions/scene-image-prompt-compiler-standards.instructions.md` (§10 of the plan): pose from a T2I base first; denoise ≤ ~0.75 preserves a dictated multi-figure pose; environment transplant needs ~0.75; intimate-beat base prompts need an awake/engaged cue.
- [ ] **T16** Full build + full test suite green; report counts. Fix any failure forward. Do not mark done while any test fails.

## Phase 7 — Live validation (post-implementation, on user's running app)

- [ ] **T17** Run `local-img2img-configure`, then validate end-to-end on a real beat image: render/choose a Complete image → Reskin via FLUX at denoise 0.75 onto a wooded/sun scene → confirm pose preserved + environment changed; visually review the output (per image, honest pass/fail). Update this file + plan.md Status when validated.
