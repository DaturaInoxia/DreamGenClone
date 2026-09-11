# Handoff Prompt — Architecture Review (first pass)

Paste this into the architecture review agent. The agent reviews the plans only; it writes no code
and modifies no files.

---

**Role:** You are an architecture review agent. Do **not** write implementation code. Your job is to
review a multi-item planning workstream for coherence, completeness, contradictions, and sequencing
risk, and report findings. Nothing under these items has been coded yet — this is a review of the
plans before a coding agent is dispatched.

**Read, in this order:**

1. `specs/Planning/identity-lora-program-map.md` — sequencing + single-owner table (§3), principles
   (§1b), stage order (§5), handoff order (§9).
2. `specs/Planning/identity-and-reference-model.md` — the reference model (§2) and the component
   stack (§3).
3. `specs/Planning/B-124-reference-model-and-asset-manager-shell/plan.md` — stage 1 foundation.
4. `specs/Planning/B-121-character-identity-studio/` — stage 2 (README, spec, plan, tasks,
   ui-contract, seed-prompts).
5. `specs/Planning/B-122-body-complete-identity-and-lora-image-studio/` — B-122 Phase 0 + B-123
   (plan.md + tasks.md).
6. Scan: `B-117-pose-controlnet-render/plan.md`, `B-118-pose-studio/plan.md`,
   `B-119-multi-character-layout-workflow/`, `B-120-asset-layout-extraction/`.

**Validate that these locked decisions are coherent and mutually consistent — flag any that is
violated, ambiguous, or contradicted somewhere in the docs:**

1. Foundation first: **B-124 (reference data model + grouped Asset Manager shell) is stage 1.**
2. View model: `SceneImageReferenceFaceView` stays the canonical-slot enum (contract);
   `ViewDescriptorJson` carries the extended set (up/down pitch, intermediate yaw). Enum = contract,
   variety = data.
3. Body: base + angle slots are the canonical minimum; `BodyRotationDeg` / `BodyPositionKey` are
   free data for rotations/positions.
4. Asset Manager is **grouped-only** (no flat mode), and **root-agnostic**: group key
   `(RootKind, RootId, AssetKind, ViewKey?)` covers Character / Location / Wardrobe / Prop / Style.
5. Every capability is a UI tool the user drives, never a batch ("generate 30" must not exist).
6. One base model first; the LoRA inference path is base-model-agnostic (one dataset → N profiles →
   N artifacts; inference selects the artifact matching the render model and fails fast otherwise).
7. NSFW is in scope end-to-end (unclothed body refs, ~50/50 nude cells).
8. Ownership splits: faceid/IP-Adapter render wiring = B-111 P3, scoring-CLI wiring = B-123;
   B-123 **depends on** B-117/B-118/B-120, it does not build them.

**Produce:**

1. A verdict per item: ready / ready-with-notes / needs-rework.
2. Cross-item contradictions or stale references (e.g. stage numbers, ownership wording).
3. Gaps that would block a coding agent: a missing type, a missing plan, an ambiguous seam, an
   unreachable assumption.
4. Sequencing risks: does any stage silently require a later stage? Are the "Blocks on" columns in
   the map §5 accurate?
5. Data-model review: is `ViewDescriptorJson` + the group-key model sufficient for the stated
   outcomes — extended face views, body rotations/positions, and locations/other types?
6. A prioritized list of changes to make **before** coding dispatch.

**Constraints:**

- Report only. Do not write or suggest code. Do not modify any file.
- The plans are the source of truth; the backlog and codebase are context.
- Flag anywhere a plan contradicts this repo's hard rules: no fallback/default config, no hidden
  runtime defaults, missing config fails fast, all tests green, forward-only changes (no
  `git restore`/reset).
- Note any plan that re-implements work that already exists in code (e.g. the B-108 reference-
  bootstrap machinery noted in the B-121 plan's "Verified current state").
