# B-145 — Named Location References

**State:** `implemented` — shipped 2026-10-03 (see §8). Plan revised 2026-10-02.
**Scope:** small→medium. **Priority:** medium.
**Requested by:** operator, 2026-10-02.

> **Revision note.** The first draft proposed a per-container *view catalog* (`SceneAssetLocationViews`, seeded
> front / left / right / rear / interior). The operator rejected that shape:
>
> > "no i want to be able to do this in the app, i dont want this hard coded, the containiner of shed, and naming
> > accpeted images with user entered names will be how the image references are retreived, locations and it
> > individual images need to be dynacmic easy to create, the rp session can make up any location at random."
>
> The catalog is **dropped entirely**. There is no view enum, no seeded name list, and no new table. A location
> reference is simply *an accepted image with a name the operator typed*, inside a container the operator created.

---

## 1. What already works — do not rebuild it

Verified against the live dev DB and the code, 2026-10-02.

| Requirement from the operator | Status |
|---|---|
| "locations need to be dynamic, easy to create" | **Already in the app.** `/assets/create` creates a container of any `SceneAssetType` from a typed name and navigates a Location straight to `/locations/{id}` ([AssetCreate.razor:89](../../../DreamGenClone.Web/Components/Pages/AssetCreate.razor)); entry point is the "Create Asset" button in the Asset Manager ([AssetStudio.razor:20](../../../DreamGenClone.Web/Components/Pages/AssetStudio.razor)). Nothing enforces a fixed location list. |
| "the container of shed" holds all the shed's images | **Already.** Every image generated, uploaded or edited from a container page is written to that container ([PromptAssetCreator.razor](../../../DreamGenClone.Web/Components/Assets/PromptAssetCreator.razor), [AssetUploadCreator.razor](../../../DreamGenClone.Web/Components/Assets/AssetUploadCreator.razor), [AssetStudioView.razor](../../../DreamGenClone.Web/Components/Pages/AssetStudioView.razor)). The shed already exists: `SceneAssets` `6dd275caf4fc47bb9532c66ab2243727`, `Type=Location`, `IsContainerOnly=1`. |
| Accepted images become usable references | **Already.** Approval is the gate: [ReferencePicker.razor:69](../../../DreamGenClone.Web/Components/Shared/ReferencePicker.razor) requires `Complete` + `Approved` + `ProductionVersion` + `Sha256`; written by [SceneAssetRepository.cs:607](../../../DreamGenClone.Infrastructure/RolePlay/SceneAssetRepository.cs). |
| "i will use the first approved images as reference to create the other sides" | **Already.** Bind the accepted image to the Location slot in the container's own generate panel and describe the next view. |

So the entire location workflow the operator described works today, end to end, with no hardcoded location or
or view list.

## 2. The one real gap

**An individual image has no name.**

- `SceneAssetImages` has no name column ([schema](../../../DreamGenClone.Infrastructure/RolePlay/SceneAssetRepository.cs)
  — `CREATE TABLE IF NOT EXISTS SceneAssetImages`, line ~1447).
- `ReferencePicker` labels a choice from `CandidateNotes`, else `<Asset> — image N`
  ([ReferencePicker.razor:80-86](../../../DreamGenClone.Web/Components/Shared/ReferencePicker.razor)).
- There is no UI anywhere that names an image — the image card shows badges, id, kind and tags.

Consequence: five accepted shed images (front, left, right, rear, interior) become five identical
"Maintenance Shed — image 1..5" entries, and the operator cannot tell which side they are binding. The operator's
stated retrieval rule — *"naming accepted images with user entered names will be how the image references are
retrieved"* — has nothing to retrieve from.

## 3. Design decisions

### D1 — A location reference is a named accepted image. Nothing else.

```
Location container (created in the app, any name)
  └── accepted image  →  user-entered name  →  retrieval label
```

No view enum. No per-container catalog table. No seeded name list. No per-container "required view set". The
container's own name plus the image's own name is the whole addressing scheme.

### D2 — One new column: `SceneAssetImages.DisplayName`

`TEXT NULL`, added **last** in the existing idempotent migration list
([SceneAssetRepository.cs:1424](../../../DreamGenClone.Infrastructure/RolePlay/SceneAssetRepository.cs)) and appended
to the end of `ImageSelectSql` + `ReadImage`. The reader is ordinal and documents that constraint in the code
(*"a new column may only be added at the end of the SELECT list"*) — appending is mandatory, not stylistic.

### D3 — The name is required when a **location** image is accepted

*"naming accepted images with user entered names"* — the name is part of accepting, not an optional afterthought.
Validation lives at the data boundary in [SceneAssetRepository.ApproveImageForProductionAsync](../../../DreamGenClone.Infrastructure/RolePlay/SceneAssetRepository.cs)
(which already fails fast for missing provenance, consent, licence and checksum), so it cannot be bypassed by a
second call site: if the image's container is `SceneAssetType.Location` and `DisplayName` is blank, approval throws
with an explicit message naming the image. Nothing substitutes a default name.

Images in non-location containers are unaffected — identity, wardrobe and pose approvals keep their current contract.

*(Open decision 3 below: this is the one design choice worth confirming, because it is the only place the change can
refuse an action the operator can perform today.)*

### D4 — The label is snapshotted onto the binding, never re-resolved at render time

`ReferenceApplicationSelection.ReferenceLabel` already exists for exactly this and is already carried through
`ImageStepSlotSource.ReferenceLabel`
([ReferenceSlotPlanner.cs:11-22](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceSlotPlanner.cs)),
with the rationale written on it: *"the surfaces that show a binding do not all hold the identity service, and a pack
asset id means nothing to the operator reading it."*

`ImageStepComposer.SetAssetAsync` (line ~880) currently never sets it for approved scene assets. It will, from the
picked image's name. Renaming an image afterwards therefore cannot rewrite the meaning of a render that already
happened.

### D5 — The name reaches the operator and the provenance, **not** the prompt

The reference image already carries the view. The compiler's standing rule is that an axis something structural
supplies must not be re-described in words (`AXES CARRIED BY THIS TEXT`; and
[StepPromptElementPlan.cs:69](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/StepPromptElementPlan.cs)
clears `frozenState.location` / `frozenState.environment` for a Location binding). A prose clause like "this is the
left side" would create a second, competing source for the same axis.

So the name travels to:

- the picker option and the composer's binding line (operator-facing), and
- the render's semantic role, mirroring the face/body pattern at
  [SceneImageRenderingJobHandler.cs:626-644](../../../DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs)
  → `"approved location reference for Maintenance Shed (Left side)"`.

**Explicitly out of scope:** any new prompt slot, any prompt text change, any prompt-side name.

### D6 — Everything is editable in the app, at any time

- Rename an image from its card and from the review form.
- Rename the container from its own page (already there: `AssetStudioView` "This container → Name").
- Creating a new location with a new name is one form, already built (`/assets/create`).

No app restart, no DB edit, no code change is needed for the operator to add "Back garden" or "Interior — workbench"
or an entirely new place the RP session invented.

### D7 — No fallbacks

- Missing name on a location image ⇒ accept is refused with an explicit message. No default, no id-derived name, no
  "image N" written into the record.
- No code path invents a name.
- The display chain in the picker is explicit and owns nothing hidden: `DisplayName` when set, else today's exact
  behaviour (`CandidateNotes`, else `<Asset> — image N`) so existing non-location pickers do not change.

## 4. Code changes by file

| # | File | Change |
|---|---|---|
| 1 | `DreamGenClone.Domain/RolePlay/SceneAssetModels.cs` | `SceneAssetImage.DisplayName` (`string?`), documented as the operator-entered name used to retrieve the reference |
| 2 | `DreamGenClone.Infrastructure/RolePlay/SceneAssetRepository.cs` | `DisplayName` appended to `ImageSelectSql`, `ReadImage`, `AddImageParameters`, the `INSERT INTO SceneAssetImages` list (line ~217), the `CREATE TABLE` (line ~1447) and the idempotent `ALTER TABLE` list (line ~1424); new `SetImageDisplayNameAsync(imageId, name)`; location-name validation inside `ApproveImageForProductionAsync` |
| 3 | `DreamGenClone.Application/RolePlay/ISceneAssetRepository.cs` | `SetImageDisplayNameAsync` + the approval contract doc |
| 4 | `DreamGenClone.Web/Application/RolePlay/ISceneAssetService.cs` + `SceneAssetService.cs` | `SetImageDisplayNameAsync` (blank name rejected at the boundary) |
| 5 | `DreamGenClone.Web/Components/Assets/ProductionApprovalForm.razor` | A **Name** field (pre-filled from the image), saved before approval; required-and-disabled submit when the container is a location and the name is blank; the container's type arrives as a new parameter |
| 6 | `DreamGenClone.Web/Components/Pages/AssetReview.razor` | Passes `_asset.Type` into the approval form |
| 7 | `DreamGenClone.Web/Components/Pages/AssetStudioView.razor` | Image card shows the name as a badge and offers inline rename (same pattern as the container name box) |
| 8 | `DreamGenClone.Web/Components/Shared/SceneAssetReferenceChoice.cs` | Carry the image's name beside `Asset`/`Image` |
| 9 | `DreamGenClone.Web/Components/Shared/ReferencePicker.razor` | Offer one option per accepted image labelled `<container> — <name>`; `ChangeAsset` passes the name through |
| 10 | `DreamGenClone.Web/Components/Shared/ImageStepComposer.razor` | `SetAssetAsync` sets `ReferenceLabel: choice.Label` |
| 11 | `DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs` | Location semantic role names the image (D5) |
| 12 | `DreamGenClone.Web/Application/RolePlay/ReferencePickerEmptyReason.cs` | Wording for "a location exists but no image of it has been accepted yet" (mirrors the existing two-cause message) |
| 13 | `DreamGenClone.Web/Components/Pages/LocationStudio.razor` | Replace the "Location views … not yet implemented" card with a plain instruction to use the image name field — **no new panel, no new table** |

Optional phase 2, only if wanted:

| # | Change |
|---|---|
| 14 | An "accept this image as the location reference for *<detected scene location>*" action from the RP workspace, creating the container if it does not exist — the convenience for *"the rp session can make up any location at random"*. Not required for the workflow; the operator can already create the container by name. |

## 5. Tests

All must be green before the work is called done (repo rule).

- `SceneAssetRepositoryTests` — migration idempotent (run twice); `DisplayName` round-trips; a **location** image
  with a blank name refuses approval with an explicit message; a non-location image still approves unchanged; the
  ordinal reader shift is caught by asserting every field after the new column.
- `SceneAssetServiceJobTests` (or a focused sibling) — `SetImageDisplayNameAsync` rejects a blank name.
- `ImageStepReferenceLabellingTests` — the name reaches `ReferenceApplicationSelection.ReferenceLabel` through
  `ReferenceSlotPlanner.Plan`, and survives an image rename afterwards (snapshot, D4).
- `ReferencePickerEmptyReasonTests` — the new wording; a container with no images keeps its current wording exactly.
- `SceneImageServiceJobTests` — the render's semantic role names the image; an unnamed location image renders exactly
  as today.
- `AssetStudioUiContractTests` — reused, not bypassed; it asserts on source text for the two edited pages and will
  need updating.
- `StepPromptElementPlan` / `ReferenceBindingPromptRemoval` tests must stay green **unchanged** — they are the proof
  that D5 held and no prompt clause was added.

## 6. Open decisions — RESOLVED

All four were settled by the operator on 2026-10-02/03:

1. **Is the name required to accept a location image?** — **Required.** Implemented as
   `SceneAssetImageNaming.IsNameRequiredForApproval` in the domain, read by both the store's approval gate and the
   review form so the two cannot disagree.
2. **May an image be renamed after acceptance?** — **Yes.** Past renders keep the name they were bound with, because
   the label is snapshotted onto the binding (`ReferenceLabel`).
3. **Is the phase-2 "accept as reference for the scene's detected location" action wanted now?** — **Not now.** The
   operator can already create a container by name at `/assets/create`, so nothing is blocked.
4. **Should a location container with no accepted image appear in the dropdown?** — **No.** Only accepted images
   appear; `ReferencePickerEmptyReason` explains the empty list and now names the naming requirement.

## 7. Relationship to neighbouring items

- **B-124** — consumes the reference model and the `/locations/{id}` shell; adds no shell, no table.
- **B-139** (360° equirectangular reference) — a panorama is just another accepted, named image; it needs no model
  change.
- **B-140** (provenance, tags, search) — a `locationview:*` style tag could compose with the name, but the name is
  the primary key and does not depend on tags.
- **B-144** (location LoRAs) — a container of named accepted images is exactly the dataset a location LoRA needs;
  naming makes that set enumerable instead of a folder of GUIDs.

## 8. Implementation record (2026-10-03)

**Schema.** `SceneAssetImages.DisplayName TEXT NULL`, appended LAST in the idempotent `ALTER TABLE` list because the
image reader is ordinal and the code says so explicitly. No new table. No view enum. No seeded name list.

**Writers.** `SceneAssetRepository.SetImageDisplayNameAsync` is the ONE writer, a narrow `UPDATE`, and `DisplayName`
is deliberately **absent** from the image upsert's `ON CONFLICT` list — the same ownership split `TagsJson` uses, so a
render completing or an edit stage writing back its row cannot erase a name an operator typed. A blank name is refused
at the repository *and* at the service.

**The gate.** `SceneAssetRepository.ApproveImageForProductionAsync` calls `RequireLocationImageNamedAsync`, which reads
the container's type and refuses an unnamed location image with a message naming the image, the container and the
remedy. The predicate is `SceneAssetImageNaming.IsNameRequiredForApproval` — one owner, read by the form too, so the
form cannot offer an approval the store will refuse.

**Surfaces.** The review form gained a **Name** field (required and explained for a location, optional elsewhere) and
an `AssetType` parameter; `AssetReview` passes the container's type. The image card gained inline naming, saved on
change. `ReferencePicker` labels each choice `<container> — <name>`, falling back to the older
`CandidateNotes` / `image N` labelling only when nothing is named. `ImageStepComposer.SetAssetAsync`,
`AddAssetAsync` and `ReferenceApplyPanel.SetAssetAsync` all carry the name onto the binding via
`ImageStepSlotSource.ReferenceLabel`. `MediaEditReferenceResolver` records it in render provenance as
`location continuity (Left side)`.

**No prompt change** — D5 held. `StepPromptElementPlan` and `ReferenceBindingPromptRemoval` tests are untouched.

**Follow-up, same day: the approval form's ceremony removed.** The operator's response to the finished form was blunt —
*"I dont need any of these fields? ceremony i do not need"*, with the fields filled in as `adfs` / `BA`. That is the
correct verdict on them: nothing in the app parses `SourceProvenanceJson` or `CompatibilityMetadataJson` (verified by
search — they are written, displayed in the Asset Studio provenance panel, and read by nothing), and the use scope
merely restates the container the image already belongs to. Making an operator type them per image produced junk, not
governance.

So `SceneAssetApprovalDefaults` (domain) now supplies the recorded values — consent/licence `NotApplicable`, licence
label `NA`, policy `general`, compatibility `{}`, provenance `{"source":"operator-approved"}` — and the use scope is
derived from the container type **only where the type settles it** (`Location`, `Wardrobe`, `CharacterFace`,
`CharacterBody`); it is left empty for a prop, style, playground, character, production frame or character pose, so the
form still asks for a real answer instead of inventing one. The fields moved into a collapsed
*"Recorded with this approval"* disclosure with a one-line summary of exactly what will be written, so the record stays
visible without being a wall of inputs.

Approving a location image is now: **type the name → click Approve.** The button's disabled state also names its own
reason instead of just greying out — the trap that made this step unfindable twice. Pinned by
`SceneAssetApprovalDefaultsTests` (the defaults are non-blank, the JSON defaults parse as objects, the consent/licence
defaults are not the refused `Unknown` state, and the scope is derived only for the types that settle it).

**The badge that caused the report.** The operator's original "nothing shows in the location list" was **not** a bug:
they had used the review deck's **Accept**, which writes `CandidateDecision = Accepted` (this batch's pick) and not
`ProductionApprovalStatus`. Asset Studio rendered that as a green badge that read as approval. It is now
`picked: Accepted` in a neutral colour with a title saying it is not a production approval, and the Accept button's
tooltip says the same at the moment of the click.

**Evidence.** `dotnet build DreamGenClone.Tests` → 0 errors. Full suite: **3794 passed / 4 failed**, and all four
failures are pre-existing and unrelated — 3 × `SdxlSceneImagePromptBuilderTests` (B-135, already documented) and
1 × `SceneLoraSelectionWireTests.Read_WithAMalformedStack_Throws`, whose file and test are **untracked** new files
from in-flight B-143 work. New tests: `LocationImageNamingTests` (10 cases: the predicate per type, the narrow writer
vs an ordinary upsert, blank refusal, approval refusal, approval of a named image, a non-location image unchanged, and
the empty-picker wording) and `SceneAssetApprovalDefaultsTests` (5 cases). `AssetStudioUiContractTests` updated for the
changed review-form markup and extended to assert the naming contract.
