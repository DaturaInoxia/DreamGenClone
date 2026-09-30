# B-135 — Image Playground (plan)

**Design:** `spec.md` (locked decisions D1–D20). **Tasks:** `tasks.md`.
**Route:** `/playground`

---

## 1. Architecture

Six layers, in dependency order. Nothing in the UI layer may be built before its bindings layer is green.

| Layer | What it owns | Home |
|---|---|---|
| **1. Profiles** | one row per checkpoint: dialect, budget, required components, forbidden tokens, pose-in-text, negative + citation, settings envelope, system prompt, examples | `DreamGenClone.Domain/RolePlay/ImageCompilerProfile.cs` + `ImageCompilerProfiles` table |
| **2. Compiler resolution** | resolve a compiler **by checkpoint profile** (not by family); fail fast when a checkpoint has no profile | `ImageCompilerProfileRegistry` (replaces `ISceneImagePromptCompilerRegistry` family keying) |
| **3. Cells & suites** | `ImageSuite` / `ImageSuiteCell` with `UserDirection`, `ExpectedPrompt`, the full binding declaration, seed policy, settings, declared gates + tolerances | Domain + `ImageSuites` / `ImageSuiteCells` tables |
| **4. Runs** | `ImageRun` / `ImageRunCell`: resolved variables, request graph, image, per-layer verdicts, gate results, timing/cost | Domain + `ImageRuns` / `ImageRunCells` tables |
| **5. Evaluators & gates** | prompt conformance (pure), request assertions (pure), native image gates | `DreamGenClone.Web/Application/RolePlay/Evaluation/` |
| **6. The surface** | `/playground`: rail (suites → cells) · the step · runs + comparison | `Components/Pages/Playground.razor` + shared components |

Reused, not rebuilt: `ImageStepComposer.razor` (B-130), `ModelPicker` (B-131), `ReferenceStrategyCatalogue` + the capability-gated resolver (B-129), `ReferencePicker`, `ScenePromptOverridesApplier` (D4 of B-130), `ISceneImageCharacterLoraResolver`, `IHostedJobQueue`, the review decks' decision vocabulary.

---

## 2. Data model

Follows the repo's established persistence conventions: `pragma_table_info`-guarded `ALTER TABLE` for new columns (the B-032/B-131 pattern in `SqlitePersistence.cs`), enum members persisted by **name**, one row per profile/cell/run.

```
ImageCompilerProfiles      Id · Name · CheckpointModelId · Dialect · MinChars · MaxChars · MaxTokens
                           RequiredComponentsJson · ForbiddenTokensJson · PoseInText
                           Negative · NegativeSource · SettingsEnvelopeJson
                           SystemPrompt · ExamplesJson · Version · UpdatedUtc

ImageSuites                Id · Name · Version · Kind · Status · Description · UpdatedUtc

ImageSuiteCells            Id · SuiteId · Ordinal · Name · CheckpointProfileId
                           UserDirection · ExpectedPrompt
                           BindingsJson          -- every axis: mode + value + declared strategy
                           SeedJson              -- fixed / list / grid
                           SettingsJson          -- steps/cfg/sampler/scheduler/resolution/refCount
                           CompilerLlmJson       -- model + temperature + seed (D11)
                           GatesJson             -- declared gates + configured tolerances
                           SimilarityTolerance   -- per cell, never defaulted (D12)

ImageRuns                  Id · SuiteId · SuiteVersion · Label · VariableJson · HostJson
                           StartedUtc · CompletedUtc · Status · CreatedBy

ImageRunCells              Id · RunId · CellId · UserDirectionUsed · CompiledPrompt · ExpectedPromptUsed
                           PromptLayerJson · RequestLayerJson · RequestGraphJson
                           RenderFromPath        -- 'Compiled' | 'Expected' (D19)
                           ImageId · VisualVerdict · GateResultsJson · TimingMs · CostUsd
```

**Binding declaration (D17)** — one entry per axis, each `{ Axis, Mode, Value, DeclaredStrategy }`, with `Mode ∈ Text | Reference | Adapter | Lora`:

| Axis | Modes |
|---|---|
| Identity (per character) | identity text · reference image (pack view) · IP-Adapter · character LoRA (artifactId + strength) |
| Pose | pose text · skeleton reference (pose library) · ControlNet adapter |
| Location | location text · reference image · depth/canny control |
| Wardrobe | wardrobe text · reference image |
| POV · lighting | text/camera only — no image slot |

The run records the **resolved** strategy per axis, so a proof shows what actually executed rather than what was requested.

---

## 3. Compiler profile refactor

**Today:** `ISceneImagePromptCompilerRegistry.Resolve(SceneImageModelFamily, SceneImagePromptDialect)` → BigLust and Juggernaut share `SdxlSceneImagePromptCompiler`; **Qwen-2.1 and FLUX both inject `SdxlSceneImagePromptBuilder`**.

**After:** resolution is by `ImageCompilerProfile`, and the family/dialect pair is *derived from the profile* rather than used as the key. A checkpoint with no profile **fails fast**, naming the checkpoint — which is exactly what governance rule 4 already requires (*"a model with no researched settings cannot be the target of a compiler"*).

Profile-shaped compilers get their own builder where the nuance is real:

| Profile | Prompt shape |
|---|---|
| **BigLust v1.6** | small, low-detail natural language; CFG ~4–5; no author guide ⇒ profile is derived from our own A/B |
| **Juggernaut XL Ragnarok** | small, low-detail natural language; 832×1216; DPM++ 2M SDE; 30–40 steps |
| **Pony V6 / Pony Realism** | dense tags; the full 6-tag quality string; explicit camera view and count tags; the **cited** short guard negative |
| **Qwen-Image-2.1** | **long, detailed** natural language — its own builder, no longer the SDXL one |
| **FLUX.1-dev / FLUX.2** | natural language; FLUX.2 already rejects the negative field |
| **API models** | natural language; no dialect |

This is where the external research lands (§6 of the spec): a cited research document per checkpoint, folded into the profile row and the standards doc.

---

## 4. Negative purge (D10)

**Remove the capability, not just the values** — the FLUX.2 compiler already shows the pattern (it rejects `negative_prompt` at any nesting level).

| Surface | Change |
|---|---|
| `ISceneImagePromptCompiler` | delete `CanonicalNegativePrompt` + `BuildNegativePrompt` |
| `SceneImagePromptCompilers.cs` | delete the Pony hardcoded list (`"lowres, bad anatomy, bad hands, …"`) and every empty override |
| `SceneImageStudioSettings.NegativePrompt` | delete the field; the Studio's negative textbox goes with it |
| `SceneImageProductionSettingsContract`, `IdentityControlledRequestCompiler`, `ReviewDeckShared`, `ISceneAssetService`/`SceneAssetService`, `PoseTestRenderService` | delete the negative carried through each |
| `SdxlSceneImagePromptBuilder` / `PonySceneImagePromptBuilder` | `BuildDeterministicBeatNegativePrompt` **moves to the profile** — the Pony profile's cited guard set is the only non-empty value in the system |
| `ComfyUIImageClient` + `RunPodServerlessImageClient` | drop the negative parameter; the workflow builder reads the profile |
| `helpers/runpod/workflows/*.json` + LoRA cell templates | empty the negative node |
| `SceneImageRenderingJobHandler.ResolveNegativePromptAsync` | resolves from the profile, or empty |
| **Docs** | `scene-image-prompt-compiler-standards.instructions.md` §3.2/§3.3/§4 and `pony-v6-prompting.instructions.md` rewritten to one rule: **describe the desired state**; Pony's short guard negative documented as the cited deviation |
| **Guard test** | fails if any path can produce a negative the profile did not declare (mirrors B130-023's "no composer surface hardcodes a strategy list") |

---

## 5. Gates (D7)

Enforced, natively, in C# — no Python at runtime, no ONNX (D7):

| Gate | Mechanism | Source of truth |
|---|---|---|
| **Region containment** | mean abs diff inside vs outside the rect, blurred edge; compared against an unmasked control | `tools/qwen-region-proof/measure_region.py` (measured: contained = 0.319 outside vs 19.86 for the control, 62×) |
| **Pose agreement** | DWPose readback via the app's **own ComfyUI client**, scored as joint geometry as % of figure height | `tools/pose-angle-probe` (front 2.12 %, 3/4 2.57–2.68 %, profile 3.50–4.28 %) |
| **Presence / sanitisation** | subject count vs declared count; pixel heuristics | `tools/consistency-scoring/scoring/{presence,sanitisation}.py` |

Advisory only (shown, never blocking): **eyes/head angle** (`tools/eye-validation`, MediaPipe — operator judges visually) and **identity / subject similarity / adherence** (`tools/consistency-scoring`; these are DINOv2 + CLIP, i.e. ONNX work that D7 removes from scope).

**Two rules attach to the ported gates:**
1. Each C# gate must reproduce the **same measurement** the approved tool defines — never a re-derived heuristic.
2. Each ported gate is **qualified against its Python tool** on a fixed image set before it may gate anything, and the agreement is recorded as that port's own proof.

`score_presence`'s mechanism must be confirmed before it is ported — its imports are function-local, so whether it needs a detector is an open question for B135-006.

---

## 6. Phases

| Phase | Contents | Gate to proceed |
|---|---|---|
| **P1 — Profiles & resolution** | profile domain + table + repository + seed rows; compiler resolution by profile; negative purge; guard tests | Build 0 errors; negative guard test green; family keying gone |
| **P2 — Cells, runs, evaluators** | suites/cells/runs persistence; prompt conformance evaluator; binding declaration; run executor (single or batch, D14) | A cell round-trips; both free layers assert on a real step |
| **P3 — Native gates** | region + pose + presence/sanitisation, each qualified against its Python tool | Each gate's qualification record committed |
| **P4 — The `/playground` surface** | rail · step · two-prompt cell editor · render-from-either · binding matrix UI · save-as-recipe · route switch · sweep runner · chain view · run history + purge | Operator can run a cell end to end in the browser |
| **P5 — Proof** | run-vs-run cell-paired compare (one declared variable) · compiler-fidelity compare · suite seeding from the catalogs and flows · specimen capture from a real moment · Promote | A baseline-vs-LoRA comparison produces a verdict |

**Deliberately not in v1:** automatic drift flagging (operator decision — comparison only), ONNX model gates, and any suite scheduled to run unattended.

---

## 7. Blast radius

**Touched, all additive except the purge:**
`SceneImagePromptCompilers.cs` · `ISceneImagePromptCompiler.cs` · `Models/SceneImageStudioSettings.cs` · `SceneImageProductionSettingsContract.cs` · `IdentityControlledRequestCompiler.cs` · `ReviewDeckShared.cs` · `ISceneAssetService.cs`/`SceneAssetService.cs` · `PoseTestRenderService.cs` · `SceneAssetGenerationJobHandler.cs` · `SceneImageRenderingJobHandler.cs` · `SdxlSceneImagePromptBuilder.cs` · `PonySceneImagePromptBuilder.cs` · `BodyReferencePromptCompiler.cs` · `CharacterIdentityBodyService.cs` · `ComfyUIImageClient.cs` + `RunPodServerlessImageClient.cs` · `SqlitePersistence.cs` · `helpers/runpod/workflows/*.json` · three instruction docs.

**`ImageStepComposer.razor`** gains the explicit binding-mode selectors (D17) — the highest-risk file, and the one that must be edited in micro-steps per the Razor rules.

**Not touched:** the RP engine, the beat/moment pipeline, the production render path's *behaviour* (only the negative resolution source changes), identity/body pack flows, the LoRA training dispatch.

---

## 8. Evidence plan

| Artefact | Where |
|---|---|
| Native gate qualification vs its Python tool | `tools/<gate>-proof/` package + a committed result |
| Compiler profile research, per checkpoint, with citations | `specs/Planning/B-135-image-playground/research/<checkpoint>-prompting.md` |
| The `ExpectedPrompt` catalog per checkpoint | seeded suite cells (DB) + export |
| Run export in the committed evidence shape | `specs/image-generator-tests/**` (images + `manifest.json`) |
| Negative purge proof | guard test + a grep-able statement that no non-profile negative path exists |

---

## 9. Risks

| Risk | Mitigation |
|---|---|
| **Compiler output is non-deterministic** (LLM-backed for SDXL/API/Qwen/FLUX; Pony's builder is deterministic) | D11 — pin the compiler LLM as a run variable; property checks are the gate, similarity is a tolerance. Pony cells may assert exactly |
| **The purge is reverted by a later "fix"** | The capability is deleted, plus a guard test — the same discipline that stopped the strategy-list regression (B130-023) |
| **Gate port drifts from the canonical tool** | Each gate is qualified against its tool before use; the tool stays the definition |
| **Profile research is thin for BigLust** (no author guide exists) | its profile is *derived from measured A/B in this feature* and labelled as such — never presented as author-recommended |
| **Seeding suites changes the committed evidence** | Seeding **reads** the catalogs; it never rewrites `specs/image-generator-tests/**`. Export is a separate, explicit action |
| **B-127 lands late** | P2/P3 build against the current key; the comparison surfaces (P5) are the part that needs template-keyed identity, and they fail fast if it is absent |

---

## 10. Open items

Carried from `spec.md` §7: the similarity measure (D12), the sweep declaration shape, specimen capture's expected-prompt provenance, and suite versioning frozen-on-first-run. None blocks P1.
