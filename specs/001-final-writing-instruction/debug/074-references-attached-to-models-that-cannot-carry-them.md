# References attached to models that cannot carry them: Krea 2 Composition renders and every camera-angle pass failed

## Report

Reported 2026-10-13 from the Composition Composer and the Scene Image Editor:

> edit camera angle is broke, recent changes are adding reference images to models that do not support it —
> `Reference strategy 'NativeMultiReference' for 'Location' is unavailable: Model 'Qwen Image Edit Rapid-AIO NSFW v23 + Multi-Angle Camera LoRA (Local ComfyUI)' does not declare support for 'NativeMultiReference' in Model Manager.`
> this is also the case with krea2 on the composition renders, it added location refernece but not supported, you should not be added to models that do not support thinkgs.

Both symptoms are one defect: a `Location` reference binding is created or carried **without ever asking the target
model whether it can carry it**.

Failing jobs from the dev DB (`DurableBackgroundJobs`, `ErrorCode = durable_handler_unclassified_failure`):

| Job | Created (UTC) | Model | Message |
|---|---|---|---|
| `media-edit-image-editing:a8114ced…` | 2026-10-14T00:58:28 | Rapid-AIO + Multi-Angle Camera LoRA | the text quoted above |
| `media-edit-image-editing:16b3828f…` | 2026-10-14T00:54:20 | Rapid-AIO + Multi-Angle Camera LoRA | the text quoted above |
| `scene-image-rendering:f07475df…` | 2026-10-13T14:50:34 | Krea 2 Turbo | same shape, Krea-named |
| `scene-image-rendering:b6c78a6e…` | 2026-10-13T14:50:07 | Krea 2 Turbo | same shape |
| `scene-image-rendering:42f5ca9a…` | 2026-10-13T14:49:03 | Krea 2 Turbo | same shape |

The two camera passes that DID work (`media-edit-image-editing:d195f089…`, `47395739…`, both 2026-10-13T19:52) ran on
an image whose record carried **no** bindings at all.

## Analysis

**One throw site for both symptoms.** `MediaEditReferenceResolver.ResolveAsync`
(`Application/RolePlay/Editing/MediaEditReferenceResolver.cs:87`) refuses a binding whose strategy the model does not
declare. It is reached from `SceneImageRenderingJobHandler.cs:639` (render) and
`SceneImageMediaEditSubjectWriter.BuildAssetReferencesAsync` (edit) — i.e. only once the row and the job exist.

What the model rows actually declare (`RegisteredModels.SupportedVisualStrategiesJson`):

| Model | Declared | Can carry a reference image? |
|---|---|---|
| Qwen-Image-2.1 (Local ComfyUI) | `["NativeMultiReference"]`, qualified | yes — 8 location-carrying compositions rendered fine on 2026-10-13 |
| Krea 2 Turbo (Local ComfyUI) | `["TextOnly","Lora"]` | no |
| Qwen Image Edit Rapid-AIO … + Multi-Angle Camera LoRA | `[]` | no |

**Cause 1 — the Composition seed could not see the model (Krea 2).**
`LocationBackdropSlotPrefill.For` hardcodes `Strategy = NativeMultiReference`, and the only guard was
`HasLocationSlot(blueprint)` — a question about the *blueprint*, and
`ImageStepBlueprintFactory.ForPackIdentityComposition` declares the `Location` slot for **every** model. So the guard was
always true, `CompositionComposer.razor`'s notice (`"This model cannot carry the location reference; the bound backdrop
was not applied."`) was unreachable, and the binding was written straight into `_referenceBindings`, bypassing both the
planner and the composer's model-aware slot menu (`ReferenceApplyPanel`'s `Intersect(…, ExecutableStrategies)`).

**Cause 2 — the camera pass inherited the source's bindings (the camera bug).**
`SceneImageService.EnqueueMultiAngleEditAsync` copied `AppliedReferenceBindingsJson = source.AppliedReferenceBindingsJson`
onto the derived row (the Preset enqueue did the same), and
`SceneImageMediaEditSubjectWriter.PrepareMultiAngleAsync` → `BuildAssetReferencesAsync` then re-resolved every inherited
binding against the **editor** model. The camera-LoRA editor declares `[]`, so the pass died before it could render. The
same inheritance also rewrote the camera instruction through `BuildReferenceAwareInstruction`, although that stage
hash-verifies its own recorded instruction.

**Contributing factor.** The guard meant to catch a stale binding —
`SceneImagePromptGenerationJobHandler.EnsureReferencesAreCarryable` — compares the strategy against the **surface's**
static implemented set, not the resolved model's declaration. `NativeMultiReference` is in the generation surface's set,
so Krea 2 passed that guard and only died later, in the render.

**One decision path.** `IReferenceStrategyResolver.ResolveAsync(modelId, strategy)` is the single owner of "can THIS
model carry THIS strategy" (model declaration + qualification). Every fix below asks it rather than re-deriving it.

## Plan

1. Make the location-backdrop seed ask the model: seed only when the render model resolves `NativeMultiReference`;
   otherwise seed nothing and show the existing notice carrying the resolver's own reason.
2. Stop carrying the source's bindings into the two source-IMAGE transform stages (MultiAngle, Preset).
3. Prove a pass's reference images against the model that will run it at every EDIT enqueue that carries bindings
   (Finish, Identity, Edit), refusing there by name instead of deep in the job.
4. Tests, build, full suite, debug record.

## Resolution

- `ImageStep/LocationBackdropSlotPrefill.cs` — new `ResolveAsync(strategies, registeredModelId, blueprint, backdrop)`
  returning `LocationBackdropSeed(Outcome, Binding, Reason)`, the ONE seeding decision: `NoLocationSlot`,
  `NoModelSelected`, `ModelCannotCarry` (with the resolver's reason), or `Seeded`. `For`/`HasLocationSlot` unchanged.
- `Components/Pages/CompositionComposer.razor` — the seed calls `ResolveAsync` with `CompositionRenderModelId` (the
  render's own expression: the identity model when identity-on-create, else the composition model) and never builds a
  binding itself; a forced re-seed withdraws the binding IT seeded (`_locationSeededImageId`) and re-proves it, and the
  re-seed now also runs when the identity model changes or identity-on-create is toggled (both move the render model).
- `Application/RolePlay/SceneImageService.cs` — the Preset and MultiAngle records no longer copy
  `source.AppliedReferenceBindingsJson`; new `RequireReferenceImagesAreCarryableAsync` proves every binding that
  travels as a reference image (`MediaEditReferenceResolver.CarriesReferenceImage` — the resolver's OWN predicate,
  exposed for this) against the model that will run the pass, and is called at the Finish, Identity and Edit enqueues
  before their rows are inserted. A new optional `IReferenceStrategyResolver` dependency supplies the decision.
- `Editing/MediaEditReferenceResolver.cs` — new `public static bool CarriesReferenceImage(surface, binding)` exposing
  the classification `ResolveAsync` selects by, so the enqueue proof cannot drift from it.
- Tests — `LocationBackdropSlotPrefillTests` (four `ResolveAsync` cases: capable, incapable, no slot, no model);
  `SceneImageServiceJobTests` (`EnqueueEditAsync_WithAReferenceTheEditorModelCannotCarry_RefusesBeforeAnyRowOrJobExists`,
  plus a `referenceStrategies` seam in its `Build` helper); `ReferenceCarryabilityGuardTests` (the inheritance is gone,
  the enqueue proof uses the resolver's predicate, the composition page asks the seed owner);
  `FinalImagePromptPlanGuardTests` and `SceneImageStudioUiContractTests` updated for the new seed call shape and the two
  additional forced re-seed sites (assertion text changed, subject preserved).

## Validated

- [x] `dotnet build DreamGenClone.Web/DreamGenClone.csproj` — **0 errors**; no warning from any changed file
      (`SceneImageService.cs`, `LocationBackdropSlotPrefill.cs`, `MediaEditReferenceResolver.cs` clean; the three
      `CompositionComposer.razor` warnings are the pre-existing CS8604/CS0169 ones, shifted by the insertion).
- [x] Focused set — `LocationBackdropSlotPrefillTests`, `ReferenceCarryabilityGuardTests`, `SceneImageServiceJobTests`,
      `FinalImagePromptPlanGuardTests`, `SceneImageStudioUiContractTests`, `CompositionPoseConsolidationContractTests`,
      `CompositionComposerNativeReferenceContractTests`, `ReferenceStrategyResolverTests`:
      **118 passed, 0 failed**.
- [x] Full suite — **4273 passed / 27 failed / 4300 total**. All 27 failures are the pre-existing unrelated
      `CharacterLora*` workstream (`CharacterLoraCoveragePlanGeneratorTests` 25, `CharacterLoraCellTemplateSeedTests` 1,
      `CharacterLoraCoverageSchemaTests` 1) already reported in record 072; none reference the reference pipeline.
- [ ] Pending operator acceptance: (a) render a Composition on Krea 2 with a bound location backdrop — the composer must
      show "This model cannot carry the location reference…" and the render must proceed text-only; (b) run a camera-angle
      edit on an image that was rendered with a location reference — it must render.

## Verified but NOT fixed in this change (reported, not silently left)

- **`SceneImagePromptGenerationJobHandler.EnsureReferencesAreCarryable` still checks the SURFACE set, not the resolved
  model** (`SceneImagePromptGenerationJobHandler.cs:463-503`). Its own doc comment claims it refuses "a binding that
  claims to supply one when the resolved model cannot carry it", which it does not do — `NativeMultiReference` is in the
  generation surface's implemented set, so a Krea 2 binding passes it. Reachability is limited because the composition
  surface clears bindings on a model change and the shared composer intersects each slot's strategies with the selected
  model's `QualifiedStrategies`; the seed was the one path that bypassed both, and that is what step 1 closes. Making
  that guard model-aware needs the resolver injected into that handler and is a separate, approved change.
- **The render enqueue carries no equivalent proof.** `SceneImageService.EnqueueRenderAsync` still writes
  `request.ReferenceApplications` without asking the model, so a render surface that does not intersect its strategies
  per model (the asset-create path is documented as `["TextOnly"]`-only) has no enqueue-time refusal. Left alone to keep
  this change inside the approved scope.
- **The face `region` the vision compiler reports remains imprecise** (record 073, unchanged by this work).
