# B-137 — Tasks (Krea 2 local ComfyUI generation)

Dependency-ordered. Each task names the files it touches and its impact so blast radius is visible
before work starts. `[P]` = parallelisable with the task above it. Gates lock the phases: bindings
before resolution, resolution before the graph builder, graph before UI, UI before seed, seed before
smoke.

Legend: `[ ]` open · `[x]` done · `[~]` partially done.

Locked decisions are `plan.md` §1 (D1–D7); the scene-LoRA architecture is `plan.md` §4; the test list
is `plan.md` §5. No task reintroduces a negative prompt (D4) or a fallback/default branch (repo rules).

---

## Phase 1 — Domain + refs (bindings)

### B137-001 — Enum values + domain compatibility
Add `SceneImageModelFamily.Krea2 = 6`, `SceneImagePromptDialect.Krea2NaturalLanguage = 5`, and the
`(Krea2, Krea2NaturalLanguage)` pair to `SceneImagePromptMetadata.IsCompatible`. Append-only, never
renumber (persisted values are indexed by DbQuery).
- **Files:** `DreamGenClone.Domain/ModelManager/SceneImageModelFamily.cs`
- **Impact:** additive enum + one allow-list pair. Nothing else compiles against the new values yet,
  so this is the lowest-risk task and the seed for every later one.
- **Analysis done:** value 6 is free (QwenImage21 = 5); dialect 5 is free (QwenImageEditRemix = 4).
- **Test:** `SceneImageModelFamilyTests` true pair + false cross-pairs (Krea2 × tag/NL/Flux dialects).

### B137-002 — `Krea2Refs` + `Krea2ModelSettings` `[P]`
`record Krea2Refs(UnetName, ClipName, VaeName, Steps, Cfg, Sampler, Scheduler, Denoise, Width,
Height, IReadOnlyList<SceneLoraSpec> BaseLoras)` with a `static Resolve(capabilityQualificationsJson)`
that fails fast naming the missing property. Mirror `QwenImage21Refs` / `QwenImage21ModelSettings`.
`BaseLoras` is present for symmetry but **empty for Krea2** (nothing is force-applied, D5/§4).
- **Files:** `DreamGenClone.Domain/ModelManager/Krea2Refs.cs` (new),
  `DreamGenClone.Web/Application/ModelManager/Krea2ModelSettings.cs` (new)
- **Impact:** new files only; no call site yet. The fail-fast contract is the regression surface —
  a missing `steps`/`cfg`/etc. must name the setting, not default.
- **Analysis done:** the verified graph uses steps=8, cfg=1, euler, simple, denoise=1, 1024² —
  captured here as resolved values, never hardcoded at the build site.
- **Test:** `Krea2ModelSettingsTests` round-trip; missing property fails fast naming it.

### B137-003 — `ResolvedImageModel` + `ResolvedSceneLora` + `SceneLora` catalog entity
`ResolvedImageModel` gains `Krea2Refs? Krea2 = null` (only for `Krea2`) and
`IReadOnlyList<ResolvedSceneLora>? SceneLoras = null`. Add `record ResolvedSceneLora(FileName,
Strength, Purpose?)` — deliberately NOT `ResolvedCharacterLora` (scene/act LoRAs must not masquerade
as identity). Add the catalog row `SceneLora` (Id, FileName, DisplayName, SceneImageModelFamily,
Category, DefaultStrength, IsEnabled).
- **Files:** `DreamGenClone.Domain/ModelManager/ResolvedImageModel.cs`,
  `DreamGenClone.Domain/ModelManager/ResolvedSceneLora.cs` (new),
  `DreamGenClone.Domain/ModelManager/SceneLora.cs` (new)
- **Impact:** additive nullable fields on a shared record + two new records. No reader/writer yet;
  the `SceneLoras` separation from `Loras` (identity) is the architectural point of §4.
- **Analysis done:** `ResolvedImageModel.Loras` is identity-only and fail-fast for unwired families;
  scene LoRAs must be a distinct list so they chain before identity without binding to a character.
- **Test:** none standalone — exercised by B137-012 and the repository tests (B137-010).

---

## Phase 2 — Scene-LoRA storage & resolution (D5)

### B137-010 — `SceneLoraRepository` + migration
`ISceneLoraRepository` / `SceneLoraRepository` with `ListAsync(family)` returning the compatible
catalog subset. Migration is a presence-checked `CREATE TABLE` following `SqlitePersistence.cs`.
- **Files:** `DreamGenClone.Infrastructure/ModelManager/SceneLoraRepository.cs` (new),
  `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
- **Impact:** new table + new read path. Read-only at render time; no write surface in the app.
- **Analysis done:** the catalog is the single source of what LoRAs exist and their family/category —
  the picker and the resolver both read it; nothing hardcodes the list.
- **Test:** `SceneLoraRepositoryTests` — `ListAsync(family)` returns only the compatible subset;
  seed round-trips.

### B137-011 — `SceneImageStudioSettings.SceneLoras` + selection type `[P]`
Add `List<SceneImageLoraSelection> SceneLoras` (a small `{ FileName, Strength }` type) to the
settings contract. Serialisation round-trips; a selection for a different family is rejected at
resolve time, not silently dropped.
- **Files:** `DreamGenClone.Web/Application/RolePlay/Models/SceneImageStudioSettings.cs`
- **Impact:** additive field on a persisted settings object (contracts must stay backward-compatible —
  null/empty means "no scene LoRAs", which is today's behaviour).
- **Analysis done:** the selection travels on the render request, not on the model row (D5) — the
  model carries no base stack.
- **Test:** settings serialise/round-trip; cross-family selection fails fast.

### B137-012 — Resolution service: compatibility + `Krea2` + `SceneLoras` populate
The **duplicated** `IsCompatible` allow-list (~line 619) gains the Krea2 pair. When the resolved
family is `Krea2`, resolve `Krea2Refs` into `ResolvedImageModel.Krea2`. Populate
`ResolvedImageModel.SceneLoras` from the request's selection (resolved via the catalog), not from
the model row.
- **Files:** `DreamGenClone.Web/Application/ModelManager/ModelResolutionService.cs`
- **Impact:** one duplicated allow-list edited; resolution path touched. Both allow-lists
  (`RegisteredModelRepository.cs:331` and this file ~619) must agree — keep them in lock-step.
- **Analysis done:** two duplicated allow-lists exist; B137-012 and B137-021 edit one each. The
  second edit is in Phase 3 only because the graph builder needs the resolution result first.
- **Test:** `ModelResolutionServiceTests` — Krea2 pair resolves; Krea2 model populates `Krea2` refs;
  missing capability JSON fails fast.

---

## Phase 3 — Graph builder (Krea2 workflow + LoRA chain)

### B137-020 — `BuildKrea2Workflow` + family switch + `ApplyCharacterLoras` generalisation
`BuildKrea2Workflow(unet, clip, vae, prompt, size, seed, krea2Refs)`:
`UNETLoader` → `[base LoRA chain]` → `[scene/act LoRA chain]` → `[character LoRA chain]` →
`KSampler(steps, cfg, euler, simple, denoise 1)`; `CLIPLoader(clip, type='krea2')` →
`CLIPTextEncode` → positive; `ConditioningZeroOut(positive)` → negative (no negative text encode);
`VAELoader(qwen_image_vae)` → `VAEDecode` → `SaveImage`. Wire `Krea2` into the family switch.
Generalise `ApplyCharacterLoras` so the Krea2 family is in the LoRA-wired set (today only Pony/SDXL;
it fails fast otherwise), chaining via `LoraLoaderModelOnly` (the verified graph used it).
- **Files:** `DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs`
- **Impact:** the one shared-code touchpoint. The generalisation must preserve Pony/SDXL output
  byte-for-byte; their existing tests are the guard.
- **Analysis done:** 59-cell proof verified `LoraLoaderModelOnly` chaining and the exact node set;
  no negative text-encode is emitted (D4) — negative is `ConditioningZeroOut` of the positive.
- **Test:** `ComfyUIImageClientKrea2Tests` (new) — graph shape (`UNETLoader`/`CLIPLoader(type=krea2)`/
  `ConditioningZeroOut`/`KSampler` present; no negative text-encode node); `Krea2Refs` missing ⇒
  fail-fast; scene LoRA chained in order; character LoRA appended last; no-LoRA graph unchanged.
  Plus the existing Pony/SDXL client tests stay green.

### B137-021 — `RegisteredModelRepository` duplicated allow-list `[P]`
The **other** `IsCompatible` allow-list (~line 331) gains the Krea2 pair.
- **Files:** `DreamGenClone.Infrastructure/ModelManager/RegisteredModelRepository.cs`
- **Impact:** one duplicated allow-list edited. Must mirror B137-012 exactly.
- **Test:** covered by `ModelResolutionServiceTests` + existing repository tests.

### B137-022 — Serverless gate: Krea2 fails fast `[P]`
Krea2 is local-only (D7): the compatibility check in `RunPodServerlessImageClient` fails fast
(`unsupported`) for a Krea2 model on a serverless provider instead of emitting a wrong payload.
- **Files:** `DreamGenClone.Infrastructure/Models/RunPodServerlessImageClient.cs`
- **Impact:** guard only; no new payload path. Actual RunPod support is deferred.
- **Test:** a Krea2 model routed to serverless throws `unsupported`, naming the family.

---

## Phase 4 — Prompt compilers

### B137-030 — Krea2 system prompt
`For(Krea2, canonical)` returns the brief-shape system prompt encoding the four rules (D6) and the
photoreal default ("Photorealistic editorial photograph … 35mm, shallow depth of field").
- **Files:** `DreamGenClone.Domain/RolePlay/SceneImageCompilerSystemPrompts.cs`
- **Impact:** one system-prompt string. The four rules are the regression surface.
- **Analysis done:** D6 = photographic brief (format + subject + action + lens + DoF), never body
  noun-lists, never framing demands, never a face-facing clause on act prompts, both actors named.
- **Test:** prompt shape assertions — no body noun-list, no framing demand, no face-facing clause.

### B137-031 — `Krea2SceneImagePromptCompiler` + registry + DI `[P]`
A natural-language brief compiler registered in the registry and DI.
- **Files:** `DreamGenClone.Web/Application/RolePlay/SceneImagePromptCompilers.cs`,
  `SceneImagePromptCompilerRegistry`, `Program.cs`
- **Impact:** new compiler instance; registry gains one entry. No existing compiler changes shape.
- **Test:** `SceneImagePromptCompilerRegistryTests` — the Krea2 compiler resolves for the new pair.

### B137-032 — Family slug + text-only body/wardrobe lists `[P]`
`Krea2 => "krea2"` in the family→slug map; Krea2 joins the natural-language families for text-only
body/wardrobe briefs (no reference-image strategy — Krea2 has no reference conditioning, D1).
- **Files:** `DreamGenClone.Web/Application/RolePlay/WardrobeItemPromptGenerationJobHandler.cs`,
  `DreamGenClone.Web/Application/RolePlay/BodyReferencePromptCompiler.cs`,
  `DreamGenClone.Web/Application/RolePlay/WardrobeItemPromptCompilers.cs`
- **Impact:** additive family entries; only text paths are enabled. Confirm the gating lists and add
  Krea2 only where the path is text.
- **Test:** text-only body/wardrobe compile for Krea2 succeeds; reference strategy is refused.

---

## Phase 5 — Job handler wiring

### B137-040 — Resolve scene-LoRA selection at render time
`SceneImageRenderingJobHandler` resolves `SceneImageStudioSettings.SceneLoras` (the user's
multi-select) into `ResolvedImageModel.SceneLoras`, beside the existing `ResolveCharacterLorasAsync`
path.
- **Files:** `DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs`
- **Impact:** the render request now carries scene LoRAs through to the client. Character LoRAs are
  still appended last by the builder.
- **Test:** `SceneImageRenderingJobHandlerTests` — selection reaches `ResolvedImageModel.SceneLoras`
  in the user's order; empty selection = null list (unchanged graph).

---

## Phase 6 — UI

### B137-050 — Model/dialect dropdown options
Family dropdown gains "Krea 2"; dialect dropdown gains "Krea2NaturalLanguage".
- **Files:** `DreamGenClone.Web/Components/Pages/ModelManager.razor` (~451),
  `DreamGenClone.Web/Components/Shared/ModelDetailsEditor.razor` (~117)
- **Impact:** two option lists. Razor editing rules apply (full-context read, no hallucinated tag
  helpers).
- **Test:** manual — dropdowns render the new options.

### B137-051 — Studio Krea2 branch + `SceneLoraPicker` component
`SceneImageStudio.razor` (~1639): add a Krea2 branch with the reference slot list **absent** (no
reference slots — matching B-130's capability-formed surface, "absent, not disabled"). Add the
`SceneLoraPicker` multi-select component (filtered by the selected model's family, one strength per
entry, defaulting to the catalog's `DefaultStrength`), wired into `SceneImageStudio` and
`CompositionComposer`.
- **Files:** `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`,
  new `SceneLoraPicker` component, `CompositionComposer` wiring
- **Impact:** the only UI task with real surface. The picker must filter to the family (D5) and the
  Krea2 branch must hide reference slots.
- **Analysis done:** Krea2 has no native reference-image support (verified: no krea-named reference
  node; 2.1's autogrow image slots are 2.1-only) — so the slot list is absent for Krea2.
- **Test:** picker contract test — cross-family LoRA not offered; selected strength serialises.

---

## Phase 7 — DbQuery + seed (destructive DB step — backup first)

### B137-060 — Family name array
Add `"Krea2"` to the family name array at index 6.
- **Files:** `DreamGenClone.DbQuery/ModelManagerTransfer.cs` (~148)
- **Impact:** DbQuery tooling only; position must match enum value 6.
- **Test:** DbQuery family-name lookup returns "Krea2" for value 6.

### B137-061 — Seed scene-LoRA catalog + model row (via dbq)
Backup `dev.db` first (destructive step). Seed: (a) the scene-LoRA catalog rows for Krea2 (NSFW V4,
the five grounded act LoKRs, anatomy/realism helpers) with family + category; (b) one model row —
family `Krea2`, dialect `Krea2NaturalLanguage`, identifier `krea2_turbo_fp8_scaled.safetensors`,
protocol ComfyUI, under the existing local ComfyUI generation provider,
`CapabilityQualificationsJson` = unet/clip/vae/steps=8/cfg=1/euler/simple/denoise=1/resolution=1024.
Additive only — never repoint the `RolePlaySceneImage` default.
- **Files:** dbq seed command / `.sql`; `dev.db` (backup first)
- **Impact:** persisted data only. Nothing is force-applied; the model row carries no base LoRA stack.
- **Test:** catalog lists only Krea2-family rows; the model row resolves to `Krea2` refs.

---

## Phase 8 — Tests + verification

### B137-070 — Test assembly + green run
Assemble the full test set (§5) and run the targeted blast-radius classes:
`SceneImageModelFamilyTests`, `ComfyUIImageClientKrea2Tests`, `Krea2ModelSettingsTests`,
`SceneLoraRepositoryTests`, `SceneImageStudioSettingsTests`, `SceneImagePromptCompilerRegistryTests`,
`ModelResolutionServiceTests`, `SceneImageRenderingJobHandlerTests`, `ImageSuiteRenderDriverTests`.
`ImageSuiteImporterTests` count anchor is already 49 (it moves whenever the catalog grows — intended).
- **Files:** `DreamGenClone.Tests/RolePlay/**` (new test files + existing)
- **Impact:** test-only. Repo rule: implementation changes must leave the suite green; targeted run,
  not the full suite.
- **Test:** all listed classes green; existing Pony/SDXL client tests unchanged.

### B137-071 — Playground smoke + verification
Import the `baseline` catalog, pick the Krea2 model + a cell, render; compare against the 59-cell
proof (same graph, same prompt text). Confirm the negative-prompt field is disabled/declared for the
Krea2 row, and that selecting a character LoRA with a Krea2 model no longer throws
`unsupported_lora_family`.
- **Files:** none (manual verification)
- **Impact:** end-to-end confirmation; the proof gallery is the reference.
- **Analysis done:** this is the rollout gate — build (0 errors) + targeted tests green + smoke pass.

---

## Verification gates (in order)

1. Phase 1 green → enum/refs/entity exist; domain compiles.
2. Phase 2 green → catalog + resolution return Krea2 refs + scene LoRAs.
3. Phase 3 green → Krea2 workflow built; Pony/SDXL tests unchanged.
4. Phase 4 green → Krea2 compiler resolves; brief-shape prompt emitted.
5. Phase 5 green → selection reaches the render request.
6. Phase 6 green → dropdowns + picker + absent reference slots.
7. Phase 7 green → seeded catalog + model row resolve; `dev.db` backed up first.
8. Phase 8 green → targeted tests green + playground smoke matches the 59-cell proof.
