# Image compiler + region roadmap — state, decisions, next

**Written:** 2026-09-30. Consolidates three threads that were discussed together and kept getting
confused with each other. They are separate problems with separate answers; that separation is the point
of this document.

| Thread | What kind of problem | Status |
|---|---|---|
| **A. Compiler granularity** — one per model, or per dialect? | design decision (mechanism mostly exists) | decided: split by **language/capability**, not by model |
| **B. Qwen-2.1 prompt grounding** — official vendor prompts or our own? | content decision | decided: **route 1 now**, route 2 parked |
| **C. Edit-capability wiring** — edit compilers per model; region/panorama | build work | partially built; ordered plan in §4 |

---

## 1. Current state, verified 2026-09-30

### 1.1 Create (text-to-image) path

| Fact | Where |
|---|---|
| All compiler prose lives in ONE place: `SceneImageCompilerSystemPrompts`, and `For(family, canonical)` maps a family to its text | `DreamGenClone.Domain/RolePlay/SceneImageCompilerSystemPrompts.cs` |
| `Sdxl`, `Api`, **`Flux` and `QwenImage21` all return the SDXL-branded** `NaturalLanguageBeat/Canonical` | same file, `For(...)` switch |
| `Pony` returns `PonyTagsBeat/Canonical` | same file |
| That mapping is a **self-declared, deliberately unfixed gap** — the file's own banner: four families use the natural-language texts, "so FLUX and Qwen cells are told they are SDXL-family… correcting it means editing researched compiler prose per family, which is research work (governance rules 4 and 9), not a rename" | same file, class banner |
| The per-family compilers are **routing classes only** (Family + Dialect + a builder). `QwenImage21SceneImagePromptCompiler` and `FluxSceneImagePromptCompiler` both inject `SdxlSceneImagePromptBuilder` **by concrete type** — deliberately, because binding `ISceneImageLLMPromptBuilder` handed Qwen-2.1 the Pony tag builder (2026-09-24 defect) | `DreamGenClone.Web/Application/RolePlay/SceneImagePromptCompilers.cs` |
| `ImageCompilerProfile` = **one row per checkpoint** (D13) carrying dialect, budget (Min/Max chars, Max tokens), required components, forbidden tokens, PoseInText, negative + citation, settings envelope, **its own `SystemPrompt`**, examples, version | `DreamGenClone.Domain/RolePlay/ImageCompilerProfile.cs` |
| 13 profiles seeded from real `RegisteredModels.ModelIdentifier` values; the row's `SystemPrompt` is seeded from `For(family, canonical: false)`, and the insert is **DO NOTHING** so an operator edit survives a reseed | `DreamGenClone.Infrastructure/RolePlay/ImageCompilerProfileRepository.cs` (~L239, seed L275+) |
| Resolution is **by profile** with fail-fast naming the checkpoint (`Resolve(profile)`) | same repo / `SceneImagePromptCompilers.cs` registry |
| **B135-008 is NOT implemented**: the only builders in the solution are `SdxlSceneImagePromptBuilder` and `PonySceneImagePromptBuilder`. No `QwenSceneImagePromptBuilder.cs`, no `BigLustSceneImagePromptBuilder.cs`, no `JuggernautSceneImagePromptBuilder.cs` | `**/*SceneImagePromptBuilder.cs` = 4 files, all SDXL/Pony |
| SDXL prose WAS enhanced in B-135: rules 7–10 adopted from the operator-supplied SDXL rewriter prompt, with tag-list grammar, backtick wrapper, numeric-age clamp and race→appearance inference **REJECTED** (rule 8 is the corrected form) | `SdxlSceneImagePromptBuilder.cs` L176/L192; assessment in `research/sdxl-rewriter-prompt-assessment.md`; test `SdxlRewriterRulesTests` |

### 1.2 Edit (source-image) path

| Fact | Where |
|---|---|
| There is exactly **ONE** edit compiler for every editor model: `QwenSceneImageEditPromptCompiler`, `SystemPromptVersion = "qwen-edit-rules-v3"`, schema `scene-image-edit-compiler-v1` | `DreamGenClone.Web/Application/RolePlay/QwenSceneImageEditPromptCompiler.cs` |
| It is a **singleton**, injected into six production classes plus a test fake: `MediaEditCompilationService`, `MediaEditCompilationJobHandler`, `SceneImageEditCompilationService`, `SceneImageEditCompilationJobHandler`, `SceneAssetImageEditCompilationService`, `SceneAssetImageEditCompilationJobHandler` | `Program.cs:578` |
| Its system message is Qwen-edit branded and mentions **no** model/version — it is not 2.1-aware, and has no vocabulary for region confinement or canvas change | same file, `BuildSystemMessage()` |
| The **render** side already discriminates editors by `ImageEditorGraphKind`: `SplitUnet`, `MergedCheckpoint`, `QwenImage21Native` | `DreamGenClone.Domain/ModelManager/ImageEditorGraphKind.cs` |
| **Registered editors today: 4 × `MergedCheckpoint` (all 2511-family) + 1 × `QwenImage21Native` (Qwen-Image-2.1 Editor)** | dev DB, query in `artifacts/tmp/dbquery/queries/editor_graph_kinds.sql` |
| Masked-region confinement is **refused** on anything that is not `QwenImage21Native` | `ComfyUIImageEditingClient` + the handler's model check |

### 1.3 Official Qwen-2.1 prompt enhancer (PE)

| Fact | Where |
|---|---|
| Both official prompts vendored byte-exactly at a pinned commit (`7307809`), with a scripted fetch | `helpers/qwen-prompt-enhancer/{fetch-official-prompts.ps1,prompts/system_prompt_t2i.txt,prompts/system_prompt_edit.txt}` |
| An asset test asserts the vendored contract | `DreamGenClone.Tests/RolePlay/QwenPromptEnhancerPromptAssetTests.cs` |
| **No production code reads them** — `QwenPromptEnhancer` appears in exactly one file, that test | grep, 2026-09-30 |
| Research + integration plan (PE1–PE5) + the route decision | `specs/Planning/B-135-image-playground/research/qwen-2-1-prompt-enhancer.md` (§6 gates, §7 tasks, **§8 decision**) |

### 1.4 Region / confinement (B-130-021, B-131) — already built

Landed: masked latent in the 2.1 graph · `MediaEditRegionOperation` · `ImageRegionMaskEngine` ·
`MaskedRegion` in the operation composite · the edit handler builds and passes the mask · the payload is
read back and refused on the model-less path · `Region` on the request DTOs and at all four enqueue
sites · `ImageEditRunRequest.Region` + both store adapters · `RegionDragCalculator` · persisted
`RegionGrowMaskBy` / `RegionFeatherPixels` · the UI panel (draw on the **source** image, move, resize,
properties on a **Region** tab).

**Not yet proven:** no real render has gone through the region path. The mask is proven at graph-JSON
level only. The confinement render (CASE-23 step 2) is outstanding — and the local ComfyUI host was up
when this was written.

---

## 2. Decisions made, and the reasoning that produced them

### D1 — Compiler granularity: split by **language and capability**, not by model

Keep the **per-checkpoint profile** (it is the citation home and it is cheap). Write **prose per
dialect**, because that is where the evidence is:

| Prose source | Covers | Why |
|---|---|---|
| SDXL natural-language (`NaturalLanguage*`) | bigLust, juggernautXL, the API family | Same dialect; the differences between them are budget/envelope, and those are profile fields |
| Pony tags (`PonyTags*`) | Pony V6, Pony Realism | Different vocabulary + the one cited guard negative |
| Qwen long-form (**new**) | Qwen-2.1, and Qwen-2.0-Pro with a different capability envelope | Appetite is measured (300–1600 chars) |
| FLUX | shared natural-language prose + its own profile | No negatives and a longer window are envelope facts; "needs its own grounding" is a follow-up, not yet measured |

Rejected: one researched, cited prose source per checkpoint. With thirteen seeded profiles that is
thirteen texts to keep current, and the realistic failure is near-duplicates that drift unnoticed.
**Split only on measurement** — `B135-031` (compiler-fidelity compare, one declared variable) is the
tool. BigLust's "small low-detail" preference is itself labelled **measured-pending** in the plan.

### D2 — Qwen-2.1 grounding: **route 1 now**, route 2 parked

- **Route 1 (plan of record):** write our own Qwen long-form prose, adopting the vendor's documented
  rules/prohibitions, cited to the research doc. Needs no new weights; closes the SDXL-branding defect;
  testable.
- **Route 2 (parked, needs its own analysis):** use the vendor prompts byte-exactly. **Requires the PE
  checkpoints** (`Qwen-Image-2.1-PE-T2I` / `-PE-I2I`, fine-tuned Qwen3.5-VL 9B). Vendor-stated: on the
  stock model they "will load and generate" but it was never trained against them → expect
  `parse_ok: false` on most rows.

The line to hold: the vendor's **contract** is what their weights emit; the **guidance** is ours to
write. Full gates + six open questions: research doc **§8**.

### D3 — Edit compilers: **two**, keyed on graph kind; build 2.1 first

2511 edit and 2.1 edit do need their own — not because the prose voice differs, but because the
**capability envelope and the response contract differ**:

- **2.1 native:** up to sixteen reference slots through one `TextEncodeQwenImage21` node; masked-region
  confinement; and per the vendor's own edit prompt, canvas/aspect decisions (`wh_ratio` /
  `ratio_follow`), mandatory `<imageN>` tagging at N≥2, outpainting and panorama rules.
- **2511 (merged):** `QwenImageEditPlusPipeline`, **immutable ordered image list**, seed 0, true_cfg 4.0,
  blank negative, 40 steps, guidance 1.0 — the instruction names each image's role and states changes +
  preserved. No documented canvas change; no region mask.

One shared prompt cannot honestly serve both: 2511's compiler has no canvas field and must not invent
one. **Use the discriminator that already exists** — `ImageEditorGraphKind` (4 merged : 1 native today).
Promote to a per-editor compiler profile only if a future editor shows it needs its own text.

---

## 3. What is explicitly NOT being done now

- Route 2 / the PE prompts (**B135-040 … B135-044 are blocked** on the §8 analysis — do not start them
  by pointing an untrained model at a prompt it will mostly fail to answer).
- A bespoke prose source for each of the thirteen checkpoints.
- FLUX's own prose (its profile row carries the differences for now).
- `B135-031` (compiler-fidelity compare) — **PARKED 2026-10-04** (operator): the one-declared-variable
  measurement that would settle whether the 2.1 edit compiler's extra prose improves output and whether
  BigLust/Juggernaut/FLUX need their own builders. Until it is built, shared SDXL prose stays and N8 stays
  deferred.

---

## 4. What needs to happen next — ordered

| # | Task | Files | Test / done-when | Why this order |
|---|---|---|---|---|
| **N1** | ✅ **DONE 2026-10-01** — **Qwen-2.1 long-form CREATE builder** (B135-008 slice 1, route 1) — `Qwen21Beat`/`Qwen21Canonical` prose added to `SceneImageCompilerSystemPrompts`, `For(QwenImage21, …)` points at it, `QwenSceneImagePromptBuilder` (+ `IQwenSceneImagePromptBuilder`) created, `QwenImage21SceneImagePromptCompiler` + `Program.cs` rewired, profile seed derives the Qwen row's `SystemPrompt` from it | `SceneImageCompilerSystemPrompts.cs`, `QwenSceneImagePromptBuilder.cs`, `NaturalLanguageSceneImagePromptBuilder.cs` (shared base), `QwenImage21SceneImagePromptCompiler.cs`, profile seed; `Program.cs` | A Qwen compile never emits Pony tags, never a quality booster (`masterpiece`/`8K`/`highly detailed`/`award-winning`), never the SDXL style tail; stays inside 300–1600 chars; the profile row's `SystemPrompt` is the Qwen text | Closes a defect the code itself documents, needs no new weights, and is the seam route 2 later swaps |
| **N2** | ✅ **DONE 2026-10-01** — **Split the edit compiler** by `ImageEditorGraphKind`. Phase 1: `SceneImageEditPromptCompilerBase` + `QwenSceneImageEditPromptCompiler` (2511) + `QwenImage21EditPromptCompiler` (2.1, `SystemPromptVersion` = `qwen-edit-2.1-rules-v1`) + `ISceneImageEditPromptCompilerResolver` (fail-fast on null/unknown, `Resolve(ResolvedImageEditorModel)` normalises serverless→merged, `ResolveByVersion`). Phase 2: the six compile sites now resolve the editor model (explicit `EditorModelId` on the three compile requests, else the function-default) and select the compiler; the three job handlers re-select via `ResolveByVersion(attempt.SystemPromptVersion)`; `IImageEditWorkspaceService.PrepareAsync` + `ImageEditWorkspace.razor` carry the editor model id. | `ISceneImageEditPromptCompilerResolver`, the two implementations, the six injection sites, the three compile requests + workspace + Razor, `Program.cs`, the test fake | A `QwenImage21Native` editor resolves the 2.1 compiler; a `MergedCheckpoint` editor resolves the ordered-reference one; an unknown/unset graph kind fails fast; the persisted `SystemPromptVersion` on the attempt names the compiler that compiled it | It is the precondition for teaching either path anything new |
| **N3** | ✅ **DONE 2026-10-01** — **2.1 edit prompt content** (route 1, edit side) — `QwenImage21EditPromptCompiler.BuildSystemMessage()` now overrides the inherited 2511 text with the 2.1 instruction grounded in `research/qwen-2-1-prompt-enhancer.md` §5/§8: attribute disentanglement at full strength (both failure modes named), anchor-on-the-image, preservation stated affirmatively ("keep X unchanged", never "do not change X"), identity pointed at the source/reference image rather than re-described, and region-confinement language. The 2511 instruction is unchanged (pinned by test). | `QwenImage21EditPromptCompiler.cs` | Compiled 2.1 instructions obey those rules; the 2511 compiler's output is unchanged | Gives EVP-2/EVP-3/EVP-4 a compiler that can *say* what the model does |
| **N4** | ✅ **DONE 2026-10-01** — **Region render proof** (CASE-23 step 2) — operator rectangle run changed only the ringed subject, and the feathered-mask fix removed the visible seam | real app path (scene image editor) | The rectangle run + the baked-feather mask confirm confinement + no hard seam | The one missing piece of region evidence; also validated the mask path N3 depends on |
| **N5** | ✅ **DONE 2026-10-02** — **Region-aware instruction** — a set region adds the confinement clause to the compiled prompt (compile request → context `Region` → `BuildMessages`), a non-region run's does not | `SceneImageEditCompilerContext` + `Region`; 3 compile requests/attempts/job handlers; `ImageEditWorkspace.razor` passes the drawn region | A region run's compiled prompt contains the confinement clause; a non-region run's does not (pinned by test) | Without it the mask confines *pixels* while the instruction invites whole-frame change — the two would disagree |
| **N6** | **Feather + panorama** (EVP-2, EVP-3) | feather is BAKED into the mask PNG (`ImageRegionMaskEngine` paints the falloff; no `FeatherMask` node — that node feathers the frame border only). Outpaint is PROVEN (CASE-24): `ImagePadForOutpaint` + inverted `MaskRectArea` + `VAEEncodeForInpaint` preserves the original and generates the new strip. App wiring DONE 2026-10-02 (operation → workspace → worker → client pads + intent pre-fill). Compiler-side outpaint awareness DONE 2026-10-03 (`Outpaint` on the compile context + geometry clause; a ready result may carry an empty targets list; `OutpaintJson` persisted). Follow-up: make the edit intent OPTIONAL for outpaint (blank = extend naturally) and show the run-time geometry clause in the submitted-prompt preview | Measured: no seam at the region edge; panorama extends the canvas with the original area preserved | Only after N5, because both are expressed *in the instruction* |
| **N7** | ✅ **DONE 2026-10-03** — **Per-checkpoint research docs** (B135-037) — one per checkpoint, with citations | `research/biglust-v16-prompting.md`, `juggernaut-ragnarok-prompting.md`, `pony-v6-prompting.md`, `pony-realism-v23-prompting.md`, `flux1-dev-prompting.md`, `flux2-pro-prompting.md`, `krea2-turbo-prompting.md`, `api-serverless-provisional.md` (the six API rows stay PROVISIONAL); Qwen-Image-2.1 already covered by `qwen-2-1-prompt-enhancer.md` | Each profile row's values resolve to a cited source or are labelled provisional — the seed's `ResearchSource` strings already carry the citations; the docs make each one durable and discoverable | It is the governance precondition for any prose work (rules 4 and 9) |
| **N8** | ⏸️ **DEFERRED 2026-10-03** — **BigLust/Juggernaut small-form builder** (B135-008 slice 2) — **only if measured** | new builders + profile rows | `B135-031` shows the shared SDXL prose produces a worse prompt for them | Operator deferred to focus on other models; their differences stay profile data; re-open only if B135-031 measures a gap |

### Decision points that are still open (need the operator)

1. **N1 scope:** Qwen-2.1 alone, or Qwen + BigLust/Juggernaut in the same pass?
2. **Canvas support:** region-only first, or region **and** aspect/canvas change (panorama/outpaint) in
   the same slice? Canvas needs the response-contract decision that route 2 would settle.
3. **PE weights:** register `Qwen-Image-2.1-PE-*` (VRAM + licence questions), or stay on route 1
   indefinitely and treat route 2 as closed?

---

## 5. Pointer map

- Compiler prose (single home): `DreamGenClone.Domain/RolePlay/SceneImageCompilerSystemPrompts.cs`
- Profiles: `DreamGenClone.Domain/RolePlay/ImageCompilerProfile.cs` · `ImageCompilerProfileValidation.cs` ·
  `DreamGenClone.Infrastructure/RolePlay/ImageCompilerProfileRepository.cs` (seed)
- Create compilers (routing): `DreamGenClone.Web/Application/RolePlay/SceneImagePromptCompilers.cs` ·
  `Program.cs:585`
- Builders: `SdxlSceneImagePromptBuilder.cs` · `PonySceneImagePromptBuilder.cs`
- Edit compiler (single): `DreamGenClone.Web/Application/RolePlay/QwenSceneImageEditPromptCompiler.cs` ·
  `Program.cs:578`
- Editor discrimination: `DreamGenClone.Domain/ModelManager/ImageEditorGraphKind.cs` ·
  `artifacts/tmp/dbquery/queries/editor_graph_kinds.sql`
- PE research + decision: `specs/Planning/B-135-image-playground/research/qwen-2-1-prompt-enhancer.md`
  (§6 gates, §7 tasks, §8 decision) · tasks `…/tasks.md` (B135-008 note, B135-039, B135-040–044)
- Region feature: `DreamGenClone.Web/Application/RolePlay/Editing/{MediaEditOperations.cs, ImageRegionMaskEngine.cs, RegionDragCalculator.cs, MediaEditImageEditingJobHandler.cs, MediaEditCompilationService.cs, ImageEditWorkspaceModels.cs}` ·
  `DreamGenClone.Infrastructure/Models/ComfyUIImageEditingClient.cs` ·
  `DreamGenClone.Web/Components/Editing/ImageEditWorkspace.razor(.css)` ·
  `DreamGenClone.Web/wwwroot/js/roleplay-workspace.js` · evidence `specs/image-generator-tests/qwen-21-native-reference/CASE-23-circle-region-selection.md`
