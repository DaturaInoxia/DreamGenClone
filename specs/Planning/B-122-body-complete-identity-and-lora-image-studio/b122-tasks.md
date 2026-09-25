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

- [x] **B122-000a** Confirm B-124 is complete: `SceneImageReferenceBodyView`,
  `SceneImageReferenceBodyState`, `ViewDescriptorJson`, `BodyView`, `BodyState`, `PackScope` and
  `CanonicalFullBodyAssetId` round-trip through both asset stores; record exact symbols and tests.
  *Done 2026-09-21* — all symbols present; round-trip verified in `CharacterImageIdentityRepository`
  (CREATE + ALTER + INSERT + SELECT ordinals 16/17/18, supersede copy, `ValidateAsset` contract). The three
  fields are **insert-time only** (immutable after upload — no UPDATE path). `SceneAssets` carries a parallel,
  unpopulated copy that Phase 0 must not write. **No test covers the body/descriptor round-trip or any
  `PackScope` value today.** See `phase0-prerequisite-verification.md` §B122-000a.
- [x] **B122-000b** Confirm B-121 is complete: target-kind registration, resumable build/step store,
  prompt-template/settings resolver, shared same-image edit primitive, validation capability and
  descriptor-aware promotion; record exact service methods. Stop if any requires a fork.
  *Done 2026-09-21* — machinery reusable as-is (signatures recorded). Three body-specific pieces are MISSING
  and land inside Phase 0: no `Body` step plan is seeded, no body handler keys exist, and template keys are
  hardcoded face consts (the plan's `TemplateKey` is read by no production handler). The angle service is typed
  to `CharacterIdentityAngleView` (face), so body views need the axis plumbed rather than a second service.
  Nothing requires a fork. See §B122-000b.
- [x] **B122-000c** Confirm how existing `CharacterBodyProfileVersion` / body descriptors relate to
  the identity pack. Record the single owner for `BodyCard`; do not create two mutable sources.
  *Decided 2026-09-21 (user choice):* a **new dedicated, character-owned typed BodyCard** — table
  `CharacterBodyCards` (PK `CharacterProfileId`) + `ICharacterBodyCardRepository`. `BodyCard` existed nowhere in
  code before this. Rejected: typing the pack's opaque `DescriptorSnapshotJson` (would be per-pack and could not
  exist before a pack) and reviving the production-dead `CharacterBodyProfileVersion`/`CharacterBodyAssetBinding`
  aggregate (right shape, wrong purpose — a generic appearance-version snapshot, reachable only from its own
  tests plus a `ProductionMediaRepository` FK). Neither of those stores is written by Phase 0, so there is
  exactly one mutable body source. See §B122-000c and `debug/050`.
- [x] **B122-000d** Read the current identity-pack approval/supersede API and record the exact change
  needed to promote a `FaceOnly` draft or superseding version to `BodyComplete` without mutating an
  approved pack in place.
  *Done 2026-09-21* — `ValidateApprovalSet` **already implements the entire `BodyComplete` contract** (5×2 body
  matrix, `CanonicalFullBodyAssetId` presence, unclothed-`Front` check) and is dead only because of the early
  return at `CharacterImageIdentityRepository.cs:182` plus the fact that nothing sets `PackScope`/
  `CanonicalFullBodyAssetId` (drafts are always `FaceOnly`; `ApproveAsync` cannot set them). Change: explicit
  scope at draft creation + set scope/canonical pointer on the draft before approval; supersede already carries
  both. See §B122-000d.

## A. BodyCard and persisted configuration

- [x] **B122-001** Add the canonical `BodyCard` fields from `plan.md` Phase 0: shape, height/build,
  skin, body hair, tattoos with exact placement, scars/marks and grooming. Every required `[DECIDE]`
  value must be resolved before body-reference generation can begin.
  *Done 2026-09-21* — `CharacterBodyCard` + `CharacterBodyCardFields` (label + `RequiresDecision` per field;
  the `[DECIDE]` items are body hair, tattoos, grooming). `RequireReadyForGeneration()` fails fast naming every
  unanswered field, and `ToPromptLine()` refuses to render while incomplete; there is no "empty means none".
- [x] **B122-002** Persist `BodyCard` on the single owner established by B122-000c, with optimistic
  concurrency/version behavior matching the existing character/identity aggregate.
  *Done 2026-09-21* — new `CharacterBodyCards` table (PK `CharacterProfileId`) + `ICharacterBodyCardRepository`;
  `SaveAsync(card, expectedVersion)` creates at 0 and otherwise updates `WHERE Version = $expectedVersion`,
  throwing with both versions named on a mismatch (and refusing a duplicate create or an update-without-card).
  Schema is ensured at startup; verified live in the dev DB.
- [x] **B122-003** Seed body-target prompt templates and behavior settings into B-121's existing
  stores under a body target namespace. Include clothed acquisition, unclothed acquisition,
  canonical-angle creation, extended rotation/position creation and body-normalization edit keys.
  No prompt or behavior default may be embedded in code.
  *Done 2026-09-21* — eight `identity.body.*` keys seeded into the ONE `ImageWorkflowPromptTemplates` store
  (clothed/unclothed acquire, the four canonical angle keys, extended rotation, normalize) with the card line as
  `{BodyCard}`; new nullable `ReferenceWorkflowSettings.BodyModelId` (column + ALTER guard + SELECT + writer),
  left unset by the seed on purpose so a body action fails fast naming it rather than reusing a face model.
- [x] **B122-004 [P]** Test BodyCard validation/round-trip/concurrency, unresolved `[DECIDE]`
  rejection, template resolution/override/reset, and fail-fast diagnostics for every missing setting.
  *Done 2026-09-21* — `CharacterBodyCardTests` (14): field order, the `[DECIDE]` set, the fail-fast message per
  unanswered field, the exact canonical line, create/update/round-trip, stale-version refusal with the stored
  card untouched, duplicate create, update-without-card, unknown character, all eight seeds resolvable with
  seed-equals-body, character override + reset, and body/face key coexistence. 116 green on the
  identity/workflow filter. See `specs/001-rp-prompt-redesign/debug/050-b122-phase0-section-a-bodycard.md`.

## B. Body target in the shared pipeline

- [x] **B122-005** Register `CharacterBody` as a B-121 pipeline target kind. Its configured step set
  is: select/acquire base → validate base → create one requested canonical/extended view → validate
  view → promote. Reuse the existing build and per-step records unchanged.
  *Done 2026-09-22* — the Body plan is seeded into `CharacterIdentityStepPlans` (Kind=2): `Front` (front.body),
  `Validate` (validate.body.base), `Angles` (angle.body), `ValidateView` (validate.body.view, a new step member
  the face plan does not use), `Promote` (promote.bodyPack). Same build rows, same step records, same repository;
  the plan is ensured at startup so both plans are in the database from boot. Verified live.
- [x] **B122-006** Add upload and configured-model generation for a clothed `Front` base. Both routes
  converge on the same artifact shape with explicit `BodyView=Front`, `BodyState=Clothed` and a body
  descriptor.
  *Done 2026-09-22* — `CharacterIdentityBodyService.GenerateAsync` (configured model + explicit image size) and
  `UploadAsync` both write a `CharacterIdentityBodyView` row with `State=Clothed`, `View=Front` and the resolved
  prompt/model; the request key itself is the descriptor (state + canonical slot), stored as columns rather than
  a JSON blob. One request = one image, in the body container.
- [x] **B122-007** Add upload and configured same-image edit/generation for the unclothed `Front`
  base. It must reuse B-121's edit primitive and store explicit `BodyState=Unclothed`; the workflow
  does not invent or alter anatomy beyond the selected model's output.
  *Done 2026-09-22* — the unclothed base is an edit of the ACCEPTED clothed base through the shared
  `ISceneAssetService.EnqueueImageEditAsync`, or an upload/generation; it records `State=Unclothed`,
  `View=Front`. The clothed base is the root: asking to edit it is refused naming that a base is generated.
- [x] **B122-008** Add the per-view action for canonical 3/4-left/right and profile-left/right in
  either explicit body state. One click/request creates one view. Store `BodyView`, `BodyState` and
  descriptor data; never run an angle or state sweep.
  *Done 2026-09-22* — `EditFromAcceptedSourceAsync` resolves the parent: the three-quarters come off the accepted
  front base in the same state, a profile off the accepted three-quarter on its own side (right profile waits on
  the right three-quarter — asserted by test). Missing/unaccepted parent fails fast naming it. No sweep type
  exists anywhere in the surface.
- [x] **B122-009** Add the per-view action for extended body rotations/positions. Store free
  `BodyRotationDeg`/`BodyPositionKey`, explicit `BodyState`, and no canonical `BodyView`.
  *Done 2026-09-22* — extended keys are `(State, rotation, positionKey)`; `CharacterIdentityBodyViewKey.Validate`
  refuses a canonical view that also carries a rotation, an extended view without a rotation or position, and a
  rotation outside −180…180 — so a stored row can never be ambiguous about which slot it fills. Test asserts a
  stored extended row keeps `View = null`, its rotation and its position key alongside the base row.
- [ ] **B122-010 [P]** Test target registration without pipeline forking, resume/re-run behavior,
  one-output-per-request, explicit state/view metadata, and rejection of missing or axis-invalid
  metadata.
  **PARTIAL** — `CharacterIdentityBodyServiceTests` (13) covers registration without a fork (the plan test),
  one-output-per-request, explicit state/view metadata, the full derivation chain with its refusals, axis
  validation (4 theory cases) and the base accept completing the acquisition step. **Deliberate difference to
  record:** a body view keeps ONE row per request whose output is replaced on re-run (the images stay in the
  container as history), instead of the face pipeline's per-attempt rows — B-122 does not ask for a body attempt
  table. A re-run test asserting that shape is still to be written.

## C. Body-reference validation

- [x] **B122-011** Persist per-view body-invariant findings: body shape/proportion consistency,
  tattoo/mark presence and exact placement, skin/body-hair/grooming consistency, anatomy review and
  reviewer attribution/time. Keep unsupported judgments manual; do not invent an automated detector.
  *Done 2026-09-22* — `CharacterIdentityBodyCheck` (four checks, each with a `Pass`/`Fail`/`NotReviewed`
  verdict) is stored on the view's own row with a note, the reviewer's name and the review time. No detector was
  invented: the anatomy check is explicitly the reviewer's judgement.
- [x] **B122-012** Reuse B-121's shared reference-quality capability where applicable. Body-specific
  findings extend that result and do not alter or duplicate the eye/face-landmark implementation.
  *Done 2026-09-22* — `AnalyzeQualityAsync` runs the ONE `IReferenceImageQualityAnalyzer` over the view's image
  and stores its rating + notes on the view (test asserts the stored values equal the analyzer's own output for
  the same bytes). It reports only; it never accepts or refuses. The eye/landmark path is untouched.
- [x] **B122-013** Block view acceptance when required findings are missing or failed. A manual
  override must be explicit, attributed, reasoned and persisted; there is no silent pass.
  *Done 2026-09-22* — `AcceptAsync` refuses until every check is a recorded pass, naming the view and each
  missing/failed check (an unreviewed check reads "(not reviewed)"), and the message states the override route.
  A persisted override with reason + author (both required) defers the gate; silence never does.
- [x] **B122-014 [P]** Test failed/missing findings, explicit overrides, state-to-state invariant
  checks and exact diagnostics naming the affected view/state.
  *Done 2026-09-22* — `CharacterIdentityBodyServiceTests` (20 total, 7 new): the never-reviewed refusal naming
  all four checks; a single failed check refused with the others not mentioned; findings attributed +
  time-stamped and isolated per view AND per state (clothed vs unclothed front are different slots); a reviewer is
  required; an explicit attributed override accepts a failed view and is persisted; the quality result matches the
  shared analyzer. Run: identity/reference/prompt/edit filter → **262 passed / 0 failed**.

## D. Promotion to a body-complete pack

- [x] **B122-015** Extend the shared descriptor-aware promotion path for full-body assets. It must
  reuse B-121's service and B-124's schema, not introduce another promotion service.
  *Done 2026-09-22* — still the ONE `CharacterIdentityPromotionService`, now dispatching on `build.TargetKind`
  (`GetFaceReadinessAsync` / `GetBodyReadinessAsync`, `PromoteFaceAsync` / `PromoteBodyAsync`). No second service,
  no second pack store: body slots are written through `ICharacterImageIdentityService` with `BodyState`+`BodyView`.
- [x] **B122-016** Promote only into a draft/superseding pack with explicit
  `PackScope=BodyComplete`. Require five accepted canonical face slots plus matching accepted clothed
  and unclothed five-slot body sets. Set `CanonicalFullBodyAssetId` to the approved unclothed
  `Front`; never mutate an already approved `FaceOnly` pack in place.
  *Done 2026-09-22* — `CreateDraftPackAsync` now takes a **required** scope (closing the B121-031 default note) and
  never narrows; `SetDraftPackScopeAsync` is the single widening path and accepts **drafts only** (an approved pack
  is refused, naming supersede). The target is always an existing draft; without one the readiness says to promote
  the face build or supersede. The pointer is the uploaded unclothed `Front` asset, write-guarded to be a
  `FullBody` unclothed `Front` of that pack.
- [x] **B122-017** Fail fast with the complete list of missing/invalid face slots, body states, body
  slots, validation findings or canonical pointers. B-123 must be able to query one unambiguous
  approved `BodyComplete` pack.
  *Done 2026-09-22* — one reason per problem, each naming its slot: the 5 face slots (missing vs present-but-
  not-approved, said separately), the 10 body slots (never started / not accepted with the **findings quoted** /
  accepted without an image), and a stored canonical pointer that is not this pack's unclothed `Front`. Supersede
  refuses a pointer the pack does not own rather than carrying it forward, and `GetLatestApprovedPackAsync`
  resolves the approved `BodyComplete` pack for B-123.
- [x] **B122-018 [P]** Test successful promotion, missing slot/state rejection, wrong canonical-body
  pointer rejection, superseding-version behavior, and preservation of the prior approved pack.
  *Done 2026-09-22* — `CharacterIdentityBodyPromotionTests` (8) runs on the REAL pack store and approval contract,
  so `ValidateApprovalSet` decides: promote → 10 tagged body assets → the pack approves → the body build reaches
  `Complete`; the ten slots named when never started; the face half named two ways; no draft refused; findings
  quoted; refusal writes nothing; a clothed pointer refused at approval; supersede preserves the prior pack and
  the new draft approves without re-promoting. Plus `CharacterImageIdentityRepositoryTests` +2 (pointer follows the
  copy; a foreign pointer is refused), `CharacterImageIdentityServiceTests` +6 (scope/widening/narrowing guards),
  and the face promotion now asserts it creates `FaceOnly`. Runs: identity/reference/scene-asset filter →
  **301 passed / 0 failed**; body filter → 83.

## E. User-facing tool

- [x] **B122-019** Add the BodyCard editor and body-target entry action inside the B-121 Identity
  Studio/Asset Manager surface. Follow the repository Razor instructions before editing.
  *Done 2026-09-22 (E-1, `debug/056`)* — the Body tab holds the seven-field card editor (labels and `[DECIDE]`
  marks read from `CharacterBodyCardFields`, readiness shown in the card's own words, save through the new
  `ICharacterIdentityBodyService.SaveBodyCardAsync` under the loaded version) and the body-build entry action
  (`CharacterIdentityTargetKind.Body`). The studio now selects the face build and the body build **by kind** — the
  old `_build = builds.FirstOrDefault()` would have rendered the body plan under the Faces tab. Verified live in
  the browser on the dev DB.
- [x] **B122-020** Add clothed/unclothed view grids showing canonical requirements, extended views,
  validation state, attempt history and one-view create/edit/upload/re-run actions.
  *Done 2026-09-23/24 (E-2, `debug/063`, `debug/064`, `debug/065`, `debug/068`)* — `BodyViewsPanel` renders the
  canonical slots of both states with status, findings verdicts, candidate deck (accept/undecide/delete),
  per-view prompt length against the 800-char ceiling, the accepted-base + skeleton + identity-face preview of what
  a render sends, and one-view actions only (Generate / Render angle / Edit from accepted source / Review deck /
  Edit image). **The canonical set is 6 views per state, not 5**: the operator added a **Back** view (full back,
  no face, `angle-back.png` skeleton, `identity.body.angle.render.back` clause, `CharacterBodyWorkflowKeys.RenderBack`),
  so a `BodyComplete` pack is 12 body + 5 face = 17 assets. Extended views are refused by name rather than rendered .
- [x] **B122-021** Add promotion readiness with exact missing-item diagnostics. Do not expose a batch,
  sweep or "generate all" action.
  *Done 2026-09-24 (E-3, `debug/068`)* — the Body tab renders its own **Promote the body set to a BodyComplete pack**
  panel from `PromotionService.GetReadinessAsync(_bodyBuild.Id)`: one card per slot (5 face slots of the target draft
  + the 12 body slots) with Ready/Blocked, and every unmet requirement listed verbatim as its own line. The panel is
  gated by **readiness, not by the build's step index** — the body plan's remaining steps (Validate → Angles →
  ValidateView → Promote) are completed *by* the promotion, so the old `CurrentStep >= Promote` gate was circular and
  could never open; that guard is now face-only, and a source-contract test forbids it in the body path. The body
  section still exposes no batch/sweep/"generate all" action.
- [ ] **B122-022 [P]** Add source-contract/component tests and run Razor diagnostics for every
  touched component.
  **PARTIAL** — `CharacterStudioBodyContractTests` (10) covers the Body tab's structural invariants, the gender
  lookup, the male catalogs and (as of E-3) the promotion panel's readiness gating and its wiring to the body build;
  `CharacterIdentityBodyViewsPanelTests` (19) covers the view grid; `CharacterIdentityBodyServiceTests` and
  `CharacterIdentityBodyPromotionTests` (8) cover the service and the promotion. Run Razor diagnostics on each touched
  component before closing this item.

## F. Validation and handoff

- [ ] **B122-023** Run focused domain/repository/service/UI tests, then the affected test project;
  record commands and exact counts with all tests green.
- [ ] **B122-024** Run a live body build for one real character: resolve BodyCard, create/review each
  clothed and unclothed canonical view individually, add one extended rotation/position, promote a
  superseding `BodyComplete` pack, and verify it groups correctly in Asset Manager. Record build,
  pack and asset ids; no hand-run scripts or direct DB writes.
- [ ] **B122-025** Record grep/source proofs that there is one pipeline, one template store, one edit
  primitive, one promotion path, no batch action and no inferred/default body state.
- [ ] **B122-026** Expose the body render's remaining settings on a UI-backed surface: `AngleYawMinAbsPercent`
  and `QualityGateMinSharpness` are read from `ReferenceWorkflowSettings` for every body render
  (`CharacterIdentityBodyService`), but no screen edits them — the same class of hidden-configuration gap the
  no-fallback rule forbids. `BodyModelId` / `BodyImageSize` already have a settings card in the Body tab.

## Definition of done

B-122 is complete only when a user can produce and approve a `BodyComplete` pack entirely through
the UI, B-123 can resolve it without inference or fallback, all required canonical face/body assets
and states are present, the live evidence is recorded, and all affected tests are green.