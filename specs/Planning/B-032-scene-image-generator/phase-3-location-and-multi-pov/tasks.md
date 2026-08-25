# Phase 3 Tasks - Location and Multi-POV

**Prerequisite:** Phase 2 exit gate is recorded.

## A. Location Profiles

- [ ] P3-001 Add location-profile, landmark, reference, status, and provenance records.
- [ ] P3-002 Add repository/storage abstractions, SQLite schema/indexes, and implementation.
- [ ] P3-003 Add draft, approve, supersede, bounds, and in-use-delete rules.
- [ ] P3-004 Add location profile UI with dimensions, landmarks, visual intent, references, and
  exclusions.
- [ ] P3-005 [P] Add repository, versioning, file-integrity, and validation tests.

## B. Canonical Visual Plan

- [ ] P3-006 Add visual-plan, actor, object, relationship, and evidence records using the coordinate
  contract in `data-model.md`.
- [ ] P3-007 Add complete-snapshot repository and additive schema.
- [ ] P3-008 Define supported relationship predicates and deterministic validators.
- [ ] P3-009 Add optional schema-bound draft compiler using exact beat/location/identity versions.
- [ ] P3-010 Add plan review, edit, approve, supersede, and stale-state orchestration.
- [ ] P3-011 [P] Add schema parse, invention rejection, transform, evidence, and version tests.

## C. Three.js Blocking Editor

Pose-specific behavior, contracts, build gates, and acceptance cases for P3-014 through P3-024 are
defined in [`pose-editor-plan.md`](pose-editor-plan.md).

- [ ] P3-012 Read Razor instructions and complete target Razor/JS/CSS context before editing.
- [ ] P3-013 Add Three.js through the repository's package/static-asset strategy with a pinned
  version and recorded license; do not load an unpinned CDN dependency.
- [ ] P3-014 Implement isolated `SceneBlockingEditor` DTO interop and deterministic lifecycle.
- [ ] P3-015 Add floor/landmarks, actor proxies/skeletons, object proxies, camera, selection,
  transform, joint, orbit/pan, undo/redo, and save-as-version controls.
- [ ] P3-016 Add non-overlapping actor-region preview and relationship-anchor visualization.
- [ ] P3-017 Validate reload equivalence and resource disposal.
- [ ] P3-018 [P] Add DTO/serialization tests, Razor diagnostics, and desktop/mobile browser checks.

## D. Shots and Controls

- [ ] P3-019 Add shot-plan and control-asset/manifest records, repositories, schema, and staleness.
- [ ] P3-020 Add camera/crop/visibility authoring and shot freeze/supersede UI.
- [ ] P3-021 Implement preview, depth, pose, semantic-mask, and actor-region exports with stable
  dimensions and canonical colors/keys.
- [ ] P3-022 Implement `ISceneControlCompiler`, canonical manifest serialization, and input hashing.
- [ ] P3-023 Add control compilation background job, dedupe, statuses, logs, and diagnostics.
- [ ] P3-024 [P] Add pixel fixture, hash stability, region overlap, manifest, and stale-state tests.

## E. Spatial-Control Proof and Configuration

- [ ] P3-025 Freeze one approved plan, wide/medium/reverse shots, control assets, identity inputs,
  seeds, and scorecard.
- [ ] P3-026 Build the smallest standalone ComfyUI workflow that combines the selected Phase 2
  identity path with required spatial controls.
- [ ] P3-027 Run fixed cases, record causal control evidence and artifacts, and reject controls that
  do not improve the scored constraints.
- [ ] P3-028 Add only the accepted control profile fields to Model Manager, with exact artifacts,
  weights, start/end values, workflow/node revision, and compatibility.
- [ ] P3-029 Add one strict resolver and health diagnostics with no default or alternate controls.
- [ ] P3-030 [P] Add resolver and pinned-workflow contract tests.

## F. Scene-Controlled Rendering

- [ ] P3-031 Extend controlled request/client with the exact control manifest.
- [ ] P3-032 Add scene-controlled render-attempt provenance and handler path.
- [ ] P3-033 Add explicit Studio plan/shot/control/render workflow and status/error surfaces.
- [ ] P3-034 Enforce stale/missing plan, shot, manifest, identity, and model failures before enqueue.
- [ ] P3-035 [P] Add orchestration, idempotency, provenance, and no-downgrade tests.
- [ ] P3-036 Execute and score wide, medium, and reverse/side application renders from one frozen
  plan.
- [ ] P3-037 Run affected tests, solution build, full suite, browser matrix, and record exit evidence.
