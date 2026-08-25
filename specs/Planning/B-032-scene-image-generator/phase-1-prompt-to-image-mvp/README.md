# Phase 1 - Prompt-to-Image MVP

**Status:** Implemented; final manual acceptance coverage remains
**Epic:** B-032 Scene Image Generator

## Purpose

Phase 1 proves the complete application path from a selected roleplay interaction to persisted
scene images. It also includes the later approved raw-instruction Qwen source-image editing
vertical slice. Phase 1B replaces that raw pass-through with a vision-aware compiler and dedicated
editor before Phase 2 begins persisted character identity packs and repeatable conditioning.

## Implemented

- Model Manager image providers, image models, function assignments, content policy, and strict configuration resolution.
- Two-stage background pipeline: interaction/beat analysis to an editable prompt, then image rendering.
- Separate Pony V6 and Juggernaut/SDXL prompting and ComfyUI workflows.
- Image Studio, prompt refinement, regeneration/version retention, workspace indicators, session gallery, and deletion.
- SQLite metadata, local image storage, provenance, statuses, debug events, job deduplication, and explicit failure diagnostics.
- Manual Qwen Image Edit 2511 source-image editing as a separate path that does not alter Pony or SDXL text-to-image generation.

## Qwen Proof And Integration

### Purpose

Qwen edits an existing render from a natural-language instruction. It is intended for semantic corrections and controlled scene changes while visually preserving unrelated people, clothing, composition, lighting, and background. It is not the initial text-to-image generator and is not claimed to provide pixel-identical preservation.

### Tested

One Juggernaut two-person base image was used for six independent Qwen edits:

1. Change only the man's shirt from blue to red.
2. Add black rectangular glasses only to the woman.
3. Raise the man's right arm into a wave with correct laterality and coherent hand topology.
4. Change the woman's body to a pregnant silhouette while retaining clothing and visible limbs.
5. Rotate the woman into a coherent left-facing profile toward the man.
6. Swap the two characters' positions while retaining identity and clothing ownership.

The covered non-explicit proof passed 6/6. Acceptance covered requested-edit presence, people count, visual identity, clothing ownership, unrequested pose/limb preservation, framing, lighting, background, laterality, body orientation, and spatial assignment. Preservation was judged visually, not pixel-for-pixel.

Adult-content editing was not part of the scored proof; it was later exercised in an exploratory, unscored staged session (`specs/image-generator-tests/qwen/images/adult-fellatio/`). The result is therefore `passed-covered-scenarios`, not complete coverage of every intended content path. Exact prompts, seeds, prompt IDs, timings, hashes, observations, and outputs are preserved under `specs/image-generator-tests/qwen/`.

### Added To The Application

- `RolePlaySceneImageEditor` as a dedicated Model Manager function.
- Persisted diffusion model, text encoder, VAE, steps, CFG, sampler, scheduler, denoise, AuraFlow shift, and CFG normalization settings.
- One strict editor-model resolution path with no hidden defaults or fallback editor.
- A ComfyUI Qwen client for source upload, workflow submission, history polling, output download, and explicit error reporting.
- A dedicated scene-image editing background job and request contract.
- A per-completed-image instruction and manual Edit action in Image Studio.
- Derived-image persistence linking each edit to its source image.
- Validation for source completion, session/interaction ownership, file path, readable input, and non-empty instructions.
- Focused tests for persisted workflow settings, edit record/job creation, and fail-fast invalid-input behavior.

## Remaining Phase 1 Work

Only the final end-to-end manual POC acceptance coverage in T068 remains:

- Confirm an explicit request is clamped or clearly rejected by a `SfwFiltered` provider.
- Confirm the adult-allowed generation path with an `AdultAllowed` provider.
- Test Qwen adult-content editing separately; the 6/6 proof did not cover it.
- Confirm the unset-policy and unconfigured-model guidance in the running UI.
- Record a representative style/size image-quality sample.
- Exercise the complete running-app workflow: generate prompt, edit prompt, render, regenerate with prior version retained, indicator, gallery, Qwen edit, and delete.

Builds, automated tests, Qwen non-explicit proof, backlog state, and the Phase 2 identity/LoRA scope
decision are complete. T069 is closed. Phase 1B can begin independently of the remaining Phase 1
manual acceptance checks. Phase 2 now waits for Phase 1B's multimodal/provenance exit gate, and
Phase 1 should not be described as fully accepted until its remaining checks are recorded.
