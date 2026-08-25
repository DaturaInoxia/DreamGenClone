# Phase 1B Tasks - Vision-Aware Image Editing

**Execution rule:** Complete in order unless marked `[P]` or authorized by the 2026-08-25
deployment amendment. Check a task only after its tests and evidence are recorded. Pod deletion
tasks require the exact preflight in the migration runbook.

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
  Evidence: `proofs/one-pod-runtime-thresholds-2026-08-25.md`. The old 180-second transition gate
  is superseded for initial deployment by the accepted measured startup envelope in
  `multi-pod-separation-plan.md`.
- [X] P1B-005 Confirm the one-pod/one-volume topology and select simultaneous residency or explicit
  same-pod model load/unload scheduling only from measured evidence.
  Evidence: `proofs/one-pod-runtime-thresholds-2026-08-25.md`; same-pod scheduled GPU residency
  is selected for the initial proof.

## B. Standalone Vision Proof

- [X] P1B-006 Add pinned provision/start/stop/health scripts and a non-destructive inventory script.
  Evidence: `helpers/runpod/qwen-vl-edit-compiler/`.
- [X] P1B-007 Provision the candidate in a separate runtime directory on the same pod without
  modifying Qwen Edit; verify hashes and OpenAI-compatible image request support.
  Accepted by explicit user waiver of the old 180-second transition gate. Functional evidence:
  `proofs/qwen-vl-provision-attempt-2026-08-25.md`; decision and accepted startup envelope:
  `multi-pod-separation-plan.md`.
- [X] P1B-008 Freeze the compilation corpus, expected statuses, source checksums, and scoring rubric.
  Evidence: `compiler-corpus.json` freezes ten deterministic cases, expected terminal statuses,
  exact source byte count and SHA-256, model/schema/prompt identities, request settings, required
  visible-target terms, and forbidden inventions. `compiler-corpus-rubric.md` defines the automatic
  and human acceptance gates. The corpus has not been executed; P1B-009 and P1B-010 remain open.
- [ ] P1B-009 Run the corpus once with fixed settings; record raw/parsed output, latency, peak VRAM,
  refusal behavior, and scores.
- [ ] P1B-010 Accept the candidate only if schema validity, target accuracy, ambiguity precision,
  invention, and content-policy gates pass; otherwise stop and record failure.

**Ordering amendment:** P1B-011 through P1B-035 may begin while P1B-008 through P1B-010 continue.
The open corpus tasks remain blocking gates for production enablement, application end-to-end
acceptance, pod migration completion, and Phase 1B exit. Functional P1B-007 acceptance is not
corpus-quality acceptance.

## C. Persistence and Domain

- [X] P1B-011 Add edit session, compilation attempt/result, target region, status, and revision types.
- [X] P1B-012 Add compiler and reserved validator functions plus multimodal capability/config fields.
- [X] P1B-013 Extend scene image edit provenance additively.
  Evidence: `SceneImageEditDomainTests` (5 passed), full solution build succeeded, and full suite
  passed (1,281 tests) on 2026-08-25.
- [X] P1B-014 Add SQLite tables, columns, indexes, and delete guards.
- [X] P1B-015 Implement repositories with monotonic state, ordinal, checksum, and staleness rules.
- [X] P1B-016 [P] Add migration, repository, revision, staleness, and referential-integrity tests.
  Evidence: `SceneImageEditRepositoryTests` (5 passed), existing scene-image/domain tests
  (14 passed), full solution build succeeded, and full suite passed (1,286 tests) on 2026-08-25.

## D. Multimodal Model Infrastructure

- [X] P1B-017 Add multimodal request/result/client and model-resolution abstractions.
- [X] P1B-018 Implement the selected OpenAI-compatible multimodal request with image-size validation,
  schema constraint, cancellation, timeout, and binary-safe diagnostics.
- [X] P1B-019 Add strict compiler-model resolution and health check with one active source path.
- [X] P1B-020 Add Model Manager controls for image capability, limits, compiler assignment, content
  policy, and every required generation value.
  Evidence: provider and model editors expose the persisted multimodal lifecycle, readiness,
  capacity, security, image-limit, revision, and Qwen generation settings; compiler and validator
  assignments only offer image-input models. Strict UI validation matches the single
  `ModelResolutionService` contract. Desktop and 390x844 browser checks passed on 2026-08-25;
  the mobile document had no horizontal overflow and wide function data remained inside its
  intentional responsive table scroller.
- [X] P1B-021 [P] Add resolver no-fallback, request JSON, response, timeout, limit, and log-safety tests.
  Evidence: focused multimodal transport/resolution tests passed (28 tests), scene-image regression
  tests passed (185 tests), full solution build succeeded, `git diff --check` passed, and the full
  suite passed (1,306 tests) on 2026-08-25. The build retains one pre-existing AngleSharp advisory.

## E. Compiler and Jobs

- [X] P1B-022 Implement versioned Qwen edit compiler messages and strict parser.
- [X] P1B-023 Add compilation request/result validation and clarification contract.
  Evidence: `QwenSceneImageEditPromptCompilerTests` passed (15 tests), scene-image regression tests
  passed (203 tests), and the full suite passed (1,321 tests) on 2026-08-25. The compiler is pure:
  it builds versioned messages and strictly parses terminal output only; model invocation, persistence,
  queueing, and Qwen edit execution remain outside this slice.
- [X] P1B-024 Add background job type, payload, handler, dedupe, states, and structured debug events.
- [X] P1B-025 Add orchestration for create, compile, clarify, revise, and stale invalidation.
- [X] P1B-026 Require exact accepted prompt revision and source checksum in Qwen edit enqueue.
- [X] P1B-027 Remove the new-edit raw pass-through decision path while preserving historical reads.
- [X] P1B-028 [P] Add parser corpus, prompt contract, handler, idempotency, failure, and enqueue tests.
  Evidence: compilation jobs use attempt-id dedupe, immutable source/model/prompt snapshots, strict
  monotonic states, one configured multimodal call, revision-zero creation, structured clarification
  retries, and safe debug metadata. Qwen edit enqueue and execution both call the repository's single
  exact-lineage/latest-revision/source-hash/prompt-hash decision path; raw intent is retained only as
  provenance and is never sent to the editor. Focused compiler/job/repository/enqueue tests passed
  (41 tests), the scene-image and transport regression set passed (218 tests), the full solution build
  succeeded, and the full suite passed (1,327 tests) on 2026-08-25. The build retains one pre-existing
  AngleSharp advisory.

## F. Dedicated Editor

- [X] P1B-029 Read Razor instructions and full Studio/gallery/style context before editing.
- [X] P1B-030 Add the dedicated route and source-image loading/error states.
- [X] P1B-031 Add intent entry, prepare action, progress polling, grounded summary, and clarification.
- [X] P1B-032 Add advanced compiled-prompt revision with visible stale/recompile behavior.
- [X] P1B-033 Add run action, source/result comparison, failures, and lineage history.
- [X] P1B-034 Replace inline Studio raw editing with navigation and add gallery edit navigation.
- [X] P1B-035 [P] Run Razor diagnostics and desktop/mobile browser workflow checks.
  Evidence for P1B-029 through P1B-035: the dedicated editor loads only completed source images,
  creates checksum-anchored edit sessions, compiles and polls, renders ready/clarification/invalid/
  failed states, exposes grounded summaries and targets, makes revised prompts visibly stale, runs
  only exact accepted revision/checksum lineage, and displays before/after history. Studio and
  gallery now navigate to this route; no web-application raw edit-instruction assignment remains.
  Focused editor/compiler pipeline tests passed (43 tests), the web project build succeeded with
  one pre-existing AngleSharp advisory, and current Razor diagnostics report no errors in all five
  touched Razor files. Browser checks on 2026-08-25 verified the Model Manager and a real completed
  source at 1440x900 and 390x844 with visible source/intent controls, no overlap, and no document
  overflow; the nonexistent-source route rendered its explicit unavailable/not-complete state.
  Compilation was intentionally not submitted because the configured Qwen VL pod is unavailable.

## G. Pod Migration

**Dedicated-pod amendment:** `multi-pod-separation-plan.md` supersedes the original same-pod
migration wording. The legacy combined pod and volume remain intact until all replacement
deployments, cutovers, and stabilization gates pass. Termination and volume deletion remain separate
operations requiring explicit user authorization.

- [ ] P1B-036 Drain queued/active capability work and rerun the sanitized legacy inventory
  immediately before cutover; do not modify or delete the legacy source deployment.
- [ ] P1B-037 Prove Juggernaut remains the sole configured production text-to-image assignment and
  that Pony is not assigned to an active production function.
- [ ] P1B-038 Verify each dedicated deployment manifest, pod, volume, pinned artifacts, runtime,
  exposed-TCP SSH access, and exact identity before application assignment.
- [ ] P1B-039 Provision and verify the accepted Qwen VL compiler on its dedicated image-vision pod
  and volume while preserving the legacy combined deployment.
- [ ] P1B-040 Configure private/authenticated endpoints and `ManagedDedicatedPod` lifecycle values
  for each selected capability; keep credentials and discovered hosts/ports local.
- [ ] P1B-041 Independently run Juggernaut, Qwen Edit, Qwen VL, and DWPose start, SSH, health,
  inference, stop/restart, storage, and GPU evidence on their dedicated pods.
- [ ] P1B-042 Cut over one proven capability assignment at a time, verify no production assignment
  or queued work uses the legacy pod, then deprecate and stop it while preserving its pod and volume.

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