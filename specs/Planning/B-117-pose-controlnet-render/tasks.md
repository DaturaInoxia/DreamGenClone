# B-117 Tasks — Pose-controlled composition render

**Execution rule:** B-118 must be complete first. Complete tasks in order unless `[P]`; every task
ends with focused tests green. Fix forward only. Read `plan.md`, the program map, prompt-compiler
standards, model-specific prompting instructions and Razor instructions before matching edits.

**Boundary:** B-117 owns OpenPose ControlNet render wiring and activation of B-118 Apply. It does not
own pose authoring/extraction, B-120's derived store, or B-123's quantitative scorer.

## 0. Verify before writing code

- [ ] **B117-000a** Confirm B-118's persisted `PosePreset` and render-request contract; record exact
  types/methods and one known-good pose fixture.
- [ ] **B117-000b** Confirm B-120's approved OpenPose derived-asset lookup contract or mark that
  source variant blocked until B-120 lands. Do not fabricate a temporary derived store.
- [ ] **B117-000c** Re-probe the configured ComfyUI provider for the exact OpenPose ControlNet and
  preprocessor node inputs plus qualified checkpoint families. Record evidence; do not assume names.

## A. Capability and request contracts

- [ ] **B117-001** Add the Model Manager ControlNet capability/profile fields required by the plan:
  exact weight reference and UI-configured strength constraints for each qualified model. Missing or
  unqualified capability fails fast; no plain-render fallback.
- [ ] **B117-002** Add `PoseControlSource` (`PosePreset` | `DerivedStructureAsset`) and the single-
  render request/provenance fields. Resolve exactly the selected source kind/id and explicit mirror
  transform; never convert or fall back between variants.
- [ ] **B117-003 [P]** Test capability round-trip, invalid/missing settings, wrong provider/family,
  source-kind mismatch and provenance serialization.

## B. Workflow and render route

- [ ] **B117-004** Add the OpenPose ControlNet workflow builder for each explicitly qualified model
  family, using the verified node contract and configured weight/strength. Do not alter unrelated
  generation defaults.
- [ ] **B117-005** Add request validation and the render job route in the existing scene-image
  service/handler path. One request enqueues exactly one image and records source/model/settings.
- [ ] **B117-006** Activate B-118 Apply and expose the same action in the composition/cell caller
  boundary. Unsupported capability remains visibly unavailable and cannot dispatch.
- [ ] **B117-007 [P]** Test graph nodes/connections, exact source bytes, one-image dispatch,
  provenance, retry behavior and no-fallback failures for every unsupported combination.

## C. UI and live qualification

- [ ] **B117-008** Add pose source/model/strength controls using B-118's picker. Strength limits and
  initial value come from persisted capability data, not code defaults.
- [ ] **B117-009 [P]** Add source-contract/component tests, run Razor diagnostics and verify desktop/
  mobile layouts without overflow or control-state shifts.
- [ ] **B117-010** Live-proof each enabled model family with versioned known-good B-118 poses. Record
  request/image ids, workflow snapshot, model/weight/strength and human pose review. A failed family
  stays unqualified; do not weaken criteria or fall back.

## D. Validation and handoff

- [ ] **B117-011** Run focused tests and the affected test project; record exact green counts.
- [ ] **B117-012** Prove B-118 Apply produces one image with exact pose provenance and that the B-123
  caller can submit/keep/discard one attempt through the same route. Quantitative adherence remains
  B-123's later gate.
- [ ] **B117-013** Record source proofs for one OpenPose route, no pose-store duplication, no scorer
  implementation, no batch/sweep and no plain-render fallback.

## Definition of done

B-117 is complete when B-118 Apply and the shared single-render route work with qualified models,
all provenance/configuration is explicit, known-good live proofs are reviewed, and tests are green.