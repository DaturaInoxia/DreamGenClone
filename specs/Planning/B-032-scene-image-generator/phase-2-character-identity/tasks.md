# Phase 2 Tasks - Character Identity

**Execution rule:** Complete in order unless marked `[P]`. Check a task only after its tests and
evidence are recorded.

**Prerequisite:** Record the Phase 1B vision-aware editing exit gate before starting P2-001.

## A. Persistence and Assets

- [ ] P2-001 Add identity enums and records in `DreamGenClone.Domain/RolePlay`.
- [ ] P2-002 Add repository interfaces in `DreamGenClone.Application/Abstractions`.
- [ ] P2-003 Add identity/reference/evaluation schema and indexes in
  `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`.
- [ ] P2-004 Implement identity repository, immutable version rules, and in-use delete guards in
  `DreamGenClone.Infrastructure/RolePlay`.
- [ ] P2-005 Extend safe scene-image asset storage for reference ingest, metadata, checksums, and
  reference-aware deletion.
- [ ] P2-006 [P] Add repository, migration, path safety, checksum, and approval validation tests.

## B. Identity Pack UI

- [ ] P2-007 Read the Razor instruction and full target component context; select the existing
  character-profile management surface or add a narrowly scoped identity page.
- [ ] P2-008 Add upload, asset-kind, provenance, consent, canonical-face, and approval controls.
- [ ] P2-009 Add pack version history, supersede action, and referenced-asset delete diagnostics.
- [ ] P2-010 [P] Add service tests and Razor diagnostics for the curation flow.

## C. Conditioning Proof

- [ ] P2-011 Inventory the current isolated/production ComfyUI environments and storage; do not
  modify either host.
- [ ] P2-012 Record candidate IP-Adapter and PuLID node/model revisions, licenses, dependency delta,
  artifact sizes/hashes, and a forward-fix recovery plan.
- [ ] P2-013 Obtain explicit approval, install candidates in an isolated runtime, and verify node
  discovery without modifying the production endpoint.
- [ ] P2-014 Freeze two approved identity packs, six composition cells, two seeds per cell, prompts,
  regions, workflows, and score manifest.
- [ ] P2-015 Run each candidate exactly once over the 12 cases and persist outputs/scorecards.
- [ ] P2-016 Select one mechanism only if it meets the gate; otherwise stop and record the closest
  failed constraints before proposing another mechanism.

## D. Model Resolution and Client

- [ ] P2-017 Add selected mechanism fields to the registered image model and Model Manager forms;
  all values are persisted and required.
- [ ] P2-018 Add `ResolvedIdentityImageModel` and one strict resolver with checkpoint/capability
  compatibility checks.
- [ ] P2-019 Add `IIdentityConditionedImageClient` and controlled request/result DTOs.
- [ ] P2-020 Implement the selected API-format ComfyUI workflow using the frozen proof as a fixture.
- [ ] P2-021 [P] Add resolver failure tests and byte-for-structure workflow JSON tests.

## E. Controlled Render Slice

- [ ] P2-022 Add immutable render-attempt and actor-assignment persistence.
- [ ] P2-023 Add an identity request compiler that requires exact approved pack versions and
  non-overlapping regions for multiple actors.
- [ ] P2-024 Add background job type, payload, handler, dedupe, statuses, logs, and debug events.
- [ ] P2-025 Add service enqueue validation; missing packs/regions/profile fail before record creation.
- [ ] P2-026 Add an explicit `Identity controlled` Studio action and provenance display without
  changing the existing prompt-only action.
- [ ] P2-027 [P] Add compiler ownership, handler idempotency/failure, and provenance tests.

## F. Matrix and LoRA Decision

- [ ] P2-028 Add matrix result persistence/reporting and manual scoring controls.
- [ ] P2-029 Execute the application path against all frozen cases and compare submitted provenance
  to the standalone proof.
- [ ] P2-030 Record `NotRequired`, `Required`, or `Deferred` with evidence.
- [ ] P2-031 If and only if `Required`, create an approved LoRA sub-plan and dataset manifest before
  any training; evaluate the artifact with the same matrix.
- [ ] P2-032 Run affected tests, solution build, full test suite, and record the manual exit gate.
