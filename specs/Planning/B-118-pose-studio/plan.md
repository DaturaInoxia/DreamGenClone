# B-118 — Pose Studio (interactive pose tool)

**State:** `planned` (design artifact — no code written). **Scope:** medium.
**Program:** `specs/Planning/identity-lora-program-map.md` — stage 3 of the component-readiness
order; **owns** the pose store + DWPose extract + pose library. Feeds B-117 (render) and B-123
(cell-workspace pose picker).

## Purpose

The interactive tool the user uses to manage and produce poses. It is **not** the render (B-117),
**not** the derived-asset store (B-120), and **not** an automation sweep — it is a set of four user
actions around a pose library.

## User workflow (the verbs, in order)

1. **Browse / search** the seeded 472-pose library, grouped by category, with a skeleton preview per
   pose and a `known-good` badge (standing / squatting / kneeling-feet-down verified to hold under
   OpenPoseXL2).
2. **Extract** a pose from an uploaded or existing image via the local DWPose preprocessor, with the
   **head-keypoint validation rule** — nose + neck + both shoulders must be present; a faceless
   skeleton leaves the head unconstrained and is rejected with that reason, never silently accepted.
3. **Edit** the extracted (or library) skeleton on a 2D canvas: drag COCO-18 body joints, mirror,
   undo, reset. Hands/face are not edited (OpenPoseXL2 does not reliably honour them).
4. **Save** as a named preset (`name`, `category`, keypoint JSON, skeleton PNG, thumbnail,
   `known-good` flag).
5. **Apply** any saved/extracted pose with a chosen model — this is a single render the user requests,
   delegated to B-117. The apply action is one pose, one model, one render; there is no sweep.

## Components (one = one user-facing capability)

| Component | What it is |
|---|---|
| `PosePreset` store | name, category, keypoint JSON, skeleton PNG, thumbnail, `known-good`, provenance (extracted-from vs authored) |
| Seed importer | imports the git-tracked 472-pose NSFW OpenPose pack (`helpers/runpod/openposeNSWFPosePackage_final/`) idempotently |
| DWPose extract client | LoadImage → DWPreprocessor → SaveImage on local ComfyUI, returning keypoints + skeleton PNG |
| 2D skeleton editor | Blazor + JS canvas; drag joints, mirror, undo; COCO-18 body joints only |
| Apply action | hands a `PosePreset` + chosen model to the B-117 render route |

## Seams

- **Feeds B-117:** the render route takes a `PosePreset` id (not a raw file) + model + strength.
- **Feeds B-123:** the cell workspace's pose picker reads this library and shows the skeleton
  preview + `known-good` badge.
- **B-120 boundary:** B-120 owns the *derived-asset record* (versioned, approved, provenance-tracked)
  that holds a produced control image. The `PosePreset` store is the authoring side; B-120 is the
  cataloguing side. A pose used in a render may *also* be catalogued as a derived asset by B-120 —
  the two do not merge.

## Non-goals

- No ControlNet render wiring (B-117).
- No depth / canny / segmentation extraction (B-120).
- No pose-conditioned batch generation for a character (B-123 drives cells; this tool never runs
  many renders at once).

## Acceptance (the wireframe test — each must show a working artefact)

1. Browsing the library shows a skeleton preview for every pose, and search filters by category/name.
2. Extracting a pose from an image shows the skeleton overlaid; a faceless image is rejected with the
   head-keypoint reason.
3. Editing a skeleton and saving it creates a preset that reloads identically.
4. Applying a saved pose with a chosen model produces exactly one image (via B-117), whose provenance
   names the pose and model.
5. The B-123 cell workspace can open this tool and pick a pose without leaving the workflow.

## Files

- This plan: `specs/Planning/B-118-pose-studio/plan.md`.
- Pose pack: `helpers/runpod/openposeNSWFPosePackage_final/` (git-tracked).
- Render hand-off: `specs/Planning/B-117-pose-controlnet-render/plan.md`.
