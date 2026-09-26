# B-130 — Implementation plan

Companion to `spec.md` (what and why) and `tasks.md` (the ordered work). This file is the architecture:
the data model, the components, the seams into the engine, and the phasing.

---

## 1. The shape of the change

Three layers, built bottom-up so each phase is independently verifiable:

1. **Bindings layer (no UI)** — a step's slots become ordered reference bindings on the record, and the
   render path consumes them. This is where the current create-path defect lives; it is fixed first
   because nothing above it can be trusted until a native-reference render actually carries its faces.
2. **Composer component** — one Blazor component that renders a step: model → slots → prompt → settings →
   submit, driven entirely by a host-supplied blueprint.
3. **Hosts** — each of the seven surfaces becomes a thin caller with its own blueprint.

Prompt adaptation (D4) sits between layers 1 and 2: it is pure logic over the slot set, and the hosts never
see it.

---

## 2. Data model

### 2.1 The blueprint (new, Domain)

```
ImageStepBlueprint
  StepKind            : enum   (Compose | IdentityApply | PoseRender | BodyView | LoraCell | Edit | AssetCreate)
  Title               : string
  SourceMode          : enum   (None | ProducedImage | SelectedAttempt)
  Slots               : IReadOnlyList<ImageStepSlotBlueprint>
  PersistenceKind     : enum   (Throwaway | SceneImage | SceneAsset | LoraCellAttempt | EditRevision)
  AllowsBatch         : bool   (default false — only the pose-library library build opts in)
  DefaultModelRole    : string (which configured model role to resolve when the caller pins none)

ImageStepSlotBlueprint
  SlotKind            : enum   (Face | Body | Pose | Location | Wardrobe | CharacterPose)
  ActorKey            : string? (which character the slot belongs to; null = scene-level)
  Required            : bool
  PrefillSource       : enum   (None | MomentCast | PackCanonicalFace | PackCanonicalBody | RecordRule | CallerSupplied)
  AllowedSources      : IReadOnlyList<ReferenceSourceKind>   (D7 — host declares, never inferred)
```

### 2.2 Ordered bindings (extend, do not replace)

`ReferenceApplicationSelection` gains, additively so persisted JSON stays readable:

```
Ordinal : int?    (1-based; null = legacy element order, normalised at load)
Kind    : string? (Face | Body | Pose | Location | Wardrobe | CharacterPose; null = legacy ElementKey)
Source  : enum    (ApprovedAsset | ScratchImage | PoseLibrarySkeleton | CharacterPoseAsset)
```

`SceneImageRecord.IdentityReferenceBindingsJson` already carries the identity faces in exactly the shape
`SceneImageRenderingJobHandler.BuildNativeReferencesAsync` reads (`Ordinal`, `CharacterId`,
`CharacterName`, `FileRelativePath`, `Sha256`). **That is the existing ordered-binding channel — the
composer builds on it rather than inventing a second one.**

### 2.3 Character pose asset (D8) — lives in `SceneAssets`

**Decided by the operator 2026-09-25: a character pose asset IS a `SceneAsset`, with a new
`SceneAssetType.CharacterPose` appended to the enum.** No new store, no new table.

Append-only is required and safe: the `Type` column stores the enum **name**, so adding a member cannot
disturb existing rows.

**Why this is the right call.** A pose asset inherits everything the asset subsystem already enforces —
`ProductionApprovalStatus` / `ProductionVersion` / `Sha256` / `FileRelativePath`, the consent / licence /
content-policy fields, the asset tree, `ReferencePicker`, `ProductionApprovalForm`, and the render path's
immutable-selection revalidation. Most importantly it travels through the **same
`ReferenceApplicationSelection` channel** the render already validates, so the Pose slot needs **no second
reference mechanism** — which was the single largest hidden cost in the new-store option.

The facts a pose asset must carry already have columns. Reuse them; do not invent parallel ones:

| Fact | Column |
|---|---|
| it is a pose | `Type = 'CharacterPose'` |
| which pose | `ViewDescriptorJson` — the user-defined-view pattern (B-129 §5.1) |
| wardrobe state (clothed / unclothed / …) | `BodyState` |
| body build state | `BodyView` |
| whose pose | `CharacterProfileId` (and `CharacterTemplateId` per B-127 — D8's "any RP session") |
| the image itself | `FileRelativePath` + `Sha256` + `Width` / `Height` |
| selectable as a reference | `ProductionApprovalStatus = Approved` |

**One sub-question this creates, for B130-018:** whether "which pose" needs a queryable typed column or
`ViewDescriptorJson` is enough. Measured constraint 4 makes wardrobe state part of the lookup, so the picker
must filter on state — and a JSON descriptor filters poorly in SQL. That trade-off is the thing to settle,
not the storage location.

---

## 3. Components

| Component | Responsibility |
|---|---|
| `Components/Shared/ImageStepComposer.razor` | The one composer. Renders model → slots → prompt → settings → submit from a blueprint. No host-specific logic. |
| `Components/Shared/ReferenceSlotList.razor` | Ordered slot list: add / remove / reorder, ordinal shown, kind badge. |
| `Components/Shared/ReferenceSlotCard.razor` | One slot: current source + thumbnail + clear/replace. |
| `Components/Shared/SlotSourcePicker.razor` | Picks a source **by the slot's `AllowedSources`** — approved asset, scratch render, skeleton, character pose asset. |
| `Components/Shared/StepPromptPanel.razor` | Prompt text, staleness notice (D5), what-will-be-omitted list, Generate/Refine. |

Supporting application services:

| Service | Responsibility |
|---|---|
| `ImageStepBlueprintFactory` | Builds the blueprint per host (`ForProductionStudio`, `ForLoraCell`, …). The only place host knowledge lives. |
| `ReferenceSlotPlanner` | Expands a blueprint + Moment/record into resolved slot bindings. Fail-fast when a required slot cannot resolve. |
| `StepPromptElementPlan` | **D4**: slot set → the payload scopes to remove. Pure, testable, no DB. |
| `StepPromptStaleness` | Detects that bindings changed since the prompt was generated. |

---

## 4. Seams into the engine (keep them narrow)

1. **Native-reference create carries its faces.** `SceneImageService.EnqueueRenderAsync` must write
   `IdentityReferenceBindingsJson` when the render is native-reference (B130-001). *Partly written already
   in the working tree and unverified — treat as not done until built and tested.*
2. **Render mode derives from the bindings**, not from a separate checkbox. A step whose slots resolve to
   `NativeMultiReference` is a `NativeReference` render; the UI must not offer a mode the bindings
   contradict. (Today both composer pages hardcode `_applyIdentityOnCreate ? IdentityControlled : PromptOnly`.)
3. **`resolution` accepts `0`** ✅ done (B130-003) — "keep each reference at its own size", the live node's own
tooltip, measured working at 6 references (111 s vs 126 s at 1024). The app previously refused it.
4. **`MaxReferences` corrected 16 → 10** ✅ done (B130-003), with the evidence recorded on the row. The value is
enforced in `ComfyUIImageClient`, so the correction changes real behaviour rather than documentation. **Capping
what the picker OFFERS at ≤ 6 for a pose-carrying step is B130-006**, because the cap depends on whether the
step carries a pose — which `MaxReferences` cannot express, and conflating the two is how the original 16 got
written down.
5. **Region editing** (CASE-21) enters as a **slot-independent step operation** (`MediaEditOperationKind`
   gains a region kind), not as a reference slot — the mechanism is a masked latent, not a reference image.
   NOTE the four call sites that must branch on `Kind != Edit` (`media-edit-operations-crop-enhance.md`).

---

## 5. Phasing

Each phase ends in something demonstrable and green; no phase leaves a surface half-migrated.

| Phase | Content | Demonstrable result |
|---|---|---|
| **P1 — Bindings** | B130-001..003: native create writes its faces; render mode derived; `MaxReferences`/`resolution` corrected | A native-reference render from the create path carries **identity faces + location + pose**, verified on the host |
| **P2 — Slots** | Blueprint + slot model + planner + `Ordinal`/`Kind` on selections | The planner produces the ordered binding set for a Moment, testable without UI |
| **P3 — Prompt** | `StepPromptElementPlan` + staleness, wired through `ScenePromptOverridesApplier` | Filling a Location slot removes the location prose; the operator sees what was omitted |
| **P4 — Component** | The five components; blueprint factory for **one** host (Production Studio) | The studio composes through the new component; the Finish-stage panel and the duplicate prompt card are gone |
| **P5 — Hosts** | Port Composition Composer, Scene Image Compose, LoRA cell, pose library, asset creator, edit workspace | One composer, seven blueprints, no divergent layouts |
| **P6 — Character pose assets** | Store + picker source + library batch | "Render Becky's pose library, clothed and unclothed" → a pickable library usable from any session |
| **P7 — Region & panorama** | CASE-21 mechanism as a step operation in the shared edit workspace | Circle/painted region edit and outpaint through the same composer |

**B-127 is a hard dependency for P6 only** — P1–P5 must not wait on it.

---

## 6. Ordering rule for the hosts (why P6's batch is special)

The LoRA studio carries an explicit operator rule: **no batch** ("the training set is judged frame by
frame; a sweep is how a set of near-duplicates gets made"). D8's pose-library build **is** a batch. Both
are correct — which is exactly why `AllowsBatch` is a blueprint field and not a component behaviour. The
component must never impose either.

---

## 7. Test strategy

- **Pure-logic tests first**: `StepPromptElementPlan` (slot → scopes), `ReferenceSlotPlanner` (prefill +
  fail-fast), staleness. These need no DB and no host.
- **Contract tests** for the blueprint factory: every host's blueprint resolves, and every slot's
  `AllowedSources` is a subset of the sources the picker can actually offer.
- **Engine tests** for B130-001: a native-reference create persists bindings; an empty binding set fails
  fast **by name**; a requested-but-unapproved pack fails fast.
- **Structural guard**: a test asserting no composer surface hardcodes a per-element strategy list
  (the class of defect that produced the "capabilities are not refreshed" report).
- **Host proof** for P1 and P7 only, using `helpers/local-comfyui-host/run-qwen-2-1-proof.ps1` with the
  app's own emitted graph, per the existing proof discipline.

---

## 8. Risks

| Risk | Mitigation |
|---|---|
| The composer becomes a god-component with seven special cases | Blueprint factory is the **only** place host knowledge lives; a review rule: if `ImageStepComposer.razor` needs an `if (host == …)`, it belongs in the blueprint. |
| Prompt adaptation silently diverges from what the render sends | The removal plan is derived from the **same** slot set the render consumes, and the UI lists what was omitted. |
| P1 changes a create path with production data | The change is additive (a previously-null column) and fails fast rather than changing existing renders. Existing rows are unaffected. |
| Character pose assets multiply (poses × wardrobe × body states) | The key is exact; the picker must filter by the step's wardrobe/body state, and the library build must be explicit about which states it renders. |
| CASE-22's ≤6 ceiling makes the 6-ref blueprints unusable | Cap from configured data; a step needing more fails fast with the measured reason rather than rendering a dropped pose. |
