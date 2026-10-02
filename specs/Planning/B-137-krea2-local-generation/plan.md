# B-137 — Krea 2 (Krea-2 Turbo) local ComfyUI generation

**State:** designed · **Priority:** medium · **Scope:** medium · **Date:** 2026-10-01

Add Krea-2 Turbo (12B DiT, Qwen3-VL 4B text encoder, Qwen Image VAE) as a scene-image generation
family on the local ComfyUI host (RTX 5080 16 GB). Text-only, photorealistic default. No reference
conditioning, no edit, no style-reference graph.

---

## 1. Locked decisions (do not re-litigate)

- **D1 — Text-only generation.** Krea 2 is wired as plain t2i with a photorealistic default prompt
  style. **No image style reference** (user decision 2026-10-01), no 2.1-style reference slots, no
  edit path, no ControlNet. The only conditioning is text + LoRAs.
- **D2 — Enum values append-only.** `SceneImageModelFamily.Krea2 = 6`,
  `SceneImagePromptDialect.Krea2NaturalLanguage = 5`. Never renumber (persisted; DbQuery indexes by
  value).
- **D3 — Everything is UI-backed, fail-fast.** Artifacts (unet/clip/vae), sampling (steps/cfg/
  sampler/scheduler/denoise/resolution) and the LoRA stack are resolved from the model row
  (`CapabilityQualificationsJson`) via a `Krea2Refs` record, mirroring `QwenImage21Refs`. No
  hardcoded defaults, no fallback branches; missing config fails fast naming the setting.
- **D4 — Negative prompt: already gone app-wide.** B-135 purged the negative as a value
  (`SceneImageStudioSettings.NegativePrompt` deleted; no compiler emits one). Krea 2 needs nothing:
  its graph's negative is `ConditioningZeroOut` of the positive, and the app already supplies none.
  Do not reintroduce a negative for this family.
- **D5 — Scene LoRAs are a user-picked MULTI-SELECT, filtered to the selected model.** A scene-LoRA
  catalog (persisted rows: filename, display name, family, category, default strength) feeds a
  multi-select picker that shows only LoRAs compatible with the selected model's family/checkpoint.
  The selection travels on the render request and chains in the user's chosen order, with character
  identity LoRAs appended last. Nothing is force-applied. See §4.
- **D6 — Prompt shape is the compiler's contract.** The Krea 2 compiler emits photographic briefs
  (format + subject + subject's action + lens + depth of field), never body noun-lists, never
  framing demands, never a face-facing clause on act prompts, and names both actors of an act.
- **D7 — Local only.** RunPod/cloud Krea 2 and character-LoRA training for Krea 2 are out of scope
  (deferred); the family is not offered on serverless providers initially.

## 2. What is already done (do not redo)

- The 59-cell proof matrix (`helpers/local-comfyui-host/run-krea2-proof.ps1`), all renders reviewed.
- Host models installed and verified: `krea2_turbo_fp8_scaled`, `qwen3vl_4b_fp8_scaled`,
  `qwen_image_vae` + the NSFW V4 LoRA and the five grounded act LoKrs.
- The baseline catalog (`specs/image-generator-tests/baseline`) carries `krea2` variants for all 49
  positions, a `krea2` model legend + dialect note, and four new woman-receiving-oral cells.
- The feasibility fixes across all eight dialects (25 strings), render-validated.

## 3. File-by-file change plan

### Domain

1. `DreamGenClone.Domain/ModelManager/SceneImageModelFamily.cs`
   - Add `Krea2 = 6` to `SceneImageModelFamily`; `Krea2NaturalLanguage = 5` to
     `SceneImagePromptDialect`; add `(Krea2, Krea2NaturalLanguage)` to
     `SceneImagePromptMetadata.IsCompatible`.

2. `DreamGenClone.Domain/ModelManager/Krea2Refs.cs` **(new)**
   - `record Krea2Refs(UnetName, ClipName, VaeName, Steps, Cfg, Sampler, Scheduler, Denoise,
     Width, Height, IReadOnlyList<SceneLoraSpec> BaseLoras)`.
   - `static Krea2Refs Resolve(string capabilityQualificationsJson)` — fails fast naming the missing
     property. Precedent: `QwenImage21Refs` / `QwenImage21ModelSettings`.

3. `DreamGenClone.Domain/ModelManager/ResolvedImageModel.cs`
   - Add `Krea2Refs? Krea2 = null` (only for `SceneImageModelFamily.Krea2`; graph builder requires it).
   - Add `IReadOnlyList<ResolvedSceneLora>? SceneLoras = null` — the non-identity LoRA list (§4).

4. `DreamGenClone.Domain/ModelManager/ResolvedSceneLora.cs` **(new)**
   - `record ResolvedSceneLora(string FileName, double Strength, string? Purpose = null)` — no trigger
     token, no character binding, no SHA. Deliberately NOT `ResolvedCharacterLora`: scene/act LoRAs
     must not masquerade as identity.

4a. `DreamGenClone.Domain/ModelManager/SceneLora.cs` **(new)** — the catalog row:
    `Id`, `FileName`, `DisplayName`, `SceneImageModelFamily`, `Category` (unlock/act/anatomy/style),
    `DefaultStrength`, `IsEnabled`. **This is the storage spot for Krea2's model LoRAs** (and, per
    family, each family's). Character LoRAs live in the SEPARATE existing `CharacterLoraArtifact`
    store and are untouched by this item.

4b. `DreamGenClone.Infrastructure/ModelManager/SceneLoraRepository.cs` **(new)** + `ISceneLoraRepository`
    — `ListAsync(family)` returning the compatible catalog subset; read-only at render time.

4c. `DreamGenClone.Web/Application/RolePlay/Models/SceneImageStudioSettings.cs` — add
    `List<SceneImageLoraSelection> SceneLoras` (a small `{ FileName, Strength }` selection type), and a
    `SceneLoraPicker` multi-select component (filtered by the selected model's family) wired into
    `SceneImageStudio` and `CompositionComposer`.

### Web / Application

5. `DreamGenClone.Web/Application/ModelManager/ModelResolutionService.cs`
   - The **duplicated** `IsCompatible` allow-list (~line 619) gains the Krea2 pair.
   - When the resolved family is `Krea2`, resolve `Krea2Refs` and populate `ResolvedImageModel.Krea2`.
   - Populate `ResolvedImageModel.SceneLoras` from the request's selected scene-LoRA list (resolved
     from `SceneImageStudioSettings.SceneLoras`), not from the model row.

6. `DreamGenClone.Web/Application/ModelManager/Krea2ModelSettings.cs` **(new)**
   - The `Resolve`/`TryResolve` surface backing step 5; fail-fast with explicit diagnostics.

7. `DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs`
   - Resolve `SceneImageStudioSettings.SceneLoras` (the user's multi-select) into
     `ResolvedImageModel.SceneLoras`, beside the existing `ResolveCharacterLorasAsync` path.

### Infrastructure

8. `DreamGenClone.Infrastructure/ModelManager/RegisteredModelRepository.cs`
   - The **other** duplicated `IsCompatible` allow-list (~line 331) gains the Krea2 pair.

9. `DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs`
   - `BuildKrea2Workflow(unet, clip, vae, prompt, size, seed, krea2Refs)`:
     `UNETLoader(unet, default)` → `[base LoRA chain]` → `[scene/act LoRA chain]` →
     `[character LoRA chain]` → `KSampler(steps, cfg, euler, simple, denoise 1)`;
     `CLIPLoader(clip, type='krea2')` → `CLIPTextEncode` → positive;
     `ConditioningZeroOut(positive)` → negative (no negative text encode emitted);
     `VAELoader(qwen_image_vae)` → `VAEDecode` → `SaveImage`.
   - Wire `Krea2` into the family switch.
   - **Generalize `ApplyCharacterLoras`** to append the `SceneLoras` chain for Krea2 (reuse the same
     chain mechanics; Krea2 uses `LoraLoaderModelOnly` since it has no separate CLIP conditioning —
     verify against the verified Krea2 graph, which used `LoraLoaderModelOnly`).
   - The Krea2 family must be in the LoRA-wired set (today only Pony/SDXL are, and it fails fast
     otherwise).

10. `DreamGenClone.Infrastructure/Models/RunPodServerlessImageClient.cs`
    - Krea2 is local-only: gate the compatibility check so a Krea2 model on a serverless provider
      fails fast (`unsupported`), rather than emitting a wrong payload. (Defer actual RunPod support.)

### Prompt layer

11. `DreamGenClone.Domain/RolePlay/SceneImageCompilerSystemPrompts.cs`
    - `For(Krea2, canonical)` returns the brief-shape system prompt encoding the four rules (D6) and
      the photoreal default ("Photorealistic editorial photograph … 35mm, shallow depth of field").

12. `DreamGenClone.Web/Application/RolePlay/SceneImagePromptCompilers.cs` +
    `SceneImagePromptCompilerRegistry` + `Program.cs`
    - A `Krea2SceneImagePromptCompiler` (natural-language brief) registered in the registry and DI;
      register in `Program.cs` alongside the existing compilers.

13. `DreamGenClone.Web/Application/RolePlay/WardrobeItemPromptGenerationJobHandler.cs`
    - Family → slug map: `Krea2 => "krea2"`.

14. `DreamGenClone.Web/Application/RolePlay/BodyReferencePromptCompiler.cs` +
    `WardrobeItemPromptCompilers.cs`
    - Krea2 is a natural-language family but has **no reference conditioning**, so it joins the
      natural-language families for *text-only* body/wardrobe briefs (no reference-image strategy).
      Confirm the family lists that gate these compilers and add Krea2 only where the path is text.

### UI

15. `DreamGenClone.Web/Components/Pages/ModelManager.razor` (~451) and
    `DreamGenClone.Web/Components/Shared/ModelDetailsEditor.razor` (~117)
    - Family dropdown options gain "Krea 2"; dialect dropdown gains "Krea2NaturalLanguage".

16. `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` (~1639)
    - The QwenImage21-specific branch: add a Krea2 branch (no reference slots — the slot list is
      ABSENT for Krea2, matching B-130's capability-formed surface).

### DbQuery / data

17. `DreamGenClone.DbQuery/ModelManagerTransfer.cs` (~148)
    - Add `"Krea2"` to the family name array (position must match enum value 6).

18. Seed data (via dbq, backup `dev.db` first):
    - The scene-LoRA catalog rows for Krea2 (NSFW V4 + the five grounded act LoKRs + anatomy/realism
      helpers), each with its family and category.
    - One model row: family `Krea2`, dialect `Krea2NaturalLanguage`, identifier
      `krea2_turbo_fp8_scaled.safetensors`, protocol ComfyUI, under the existing local ComfyUI
      generation provider, `CapabilityQualificationsJson` carrying unet/clip/vae/steps=8/cfg=1/
      euler/simple/denoise=1/resolution=1024.
    - Additive only — never repoint the `RolePlaySceneImage` default.

## 4. Scene-LoRA architecture (D5)

Today `ResolvedImageModel.Loras` is identity-only (`ResolvedCharacterLora`), populated by the cast
resolver, chained via `ApplyCharacterLoras`, and **fail-fast for families that aren't wired** (only
Pony and SDXL). The new requirement is a **user-picked, multi-select scene-LoRA set, filtered to the
selected model**, so:

- **A scene-LoRA catalog** (persisted rows: `Id`, `FileName`, `DisplayName`, `SceneImageModelFamily`,
  `Category`, `DefaultStrength`, `IsEnabled`) is the single source of what LoRAs exist and which
  family/checkpoint each belongs to. Seeded via dbq with the proven set — for Krea2: NSFW V4, the
  five grounded act LoKRs, and the anatomy/realism helpers; other families (BigLust `dgc_lora...`,
  Flux CubeyAI, etc.) are added when those families are wired.
- **The picker filters by the selected model's family** (and, when a checkpoint is pinned, by its
  category), showing only compatible LoRAs. It is multi-select, one strength per entry, defaulting to
  the catalog's `DefaultStrength`.
- **The selection travels on `SceneImageStudioSettings.SceneLoras`** and is resolved to
  `ResolvedImageModel.SceneLoras` (a `List<ResolvedSceneLora>`), which the graph builder chains in the
  user's chosen order. Character identity LoRAs are appended LAST so identity stays closest to the
  subject (the 59-cell proof loaded V4 before the act LoKr, both via `LoraLoaderModelOnly`).
- **Nothing is force-applied.** The model row carries no base stack; a render with no selection and no
  character LoRAs emits no loader nodes and the graph is byte-identical to the family builder's output
  (the existing invariant).
- **Krea2 LoRA wiring is a prerequisite**: `ApplyCharacterLoras` only knows Pony/SDXL today and fails
  fast otherwise; the Krea2 builder must join the wired set (chain via `LoraLoaderModelOnly`).
- **Character LoRAs for Krea2: allowed later, not built now.** Krea2 has no character LoRA today, but
  the design must not preclude one. The extension points already exist and are deliberately left
  untouched: `CharacterLoraArtifact` rows are family-agnostic (filename + strength + trigger token),
  `CharacterLoraModelFamilies` gates which families are allowed (Krea2 is simply absent from it
  today), and the same `ApplyCharacterLoras` wiring added for scene LoRAs is exactly what a future
  Krea2 character LoRA would chain through. Adding it later = add Krea2 to
  `CharacterLoraModelFamilies` + train on the RAW model (needs a larger GPU than the 16 GB card).
  Out of scope for B-137, explicitly not foreclosed.

## 5. Tests

- `SceneImageModelFamilyTests`: add `(Krea2, Krea2NaturalLanguage)` true pair and the false
  cross-pairs (Krea2 × tag/NL/Flux dialects).
- `ComfyUIImageClientKrea2Tests` **(new)**: graph shape — `UNETLoader`/`CLIPLoader(type=krea2)`/
  `ConditioningZeroOut`/`KSampler` present; no negative text-encode node; `Krea2Refs` missing ⇒
  fail-fast naming the setting; base LoRA chain emitted when configured; scene LoRA chained in order;
  no-LoRA graph is unchanged.
- `Krea2ModelSettingsTests` **(new)**: `Resolve` round-trips the capabilities JSON; missing
  property fails fast.
- `SceneLoraRepositoryTests` **(new)**: `ListAsync(family)` returns only the compatible subset;
  seed round-trips.
- `SceneImageStudioSettingsTests` / picker contract: `SceneLoras` serializes and round-trips; a
  selected LoRA for a different family is rejected at resolve time (fail-fast, no silent drop).
- `SceneImagePromptCompilerRegistryTests`: the Krea2 compiler resolves for the new pair.
- `ImageSuiteImporterTests`: the count anchor is already 49 (it must move whenever the catalog
  grows — that is intended, not a regression).
- Targeted blast-radius run per the repo's testing rule (not the full suite): the classes above plus
  `ModelResolutionServiceTests`, `SceneImageRenderingJobHandlerTests`, `ImageSuiteRenderDriverTests`.

## 6. Verification / rollout

1. Build Web + Tests (0 errors).
2. Targeted tests green (run the classes in §5).
3. Seed the model row via dbq (backup `dev.db` first — destructive DB step).
4. Playground smoke: import the `baseline` catalog, pick the Krea2 model + a cell, render; compare
   against the 59-cell proof (same graph, same prompt text).
5. Confirm the negative-prompt field is disabled/declared for the Krea2 row, and that selecting a
   character LoRA with a Krea2 model no longer throws `unsupported_lora_family`.

## 7. Blast radius & risks

- **Shared LoRA chain** is the one shared-code touchpoint: generalizing it must preserve the Pony/SDXL
  behaviour exactly (their tests are the guard).
- **Prompt compiler** is where quality lives; the four rules are the regression surface.
- **Risk — no reference identity for Krea2.** Scenes needing face/body reference conditioning cannot
  use Krea2; the capability-formed surface must hide those slots (B-130's "absent, not disabled").
- **Risk — acts on base weights.** Fellatio-class acts need the act LoKr; the compiler should not
  claim otherwise.

## 8. Explicit non-goals (this item)

- Image style reference (D1), identity reference, edit path, ControlNet, RunPod/cloud Krea2, video.
  Character LoRAs for Krea2 are **deferred, not foreclosed** — the scene-LoRA catalog and the LoRA
  chain are designed so they can be added later without rework (§4).
