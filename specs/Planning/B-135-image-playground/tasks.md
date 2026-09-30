# B-135 — Tasks

Dependency-ordered. Each task names the files it touches so blast radius is visible before work starts.
`[P]` = parallelisable with the task above it. No UI task starts before its bindings phase is green.

---

## P1 — Profiles, resolution, and the negative purge

### B135-001 — `ImageCompilerProfile` domain + table + repository
One row per checkpoint (D13). Fields as `plan.md` §1. Migration is a presence-checked `CREATE TABLE`,
following the existing pattern in `SqlitePersistence.cs`.
- Files: `DreamGenClone.Domain/RolePlay/ImageCompilerProfile.cs`, `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`, `DreamGenClone.Infrastructure/RolePlay/ImageCompilerProfileRepository.cs`
- Test: round-trip; unknown checkpoint resolves to nothing rather than a default profile.

### B135-002 — Seed the profiles from the existing researched settings
BigLust v1.6 · Juggernaut XL Ragnarok · Pony V6 XL · Pony Realism v2.3 ULTRA · Qwen-Image-2.1 ·
FLUX.1-dev · FLUX.2 · API models — budgets, envelopes, required components and forbidden tokens taken
from `scene-image-prompt-compiler-standards.instructions.md` §3. BigLust's entry is labelled
**measured-pending** (no author guide exists).
- Files: `DreamGenClone.Infrastructure/RolePlay/ImageCompilerProfileSeed.cs`, `DreamGenClone.DbQuery/` seed command
- Test: every registered image checkpoint resolves to a profile, or names the missing one.

### B135-003 — Resolve compilers by profile, not by family
`ISceneImagePromptCompilerRegistry.Resolve(family, dialect)` becomes profile-keyed. Family and dialect
are derived from the profile. An unknown checkpoint **fails fast naming the checkpoint**.
- Files: `DreamGenClone.Web/Application/RolePlay/ISceneImagePromptCompiler.cs`, `SceneImagePromptCompilers.cs`, `Program.cs`
- Test: BigLust and Juggernaut resolve to *different* compiler instances; a checkpoint with no profile throws.

### B135-004 — Negative purge: interfaces, settings, contracts `[P]`
Delete `CanonicalNegativePrompt` + `BuildNegativePrompt`; delete `SceneImageStudioSettings.NegativePrompt`
and the Studio textbox; delete the negative threaded through the identity/asset/review/pose-test contracts.
- Files: `ISceneImagePromptCompiler.cs`, `SceneImagePromptCompilers.cs`, `Models/SceneImageStudioSettings.cs`, `SceneImageProductionSettingsContract.cs`, `IdentityControlledRequestCompiler.cs`, `ReviewDeckShared.cs`, `ISceneAssetService.cs`, `SceneAssetService.cs`, `PoseTestRenderService.cs`, `Components/Pages/SceneImageStudio.razor`, `Components/Shared/ImageStepComposer.razor`

### B135-005 — Negative purge: builders, clients, workflow JSON `[P]`
`BuildDeterministicBeatNegativePrompt` moves to the profile. The negative parameter leaves
`ComfyUIImageClient` / `RunPodServerlessImageClient`; the workflow builder reads the profile. Workflow
JSON templates and the LoRA cell templates get an empty negative node.
- Files: `SdxlSceneImagePromptBuilder.cs`, `PonySceneImagePromptBuilder.cs`, `ISdxlSceneImagePromptBuilder.cs`, `IPonySceneImagePromptBuilder.cs`, `BodyReferencePromptCompiler.cs`, `CharacterIdentityBodyService.cs`, `DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs`, `RunPodServerlessImageClient.cs`, `SceneImageRenderingJobHandler.cs`, `SceneAssetGenerationJobHandler.cs`, `helpers/runpod/workflows/*.json`

### B135-006 — Pony's cited guard negative becomes the only non-empty negative
The Pony profile carries the ~6-term guard set with its citation (`pony-v6-prompting.instructions.md`
rules 8–9 — Pony ignores `no X` in the positive, so negations must live in the negative). Every other
profile declares none.
- Files: `ImageCompilerProfileSeed.cs`, `.github/instructions/scene-image-prompt-compiler-standards.instructions.md` (§3.2, §3.3, §4), `.github/instructions/pony-v6-prompting.instructions.md`

### B135-007 — Guard test: no negative path outside a profile declaration
Mirrors B130-023's structural guard. Fails if any settings object, compiler, client call or UI surface
can produce a negative the profile did not declare; also fails a non-empty negative without a citation.
- Files: `DreamGenClone.Tests/RolePlay/ImageNegativePurgeGuardTests.cs`

### B135-008 — Per-checkpoint builders: Qwen long-form, BigLust/Juggernaut small-form
Qwen-Image-2.1 stops injecting `SdxlSceneImagePromptBuilder` and gets a long-form builder;
BigLust/Juggernaut get a small-prompt builder. FLUX gets its own grounding. This closes the defect the
code itself documents ("the shared natural-language system prompt is SDXL-branded").
- Files: `QwenImage21SceneImagePromptCompiler.cs`, `FluxSceneImagePromptCompiler.cs`, new `QwenSceneImagePromptBuilder.cs`, new `BigLustSceneImagePromptBuilder.cs`, `JuggernautSceneImagePromptBuilder.cs`
- Test: a Qwen compile never emits Pony tags or an SDXL-shaped brief; a BigLust compile stays inside its budget.
- **Route (2026-09-30):** Qwen long-form prose is written by us under **route 1** — adopt the vendor's documented
  rules and prohibitions, cited to `research/qwen-2-1-prompt-enhancer.md` §4/§5. The byte-exact vendor-prompt route
  (which needs the `Qwen-Image-2.1-PE-*` weights) is **parked pending the analysis in that document's §8**; keep the
  text behind the profile's `SystemPrompt` so it can be swapped later without rewriting the builder.

### B135-009 — `PoseInText` enforcement (D18)
`Forbidden` for BigLust/Juggernaut on complex or multi-person poses; `SimpleOnly` for Pony. A step
needing such a pose with no structural source is **refused**, naming the missing skeleton/ControlNet/source.
- Files: `ImageCompilerProfile.cs`, the builders above, `ImageStepComposer.razor`
- Test: refusal names the missing structural source; the simple single-subject path still compiles.

---

## P2 — Cells, runs, evaluators

### B135-010 — `ImageSuite` / `ImageSuiteCell` domain + tables
Includes `UserDirection`, `ExpectedPrompt`, `SeedJson`, `SettingsJson`, `GatesJson`, `SimilarityTolerance`
(never defaulted, D12), and suite versioning frozen once a run exists.
- Files: `DreamGenClone.Domain/RolePlay/ImageSuite.cs`, `ImageSuiteCell.cs`, `SqlitePersistence.cs`, `ImageSuiteRepository.cs`, `ISceneImagePromptBuilder`-adjacent service

### B135-011 — Binding declaration contract (D17)
One entry per axis with `Mode ∈ Text | Reference | Adapter | Lora`, the value, and the declared strategy;
plus the resolved-strategy record written by the run.
- Files: `DreamGenClone.Domain/RolePlay/ImageCellBinding.cs`, `ReferenceStrategyCatalogue.cs` reuse
- Test: every axis round-trips; an undeclarable mode for a chosen model fails fast.

### B135-012 — Prompt conformance evaluator (pure)
Property checks (dialect · budget · required components · forbidden tokens including `no X` negations and
story names · pose-in-text) plus similarity to `ExpectedPrompt` against the cell's configured tolerance.
Deterministic; no embeddings.
- Files: `DreamGenClone.Web/Application/RolePlay/Evaluation/PromptConformanceEvaluator.cs`
- Test: catches the 2026-09-24 Pony-tags-in-a-Qwen-draft defect; each property fails independently.

### B135-013 — Request assertion evaluator (pure) `[P]`
Assertions over the request the app already built: every declared binding present, resolved strategy
honoured, character LoRA resolved at the declared strength, render mode derived from the bindings, no
silent downgrade, no dropped reference.
- Files: `DreamGenClone.Web/Application/RolePlay/Evaluation/RequestAssertionEvaluator.cs`

### B135-014 — `ImageRun` / `ImageRunCell` domain + tables + repository
Resolved variables, host/endpoint, both prompts, request graph, image, per-layer verdicts, gate results,
timing/cost, visual verdict, `RenderFromPath`.
- Files: `DreamGenClone.Domain/RolePlay/ImageRun.cs`, `ImageRunCell.cs`, `SqlitePersistence.cs`, `ImageRunRepository.cs`

### B135-015 — Run executor: a single cell or a batch (D14)
Drives the three layers. Free layers always run; the image layer is opt-in per run. Uses the existing
`IHostedJobQueue` and the production render path (D1: it exercises the app, it is not a parallel harness).
- Files: `DreamGenClone.Web/Application/RolePlay/ImageRunExecutor.cs`, `ImageRunJobHandler.cs`

### B135-016 — Compiler LLM pinning (D11) `[P]`
Model + temperature + seed for the compiler call become declared run variables and are recorded with the run.
- Files: `ImageCellCompilerLlmSettings.cs`, the pre-processor call site

---

## P3 — Native gates

### B135-017 — Region containment gate
Mean abs diff inside vs outside the rect, blurred edge, against an unmasked control. Qualified against
`tools/qwen-region-proof/measure_region.py` on a fixed image set before it gates anything.
- Files: `Evaluation/Gates/RegionContainmentGate.cs`, qualification record under `tools/qwen-region-proof/`

### B135-018 — Pose agreement gate `[P]`
DWPose readback through the app's own ComfyUI client; joint geometry as % of figure height, shoulder span
reported. Qualified against `tools/pose-angle-probe`.
- Files: `Evaluation/Gates/PoseAgreementGate.cs`

### B135-019 — Presence + sanitisation gates `[P]`
Subject count vs declared count; sanitisation heuristics. **First confirm `score_presence`'s mechanism** —
its imports are function-local, so whether it needs a detector is unresolved. Qualified against
`tools/consistency-scoring`.
- Files: `Evaluation/Gates/PresenceGate.cs`, `SanitisationGate.cs`

### B135-020 — Advisory measurements are recorded, never blocking
Eyes/head angle and identity/subject similarity are stored on the run cell and shown; they never produce a
failing verdict (D7).
- Files: `Evaluation/AdvisoryMeasurements.cs`

---

## P4 — The `/playground` surface

### B135-021 — Shell: rail · step · runs
One route (D16). Left rail = suites → cells; centre = the step; runs and comparison beside it.
- Files: `Components/Pages/Playground.razor`, `Components/Layout/NavMenu.razor`

### B135-022 — Cell editor: two prompts, both editable
`UserDirection` ⇄ compile → `CompiledPrompt` (editable) beside `ExpectedPrompt` (editable). Changing a
binding marks the compiled prompt stale (B-130 D5).
- Files: `Components/Shared/ImageCellEditor.razor`

### B135-023 — Render from either prompt (D19)
Both the compiled and the canned prompt can drive the render; both renders are stored on the same run cell
so the compiler's quality is answerable, not assumed.
- Files: `ImageRunExecutor.cs`, `Components/Shared/ImageCellEditor.razor`

### B135-024 — Binding matrix UI (D17)
Every axis with its mode selector and the resolved strategy, including identity text / reference / IP-Adapter /
LoRA, pose text / skeleton / ControlNet, location text / reference / depth-canny, wardrobe text / reference.
Micro-step edits per the Razor rules — this is the highest-risk file.
- Files: `Components/Shared/ImageStepComposer.razor`, `Components/Shared/BindingMatrixEditor.razor`

### B135-025 — Save as recipe `[P]`
A named, re-runnable step configuration — the unit that makes a proof repeatable.
- Files: `ImageRecipe.cs`, `ImageRecipeRepository.cs`, `Components/Shared/ImageStepComposer.razor`

### B135-026 — Route switch on the same source `[P]`
Compose · re-skin (B-116) · region edit (CASE-21 mechanism) · pose ControlNet (B-117), side by side, all
capability-gated with no silent substitution.
- Files: `Components/Shared/RouteSwitchPanel.razor`

### B135-027 — Sweep runner `[P]`
Settings × seeds in one submission; single cell or batch (D14).
- Files: `ImageRunExecutor.cs`, `Components/Shared/SweepEditor.razor`

### B135-028 — Chain view `[P]`
face → body → pose → location → frame, with every intermediate reusable as a reference (B-130 D6/D7).
- Files: `Components/Shared/StepChainView.razor`

### B135-029 — Run history + purge
Runs newest-first with variables, status, gate pass counts, duration and cost; delete a run, or purge a
suite / older-than. Promoted assets survive a purge because Promote copies (D9).
- Files: `Components/Shared/RunHistoryPanel.razor`, `ImageRunRepository.cs`

---

## P5 — Proof

### B135-030 — Run-vs-run, cell-paired, one declared variable
Refuses a comparison where more than one variable differs, and names the difference.
- Files: `Components/Shared/RunCompareGrid.razor`, `ImageRunComparisonService.cs`

### B135-031 — Compiler-fidelity comparison `[P]`
Compiled-render vs canned-render for the same cell and seed, with the prompt-level conformance result.
- Files: `Components/Shared/RunCompareGrid.razor`

### B135-032 — Suite seeding from the existing catalogs and flows
Reads `specs/image-generator-tests/baseline/manifest.json` (32 positions),
`TEST-MATRIX-PROMPTS.json` (suite ids `sfw-identity`, `stock-nsfw`, `stock-nsfw-identity`,
`solo-nsfw-woman`, `solo-nsfw-man`), the per-model settings/seed policies, and the multi-step flows
(`dual-base-sex-slideshow` 20 steps, the identity matrices, the C1 structure matrix). **Reads only —
never rewrites the committed evidence.**
- Files: `ImageSuiteSeeder.cs`, `DreamGenClone.DbQuery/` seed command

### B135-033 — Specimen capture
New cell from a real RP moment or a past render, so a production defect becomes a permanent regression cell.
- Files: `Components/Shared/ImageCellEditor.razor`, `SceneImageStudio.razor`, `ReviewDeck.razor`
- Open: whether the expected prompt is authored at capture or derived from the render that succeeded.

### B135-034 — Promote (copy + keep the link, D9)
LoRA dataset member (via the existing dataset registration) · asset library item image (via the B-134
one-click provenance) · identity pack view (via the existing draft-pack promotion).
- Files: `ImagePromotionService.cs`, `Components/Shared/PromotePanel.razor`

### B135-035 — Export a run to the committed evidence shape `[P]`
Images + `manifest.json` written into `specs/image-generator-tests/**` — the shape the repo already expects.
- Files: `ImageRunExporter.cs`

---

## Cross-cutting

### B135-036 — Structural guard tests (aggregate)
1. No negative outside a profile declaration (B135-007).
2. No compiler resolves by family key alone.
3. No composer surface hardcodes a per-element strategy list (B130-023, extended to the new surfaces).
4. No cell has a defaulted similarity tolerance.
- Files: `DreamGenClone.Tests/RolePlay/ImagePlaygroundGuardTests.cs`

### B135-037 — Per-checkpoint compiler research (with citations)
One document per checkpoint: BigLust · Juggernaut · Pony V6 / Pony Realism · Qwen-2.1 · FLUX.1-dev ·
FLUX.2 · API. Feeds the profile rows and the standards doc. Honors governance rule 4 (no researched
settings ⇒ cannot be a compiler target).
- Files: `specs/Planning/B-135-image-playground/research/<checkpoint>-prompting.md`

### B135-038 — Prerequisite check: B-127 (template-keyed identity)
P5's comparisons are per character and fail fast while identity is instance-keyed. Confirm B-127 status
before starting P5; P1–P4 are unaffected.

---

## P1b — Qwen-Image-2.1 Prompt Enhancer integration

Verified findings, contract, sampling defaults and the full integration rationale:
`research/qwen-2-1-prompt-enhancer.md`. This is the **externally-sourced grounding** B135-008 needs for the
Qwen long-form compiler (governance rules 4 and 9), and it is the vendor's own reference implementation.

### B135-039 — Vendor both PE system prompts, byte-exactly, at a pinned commit
**✅ DONE 2026-09-30.** Both prompts are in the repo at their vendor bytes, downloaded from commit
`7307809d2c9d582be700a1b9b04d393fc9cdf865` (never hand-copied, and never taken from a text extraction — a web
page/fetch tool RE-WRAPS the text, which is itself a paraphrase). **Never hand-edit a vendor prompt.**
- `helpers/qwen-prompt-enhancer/prompts/system_prompt_t2i.txt` — 10,045 bytes / 192 lines / sha256 `a77c9a06…fb99`
- `helpers/qwen-prompt-enhancer/prompts/system_prompt_edit.txt` — 18,344 bytes / 205 lines / sha256 `e378fea6…6439`
- `helpers/qwen-prompt-enhancer/prompts/manifest.json` — provenance (repo, commit, path, retrieval date), per-file
  size/lines/sha256, the answer contract per task, the checkpoints, and the sampling defaults incl. the per-task
  `presence_penalty`
- `helpers/qwen-prompt-enhancer/fetch-official-prompts.ps1` — idempotent, and it **only replaces a file when the
  downloaded bytes match the recorded hash**; `-VerifyOnly` checks the files on disk with no network
- `DreamGenClone.Tests/RolePlay/QwenPromptEnhancerPromptAssetTests.cs` — **7/7 pass**: per-file hash fidelity, the
  two prompts are genuinely distinct documents, the commit is a pinned 40-hex SHA (not a branch), both state their
  answer contract (and `ratio_follow` is edit-only with the fields mutually exclusive), the t2i prompt still forbids
  quality boosters, and the recorded `presence_penalty` is per-task and not equal (1.5 vs 0)

`-VerifyOnly` output at the time of writing (exit 0):
```
OK   system_prompt_t2i.txt  a77c9a06c59b120741141d9514b95682bb8761d02bec49ca61def7b2b3d9fb99
OK   system_prompt_edit.txt  e378fea686a1431581ba4c654d332ae96adad633f144ae738ec8ce9c4fd66439
All Prompt Enhancer assets verified against commit 7307809d2c9d582be700a1b9b04d393fc9cdf865.
```

### B135-040 — PE task profiles as profile data `[P]`
Temperature 1.0 · top_p 0.95 · top_k 20 · min_p 0 · **presence_penalty 1.5 (t2i) / 0 (edit)** · max_new_tokens
16256/24000 · thinking required. The vendor states the penalty is load-bearing and "does not fail loudly", so it is
**per-task configured data with no default** — the same rule this repo already applies to gate values.
- Files: `ImageCompilerProfile` (+ PE task dimension), `ImageCompilerProfileRepository` seed

### B135-041 — `QwenPromptEnhancerAnswer` parser, fail-fast
`RewritePrompt` / `WhRatio` / `RatioFollow` with the full refusal set: exactly one of the two ratio fields set;
`rewritten_prompt` non-empty and single-paragraph; **no ratio or resolution string inside the prompt**; and refusal
when the thinking block is absent (thinking is required, so its absence means the wrong server configuration).
- Files: `DreamGenClone.Web/Application/RolePlay/QwenPromptEnhancerAnswer.cs` + tests

### B135-042 — `wh_ratio` / `ratio_follow` drive the canvas
Map `wh_ratio` into the checkpoint's **qualified dimension set**; map `ratio_follow` to "inherit the bound
reference's canvas". An unmappable ratio is refused **naming the ratio**, never silently clamped.
- Files: canvas-resolution service + `ImageStepComposer` surfacing

### B135-043 — The PE call and the render share ONE ordered reference list
The `<image1>`…`<imageN>` contract silently re-points if the order differs between the rewrite and the render
(vendor-stated). The canvas image must be identifiable from the same roles the render declares.
- Files: the PE call site, `ReferenceStrategyCatalogue` reuse

### B135-044 — Retire the SDXL-branded builder on the Qwen-2.1 path
The Qwen-2.1 path currently injects `SdxlSceneImagePromptBuilder` (the code calls this "a documented follow-up").
Replace it with the PE t2i contract, and pin the **deliberate differences** as tested behaviour: the PE prompt
forbids quality boosters and style-cue tails the SDXL builder writes, and forbids the ratio appearing in the prompt
at all.
- Files: `QwenImage21SceneImagePromptCompiler.cs`, new `QwenSceneImagePromptBuilder.cs`, `ISceneImageLLMPromptBuilder` impls

**Feasibility gates before B135-043/044:** the PE checkpoints are fine-tuned **Qwen3.5-VL 9B**, and the vendor
states the **stock** Qwen3.5-VL does not reliably emit the contract — so the PE weights must be registered
(VRAM/hosting) and their licence settled. Record the outcome here before building.
