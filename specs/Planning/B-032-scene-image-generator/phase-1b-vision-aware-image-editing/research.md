# Phase 1B Research - Vision-Aware Image Editing

**Date:** 2026-08-25
**Status:** Complete for implementation planning

## Problem

The current manual edit path stores the user's short instruction as `PromptSnapshot` and sends it
unchanged to Qwen Image Edit 2511. A request such as "make the women kneeling instead of standing"
does not identify which women are standing, their locations, supporting surface, occlusion, or the
scene details that must remain unchanged. A text-only compiler cannot derive those facts safely.

## Primary Evidence

Qwen's official `polish_edit_prompt(prompt, img)` implementation sends both source pixels and the
instruction to `qwen-vl-max-latest`. Its edit prompt contract requires direct target/change
language, minimal visual disambiguation, anatomically feasible human changes, identity consistency,
and preservation of unaffected content. This establishes source-image analysis as part of prompt
compilation rather than an optional enhancement.

The local six-case Qwen proof independently demonstrates that reliable instructions name the exact
target, requested transformation, ownership/spatial detail, desired geometry, and preserved scene
facts. That proof covers non-explicit editing only.

## Options Considered

| Option | Decision | Reason |
|---|---|---|
| Text-only prompt rewrite | Rejected as production path | Cannot see cast, pose, clothing, laterality, occlusion, or ambiguity. |
| Vision-first free-text rewrite | Necessary core, insufficient alone | Grounded, but free text cannot reliably drive UI states or validation. |
| Vision-aware schema plus application validation | Selected | Includes source analysis while making ambiguity, provenance, review, and failure explicit. |

## Vision Model Decision

The first deployment candidate is a pinned Qwen VL 7B-class instruct model in an officially
supported quantized form, served through a pinned vLLM OpenAI-compatible endpoint. The initial
proof should use `Qwen/Qwen2.5-VL-7B-Instruct-AWQ` unless host compatibility research performed at
implementation time records and approves a newer Qwen VL artifact before any download.

This is a proof candidate, not a hidden fallback chain. The exact repository revision, artifact
files, hashes, vLLM revision, CUDA/PyTorch compatibility, launch arguments, image limits, context
length, and generation settings must be frozen before evaluation. If it fails the corpus, stop and
record the failure; do not substitute a cloud model or text-only path.

Reasons for this family:

- Qwen's own edit-polishing reference uses a Qwen vision-language model.
- vLLM provides an OpenAI-compatible chat endpoint and structured/JSON output support.
- A quantized 7B-class deployment is plausible on the existing 24 GB-class development GPU when
  evaluated with controlled image dimensions and context length.
- The model is independent of Qwen Edit's `qwen_2.5_vl_7b_fp8_scaled.safetensors`; that installed
  file is a diffusion text encoder inside ComfyUI, not a general multimodal chat endpoint.

## Host Capacity Decision

The deployment topology is one pod and one persistent `/workspace` volume. That pod retains:

- Juggernaut/SDXL for initial scene generation;
- Qwen Image Edit 2511 for source-image manipulation;
- Qwen VL for source analysis and edit-prompt compilation.

No second pod is active or required for initial implementation. Separate runtime directories and
processes on the same pod isolate pinned Python/ComfyUI/vLLM dependencies; they do not imply
separate hosts. A later deployment may separate the three providers only through the migration
gates in [`multi-pod-separation-plan.md`](multi-pod-separation-plan.md).

Disk and GPU memory are separate gates. Removing Pony reclaims persistent disk for Qwen VL. It does
not prove all three retained models can be loaded into the 24 GB GPU simultaneously. All three
artifacts remain installed on the same persistent volume even if services unload or switch GPU
residency between generation, compilation, and editing operations.

Before provisioning:

1. Inventory `/workspace`, both ComfyUI runtimes, caches, checkpoints, partial downloads, and free
   bytes; record file sizes and hashes.
2. Identify the exact Pony checkpoint referenced by the tracked workflows and Model Manager.
3. Stop jobs and services that can read the artifact.
4. Remove only the verified Pony checkpoint after the user-authorized migration preflight.
5. Provision the pinned vision runtime into its own directory on the same persistent volume.
6. Verify Juggernaut, Qwen Edit, and Qwen VL artifacts are all present and healthy on that pod.
7. Measure cold start, idle VRAM, generation peak VRAM, one-image compile peak VRAM, Qwen edit peak
  VRAM, disk remaining, model unload behavior, and service-switch time.

Prefer keeping all three HTTP services available on the pod. If measured VRAM prevents simultaneous
model loading, keep all artifacts installed and use explicit sequential GPU residency: load the
model needed for generation, compilation, or editing, then release it before the next GPU-heavy
operation. This is resource scheduling within one pod, not a fallback or another deployment.
Fully loaded co-residency may be selected only if measured headroom passes the recorded threshold.

The initial Qwen VL proof measured approximately 276 seconds from process start to health and an
additional approximately 137 seconds of launcher preflight on the FUSE-backed volume. The user
accepted that startup behavior for initial application implementation and superseded the old
180-second transition gate. The persisted transition timeout must cover the measured full path with
explicit operator margin; the evidence does not justify inventing a new exact constant. One-image
inference must still complete within 90 seconds, and all other media, VRAM, storage, and no-fallback
constraints remain unchanged.

## Compiler Responsibilities

The compiler observes only visible facts needed to fulfill the request. It must:

- locate candidate target subjects or objects;
- classify the requested edit;
- identify ambiguity or contradiction;
- specify feasible pose/geometry/ownership details;
- preserve visible identity, wardrobe, unaffected cast, objects, composition, lighting, and style;
- emit a concise Qwen-specific instruction;
- avoid inventing names, relationships, hidden anatomy, or story facts.

For "make the women kneeling instead of standing," a ready result may target every visibly standing
woman and preserve all other facts. If the image contains multiple women but the user says "the
woman," the result must request clarification using visible locators such as clothing and position.

## Compiler Versus Validator

Compilation asks: "What exact edit should Qwen perform on this source?"

Phase 4 validation asks: "Comparing expected constraints and pixels, did the result satisfy them,
and what changed unintentionally?"

They share the multimodal transport and may initially resolve to the same model, but use separate
`AppFunction` values, prompts, schemas, records, and calls. A compiler result never validates or
approves its own edited output.

## Evaluation Corpus

Freeze source images and intents covering:

- one unambiguous pose change;
- plural-subject pose change;
- ambiguous singular target among multiple similar people;
- left/right and foreground/background target locators;
- clothing replacement with ownership preservation;
- object add/remove/replace;
- visible text replacement with exact quoting;
- contradictory and impossible requests;
- occlusion and reflection false-positive traps;
- source with no matching target;
- permitted adult scenes, analyzed separately and never inferred from non-explicit results.

Score target correctness, requested-change fidelity, preservation coverage, invented facts,
clarification precision, schema validity, refusal/unknown behavior, prompt usefulness to Qwen, and
end-to-end result quality. Human review remains the acceptance authority.

## Sources

- Qwen Image repository `prompt_utils.py`, especially `polish_edit_prompt(prompt, img)`.
- Qwen vLLM deployment guide: OpenAI-compatible service, quantized serving, structured output,
  context and GPU-memory controls.
- Local Qwen Image Edit 2511 proof manifest and runbook.
- Existing Phase 4 validation research and contracts.
- Existing RunPod and isolated Qwen runtime documentation.