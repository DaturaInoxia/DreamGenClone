# B-147 — Edit reference parity (face, body, wardrobe, location)

**Operator request 2026-10-03:** *"hmm it may be time to do the large pass, making the edit behave just the
composition for 2.1, i mean face, body, wardrobe, location, lora's"* — followed by *"s 1,2,3 and 4- lora's can
be decide later. Do a plan and analysis before jumping in."*

So: **S1, S2, S3 are the implementation unit. S4 (LoRAs) is designed here but deferred.**

---

## 1. The reported failure, traced

`qwen edit location is not wired up - Reference strategy 'NativeMultiReference' for 'Location' is qualified but
has no implemented graph in this editor.`

From the dev DB (`DurableBackgroundJobs`):

| Field | Value |
|---|---|
| `JobType` | `media-edit-image-editing` |
| `Status` | `Failed` |
| `CreatedUtc` | `2026-10-04T00:14:23Z` |
| `ErrorMessage` | the message above |
| `payload.subjectKind` | `2` → `MediaEditSubjectKind.AssetImage` |
| `payload.imageId` | `49905b88abf543f193e6c5b892d1f806` — lives in **`SceneAssetImages`**, `Kind = Edited` |
| `payload.editorModelId` | `8b2e4d16-3a5f-4c7e-9d10-5f6a7c8b9d20` |
| `payload.referenceApplicationsJson` | one binding: `Location`, `strategy = NativeMultiReference`, `referenceLabel = "Indoor Front"`, `usesReference = true` |

The editor model row it resolved to:

| `qwen_image_2.1_editor` | |
|---|---|
| `ImageEditorGraphKind` | `QwenImage21Native` |
| `SupportedVisualStrategiesJson` | `["NativeMultiReference"]` |
| `CapabilityQualificationsJson` | one entry — `NativeMultiReference`, `Qualified: true`, `Resolution 1024`, `MaxReferences 10` |

And that graph **does** carry references — `ComfyUIImageEditingClient.BuildQwenImage21EditWorkflow`
(`ComfyUIImageEditingClient.cs:234`): the source occupies `images.image_1`, reference *i* occupies
`images.image_{i+2}`, all through one `TextEncodeQwenImage21`.

**So the model declares only `NativeMultiReference`, the UI can only offer what the model declares, and the
gate then rejects exactly that.** The gate can never pass for this model — a face, body or wardrobe reference
would fail identically. Location is merely what was bound.

---

## 2. Why this is the same defect a fourth time

The identical failure was already found and fixed on three other hosts. `ForEditElements` is the remainder
that was never picked up — and debug 081 even records it as such:

> Moving the pack from CHANNEL to SOURCE … is the remaining step … so it is scoped rather than folded into this
> migration. — `specs/Planning/B-130-unified-image-step-composer/tasks.md` (B130-012)

| Host | Blueprint | Fixed |
|---|---|---|
| Composition | `ForPackIdentityComposition` | debug 081 — Face slot added, `IdentityPackAsset` added to `characterSources`/`buildSources` |
| Asset creator | `ForAssetCreate` | commit `75ac3d4` (2026-10-01) |
| LoRA cell | `ForLoraCell` | commit `6e57549` (2026-09-26) |
| **Edit** | **`ForEditElements`** | **never** |

`ForEditElements` (`ImageStepBlueprintFactory.cs:333`) declares, per actor, **`Body`** and **`Wardrobe`**, both
sourced only from `[ApprovedSceneAsset, ScratchImage]`, plus a multi-valued **`Location`** from the same set.
There is **no `Face` slot at all**, and **`IdentityPackAsset` is not an allowed source anywhere on it**.

That is character-for-character the shape debug 081 describes for Composition before its fix:

> …declared, per character, only a `Body` slot and a `Wardrobe` slot, both sourced from
> `[ApprovedSceneAsset, ScratchImage]`, and **no `Face` slot at all**.

So "make the edit behave like the composition" is not a new architecture. It is applying an existing,
documented fix to the last host that did not get it — and then going one step further, because unlike
Composition the edit path also has broken *engine* halves beneath the blueprint.

---

## 3. Root causes

Four, in dependency order. F1 and F2 are the reported blocker; F3 and F4 are the rest of "same as composition".

### F1 — the strategy gate is hardcoded and wrong

`SceneAssetMediaEditSubjectWriter` (`:134` compiled edit, `:202` preset) and `SceneAssetEditingJobHandler`
(`:145`) all demand one hardcoded name:

```csharp
var references = await _references.ResolveAsync(
    context.ExplicitEditorModelId, applications, qualifiedStrategy: "ReferenceConditioning", cancellationToken);
```

`ReferenceStrategyCatalogue` already documents this exact failure mode — *"the edit surface hardcoded
`[\"TextOnly\",\"NativeMultiReference\"]` as if that were the whole world"* — and `MediaEditReferenceResolver:69`
turns the mismatch into the reported error.

**What the edit surface actually implements:** every `ImageEditorGraphKind` in `ComfyUIImageEditingClient`
(`SplitUnet`, `MergedCheckpoint`, `QwenImage21Native`) accepts `referenceImageNames`. There is **no**
IP-Adapter/PuLID identity-mechanism path on the editor client at all — the identity mechanism is a
*scene-render* concept (`IIdentityConditionedImageClient`). So for an edit the implemented strategy **is**
`NativeMultiReference`, and `ReferenceConditioning` is not implemented by any editor graph. The hardcoded name
is simply wrong, and `ResolveAsync`'s singular `qualifiedStrategy` parameter is what allowed one name to stand
in for a capability set.

### F2 — only ONE of the four channels has a resolver, and the others drop silently

`MediaEditReferenceResolver:39-43` selects `ReferenceBindingShape.IsAssetBacked`
(`UsesReference && Strategy != "TextOnly"`), and `UsesReference` is
`SceneAssetId` + `SceneAssetImageId` non-empty. So:

| Channel | Binding marker | Resolver today | On an edit today |
|---|---|---|---|
| Approved scene asset | `SceneAssetId` + `SceneAssetImageId` | `MediaEditReferenceResolver` | works — once F1 lets it through |
| Identity pack face/body | `Source = IdentityPackAsset`, `IdentityPackId` + `ReferenceAssetId` | render path only (`IdentityFaceReferenceResolver` / `IdentityBodyReferenceResolver`) | **filtered out — no error** |
| Pose skeleton | `Source = PoseLibrarySkeleton` | **not a reference channel at all** — see the correction below | **not a drop (deliberate)** |
| Scratch image | `Source = ScratchImage` | **nothing found** | **filtered out — no error** |

> **CORRECTION (found during implementation, 2026-10-04).** The pose row above was WRONG in the first draft of
> this plan. A pose binding is not a reference-image channel and is not dropped: `ImageStepPoseBinding` is its
> deliberate owner — the composition host consolidates a bound pose into `renderSettings.PoseReference` and the
> render reads the skeleton from there, so resolving the binding as a reference image as well would send the SAME
> skeleton TWICE. Its own doc records that silently dropping a pose "is exactly the defect this class was written
> to remove". S2 therefore leaves pose alone and says so in the resolver's classification, rather than "fixing" a
> channel that is already correct. The scratch row was confirmed (no consumer claims it anywhere).

Two more inline copies of the same one-channel filter exist (`SceneAssetEditingJobHandler:129`,
`SceneImageEditingJobHandler:330`) — the duplication half of the same defect.

**This is the single most important finding: face and body from an identity pack do not work on *any* edit
path, and fail silently — no error, no image, no trace.** That is the silent drop the RP reference rules
forbid, and it is why "face, body" in the request is not merely a UI gap.

There *is* an existing, working precedent to copy rather than invent: `PrepareIdentityAsync`
(`SceneAssetMediaEditSubjectWriter.cs:219-280`) already builds ordered `MediaEditReference` records out of
identity-pack bindings through `ICharacterImageAssetStorageService` — which the asset writer already has
injected. That route is reachable only from the dedicated *identity edit* command, not from a step's bindings.

### F3 — the edit prompt carries none of the reference machinery

The scene render path appends, at send time, from the images actually sent:

* the **role clause** — `<image1> (Front) is the FACE reference…` (`ReferenceRoleClauses.AppendToImagePrompt`), and
* the binding-derived **removals** merged behind the operator's own (`ReferenceBindingPromptRemoval.Merge`),
  stated to the model as a `USER REMOVALS` notice.

The edit path has **neither**. `BuildReferenceAwareInstruction` produces only prose:
*"The first input image is the existing scene and is the base image. Additional reference images are guidance
only…"*. With one reference that is adequate — Qwen's own rule is *no tags at N = 1* — but **with two or more,
the references are unnamed**, which is the CASE-25 defect (`<imageN>` "mandatory and non-negotiable" for
N ≥ 2) reproduced on the edit path.

**The numbering offset is the subtle part.** On an edit the **source IS `image_1`**, so references are
`image_2 … image_N`. `ReferenceRoleClauses` numbers from `image1`, so it needs an explicit leading-image count
rather than a second copy of the numbering rule. Note also that the two numberings are *different things*:

* `MediaEditReference.Ordinal` is 1-based over the **reference list** — and it *does* decide send order, because
  `EditWithReferencesAsync` uploads `references.OrderBy(reference => reference.Ordinal)` and the workflow then
  wires the uploaded names by list position (`image_{index+2}`).
* the **prompt tag index** is that same send position **plus the leading source image**: `tagIndex = ordinal + 1`.

A careless change collapses those two and silently re-points every tag.

**Removals do not apply to edits, and that is correct rather than a gap.** There is no `CompiledMediaBrief`
anywhere under `Web/Application/RolePlay/Editing/**` (verified by search): an edit's instruction is an
operator-authored intent compiled by `ISceneImageEditPromptCompiler`, not a brief-driven image prompt. There is
no element vocabulary to remove *from*. S3 therefore carries the **role clause only**, and the plan says so
explicitly so nobody later "finishes" the missing half and removes prose the operator wrote on purpose.

### F4 — a latent bug on the same feature: the scene-image edit passes a null model id

`SceneImageMediaEditSubjectWriter.cs:323` calls the resolver with `registeredModelId: null`, so a
reference-carrying **scene-image** edit always dies earlier at
*"Reference editing requires the exact registered editor model id."* The media-edit payload already carries
`editorModelId` and the writer already receives it — the render path passes the real id and works. This is not
the operator's reported failure but it is the same feature and would be hit next.

### F5 (deferred, S4) — LoRAs are selected but never applied

`ResolvedImageEditorModel.SceneLoras` exists (`ResolvedImageEditorModel.cs:48`) but:

* **nothing sets it** — `ImageEditorModelResolver.ResolveByIdAsync` (`:146-169`) never passes `SceneLoras`; and
* **nothing reads it** — `ComfyUIImageEditingClient` only ever chains the single `model.LoraName` editor LoRA
  (`:157`, `:389`, `:540`); and
* **nothing merges the run's selection** — `MediaEditImageEditingJobHandler` never touches
  `SceneLorasJson`, though `MediaEditRunRequest.SceneLorasJson` round-trips it (B-143).

So the scene-LoRA multi-select round-trips and is then discarded. Character LoRAs on edit do not exist at all.

---

## 4. Design

Not "teach edit to copy composition" — lift what the render path already has so all surfaces share it, with one
owner per rule.

### D1 — One channel classification (extend `ReferenceBindingShape`)

`ReferenceBindingShape` already owns *which store supplied a binding* and *the order it reaches the model*
(`IsIdentityPackBinding`, `IsAssetBacked`, `InSendOrder`, `SlotKindOf`). Add the missing classification so all
four channels are named in one place:

```
ReferenceChannel { ApprovedAsset, IdentityPack, PoseSkeleton, ScratchImage }
ReferenceBindingShape.ChannelOf(binding)
```

Every consumer stops writing its own predicate. A binding whose channel has no implemented route **refuses by
name**; nothing is filtered out silently. `ScratchImage` in particular is classified and, if it turns out to
have no route on a surface, refused with the reason rather than dropped — its exact route is an open question
(§9).

### D2 — One resolver, one neutral result, three adapters

`MediaEditReferenceResolver` is already the shared resolver (render path, both edit paths, asset generation).
Give it the whole job and a neutral output:

```
record ResolvedReference(int Ordinal, ImageStepSlotKind? Slot, string Label, string FileName, string Sha256,
                         Func<CancellationToken, Task<Stream>> OpenAsync)
```

It resolves **all** channels — approved assets through `ISceneAssetRepository`, identity-pack images through
`IdentityFaceReferenceResolver` / `IdentityBodyReferenceResolver` (both already registered as singletons,
`Program.cs:236-237`), skeletons through the pose skeleton provider — and returns them in send order.

Each surface adapts at its own edge, which keeps laziness and keeps each surface's own shape:

| Surface | Adapter | Why |
|---|---|---|
| Scene render | reads bytes → `ReferenceConditionedImageInput` | the graph takes bytes; it already opens each stream this way |
| Media edit | wraps → `MediaEditReference` | the handler opens references **after** building the mask, so the resolver must stay lazy |
| Asset generate | reads bytes → `ReferenceConditionedImageInput` | same as render |

This replaces the four duplicated one-channel filters (`MediaEditReferenceResolver`, `SceneAssetEditingJobHandler:129`,
`SceneImageEditingJobHandler:330`, and the render path's own split) with one.

### D3 — The capability gate is a set owned by the catalogue

Replace `string qualifiedStrategy` with the surface's implemented set, owned by
`ReferenceStrategyCatalogue` (which already owns *meaning*, and already carries the doc comment warning about
this exact hardcoding):

```
Catalogue.ImplementedReferenceStrategiesForImageSurface(surface)   // Generate | Edit
```

The resolver then checks **membership** instead of equality, and a non-member is refused by name with the
accurate reason. `ReferenceConditioning` on an edit is refused because no editor graph implements it — not
because a caller happened to name a different string.

### D4 — The numbering rule stays single-owner

`ReferenceRoleClauses` gains a leading-image count (0 for generate, 1 for edit). `tagIndex = ordinal +
leadingImages`. `MediaEditReference.Ordinal` keeps meaning "send order within the reference list" and is
**not** reused as a tag index.

### D5 — The edit blueprint gains the pack on the BUILD, and keeps its deliberate face exclusion

`ForEditElements` mirrors `ForPackIdentityComposition` for the build only: per actor a **Body** slot accepting
`[ApprovedSceneAsset, IdentityPackAsset]`, then **Wardrobe** (multi, shared sources). Location stays as it is
(multi, shared).

**The Face slot is deliberately NOT added, and this reverses the first draft of this plan** — the
`ForEditElements` doc comment records why, and the implementation confirmed it: the edit workspace binds each
detected person to an approved identity pack through its own **Identity tab** and runs a face-only correction
there ("identity compiles from the source image and runs independently of the Edit tab"). A face slot would give
each person two mechanisms in one render — the duplicate path the reference rules forbid. **Face conditioning on
edits already exists; the change that looked like parity is the one that must not be made.** Pose is excluded for
the same reason as before (the source image fixes it, and `ImageStepPoseBinding` owns the render path's pose).

---

## 5. Staged implementation

Each stage is independently shippable and independently verifiable. Build + full suite green at the end of
each.

### S1 — capability gate (fixes the reported blocker on its own)

* `ReferenceStrategyCatalogue`: add `ImplementedReferenceStrategiesForImageSurface` (Generate / Edit).
* `MediaEditReferenceResolver.ResolveAsync`: `string qualifiedStrategy` → the surface's implemented set;
  membership check; refusal message names the surface and the strategies it implements.
* Call sites updated: `SceneAssetMediaEditSubjectWriter` ×2, `SceneImageEditingJobHandler:324`,
  `SceneAssetGenerationJobHandler:618`, `SceneImageRenderingJobHandler`.
* **Acceptance:** one approved Location bound to an asset edit reaches `EditWithReferencesAsync` instead of
  failing. A `ControlNet` / `WardrobeTryOn` binding on an edit is still refused, by name.

### S2 — one resolver, four channels (the substantive half)

* `ReferenceBindingShape.ChannelOf` (+ channel enum) — the one classification owner.
* `MediaEditReferenceResolver` resolves the identity-pack channel as well as the approved-asset one, and refuses
  the scratch channel by name; pose is classified and explicitly left to `ImageStepPoseBinding` (see the
  correction in §3).
* `SceneAssetGenerationJobHandler` and `SceneImageEditingJobHandler` stop carrying their own filters.
* `SceneAssetMediaEditSubjectWriter` gains `IdentityFaceReferenceResolver` / `IdentityBodyReferenceResolver`
  (DI already registers both), and the bespoke `PrepareIdentityAsync` loop is left as its own route rather than
  duplicated.
* `ForEditElements`: the build accepts the character's identity pack (D5), face unchanged.
* **Acceptance:** on an edit, a pack build reaches the model as a numbered reference; a pack binding on a kind a
  pack cannot supply is refused by name; nothing is silently dropped.

### S3 — prompt treatment (role clause, with the edit offset)

* `ReferenceRoleClauses` gains the leading-image count; an `AppendToImagePrompt` overload taking it.
* Edit instruction assembly appends the clause at send time (both the media-edit handler and the legacy
  `SceneImageEditingJobHandler`), from the references actually sent.
* `BuildReferenceAwareInstruction` stays as the base text and no longer needs to carry the whole burden.
* **Acceptance:** an edit with two references produces `<image2>` and `<image3>` (source is image 1), each
  naming its image; an edit with one reference produces no tags.

### S4 — LoRAs (deferred, not in this pass)

Scope when picked up: merge the run's `SceneLorasJson` onto the resolved editor model in
`MediaEditImageEditingJobHandler`; `ImageEditorModelResolver` passes `SceneLoras`; the editing client chains
scene LoRAs the way `ComfyUIImageClient` does before the sampler repoint. Character LoRAs on edit are a
separate decision (they need the character→LoRA resolution the render path has).

---

## 6. Test plan

| Stage | Tests |
|---|---|
| S1 | Resolver accepts the surface's own strategies and refuses others **by name**; one regression test reproducing the operator's exact job shape (approved Location + `QwenImage21Native` editor) asserting the run reaches the client. |
| S2 | Per channel: pack face, pack body, pose skeleton, approved asset — each reaches the reference list with the right ordinal; a pack binding on Wardrobe/Location refuses by name; `ChannelOf` classification table; the composer's `ForEditElements` offers Face + pack sources and declares Face before Body (mirroring the existing composition assertions). |
| S3 | Two references → `<image2>`/`<image3>` and correct labels; one reference → **no** tags; the numbering rule asserted against the same send-order source the render path uses (the existing send-order test is the model). |
| All | Full suite green. Existing edit tests (`MediaEditImageEditingJobHandlerTests`, `SceneAssetImageEditRunCutoverTests`, `CharacterPoseSlotTests`, `StepPromptElementPlanTests`, `ReferenceBindingPromptRemovalTests`) must stay green — several assert the current refusal text, so each changed message needs its test updated deliberately, not deleted. |

---

## 7. Blast radius

| Area | Effect |
|---|---|
| Edit surfaces | Accept more; refuse exactly one thing *less* (the wrong gate) and refuse the same things by name |
| Scene render | Behaviour unchanged in outcome; the builder is refactored onto the shared resolver. The render path's measured send order (pack, then assets, then pose) must be preserved exactly — it is pinned by `SceneImageRenderingJobHandlerNativeReferenceTests` and `ReferenceRoleClauseTests` |
| Asset generation | Same channels as render; the count guard stays |
| Blueprint | `ForEditElements` gains a Face slot and pack sources — new controls appear on the edit surface |
| Prompts | Edit instruction text changes **only when 2+ references are bound** |
| DB / pods / schema | **None.** No migration, no pod change, no data mutation |
| Not touched | `IdentityPackSlotPrefill` (still unused by these hosts), the identity CHANNEL, `PoseControlNet` editing, S4/LoRAs |

**Risks**

1. **Send-order drift** — the single most damaging outcome: a re-ordered reference list silently re-points
   every prompt tag. Mitigation: `ReferenceBindingShape.InSendOrder` stays the one owner; the existing
   send-order tests are extended to the edit path rather than replaced.
2. **Off-by-one on the edit tag offset** — `image2` vs `image1` for the source. Mitigation: one parameter,
   asserted directly in S3.
3. **Accepting a strategy a graph cannot carry** — weakening the gate must not turn a refusal into a wrong
   render. Mitigation: the gate stays (membership, not removal), and the client's own validation
   (`ValidateReferences`, unique ordinals, checksum) still applies.
4. **Refusal-message tests** — several tests assert current message text; each is updated deliberately.

---

## 8. Verification protocol

1. `dotnet build DreamGenClone.Web` — 0 errors.
2. Targeted: the edit/asset/render reference test classes named in §6.
3. **Full suite green** (`dotnet test DreamGenClone.Tests`), per the repo rule — no failing or skipped tests.
4. Live: the operator's exact job shape (an approved Location on an asset edit) completes and the reference
   appears in the render provenance; then a two-reference edit shows `<image2>`/`<image3>` in the stored
   prompt.
5. Report the decision-path evidence per the RP-engine rules: for each changed behaviour, the single
   configuration source, the one active decision path, and no fallback branch.

---

## 9. Open questions

1. **`ScratchImage`** — the blueprints offer it as a source kind and no resolver claims it. S2 now REFUSES it by
   name on an edit rather than dropping it silently. If a route is wanted (an unapproved render from this chain),
   it is its own small piece of work.
2. **Face on edits — ANSWERED during implementation:** it is already implemented, through the workspace's own
   Identity tab, and a Face slot here would duplicate the mechanism. Nothing to do; recorded in D5.
3. **Pose on edits — ANSWERED:** not a channel at all; `ImageStepPoseBinding` consolidates it for renders and the
   source image fixes it for edits. Recorded in D5 and in the §3 correction.

## 10. Implementation record (2026-10-04)

Shipped as S1 + S2 + S3 in one pass; S4 (LoRAs) deferred by the operator's instruction ("4- lora's can be decide
later").

| File | Change |
|---|---|
| `ReferenceStrategyCatalogue.cs` | `ReferenceImageSurface` + `ImplementedReferenceStrategiesFor` + `Label`; the doc records why `ReferenceConditioning` is not an EDIT strategy. |
| `ReferenceBindingShape.cs` | `ReferenceChannel` enum + `ChannelOf`. |
| `Editing/MediaEditReferenceResolver.cs` | Surface-based gate (membership, not equality); four-channel classification; identity-pack resolution through the real face/body resolvers; scratch refused by name; pose explicitly left to `ImageStepPoseBinding`; `ResolveApprovedAssetAsync` / `ResolveIdentityPackAsync` split out. |
| `Editing/IMediaEditSubjectWriter.cs` | `MediaEditReference` gains `SlotKind` (appended, optional, so positional construction still compiles). |
| `Editing/SceneAssetMediaEditSubjectWriter.cs` | Both call sites → `ReferenceImageSurface.Edit`. |
| `Editing/SceneImageMediaEditSubjectWriter.cs` | Runnable id fix (F4): the real editor model id is passed, and the two inline `UsesReference` filters use the shared predicate. |
| `Editing/MediaEditImageEditingJobHandler.cs` | Appends the role clause to the instruction with the edit offset, from the references actually sent. |
| `ImageStep/ImageStepBlueprintFactory.cs` | `ForEditElements`: the build accepts `IdentityPackAsset`; face stays absent, with the reason recorded. |
| `ImageStep/ReferenceRoleClauses.cs` | `LeadingImageCount` (Generate 0 / Edit 1), plumbed through `BlockFor` / `AppendToImagePrompt`, plus `AppendToEditInstruction`; negative leading count refused. |
| `SceneAssetEditingJobHandler.cs`, `SceneImageEditingJobHandler.cs` | Same gate + shared predicate, replacing their own copies. |
| `SceneAssetGenerationJobHandler.cs`, `SceneImageRenderingJobHandler.cs` | Pass `ReferenceImageSurface.Generate`. |

**Tests:** 4 resolver tests in `MediaEditImageEditingJobHandlerTests` (the reported failure; the refusal naming
what the surface does implement; the pack-build channel; wardrobe-by-name; scratch-by-name) + 5 numbering tests in
`ReferenceRoleClauseTests` (the edit offset, the N = 1 case, the two numberings differing by exactly the leading
image, the negative count, and that the handler applies the clause) + 1 blueprint test.

**Verification:** `dotnet build DreamGenClone.Web` 0 errors; **full suite 3860 passed / 0 failed**.
