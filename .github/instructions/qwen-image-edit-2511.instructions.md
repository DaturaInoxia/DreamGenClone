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