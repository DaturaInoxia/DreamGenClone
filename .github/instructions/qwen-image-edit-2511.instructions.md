---
description: 'Qwen Image Edit 2511 evidence, workflow, integration, and portable proof-runner rules. Read before changing Qwen source-image editing, proof workflows, model configuration, or running the Qwen/Juggernaut proof harness.'
applyTo: 'DreamGenClone.Application/Abstractions/IImageEditingClient.cs,DreamGenClone.Infrastructure/Models/ComfyUIImageEditingClient.cs,DreamGenClone.Web/Application/ModelManager/ImageEditorModelResolver.cs,DreamGenClone.Web/Application/RolePlay/SceneImageEditingJobHandler.cs,DreamGenClone.Web/Application/RolePlay/SceneImageService.cs,DreamGenClone.Web/Components/Pages/SceneImageStudio.razor,DreamGenClone.Web/Components/Pages/ModelManager.razor,DreamGenClone.Web/Components/Shared/ModelDetailsEditor.razor,DreamGenClone.Tests/RolePlay/ComfyUIImageEditingClientTests.cs,DreamGenClone.Tests/RolePlay/SceneImageServiceJobTests.cs,specs/image-generator-tests/qwen/**,helpers/runpod/qwen-image-edit-2511/**,helpers/runpod/run-qwen-simple-people-proof.ps1,helpers/runpod/verify-qwen-simple-people-proof.ps1,helpers/runpod/run-juggernaut-simple-people-base.ps1,specs/Planning/B-032-scene-image-generator/**'
---

# Qwen Image Edit 2511 Rules

## Evidence and coverage boundary

- Canonical reproducible proof: `specs/image-generator-tests/qwen/`.
- The accepted proof is six independent, non-explicit edits from `base.png`. Prompts, fixed seeds, hashes, timing, and visual acceptance are in `manifest.json`.
- Adult-content editing was exercised in an exploratory, unscored session (`images/adult-fellatio/`). Never claim scored Qwen adult-content capability from the proof.
- `exploratory/` images are retained evidence only; they are unscored and must not be presented as a quality result.

## Workflow contract

- Qwen is a source-image editor, not a text-to-image `SceneImageModelFamily`. Do not route it through Pony/SDXL selection or alter their behavior.
- Resolve Qwen only through `RolePlaySceneImageEditor` and the persisted model fields. Missing settings must fail explicitly; do not add hardcoded artifact, sampler, endpoint, or fallback values.
- Required persisted settings: diffusion model, text encoder, VAE, steps, CFG, sampler, scheduler, denoise, AuraFlow shift, CFGNorm strength.
- **Optional editor LoRA** (`ImageEditorLoraName` + `ImageEditorLoraStrength`, both nullable). A blank name means NO LoRA and emits no `LoraLoader` node at all — that is a configured state, not a fallback. A configured name REQUIRES an explicit positive strength and must never be defaulted. The LoRA feeds **model AND clip**: node 17 takes `model` from the checkpoint/UNET and `clip` from the checkpoint/CLIPLoader, then the sampler branch and BOTH text encodes are re-wired to it. Wiring only the model branch silently applies the LoRA to the image path alone.
- Validated configuration: 40 steps, CFG 4, Euler/simple, denoise 1, AuraFlow shift 3.1, CFGNorm 1. Model artifact names and hashes are recorded by the portable proof runbook.
- The app talks HTTP ComfyUI. SSH is only a development transport used to expose a private remote endpoint locally.

## Reproduction

- Use `helpers/runpod/qwen-image-edit-2511/` to provision and start an isolated pinned runtime.
- Read `specs/image-generator-tests/qwen/RUNBOOK.md` before running or changing the harness.
- First validate the committed evidence without generation: `powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/verify-qwen-simple-people-proof.ps1`.
- Test only the source-image generator with the tracked Juggernaut workflow and fixed seed: `powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/run-juggernaut-simple-people-base.ps1 -ComfyUiUrl <base-comfyui-url>`.
- Replay the six Qwen edits from a newly generated base: `powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/run-qwen-simple-people-proof.ps1 -BaseComfyUiUrl <base-comfyui-url> -QwenComfyUiUrl <qwen-comfyui-url>`.
- Pass explicit URLs. `generate-one.ps1` then bypasses the ignored local RunPod environment file; never add an endpoint, API key, SSH host, or token to source control.
- Generated replay outputs belong under ignored `artifacts/tmp/`; only the canonical proof images and metadata under `specs/image-generator-tests/qwen/` are source controlled.

## Verified findings: instruction phrasing dominates anatomy edits (2026-09-12)

Established with the SAME base image, seed 6601, graph, and checkpoint — only one variable changed per
run. Proofs: `artifacts/tmp/proofs/lora-ab/`, `artifacts/tmp/proofs/remix-aio/`.

- **Asking to "add" a body part gives the editor no reason to alter CLOTHING, so it layers a shape on
top.** `"add a male erect penis to the man standing using correct anatomy"` produced correct anatomy
pasted over a still-closed jeans fly. Re-phrasing to the clothing ACTION —
`"unzip his jeans and pull the front open so his erect penis is fully visible, correct male anatomy,
natural integration with his body"` — produced jeans pulled open with the anatomy integrated naturally.
**Always instruct the clothing/pose action, not just the anatomy.**
- **A prior claim that "the checkpoint cannot produce male anatomy" was WRONG** and must not be
repeated. `Qwen-Rapid-AIO-NSFW-v23` does hold the concept; it surfaced at editor-LoRA strength 1.0.
- **The winning configuration is the v23 checkpoint + `QwenEdit2511_AllIncludedGay_v2.safetensors`**
(Civitai model 2700552 version 3160956, 810 MB). Operator-accepted strengths: **0.8 (best), 1.0 good**.
- **`qwenImageEditRemix_aioV20.safetensors`** (Civitai 2338517 v2812714, 28,431,792,490 B, SHA-256
`10CF71B500DE46A09C48FD29B4CBE9DA07495593FE5514007B96EEAFDD68686B`) is a verified **drop-in** for the
`MergedCheckpoint` graph (baked CLIP+VAE, single `CheckpointLoaderSimple`, euler_ancestral/beta already
matches the app). It fixed anatomy SHAPE but was judged inferior to the LoRA result overall.
- SDXL anatomy LoRAs such as `Gays_Anatomy_2.safetensors` are architecturally incompatible with
Qwen-Image and must not be attached to a Qwen editor model.