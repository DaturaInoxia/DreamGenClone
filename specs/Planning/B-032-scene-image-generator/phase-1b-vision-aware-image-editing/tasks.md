# Phase 1B Tasks - Vision-Aware Image Editing

**Execution rule:** Complete in order unless marked `[P]`. Check a task only after its tests and
evidence are recorded. Pod deletion tasks require the exact preflight in the migration runbook.

## A. Host Inventory and Candidate Freeze

- [X] P1B-001 Inventory `/workspace`, container disk, model caches, active processes, queues, open
  model files, free bytes, and GPU memory; persist sanitized output under the phase proof folder.
  Evidence: `proofs/pod-inventory-2026-08-25.md`.
- [X] P1B-002 Identify the exact Pony checkpoint path, byte count, SHA-256, workflow references,
  and Model Manager registrations; record the retirement manifest.
  Evidence: `proofs/pod-inventory-2026-08-25.md`; live defaults resolve image generation to
  Juggernaut and editing to Qwen, with no active Pony default.
- [X] P1B-003 Freeze the Qwen VL candidate, repository revision, license, expected artifact files,
  sizes/hashes, vLLM revision, Python/CUDA compatibility, launch arguments, and endpoint contract.
  Evidence: `proofs/qwen-vl-candidate-manifest-2026-08-25.md`.
- [X] P1B-004 Define minimum post-install free disk, compile peak VRAM headroom, source image limit,
  cold-start limit, and runtime-switch limit; no code defaults.
  Evidence: `proofs/one-pod-runtime-thresholds-2026-08-25.md`.
- [X] P1B-005 Confirm the one-pod/one-volume topology and select simultaneous residency or explicit
  same-pod model load/unload scheduling only from measured evidence.
  Evidence: `proofs/one-pod-runtime-thresholds-2026-08-25.md`; same-pod scheduled GPU residency
  is selected for the initial proof.

## B. Standalone Vision Proof

- [X] P1B-006 Add pinned provision/start/stop/health scripts and a non-destructive inventory script.
  Evidence: `helpers/runpod/qwen-vl-edit-compiler/`.
- [ ] P1B-007 Provision the candidate in a separate runtime directory on the same pod without
  modifying Qwen Edit; verify hashes and OpenAI-compatible image request support.
  Failure evidence: `proofs/qwen-vl-provision-attempt-2026-08-25.md`.
- [ ] P1B-008 Freeze the compilation corpus, expected statuses, source checksums, and scoring rubric.
- [ ] P1B-009 Run the corpus once with fixed settings; record raw/parsed output, latency, peak VRAM,
  refusal behavior, and scores.
- [ ] P1B-010 Accept the candidate only if schema validity, target accuracy, ambiguity precision,
  invention, and content-policy gates pass; otherwise stop and record failure.

## C. Persistence and Domain

- [ ] P1B-011 Add edit session, compilation attempt/result, target region, status, and revision types.
- [ ] P1B-012 Add compiler and reserved validator functions plus multimodal capability/config fields.
- [ ] P1B-013 Extend scene image edit provenance additively.
- [ ] P1B-014 Add SQLite tables, columns, indexes, and delete guards.
- [ ] P1B-015 Implement repositories with monotonic state, ordinal, checksum, and staleness rules.
- [ ] P1B-016 [P] Add migration, repository, revision, staleness, and referential-integrity tests.

## D. Multimodal Model Infrastructure

- [ ] P1B-017 Add multimodal request/result/client and model-resolution abstractions.
- [ ] P1B-018 Implement the selected OpenAI-compatible multimodal request with image-size validation,
  schema constraint, cancellation, timeout, and binary-safe diagnostics.
- [ ] P1B-019 Add strict compiler-model resolution and health check with one active source path.
- [ ] P1B-020 Add Model Manager controls for image capability, limits, compiler assignment, content
  policy, and every required generation value.
- [ ] P1B-021 [P] Add resolver no-fallback, request JSON, response, timeout, limit, and log-safety tests.

## E. Compiler and Jobs

- [ ] P1B-022 Implement versioned Qwen edit compiler messages and strict parser.
- [ ] P1B-023 Add compilation request/result validation and clarification contract.
- [ ] P1B-024 Add background job type, payload, handler, dedupe, states, and structured debug events.
- [ ] P1B-025 Add orchestration for create, compile, clarify, revise, and stale invalidation.
- [ ] P1B-026 Require exact accepted prompt revision and source checksum in Qwen edit enqueue.
- [ ] P1B-027 Remove the new-edit raw pass-through decision path while preserving historical reads.
- [ ] P1B-028 [P] Add parser corpus, prompt contract, handler, idempotency, failure, and enqueue tests.

## F. Dedicated Editor

- [ ] P1B-029 Read Razor instructions and full Studio/gallery/style context before editing.
- [ ] P1B-030 Add the dedicated route and source-image loading/error states.
- [ ] P1B-031 Add intent entry, prepare action, progress polling, grounded summary, and clarification.
- [ ] P1B-032 Add advanced compiled-prompt revision with visible stale/recompile behavior.
- [ ] P1B-033 Add run action, source/result comparison, failures, and lineage history.
- [ ] P1B-034 Replace inline Studio raw editing with navigation and add gallery edit navigation.
- [ ] P1B-035 [P] Run Razor diagnostics and desktop/mobile browser workflow checks.

## G. Pod Migration

- [ ] P1B-036 Stop queues/services and rerun inventory immediately before migration.
- [ ] P1B-037 Remove Pony from active POC Model Manager configuration and prove Juggernaut remains
  the configured text-to-image path where required.
- [ ] P1B-038 Execute exact-path Pony checkpoint retirement with expected hash/size/name confirmation.
- [ ] P1B-039 Verify reclaimed disk, then install and verify the accepted vision runtime artifacts.
- [ ] P1B-040 Configure same-pod private endpoints and the selected GPU residency scheduler; keep
  credentials local and do not create another pod.
- [ ] P1B-041 Run Juggernaut, Qwen Edit, and Qwen VL health checks plus load/unload or co-residency
  checks and disk/VRAM evidence on the one pod.
- [ ] P1B-042 Verify Pony is absent from the active POC and Model Manager while tracked historical
  workflows/evidence remain preserved; reinstallation is outside this plan.

## H. Acceptance

- [ ] P1B-043 Run the frozen compiler corpus through the application and compare standalone hashes,
  schema versions, resolved settings, prompts, and statuses.
- [ ] P1B-044 Run the six accepted non-explicit Qwen intents through compile, review, edit, compare,
  and lineage persistence.
- [ ] P1B-045 Run separate permitted adult-analysis acceptance or record the feature as blocked for
  that policy; never inherit the non-explicit result.
- [ ] P1B-046 Verify exactly one compiler-model source and one edit-prompt execution path, with no
  raw, text-only, cloud, or default fallback.
- [ ] P1B-047 Run affected tests, solution build, full test suite, browser matrix, and record exit
  evidence before unblocking Phase 2.