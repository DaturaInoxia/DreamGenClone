# Coding Handoff — Identity → Body → LoRA Program

Use this prompt for the coding coordinator. The work is stage-gated: complete and verify one stage
before dispatching a dependent stage. Do not collapse the program into one speculative change set.

---

**Role:** You are the implementation coordinator for the identity/reference/LoRA workstream. Execute
the task artifacts in dependency order. Reuse existing code identified by each task-0 verification;
do not rebuild B-108/B-111 machinery. Fix forward only.

## Read first, in order

1. Repository `.github/copilot-instructions.md` and every path-matched instruction file.
2. `specs/Planning/identity-lora-program-map.md` — controlling ownership and sequence.
3. `specs/Planning/identity-and-reference-model.md` — controlling data contracts.
4. This handoff, then the current stage's plan/tasks only.

Before any RP-engine-adjacent code change, present the stage root cause, proposed files and blast
radius and obtain the repository-required explicit confirmation. Before Razor edits, read the full
Razor instruction and full component context. Before image compiler/workflow changes, read all
applicable model-specific and canonical prompt-compiler instructions. Any RunPod/host change must be
persisted in the registry/provisioning artifacts and restart-proofed under the repository rules.

## Locked contracts

1. B-124 is first. Features consume its schema and grouped shell; they do not add private variants.
2. Face canonical slots use `SceneImageReferenceFaceView`; body canonical slots use
   `SceneImageReferenceBodyView`. `ViewDescriptorJson` carries extended yaw/pitch/rotation/position.
3. Every full-body asset has explicit `BodyState` (`Clothed` or `Unclothed`). Never infer it.
4. `PackScope` is required persisted data. B-121 produces `FaceOnly`; B-122 produces
   `BodyComplete`; B-123 requires `BodyComplete`. Missing/unknown scope fails fast.
5. Asset Manager is grouped-only. Group key remains `(RootKind, RootId, AssetKind, ViewKey?)`, with
   `RootId` defined as the immediate version/profile owner. Cross-store row identity is
   `(SourceStore, SourceAssetId)`; versions and equal ids must not collapse.
6. Every capability is a user-driven UI tool. One render request produces one image. No generate-all,
   angle/state sweep, model sweep or hidden batch path.
7. One dataset supports N training profiles/artifacts. Inference selects the artifact matching the
   exact render base-model identity and fails fast when none matches.
8. Clothed and nude references/cells are first-class. Do not remove, sanitize or silently reroute
   explicit-content work.
9. B-121 owns the eye subprocess, template store and edit primitive. B-118 owns pose authoring/store.
   B-117 owns OpenPose rendering. B-120 owns derived assets. B-119 owns depth/canny layout rendering.
   B-123 consumes those and owns dataset gates plus LoRA inference; no duplicate implementations.
10. Configuration is persisted/UI-backed. Missing values fail explicitly; no runtime defaults,
    guessed paths, capability fallback or plain-render fallback.

## Dispatch order and gates

### Stage 1 — B-124 foundation

Execute `B-124-reference-model-and-asset-manager-shell/plan.md` tasks B124-001…009.

Exit gate: additive migration and repositories round-trip body slots/state/descriptors/scope; legacy
packs are explicitly `FaceOnly`; scope-specific approval tests pass; grouped-only Asset Manager keeps
versions and stores distinct; Razor diagnostics and affected tests are green.

### Stage 2 — B-121 Character Identity Studio

Execute `B-121-character-identity-studio/tasks.md`. Task B121-000a…e is mandatory and read-only.

Exit gate: a real face build completes in-app into an explicit `FaceOnly` pack with five canonical
and accepted extended views; configured eye/upscale/edit paths work; existing bootstrap behavior is
preserved; all eleven acceptance scenarios and affected tests are green.

### Stage 3 — B-122 body-complete identity

Execute `B-122-body-complete-identity-and-lora-image-studio/b122-tasks.md` only.

Exit gate: a real body build completes in-app one view at a time; BodyCard has no unresolved required
fields; matching clothed/unclothed canonical sets plus an extended view exist; a superseding
`BodyComplete` pack is approved; B-123 resolves it without inference; affected tests are green.

### Stages 4 and 6 — parallel foundations

- Execute `B-118-pose-studio/tasks.md`.
- Execute `B-120-asset-layout-extraction/tasks.md`.

They may run in parallel after their own task-0 checks. B-118 completes the Apply request contract,
not the real render. B-120 must not create a second pose-authoring store.

### Stage 5 — B-117 pose rendering

After B-118, execute `B-117-pose-controlnet-render/tasks.md`. The B-120 source variant may be added
only when B-120's approved derived lookup exists; do not fabricate an interim store.

Exit gate: B-118 Apply produces exactly one image through a qualified model, exact pose provenance is
stored, known-good visual proofs are recorded, unsupported combinations fail explicitly, and tests
are green. Quantitative pose scoring remains B-123.

### Stage 7 — B-119 layout workflow

After B-120, execute `B-119-multi-character-layout-workflow/tasks.md` against its plan/workflow.

Exit gate: required depth/canny routes use approved derived assets, host changes are reproducible and
restart-proof, no plain-render fallback exists, live layout proof and affected green tests are recorded.

### Stages 8–9 — B-123 LoRA Studio and inference

Execute `B-122-body-complete-identity-and-lora-image-studio/tasks.md`. Do not implement any upstream
producer inside B-123. Schema/editor work may begin after stages 1–5, but required layout cells remain
unavailable until stages 6–7 and block freeze; they never degrade to weaker control.

Exit gate: a real character is completed cell by cell through normalization, gates, captions,
registration, freeze/export, training and matching-artifact inference. Pose scoring is calibrated on
versioned good/bad fixtures. There is no batch UI/path, no duplicate upstream implementation, no
fallback, and all affected tests are green.

## Required completion report per stage

- Tasks completed and files changed.
- Exact persisted configuration sources and fail-fast behavior.
- Proof that each concern still has one active owner/path.
- Focused and affected-suite test commands with exact pass counts.
- Razor diagnostics and viewport checks when UI changed.
- Live artifact ids/screenshots/proof required by that stage's tasks.
- Remaining blockers; never mark a stage complete while a required test or dependency is failing.