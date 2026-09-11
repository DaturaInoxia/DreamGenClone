# B-117 — Pose-controlled composition render (OpenPose ControlNet)

**State:** `planned` (design artifact — no code written). **Scope:** large.
**Program:** `specs/Planning/identity-lora-program-map.md` — stage 4 of the component-readiness
order. Consumes B-118's pose store. Consumed by B-123 (pose-conditioned cells) and the scene-image
Composer.

## Purpose

Attach a pose skeleton to a composition render so a dictated pose is actually held. One pose, one
model, one render — the user picks the pose and the model and gets back an image whose pose they can
verify.

## What exists vs. what is missing

| Piece | State |
|---|---|
| Local ComfyUI stack (DWPreprocessor + `OpenPoseXL2.safetensors`) | ✅ proof-verified 2026-09-08/09 |
| Any in-app ControlNet render code | ❌ none — all renders are T2I (`denoise = 1.0` + `EmptyLatentImage`) |
| Per-model ControlNet capability declaration | ❌ none — `SupportedVisualStrategiesJson=[]` on all local rows |
| Pose picker UI | ❌ none |

## Components

1. **ControlNet capability declaration** in Model Manager: per model row, `ControlNet { weightRef,
   strengthDefault }` added to `SupportedVisualStrategiesJson`. Missing declaration → render fails
   fast (no fallback to a plain render).
2. **OpenPose ControlNet workflow builder** in `ComfyUIImageClient`:
   `LoadImage(pose skeleton) → DWPreprocessor → ControlNetLoader(OpenPoseXL2) / ControlNetApply →
   KSampler`, strength 0.35–0.85 per the prompt-compiler standards.
3. **Render route** in `SceneImageRenderingJobHandler`, gated on the resolved model declaring **and**
   qualifying ControlNet on a ComfyUI provider.
4. **Pose picker + strength UI**: reads the B-118 library, shows skeleton preview + `known-good`
   badge, strength slider; one render per request.

## User workflow

Pick a pose (from B-118) → pick a model → set strength → render → inspect the result. The pose source
is always a skeleton image from B-118's store (authored, extracted, or from the library) — never a
prose description.

## Non-goals

- No pose authoring / extraction / library (B-118).
- No depth / canny render (B-119) and no derived-asset store (B-120).
- No batch or multi-pose sweep — one render per request.

## Acceptance

1. Picking a saved pose and a qualified model produces one image whose joint geometry matches the
   skeleton within the B-123 pose-adherence scorer's tolerance.
2. Picking a model that does not declare ControlNet fails fast naming the model — no plain-render
   fallback.
3. The B-123 cell workspace can invoke this route for a single cell and keep or discard the result.

## Files

- This plan: `specs/Planning/B-117-pose-controlnet-render/plan.md`.
- Canonical hard rules: `.github/instructions/scene-image-prompt-compiler-standards.instructions.md`.
- Pose source: `specs/Planning/B-118-pose-studio/plan.md`.
