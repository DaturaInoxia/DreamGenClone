# B-130 — Tasks

Dependency-ordered. Each task names the files it touches so blast radius is visible before work starts.
`[P]` = parallelisable with the task above it. Nothing here starts a UI phase before its bindings phase is green.

---

## P1 — Bindings (no UI)

### B130-001 — A native-reference CREATE carries its identity faces
**The defect.** `SceneImageService.EnqueueRenderAsync` writes `IdentityPacksJson`/`IdentityPackId` only for
`IdentityControlled` and **never** writes `IdentityReferenceBindingsJson`; only the two identity *edit*
paths do. `SceneImageRenderingJobHandler.BuildNativeReferencesAsync` builds its faces exclusively from that
column. So a native-reference create either fails fast on an empty reference set or renders the location and
**silently drops every face** — the "no reference is ever dropped" violation.

**✅ DONE 2026-09-25.** `ResolveNativeReferenceIdentityBindingsAsync` resolves the Moment's visible cast
through `ISceneImageProductionService.ResolveIdentityReadinessAsync` and writes the bindings in the exact
shape the render handler reads (`ordinal` / `characterId` / `characterName` / `fileRelativePath` / `sha256`).
Six tests, all green: bindings written in **requested** order (not readiness order); no production service →
fails fast naming it; identity requested without a production group → fails fast naming the group;
unapproved pack → fails fast **naming the pack**; no identity + no group → **binds nothing and still renders**;
non-native modes leave the field null.

Two behaviours were **corrected during the work** and are pinned by tests, because the first draft got them
wrong: a native-reference render must **not** require a production group (a location-only native render is
legal), and a Moment with no approved packs is a legitimate state when identity was not requested (the group
can record an explicit skip) — only a *requested* pack may ever fail fast.

- Files: `DreamGenClone.Web/Application/RolePlay/SceneImageService.cs`,
  `DreamGenClone.Tests/RolePlay/SceneImageServiceJobTests.cs`
- Evidence: solution build 0 errors; 97/97 targeted tests green; the 6 new cases individually verified.

### B130-002 — Render mode derives from the bindings, not a checkbox
Both composer pages hardcoded `_applyIdentityOnCreate ? IdentityControlled : PromptOnly`. A step whose slots
resolve to `NativeMultiReference` must BE a native-reference render, and the UI must not offer a mode its
bindings contradict.

**✅ DONE 2026-09-25.** `SceneImageService.EnqueueRenderAsync` now derives the mode: a `PromptOnly` label
together with a `NativeMultiReference` binding becomes `NativeReference`, so a stale label can no longer send
the render down the prompt-only branch and cost the operator every reference they bound. No `.razor` change
was needed — the engine owns the derivation, and the page's `ResolvedRenderBadge` already displays the
resolved references.

**Two things worth carrying forward:**

1. **`IdentityControlled` is deliberately NOT overridden** by a native scene binding, which is a deviation
   from the "fail fast on contradiction" wording above. How a model carries identity is the render handler's
   own per-model decision and may itself be native references, so overriding the mode would *remove* that
   decision rather than correct a stale label. Pinned by a test. If a genuine contradiction exists here it
   should be found deliberately, not by guess.
2. **A defect in B130-001 was found and fixed while doing this:** the resolver bound the **whole visible
   cast** when the caller named no identity packs — inventing references the operator never asked for, the
   mirror of dropping them. It now binds **only what was named**. A blueprint that wants the whole cast names
   the whole cast (B130-006).

Also confirmed: the engine already validates that a native binding carries an **exact approved asset**
(`SerializeReferenceApplications`), so the derivation only ever fires on genuinely bound references.

- Files: `DreamGenClone.Web/Application/RolePlay/SceneImageService.cs`,
  `DreamGenClone.Tests/RolePlay/SceneImageServiceJobTests.cs`
- Evidence: build 0 errors; **101/101** targeted tests green. New cases: mode derived from the binding;
  text-only bindings do **not** derive; `IdentityControlled` keeps its mode; a Moment with two approved packs
  and none named binds **neither** and never even asks.
- Deferred: `SceneImageCompose.razor` line 133 still prints a hardcoded `TextOnly / ReferenceConditioning`
  summary — folded into B130-013 (that page has no slot list at all yet).

**Click path this makes usable:** `/roleplay/studio/{session}/{interaction}/production/{group}/composition`
→ pick Qwen-2.1 in **Image Model** → bind a Location reference with strategy **NativeMultiReference** →
Generate Prompt → Generate Composition. The engine derives `NativeReference`, and the render handler's
native branch resolves the approved asset.

### B130-003 — Correct the declared model capability
`MaxReferences: 16` was never tested (CASE-22 disproved it); `Resolution` rejected `0`, which the host supports.

**✅ DONE 2026-09-25.**

**Code.** `QwenImage21ModelSettings` now accepts `Resolution: 0` through a single shared
`RequireResolutionBudget`: `0` means "keep each reference at its own size" (the live node's own tooltip), a
positive value is a pixel budget, and **negative is still refused** because it is meaningless. Missing is still
a fail-fast naming the field. Previously `0` was rejected as if it were unset, so **the app refused a capability
the model has**.

**Data.** Both `RegisteredModels` rows corrected `MaxReferences` **16 → 10**, with the original claim's
replacement justified *inside the JSON* (`ReferenceCountProofId`, `MaxReferencesNote`, `ResolutionNote`) rather
than silently overwritten, so the correction is auditable from the row itself. `10` is the right value because a
10-slot graph **does** run and its references **are** consumed (a slot-5 garment transferred); what fails at 10
is *pose adherence* — a quality property, not an acceptance limit. Folding `≤6` into `MaxReferences` would have
conflated the two.

- Files: `DreamGenClone.Web/Application/ModelManager/QwenImage21ModelSettings.cs`,
  `DreamGenClone.Tests/RolePlay/ComfyUIImageClientQwenImage21Tests.cs`, the two `RegisteredModels` rows
- Tests: `Resolution: 0` accepted by **both** the generation and editor read paths; negative refused;
  missing still named. Evidence: build 0 errors, 80/80 targeted green.
- **Deferred to B130-006:** capping what the picker *offers* at the pose-carrying budget (≤6) from configured
data. That belongs with the planner because the cap depends on whether the step actually carries a pose, which
`MaxReferences` cannot express — and the honest note now sits on the row for it.

---

## P2 — Slots

### B130-004 — Slot + blueprint model (Domain) — ✅ DONE 2026-09-25
`DreamGenClone.Domain/RolePlay/ImageStepBlueprint.cs`: the blueprint, slot blueprint and all supporting
enums (`ImageStepKind`, `SourceMode`, `PersistenceKind`, `SlotKind`, `ReferenceSourceKind`, `SlotPrefill`,
`ActorKeyRequirement`). Validation refuses: no title; one slot declared twice for the same actor (two slots of
one kind would fight over the same prompt elements and iteration order would silently decide); a slot with **no
allowed source** (a control the operator could never satisfy); and a per-character slot with no actor key.

**A mid-build correction worth keeping.** The actor-key rule began as a boolean, and a test caught that a
frame-level **pose** then produced the scope `character:.position` — a key that addresses nobody. It is now
three-valued (`Required` / `Forbidden` / `Optional`), because a skeleton genuinely either steers one
character's stance or the frame as a whole. `ActorKeyRequirementFor` is total, so a new slot kind that forgot
to answer throws instead of mapping its elements somewhere wrong.

### B130-005 — Ordered bindings on the selection record — ✅ DONE 2026-09-25
`ReferenceApplicationSelection` gained additive `Ordinal` / `Kind` / `Source`, kept as text to mirror
`Strategy` and to stay loadable for rows written before slots existed. Nullable and additive, so every
persisted binding still loads. The ordinal is documented as **request data**, because slot order decides
placement — the first reference anchors the frame.

### B130-006 — `ReferenceSlotPlanner` — ✅ DONE 2026-09-25
`Web/Application/RolePlay/ImageStep/ReferenceSlotPlanner.cs`. Pure: blueprint + resolved assignments + the
model's own `MaxReferences` → ordered bindings. Ordinals follow the **blueprint's slot order**, not the
caller's list order, so a step always sends its references in the same positions. Fail-fast on: an assignment
for an undeclared slot or the wrong actor; a source the slot does not allow; an unfilled **required** slot; one
slot filled twice; a blank strategy; and a count over budget.

`EffectiveBudget` is the measured ceiling and is deliberately **not** a copy of `MaxReferences`:
`PoseCarryingReferenceBudget = 6` applies only when a **Pose** slot is filled, because that is what CASE-22
measured — at ten references the figure ignored the skeleton whichever slot it held. `CharacterPose` is a
photoreal image that does not depend on pose guidance, so it is exempt. The refusal message cites the
measurement rather than asserting an arbitrary limit.

---

## P3 — Prompt adaptation (D4)

### B130-007 — `StepPromptElementPlan` (pure) — ✅ DONE 2026-09-25
`Web/Application/RolePlay/ImageStep/StepPromptElementPlan.cs`. Keys are the **real** compiled-brief payload
properties, verified against the same snapshot fixture the applier's own tests use: `Location` →
`frozenState.location` + `frozenState.environment`; `Face`/`Body` → `character:{key}.appearance` (the
applier's special-cased appearance override, **de-duplicated** so binding both clears it once); `Wardrobe` →
`.clothing`; `Pose` → `.position` at a character, or `moment.visibleAction` when frame-wide; `CharacterPose` →
appearance + clothing + position. `Build` preserves declaration order and de-duplicates.

A test asserts every emitted scope uses a prefix the applier accepts, so a property-name typo surfaces as a
failing test rather than a wrong prompt at render time.

**Evidence for B130-004..007:** build 0 errors; **87/87** targeted tests green.

### B130-008 — Wire the plan through the existing overrides path — ✅ DONE 2026-09-25
- Files: `Web/Application/RolePlay/SceneImageService.cs` (pass overrides with the request),
  `Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs` (already applies
  `ScenePromptOverridesApplier.Apply`), `ScenePromptRemovalNotice`
- Tests: a Location-filled step's generated prompt contains no location prose; the removal notice is present
  and authoritative.

**Delivered:** `ImageStep/ReferenceBindingPromptRemoval.cs` (`Merge` binding-derived removals behind operator
overrides, `Derive` from bindings), `SceneImagePromptGenerationJobPayload` + `ScenePromptRequest` gained
`ReferenceApplications`, and `SceneImagePromptGenerationJobHandler.HandleCanonicalAsync` merges bindings through
`ScenePromptOverridesApplier.Apply`. **Evidence:** build 0 errors; removal/merge tests green.

### B130-009 — Staleness (D5) — ✅ DONE 2026-09-25
- Files: new `ImageStep/StepPromptStaleness.cs` + `Components/Shared/StepPromptPanel.razor`
- Content: bindings-hash comparison; the panel says what changed and requires an explicit Generate
- Tests: changing a binding flips stale; regenerating clears it.

**Delivered:** `StepPromptStaleness.SignatureFor` (SHA256/16 hex, order-sensitive), `IsStale`,
`DescribeChanges`; `StepPromptPanel.razor` shows the staleness alert with reasons plus the
"Supplied by the bound images" badges. **A defect in my own first draft was caught by its test:**
`DescribeChanges` passed a *string key* into `DescribeKey`, which expects the binding, so the reason text named
the signature instead of the slot. **Evidence:** build 0 errors; staleness tests green.

---

## P4 — The component, one host

### B130-010 — `ImageStepComposer.razor` + slot components — ✅ DONE 2026-09-25
- Files: new `Components/Shared/ImageStepComposer.razor`, `ReferenceSlotList.razor`,
  `ReferenceSlotCard.razor`, `SlotSourcePicker.razor`, `StepPromptPanel.razor`
- Constraints: **structurally different surface when the model has no native-reference capability — the slot
  list is absent, not disabled** (D3); no host-specific branches in the component
- Tests: a structural test that the component contains no host name; a render test per capability shape.

**Delivered:** `ImageStepComposer.razor` + `StepPromptPanel.razor` (the slot list is ABSENT, not disabled, when
the model lacks `NativeMultiReference`), reusing the existing `ReferencePicker`, `PoseLibraryPicker`,
`StrategySelector`; `SceneAssetType.CharacterPose = 9` appended to the Domain enum.

### B130-011 — `ImageStepBlueprintFactory` + Production Studio migration — ✅ PARTIAL 2026-09-25
- Files: new `Application/RolePlay/ImageStep/ImageStepBlueprintFactory.cs`,
  `Components/Pages/SceneImageStudio.razor`
- Content: `ForProductionStudio()`; replace the Finish-stage `ReferenceApplyPanel` and the duplicate prompt
  card; the production model gets a **UI selector** (it has none today)
- Tests: blueprint resolves; the studio renders through the component; contract test for `AllowedSources`.

**Delivered (factory, done):** cast-aware `ForProductionStudio(IReadOnlyList<ImageStepActor>)`,
`ForLoraCell(actor)`, `ForPoseLibraryTest()`, `ForAssetCreate()`, `ForEdit(cast)`,
`ForCharacterPoseLibrary(actor)`; `RequireCast`/`RequireActor` fail fast on an empty cast or a blank profile key.
**A test caught a design defect here:** the original actor-key rule was a boolean, so a frame-wide pose produced
`character:.position` — a scope addressing nobody. It is now three-valued
(`ImageStepActorKeyRequirement`, Required/Forbidden/Optional) and total.
**Evidence:** build 0 errors; **154/154** targeted tests green.
**NOT done (layout):** the studio's Finish stage still renders its own `ReferenceApplyPanel` — the host is not yet
migrated. Tracked with B130-012..017 below.

---

## P5 — Remaining hosts (one task per surface, all `[P]`)

Each: add a factory method, delete the surface's bespoke layout, keep the surface's persistence target.

- **B130-012** `CompositionComposer.razor` — ✅ DONE 2026-09-25 (reference section migrated; identity deliberately unchanged)

**B130-012 evidence.** The page's own model select and its four-row `ReferenceApplyPanel` are gone; the step is
presented by the composer over a new `ImageStepBlueprintFactory.ForPackIdentityComposition(cast)`. Its three legacy
element rows were UNADDRESSED (a body or wardrobe reference with no owner), which is the class of defect the actor-key
rule exists to prevent, and they are replaced by per-character slots + the frame-wide location slot. The cast comes from
the identity packs the page already resolves — and the fix that made that possible is worth naming: the loader resolved
`owner.TemplateId` to READ the packs and then **threw it away**, storing only the scenario id in `CharacterKey`. The
option now carries both, because they are two different keys for two different jobs (the scenario id joins prompt-input
rows; the template id is the actor key a slot must address).

Two deletions worth recording: the page's `OnReferenceApplicationsChanged` (orphaned by the panel's removal) and its
per-model **strategy-reset loop**, which rewrote every reference to `TextOnly` when the new model could not execute the
chosen strategy. The composer's per-slot menu intersects each slot's meaning with the selected model's capability, so the
reset became redundant — and removing it also removed a branch that could quietly downgrade what the operator bound.

**The one deliberate transitional choice:** this page's identity mechanism is still the approved-pack channel
(`IdentityPacks` + `IdentityMode`), which the render resolves into FACE references itself (verified in
`SceneImageService.ResolveNativeReferenceIdentityBindingsAsync`: it writes `canonicalFaceAssetId`, `faceView`,
`fileRelativePath`, `sha256` — faces only). So the blueprint declares no face slot: the pack IS that element's mechanism,
and adding a face slot as well would give each character two mechanisms in one render. That is why the page gains
addressed body/wardrobe/location slots and does not yet gain the face slot. Moving the pack from CHANNEL to SOURCE (pick
the character → `IdentityPackSlotPrefill` fills face and body slots → the pack channel is retired) is the remaining
step, and it is a behaviour change on a page whose identity flow the operator uses today, so it is scoped rather than
folded into this migration.

- **Evidence:** build 0 errors; RolePlay area **2514/2517**, the 3 failures being the documented pre-existing
  `SdxlSceneImagePromptBuilderTests` ones (files unmodified). Three contract tests retargeted, each keeping its
  guarantee: the owner is still resolved before the pack store is touched, the roster still resolves through the ONE
  builder, and no composition surface owns a per-element strategy list (pinned as an ABSENCE, the stronger form).
- **B130-013** `SceneImageCompose.razor` — ✅ DONE 2026-09-25, **resolved by deletion rather than migration**
- **B130-014** `LoraDatasetWorkspace.razor` — ✅ DONE 2026-09-25 (the conditioning decision MOVED, not just the layout)
- **B130-015** `PoseLibraryPage.razor` — ✅ DONE 2026-09-25 (step migration; persistence still `Throwaway`)
- **B130-016** `PromptAssetCreator.razor` — ✅ DONE 2026-09-25
- **B130-017** `ImageEditWorkspace.razor` — ✅ DONE 2026-09-25

**B130-014 evidence — the behaviour change, not a layout swap.** `CharacterLoraCellService.RenderCellAsync` no longer
resolves the face and build references privately. A new `ResolveCellBindingsAsync` turns the cell's rule into ordered
bindings (the same two pure decisions, `ResolveIdentityConditioning`/`ResolveBodyConditioning`, now the SEED instead of
the render's hidden step) and the render CONSUMES them. The step shows the two images the cell will be shot with, and
they can be changed before shooting. Four refusals, each naming its reason and each proven by a test: no build binding;
a face binding for a view that shows no face (`"shows no face"` — a reference image carries its content, so a back view
conditioned on a face trains on something the plan never shot); a reference from ANOTHER pack (would render a different
character under this trigger token); and a missing pack image. The cell's prompt also became the step's editable draft
(`_cellPromptDraft`) instead of a read-only paragraph the render ignored. A new source kind
`ImageStepReferenceSourceKind.IdentityPackAsset` was required: pack images are `SceneImageReferenceAsset` rows addressed
by their own pack id, so folding them into `ApprovedSceneAsset` would mean inventing a scene-asset id for an image that
has none. **Evidence:** build 0 errors; **409/409** targeted tests green.

**B130-017 evidence.** The edit workspace's model select and three UNADDRESSED element rows (Body/Wardrobe/Location, no
owner on either per-character element) are replaced by the composer over a new
`ImageStepBlueprintFactory.ForEditElements(cast)`. The faces are deliberately NOT declared there: this workspace picks one
face per detected person through its own target flow, so a face slot would give each person two mechanisms in one render.
The cast comes from the identity targets the operator has identified (`EditCast`), which is what makes the body and
wardrobe slots addressable at all. The workbench keeps intent → compile → prompt → run, so the composer renders with
`ShowPrompt="false"` — a second prompt box would be two controls over one value — and its submit row is hidden when a host
supplies no `OnSubmit`, by the same rule that hides the prompt panel's Generate/Refine buttons. **Evidence:** build 0
errors; **519/519** targeted tests green, including the retargeted editor contract test.

**Four composer capabilities were added because these hosts needed them** (each applies to every host):

1. `Bindings` is the ONE pre-fill channel (host-assigned). An earlier `SeedBindings` parameter was REMOVED in this same
   pass: a seed list keeps re-adding a binding the operator has just cleared, because clearing it does not change what
   the host's rule resolves to. There is deliberately one way for a step to start pre-filled.
2. `MaxReferences` became **model-derived** (`ModelReferenceLimit`, from the choice's own configured limit) instead
   of host-supplied, so a host cannot get the reference budget wrong; and the slot-list test widened from "does the
   model qualify `NativeMultiReference`" to "can the model carry ANY strategy this step's slots accept, text-only
   excluded". The narrow test would have hidden the slot list from a ControlNet pose route — the second mechanism
   `PoseTestRenderService` supports — while `TextOnly` being in every slot's meaning list would have shown slots on
   a model with no reference route at all (the D3 regression in the other direction).
   `SceneImageModelChoice` gained `MaxReferences`, populated by both listers from
   `QwenImage21ModelSettings.TryResolveReferenceCapacity` (non-throwing on purpose: this is listing data, and one
   malformed row must not take out every picker on a page).
3. `ModelPlaceholder`, so a host that requires an explicit model choice keeps that requirement instead of silently
   pre-selecting the first model.
4. `ShowPrompt`, so a host whose prompt comes from its own flow can take the model and the reference slots from the
   composer without a duplicate prompt box.

**B130-012 note (the honest blocker).** `CompositionComposer` is not a layout swap either. Its identity model + pack
selector, its pose upload, and its prompt-input inspector all read `_referenceApplications` by ELEMENT KEY (`"Identity"`,
`"Pose"`), and its request building branches on that key to attach the identity strategy and the pose strength. Migrating
the panel means rewriting those consumers around slot kinds and deciding what the pack selector now means, because
`ForProductionStudio` renders the face as a slot while this page also applies an identity PACK. Half-migrating it would
leave two identity mechanisms in one render — the same defect B130-014 had to solve properly.

**B130-011 evidence — the stage is THREE steps, not one panel.** Reading the code showed
`_productionReferenceApplications` was ONE element list sent by three different requests: the composition create
(~line 4035), the face-only identity correction (~4066), and the finish edit (~4115) — while the panel that could EDIT
that list is only rendered in the finish stage. So composition and identity renders were submitted with whatever was
bound while looking at another step's UI, and the identity step carried TWO identity channels in one request (its own
`IdentityReferences` plus whatever `Identity` binding sat in the shared list).

Done in this pass:

- The identity step no longer passes `ReferenceApplications` at all, and says why in place: its identity IS
  `IdentityReferences`, and a second list would give one face two mechanisms. A contract test now pins that absence.
- The list was split per step: `_productionCompositionBindings` (the composition step, edited in the composition stage)
  and `_productionFinishBindings` (the finish step).
- The finish stage's `ReferenceApplyPanel` is replaced by the composer over
  `ForEditElements(ProductionCast)`. `ProductionCast` is built from `_productionIdentityReadiness`, whose `CharacterId`
  is **the character TEMPLATE id** — verified in `SceneImageProductionService.ResolveIdentityReadinessAsync`, which
  resolves every frozen character id through `ResolveOwnerAsync(...).TemplateId` before reading packs and reports the
  owner id back ("so the caller can hand it straight back in a selection"). That is exactly the profile key a reference
  slot must address. Faces are NOT declared on this step, for the same reason as the edit workspace.
- **Evidence:** build 0 errors; RolePlay area **2514/2517**, the 3 failures being the documented pre-existing
  `SdxlSceneImagePromptBuilderTests` ones (files unmodified).

**What remains for B130-011:** the COMPOSITION stage has no reference UI at all today, so `ForProductionStudio(cast)` —
which is written and tested — is not yet rendered anywhere in the studio. Adding it is new UI on a stage that has none
rather than a swap, and it needs its own pass: render the composer in the composition stage against
`_productionCompositionBindings`, pre-fill from `_productionIdentityReadiness` with `IdentityPackSlotPrefill` (face per
cast member, body optional, location/pose), and delete `RevalidateProductionReferenceStrategiesAsync` once every slot's
strategy comes from the composer's own capability-filtered menu.

Operator decision, in their words: **face IS identity**; a step may carry **face only, body only, or face and body**
("it actually can be both … like it currently does in the Body Cells rotations generations"); identity packs hold faces
AND bodies across the 5 angles, with more granular and horizontal views to come; and a host that does NOT pass in a
known cast must let the operator pick **any character that has a pack**.

That resolves the pack-vs-slot question as a non-question: **the pack is the SOURCE, the step's slots are the
mechanism**, so there is one mechanism, not two. What that required, all delivered and tested:

- `IdentityPackReferenceResolver` (pure, static) — the ONE pack-reference decision: newest approved reference for the
  angle, and for a body the angle AND the wardrobe state. No substitution: a miss returns null and the caller refuses.
  Plus `AvailableFaceViews` / `AvailableBodies`, so a picker offers only what a pack can actually serve. The LoRA
  cell's two deciders now DELEGATE to it, which is why their existing tests (and wording) prove the dedup moved no
  behaviour.
- `ImageStep/IdentityPackSlotPrefill` — blueprint + pack + request → bindings in frame order. `Request.FaceOnly`,
  `BodyOnly`, `FaceAndBody` are the three shapes as named factories. A requested element with no declared slot is
  REFUSED by name, because the planner drops undeclared assignments silently.
- `ImageStepReferenceSourceKind.IdentityPackAsset` + `IdentityPackId`/`ReferenceAssetId` on a binding. A distinct
  source from `ApprovedSceneAsset`, not an alias: pack images are `SceneImageReferenceAsset` rows whose ids are only
  meaningful inside their pack.
- `ICharacterImageIdentityRepository.ListApprovedPacksAsync()` + `ICharacterImageIdentityService.ListPackOwnersAsync()`
  + `IdentityPackOwner` — every character that has an approved pack, newest pack, its approved assets, and the face
  views / body states it can serve. Pack-store-driven on purpose: the store is the truth about who can be rendered, so a
  character who has a pack but is not in the current scenario is still offered. Names come from the ONE owner-resolution
  path; an unresolvable NAME falls back to the id and logs, because a label is not an eligibility rule.
- `Components/Shared/IdentityPackPicker.razor` — the two-level picker the composer now offers on a slot that allows an
  identity-pack source: the character (settled by the slot's actor when the step knows one, otherwise chosen from the
  characters that have packs) and then the angles THAT pack can serve, each resolved through the one pack decision.
- `IdentityPackReferenceLabels` — one owner for the operator-facing axis names, which had already been duplicated as
  private helpers in two surfaces.

**What is left for B130-011/012** is therefore wiring, not design: give the page a character choice (a known cast, or
the pack-character roster), call `IdentityPackSlotPrefill` to ASSIGN the step's bindings, and delete the page's own pack
selector + 'apply identity' checkbox plus the element-key consumers around it.

**B130-013 evidence.** The route was an alias: `OnParametersSetAsync` redirects to the production workspace, and
nothing on the page ever loaded the production group it displayed, so its model select, prompt box, identity block,
compose button and attempts list were unreachable. It was a seventh copy of one layout. The copy is deleted, the
route is kept (existing links and the browser back-stack use it), and the page declares only its parameters and the
forward. **Evidence:** build 0 errors; 327/327 targeted tests green.

**B130-016 evidence.** `PromptAssetCreator` now presents its step through the composer, with image size, output count,
the render badge and the queued-outputs notice as `ChildContent`. `ImageStepBlueprintFactory.ForAssetCreate(subject)`
was corrected while migrating: it declared one Location slot and would have DROPPED the face/body/wardrobe elements
the surface already offered, so it now declares them addressed to the asset's own character when the asset has one
(`AssetStudioView` passes `SubjectActor()`), and only the frame-wide slot otherwise. Its hand-written prompt means
the prompt panel's Generate/Refine buttons are now hidden when a host has no such path — a button that does nothing
claims a capability the surface does not have. **Evidence:** build 0 errors; 127/127 targeted tests green.

**B130-015 evidence.** The pose test now renders through the composer. `TestModelChoices` lists ONLY pose-capable
models (the previous select rendered an option per model and greyed out the incapable ones — a control the operator
can never satisfy pretending to be a choice), carrying the resolver's own `Mechanism` as the choice's qualified
strategy. The selected preset is seeded into the pose slot, so it is pre-filled but not fixed; `CanTestPose()` then
requires the slot to still point at the tested pose's OWN skeleton, and `TestBlockedReason()` names the mismatch
instead of quietly rendering a different artifact. **Evidence:** build 0 errors; 327/327 targeted tests green.

**Two composer capabilities were added because these hosts needed them** (both apply to every host):

1. `SeedBindings` — bindings the HOST resolved from its own rule, applied to slots the operator has not filled.
   The merge is idempotent by construction (a seeded binding is present on the next pass), which is what keeps it
   from looping. This is what makes "pre-filled but changeable" real, and it is the mechanism B130-011/012/014 need.
2. `MaxReferences` became **model-derived** (`ModelReferenceLimit`, from the choice's own configured limit) instead
   of host-supplied, so a host cannot get the reference budget wrong; and the slot-list test widened from "does the
   model qualify `NativeMultiReference`" to "can the model carry ANY strategy this step's slots accept, text-only
   excluded". The narrow test would have hidden the slot list from a ControlNet pose route — the second mechanism
   `PoseTestRenderService` supports — while `TextOnly` being in every slot's meaning list would have shown slots on
   a model with no reference route at all (the D3 regression in the other direction).
   `SceneImageModelChoice` gained `MaxReferences`, populated by both listers from
   `QwenImage21ModelSettings.TryResolveReferenceCapacity` (non-throwing on purpose: this is listing data, and one
   malformed row must not take out every picker on a page).

**B130-014 note (resolved).** Its stated behaviour change was that the cell's auto-resolved face and body become
pre-filled slots the operator can change. That is now true, and the conditioning decision MOVED into the bindings rather
than being duplicated: see the evidence above. A layout-only migration was rejected on purpose — it would have left the
service resolving conditioning while the composer offered slots for the same elements, i.e. two mechanisms for one
element in one render.

**B130-017 note (the remaining half is done).** Its strategy-list half was already done (B130-024). The slot half is now
done too: see the evidence above. The workspace derives its cast from the identity targets it already resolves, so
`ForEdit(cast)`/`ForEditElements(cast)` can be wired without inventing a cast step.

---

## P6 — Character pose assets (D8)

### B130-018 — Pose assets live in `SceneAssets` (no new store)
**Decision recorded (operator, 2026-09-25):** a character pose asset IS a `SceneAsset` with a new
`SceneAssetType.CharacterPose`, **appended** to the enum. See `plan.md` §2.3 for the column mapping and
`spec.md` §7 for why.
- Files: `Domain/RolePlay/SceneAssetModels.cs` (appended enum member),
  `Components/Shared/ReferencePicker.razor` (offer the new type),
  `Infrastructure/RolePlay/SceneAssetRepository.cs` if the new value needs filtering support
- **Sub-decision for this task:** “which pose” via `ViewDescriptorJson` (no schema change, filters poorly)
  versus a typed column (filters in SQL, which the state-matched picker needs). Wardrobe/body state already
  exist as `BodyState`/`BodyView`.
- Tests: the new type round-trips through the asset repository; an existing asset row is unaffected;
  `ProductionReconciliationService`'s name parse still succeeds for every pre-existing row.
- **Blocked by B-127** for the “any RP session” guarantee

### B130-019 — Picker source + slot kind
- Files: `SlotSourcePicker.razor`, `ReferenceSourceKind` handling, `StepPromptElementPlan` arm
- Content: `CharacterPose` slot subsumes appearance + build + clothing + position

### B130-020 — Library build (the batch, gated by a blueprint flag)
- Files: new `Application/RolePlay/ImageStep/CharacterPoseLibraryService.cs`,
  `PoseLibraryPage.razor` entry point
- Constraints: honours CASE-22's ≤6 references; state-matched wardrobe/body; `AllowsBatch` is blueprint data;
  a generated asset is `Draft` until explicitly approved

---

## P7 — Region and panorama (CASE-21 mechanism)

### B130-021 — Region edit as a step operation
- Files: `MediaEditOperationKind` (+ a region kind), `MediaEditOperations.cs`,
  `ComfyUIImageEditingClient.BuildQwenImage21EditWorkflow` (or a sibling builder using `VAEEncodeForInpaint`),
  `MediaEditImageEditingJobHandler.ReadOperation`, both `MediaEditSubjectWriter`s, DI (executors are **scoped**)
- ⚠️ Four call sites must branch on `Kind != Edit` (`media-edit-operations-crop-enhance.md`); the scene-image
  completion guard needs the operation variant.
- Constraint: a bare rectangle leaves a **visible seam** — ship with a soft/feathered region.

### B130-022 — Panorama / outpaint
- Files: same builder, mask at the frame edge; location-view persistence

---

## Cross-cutting

- **B130-023** Structural guard test: no composer surface hardcodes a per-element strategy list. — ✅ DONE 2026-09-25
- **B130-024** Replace every `Registry`-style hardcoded strategy constant with capability lookups
  (`ReferenceStrategyResolver.ListAvailableStrategies`), including the `EditorReferenceStrategies` list in
  `ImageEditWorkspace.razor`. — ✅ DONE 2026-09-25
- **B130-025** Update `B-129-qwen-2-1-default-composer-editor.md` §8 to point here, and restore its original
  phasing text (it was rewritten to put an evidence program first). — ✅ DONE (verified 2026-09-25)

**B130-025 evidence.** `B-129` §8 already carries the normal phasing list (1..7), points at this item for the UI half
of U2–U5 ("task-ordered in `B-130-unified-image-step-composer/` (tasks.md B130-001..B130-017) rather than here — that
item owns the composer surfaces"), and states plainly that the evidence program is **not** a gate on the phasing but the
parallel "not yet measured" track in §2.1. Nothing was left to rewrite.

**B130-023/024 delivered:** new `Application/RolePlay/ReferenceStrategyCatalogue.cs` is now the ONE owner of
"what each reference element means" (`TryForElementKey`, `ForSlotKind`, `Intersect`); `ReferenceApplyPanel.razor`
and `ImageStepComposer.razor` both read it instead of keeping private copies that had drifted apart (the panel's
`Location` arm listed only ControlNet; the composer's default arm handed `Location` and `Wardrobe` the face set).
`ImageEditorModelResolver.ListImageEditorModelsAsync` now populates `QualifiedStrategies` from the same
`ReferenceStrategyResolver.ListAvailableStrategies` decision the render makes, and `ImageEditWorkspace.razor`
reports `SelectedEditorStrategies` from the selected choice instead of a page constant.
`SceneImageCompose.razor`'s "what will be submitted" panel no longer claims `ReferenceConditioning (IP-Adapter)`
for a native-reference model.

**A pre-existing test was pinning the defect:**
`SceneImageEditor_AutomaticallyAnalyzesSourceAndExposesNativeQwenReferences` asserted the literal
`EditorReferenceStrategies = ["TextOnly", "NativeMultiReference"]`. It now asserts the model-derived contract
instead. **Evidence:** build 0 errors; **186/186** targeted tests green, including the new
`ReferenceStrategyCatalogueTests` guard that reads all eight image-step surface sources.
