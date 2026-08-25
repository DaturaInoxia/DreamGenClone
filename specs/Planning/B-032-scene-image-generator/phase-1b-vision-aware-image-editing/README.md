# Phase 1B - Vision-Aware Image Editing

**Status:** Planned, next implementation slice
**Epic:** B-032 Scene Image Generator
**Prerequisite:** Phase 1 prompt-to-image and manual Qwen edit path
**Blocks:** Phase 2 character identity and the automated vision slice of Phase 4

## Goal

Turn a novice edit request into a grounded, reviewable Qwen Image Edit instruction by analyzing
the source pixels first. Deliver this as a dedicated edit page with explicit ambiguity handling,
immutable source/result lineage, and complete compiler provenance.

This is Option 3 from the design discussion. It includes the vision-first compiler from Option 2
and adds the application contract around it: schema validation, clarification, user review,
execution, comparison, and audit history.

## Implementation Package

- [`research.md`](research.md) - official Qwen guidance, model/runtime choice, and alternatives.
- [`spec.md`](spec.md) - user stories, functional requirements, and exit gates.
- [`data-model.md`](data-model.md) - edit sessions, compilation attempts, clarification, and lineage.
- [`contracts.md`](contracts.md) - multimodal client, compiler, schema, resolver, and job contracts.
- [`plan.md`](plan.md) - implementation slices, file surface, risks, and rollout.
- [`tasks.md`](tasks.md) - dependency-ordered implementation ledger.
- [`pod-migration-runbook.md`](pod-migration-runbook.md) - Pony retirement and vision runtime plan.

## Delivery

- Add a dedicated `/roleplay/image-edit/{sourceImageId}` workbench.
- Send source pixels and raw user intent to one explicitly configured vision-capable model.
- Produce a strict compilation result: ready, clarification required, or invalid.
- Show the model's grounded target/change/preservation summary before execution.
- Keep the compiled Qwen instruction editable as an advanced control.
- Submit only an accepted compiled instruction to the existing Qwen editor.
- Preserve raw intent, source analysis, compiler model/config, compiled prompt, edits, and result
  lineage as separate fields.
- Keep one RunPod pod and one persistent volume containing Juggernaut, Qwen Image Edit 2511, and a
  pinned Qwen VL vision runtime. Remove the Pony checkpoint after an inventory, hash,
  configuration-impact check, and measured capacity gate because Pony is no longer part of the POC.
- **ControlNet (B-097 re-open 2026-08-25):** provision ControlNet OpenPose (thibaud
  `controlnet-openpose-sdxl-1.0`) + SDXL Depth + DWPose aux on the single pod, and add ControlNet
  conditioning (pose + depth) to the ComfyUI workflow builder. ControlNet pose/layout control is a
  requirement pulled into earlier phases, not deferred to Phase 3. This is separate from the
  rejected OpenPose-inpainting exact-contact route.

## Boundaries

- This phase compiles user-directed edits; it does not approve generated images automatically.
- It does not merge prompt compilation with Phase 4 validation. A later validator compares source,
  result, and expected constraints using a separate model call and schema.
- It does not claim adult-scene support until the configured local vision model passes a permitted
  adult-content corpus. Unknown/refused analysis is visible and blocks compilation.
- It does not silently select another model, omit image analysis, or send raw intent when the
  configured compiler is unavailable.
- Pony is removed from the active POC deployment and Model Manager configuration. Historical source
  code, workflows, and evidence remain in source control for provenance, but reinstalling Pony is
  not part of this plan.

## Exit Gate

The phase exits only when the running application completes the frozen edit corpus through the
dedicated page, ambiguity cases request clarification, exact compilation provenance is persisted,
Qwen receives only the reviewed compiled prompt, and the single-pod migration evidence shows enough
disk for all three retained model families plus a measured GPU-residency strategy without hidden
fallback.