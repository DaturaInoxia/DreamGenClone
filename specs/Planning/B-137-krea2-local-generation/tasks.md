# B-137 — Tasks (Krea 2 local ComfyUI generation)

Dependency-ordered. Each task names the files it touches and its impact so blast radius is visible
before work starts. `[P]` = parallelisable with the task above it. Gates lock the phases: bindings
before resolution, resolution before the graph builder, graph before UI, UI before seed, seed before
smoke.

Legend: `[ ]` open · `[x]` done · `[~]` partially done.

**Status 2026-10-02: B137-001…061 implemented and green.** Targeted tests: 250/250. Two deliberate,
plan-consistent deviations are recorded in B137-002 below (no `Width`/`Height`, no `BaseLoras` on `Krea2Refs`),
both because §4 states the model row carries no base stack and the request size is the single source of truth.

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
`record Krea2Refs(UnetName, ClipName, VaeName, Steps, Cfg, SamplerName, Scheduler, Denoise)` with a
`static Resolve(capabilityQualificationsJson)` that fails fast naming the missing property. Mirror
`QwenImage21Refs` / `QwenImage21ModelSettings`. The qualification strategy is `TextToImage`.

**Deviation from the plan sketch below (deliberate, plan-consistent):** `Width`/`Height` and `BaseLoras`
were NOT added to the record. §4 states the model row carries no base stack and nothing is force-applied,
and the request's own size is the single source of truth for every family - carrying either would create a
second source of truth or a permanently-empty code path (which the repo's no-dead-path rule forbids).
The original sketch is kept for reference: `…Scheduler, Denoise, Width, Height, IReadOnlyList<SceneLoraSpec> BaseLoras`.
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
  new `SceneLoraPicker` component, `CompositionComposer` wiring- **Impact:** the only UI task with real surface. The picker must filter to the family (D5) and the
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
- **Files:** `DreamGenClone.DbQuery/Program.cs` (`b137-krea2-configure`), `DreamGenClone.Web/data/dreamgenclone.dev.db` (backup `.bak-b137-seed` first)
- **Impact:** persisted data only. Nothing is force-applied; the model row carries no base LoRA stack and the command never repoints a function default.
- **Portable form:** shipped as the idempotent named command `helpers/dbq.ps1 b137-krea2-configure` (the repo has no snapshot-refresh command, so a named command - not a copied `.db` - is how another host gets Krea 2). Re-running inserts 0 rows and leaves operator-edited rows alone.
- **Verified:** re-run on the already-seeded DB printed `catalog rows inserted 0/12` and updated the model row in place.
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

---

## Implementation status — 2026-10-02

| Phase | State | Evidence |
|---|---|---|
| P1 domain + refs (001–003) | `[x]` | Domain + Infrastructure build clean |
| P2 scene-LoRA storage & resolution (010–012) | `[x]` | `SceneLoraRepositoryTests` 6/6; `SceneImageResolutionTests` green |
| P3 Krea 2 graph + LoRA chain (020–022) | `[x]` | `ComfyUIImageClientKrea2Tests` 7/7; `SceneImageCharacterLoraGraphTests` + Qwen 2.1 client tests unchanged |
| P4 prompt compilers (030–032) | `[x]` | `SceneImagePromptCompilerRegistryTests`, `WardrobeItemPromptCompilerTests`, `BodyReferencePromptCompilerTests` green |
| P5 job handler wiring (040) | `[x]` | `SceneImageRenderingJobHandler*` + `SceneImageServiceJobTests` green |
| P6 UI (050–051) | `[x]` | Web Razor compiles; `ModelManager.razor` + `ModelDetailsEditor.razor` + `SceneLoraPicker.razor` + `SceneImageStudio.razor` |
| P6 follow-up: `CompositionComposer` (051, plan §3 item 4c) | `[x]` | **Closed 2026-10-02** — the picker is now on all four surfaces (see below) |
| P7 DbQuery + seed (060–061) | `[x]` | `ModelManagerTransfer` family array; 12 catalog rows + 1 model row seeded into `dev.db` (backup `.bak-b137-seed`) |
| P7 follow-up: **compiler profile** (missing in the first pass) | `[x]` | **Closed 2026-10-02** — see below |
| P8 tests (070) | `[x]` | **3727/3730 full suite** — only the 3 pre-existing `SdxlSceneImagePromptBuilderTests` failures remain |
| P8 smoke (071) | `[~]` | **Partial — see below.** Model row is LIVE and visible in the app; the actual image render was not run by the agent (content) |

### B137-071 smoke — what was verified, and what was not

**Verified (app restarted on the new build, Development + `data/dreamgenclone.dev.db` confirmed from the log):**

- The Playground's Model dropdown lists **"Krea 2 Turbo (Local ComfyUI) · Krea2 · Krea2NaturalLanguage"** — so the
  seeded row loads, family 6 and dialect 5 round-trip through the repository, and
  `ModelResolutionService.ValidateSceneImagePromptMetadata` accepts the pair.
- `SceneLoraRepository.ListAsync(Krea2)`'s **exact** query against `dev.db` returns the 12 seeded rows in Category /
  DisplayName order, which is what the picker renders.
- `Krea2ModelSettingsTests.Resolve_TheSeededRow_ProducesTheVerifiedGraphInputs` pins the **literal** seeded
  qualification JSON through the resolver AND the graph builder, so a seed edit that drops a field fails in tests
  rather than at the first render on another machine.
- `SceneImageStudioUiContractTests` asserts the Krea 2 branch hides the SDXL sampler envelope and that the scene-LoRA
  picker is family-driven and reaches `_settings.SceneLoras`.
- No negative-prompt control exists on the studio page for any family.

**NOT verified — the agent did not run an actual render.** Every cell in the `baseline` catalog is an explicit
sexual position/act, and the agent does not generate explicit sexual content, so the render is left to the operator.
One click each:

1. Playground → **Suites & verdicts** → Import `baseline/manifest.json`.
   **Re-import even if a `baseline-positions` suite already exists** — a suite is a snapshot of the catalog at import
   time, and this one was imported before the `krea2` variants existed, so its 45 cells carried no `krea2` variant and
   the Prompt variant dropdown offered nothing for Krea 2. After re-import it holds 49 cells, all 49 with `krea2` (no
   `no krea2` badges). See B-141 for the missing staleness indicator.
2. Select the `baseline-positions` suite, choose the **krea2** prompt variant.
3. Select the model **Krea 2 Turbo (Local ComfyUI)**, tick one cell, **Render selected**.
4. Expect: 8 steps / cfg 1 / euler / simple, and no negative prompt in the audited request
   (`SceneImageRequestSubmitted` debug event).
5. For the scene-LoRA chain, use the **Studio** (`/roleplay/studio/{sessionId}/{interactionId}`) or the
   **Composition Composer** (`/roleplay/studio/{sessionId}/{interactionId}/production/{productionGroupId}/composition`):
   both now show the Scene LoRA picker for the Krea 2 model and list the 12 catalog rows. The Playground's catalog
   run offers it too (B-140 T1.3), and the asset composer (`PromptAssetCreator`) as well. Selecting character LoRAs no
   longer throws `unsupported_lora_family` on this family.


-1. **The B-135 checkpoint compiler profile was MISSING in the first pass, and the Studio said so.** Seeding the
   model row and the LoRA catalog was not enough: every render resolves an `ImageCompilerProfile` keyed by
   `RegisteredModel.ModelIdentifier`, and a checkpoint with no profile is refused BY NAME ("No image compiler profile
   is configured for checkpoint 'krea2_turbo_fp8_scaled.safetensors'"). The B-135 seed had no Krea 2 row, so the
   Studio/Composition path could not compile a Krea 2 prompt at all.
   **Fixed** in `ImageCompilerProfileRepository.SeedProfiles()`: `profile-krea2-turbo`, family `Krea2` / dialect
   `Krea2NaturalLanguage`, `SystemPrompt` seeded from `SceneImageCompilerSystemPrompts.Krea2Beat` (the row carries its
   family's researched text, same as every other row), `MinChars 120 / MaxChars 800` (matching the ceiling the compiler
   text itself states), `MaxTokens 300` (the text encoder is Qwen3-VL 4B, NOT CLIP's 75-token window),
   `PoseInText Full` (the matrix renders explicit two-person acts from prose — the opposite of BigLust/Juggernaut/FLUX),
   `Negative` empty (the graph zeroes the positive; a negative has nowhere to go), and the three measured traps declared
   as forbidden categories (`framing-demand`, `body-noun-list`, `face-facing-clause`).
   `SettingsEnvelopeJson` is documentation only — the render reads the envelope from the model's `TextToImage`
   qualification (`Krea2Refs`), and nothing reads this field.
   **Pinned by** `ImageCompilerProfileTests.AKrea2ProfileIsNotGivenTheSharedNaturalLanguageText` plus the curated
   `RegisteredImageCheckpoints` coverage list.
   **Requires an app restart** — the seed runs inside `EnsureSchemaAsync`, so a host started before this change has no
   Krea 2 profile row until it is restarted (the insert is `ON CONFLICT DO NOTHING`, so an operator-edited row survives).
### Deliberate deviations

0. **The scene-LoRA picker is on FOUR surfaces, and the fourth was a real hole.** Plan §3 item 4c named
   `SceneImageStudio` and `CompositionComposer`; `CompositionComposer` was initially skipped, which meant the
   **production composition path** resolved `SceneLoras` but had no control to set one - a composition could not
   apply the unlock or an act LoKr at all. Closed 2026-10-02: `_sceneLoras` state, family-filtered picker, carried
   onto `renderSettings.SceneLoras`, and dropped on model change (a scene LoRA binds to a model FAMILY). Final
   surfaces: `SceneImageStudio` (studio), `CompositionComposer` (production), `Playground` (catalog runs),
   `PromptAssetCreator` (asset composer). Pinned by
   `SceneImageStudioUiContractTests.CompositionComposer_CarriesSceneLorasOnTheRender`.

1. **`Krea2Refs` carries no `Width`/`Height`/`BaseLoras`** — see B137-002.
2. **`SceneLoraPicker` renders nothing when the selected family has no catalog rows** (instead of an
   empty-state message). This is B-130's "absent, not disabled" rule and avoids a second hardcoded list of
   "which families support LoRAs" that could drift from the graph builder's wiring.
3. **The seed carries only evidence-backed LoRAs.** `style_reference` (D1 forbids), `filter_bypass3`
   (proven a no-op) and the never-rendered sliders are deliberately absent, so the menu never offers a
   strength nobody measured. On 2026-10-02 the two empty stubs were moved off the host - `filter_bypass3`
   (160 bytes) and `krea2_nsfw_prompt_adherence` (268 bytes, previously written off as merely "unrendered"
   but in fact a stub) - quarantined to `D:\ComfyUI\models\_dead_loras\` so neither can be wired up later.
   The 16 remaining `krea2*` files are all real weights.
4. **`ISceneLoraRepository.EnsureSchemaAsync()` is called at startup** (Program.cs, beside the other
   reference stores). A public `EnsureSchemaAsync` that nothing calls is the exact B-122 LoRA-policy defect
   where the first read died on a missing store.

### Remaining before `done`

- **B137-071 smoke** needs the app restarted on this build (the running instance holds the old assemblies).
- **Portability is now solved without a snapshot refresh**: `helpers/dbq.ps1 b137-krea2-configure` is an
  idempotent named command (the repo's sanctioned mechanism, since there is no snapshot-refresh command), so
  another host gets the Krea 2 model row and the catalog by running it after cloning. `snapshot.db` itself was
  deliberately NOT touched — `dev.db` holds encrypted provider keys and must never be copied over it.
- **Krea 2 is not the default** (`IsDefault = 0`), so nothing renders differently until an operator picks it.
