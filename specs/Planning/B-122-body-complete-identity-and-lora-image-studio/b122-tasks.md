# B-122 Tasks — Body-complete character identity pack

**Execution rule:** complete in order unless marked `[P]`. Check a task only after its focused tests
pass and its evidence is recorded. Finish with the affected test project green; fix forward only.

**Scope:** B-122 is only Phase 0 of `plan.md`. B-123 has a separate dispatch list in `tasks.md`.
B-122 extends B-121 through a new pipeline target kind; it does not create another studio, template
store, edit path, eye tool, pose tool or asset schema.

**Authoritative inputs, in order:** `identity-lora-program-map.md` →
`identity-and-reference-model.md` → B-124 `plan.md` → B-121 `spec.md`/`plan.md`/`tasks.md` → this file.

**Hard contracts:** every operation is a user-requested single-view action; no "generate all" or
angle/state sweep exists. All prompts, model ids, thresholds and behavior controls are persisted
configuration. Missing configuration fails fast by key. Full-body assets always carry explicit
`BodyState`; no state or view is inferred from prompt text, filename or pixels.

## 0. Verify prerequisites before writing code

- [ ] **B122-000a** Confirm B-124 is complete: `SceneImageReferenceBodyView`,
  `SceneImageReferenceBodyState`, `ViewDescriptorJson`, `BodyView`, `BodyState`, `PackScope` and
  `CanonicalFullBodyAssetId` round-trip through both asset stores; record exact symbols and tests.
- [ ] **B122-000b** Confirm B-121 is complete: target-kind registration, resumable build/step store,
  prompt-template/settings resolver, shared same-image edit primitive, validation capability and
  descriptor-aware promotion; record exact service methods. Stop if any requires a fork.
- [ ] **B122-000c** Confirm how existing `CharacterBodyProfileVersion` / body descriptors relate to
  the identity pack. Record the single owner for `BodyCard`; do not create two mutable sources.
- [ ] **B122-000d** Read the current identity-pack approval/supersede API and record the exact change
  needed to promote a `FaceOnly` draft or superseding version to `BodyComplete` without mutating an
  approved pack in place.

## A. BodyCard and persisted configuration

- [ ] **B122-001** Add the canonical `BodyCard` fields from `plan.md` Phase 0: shape, height/build,
  skin, body hair, tattoos with exact placement, scars/marks and grooming. Every required `[DECIDE]`
  value must be resolved before body-reference generation can begin.
- [ ] **B122-002** Persist `BodyCard` on the single owner established by B122-000c, with optimistic
  concurrency/version behavior matching the existing character/identity aggregate.
- [ ] **B122-003** Seed body-target prompt templates and behavior settings into B-121's existing
  stores under a body target namespace. Include clothed acquisition, unclothed acquisition,
  canonical-angle creation, extended rotation/position creation and body-normalization edit keys.
  No prompt or behavior default may be embedded in code.
- [ ] **B122-004 [P]** Test BodyCard validation/round-trip/concurrency, unresolved `[DECIDE]`
  rejection, template resolution/override/reset, and fail-fast diagnostics for every missing setting.

## B. Body target in the shared pipeline

- [ ] **B122-005** Register `CharacterBody` as a B-121 pipeline target kind. Its configured step set
  is: select/acquire base → validate base → create one requested canonical/extended view → validate
  view → promote. Reuse the existing build and per-step records unchanged.
- [ ] **B122-006** Add upload and configured-model generation for a clothed `Front` base. Both routes
  converge on the same artifact shape with explicit `BodyView=Front`, `BodyState=Clothed` and a body
  descriptor.
- [ ] **B122-007** Add upload and configured same-image edit/generation for the unclothed `Front`
  base. It must reuse B-121's edit primitive and store explicit `BodyState=Unclothed`; the workflow
  does not invent or alter anatomy beyond the selected model's output.
- [ ] **B122-008** Add the per-view action for canonical 3/4-left/right and profile-left/right in
  either explicit body state. One click/request creates one view. Store `BodyView`, `BodyState` and
  descriptor data; never run an angle or state sweep.
- [ ] **B122-009** Add the per-view action for extended body rotations/positions. Store free
  `BodyRotationDeg`/`BodyPositionKey`, explicit `BodyState`, and no canonical `BodyView`.
- [ ] **B122-010 [P]** Test target registration without pipeline forking, resume/re-run behavior,
  one-output-per-request, explicit state/view metadata, and rejection of missing or axis-invalid
  metadata.

## C. Body-reference validation

- [ ] **B122-011** Persist per-view body-invariant findings: body shape/proportion consistency,
  tattoo/mark presence and exact placement, skin/body-hair/grooming consistency, anatomy review and
  reviewer attribution/time. Keep unsupported judgments manual; do not invent an automated detector.
- [ ] **B122-012** Reuse B-121's shared reference-quality capability where applicable. Body-specific
  findings extend that result and do not alter or duplicate the eye/face-landmark implementation.
- [ ] **B122-013** Block view acceptance when required findings are missing or failed. A manual
  override must be explicit, attributed, reasoned and persisted; there is no silent pass.
- [ ] **B122-014 [P]** Test failed/missing findings, explicit overrides, state-to-state invariant
  checks and exact diagnostics naming the affected view/state.

## D. Promotion to a body-complete pack

- [ ] **B122-015** Extend the shared descriptor-aware promotion path for full-body assets. It must
  reuse B-121's service and B-124's schema, not introduce another promotion service.
- [ ] **B122-016** Promote only into a draft/superseding pack with explicit
  `PackScope=BodyComplete`. Require five accepted canonical face slots plus matching accepted clothed
  and unclothed five-slot body sets. Set `CanonicalFullBodyAssetId` to the approved unclothed
  `Front`; never mutate an already approved `FaceOnly` pack in place.
- [ ] **B122-017** Fail fast with the complete list of missing/invalid face slots, body states, body
  slots, validation findings or canonical pointers. B-123 must be able to query one unambiguous
  approved `BodyComplete` pack.
- [ ] **B122-018 [P]** Test successful promotion, missing slot/state rejection, wrong canonical-body
  pointer rejection, superseding-version behavior, and preservation of the prior approved pack.

## E. User-facing tool

- [ ] **B122-019** Add the BodyCard editor and body-target entry action inside the B-121 Identity
  Studio/Asset Manager surface. Follow the repository Razor instructions before editing.
- [ ] **B122-020** Add clothed/unclothed view grids showing canonical requirements, extended views,
  validation state, attempt history and one-view create/edit/upload/re-run actions.
- [ ] **B122-021** Add promotion readiness with exact missing-item diagnostics. Do not expose a batch,
  sweep or "generate all" action.
- [ ] **B122-022 [P]** Add source-contract/component tests and run Razor diagnostics for every
  touched component.

## F. Validation and handoff

- [ ] **B122-023** Run focused domain/repository/service/UI tests, then the affected test project;
  record commands and exact counts with all tests green.
- [ ] **B122-024** Run a live body build for one real character: resolve BodyCard, create/review each
  clothed and unclothed canonical view individually, add one extended rotation/position, promote a
  superseding `BodyComplete` pack, and verify it groups correctly in Asset Manager. Record build,
  pack and asset ids; no hand-run scripts or direct DB writes.
- [ ] **B122-025** Record grep/source proofs that there is one pipeline, one template store, one edit
  primitive, one promotion path, no batch action and no inferred/default body state.

## Definition of done

B-122 is complete only when a user can produce and approve a `BodyComplete` pack entirely through
the UI, B-123 can resolve it without inference or fallback, all required canonical face/body assets
and states are present, the live evidence is recorded, and all affected tests are green.