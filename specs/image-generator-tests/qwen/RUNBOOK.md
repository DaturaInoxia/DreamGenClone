# Qwen Image Edit 2511 Portable Proof Runbook

## Purpose

This directory (`specs/image-generator-tests/qwen/`) is the source-controlled evidence package for the six-of-six controlled, non-explicit Qwen Image Edit 2511 proof, plus the exploratory adult-content session.

- `images/` — `base.png`, the six accepted edits, `exploratory/` (four unscored interaction experiments), and `adult-fellatio/` (the staged adult-content session).
- `prompts/` — the base and edit ComfyUI workflows (the exact prompts, seeds, and settings used).
- `manifest.json` — full prompt/seed/hash metadata.

`images/exploratory/` retains four interaction experiments. They are unscored, not part of the six-of-six result, and must never be used as a capability score.

Adult-content editing was exercised in an exploratory, unscored staged session under `images/adult-fellatio/`; it is recorded as evidence only and is NOT scored capability evidence.

## Package Integrity

Before relying on the evidence, run:

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/verify-qwen-simple-people-proof.ps1
```

This checks the packaged base and six accepted edit images against the byte counts and SHA-256 values in `manifest.json`. It does not evaluate generated images or submit any work to a model.

## Runtime Prerequisites

Use an isolated ComfyUI checkout. Do not add Qwen artifacts to the production Pony/Juggernaut runtime.

On the GPU host:

```bash
bash helpers/runpod/qwen-image-edit-2511/provision-runtime.sh /workspace/comfyui-qwen-2511
bash helpers/runpod/qwen-image-edit-2511/start-comfyui.sh /workspace/comfyui-qwen-2511 3002
curl -fsS http://127.0.0.1:3002/system_stats
```

The provision script pins ComfyUI to `e4c61d75555036fa28b6bb34e5fd67b007c9f391`, downloads the exact three model artifacts, and verifies their SHA-256 values. The runtime binds only to `127.0.0.1:3002`.

For local development, expose the private Qwen endpoint with a local SSH forward. Substitute the current host, SSH port, user, and private key path from the machine-specific RunPod setup:

```powershell
ssh -tt -L 127.0.0.1:3002:127.0.0.1:3002 -o ExitOnForwardFailure=yes -o IdentitiesOnly=yes -i artifacts/runpod/ssh_ed25519 -p <ssh-port> <user>@<host> "tail -f /dev/null"
```

Keep that terminal open. Verify the forward locally:

```powershell
(Invoke-RestMethod -Uri http://127.0.0.1:3002/system_stats -TimeoutSec 10).system.comfyui_version
```

## Replay The Proof

The proof uses two intentionally separate services:

- `BaseComfyUiUrl`: a ComfyUI instance with `juggernautXL_ragnarok.safetensors` for the neutral base image.
- `QwenComfyUiUrl`: the isolated Qwen instance, normally `http://127.0.0.1:3002` while the SSH forward is active.

To test the Juggernaut base-image generation independently, run the exact tracked workflow, prompt, negative prompt, sampler settings, checkpoint name, and fixed seed:

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/run-juggernaut-simple-people-base.ps1 `
  -ComfyUiUrl "https://<your-base-comfyui-endpoint>"
```

It writes exactly one newly generated image to `artifacts/tmp/images/juggernaut-simple-people-replay/`. Compare it visually with `images/base.png`; the packaged image hash proves the original evidence, not byte-identical output across changed environments.

Run the replay with explicit endpoints:

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/runpod/run-qwen-simple-people-proof.ps1 `
  -BaseComfyUiUrl "https://<your-base-comfyui-endpoint>" `
  -QwenComfyUiUrl "http://127.0.0.1:3002"
```

The script generates a new base from the fixed seed, uploads that generated base separately for each of the six edits, and replays every exact prompt and seed from `manifest.json`. Results are saved under `artifacts/tmp/images/qwen-simple-people-replay/`, which remains intentionally ignored.

Review replay output visually. A replay is not required to be byte-identical when ComfyUI, CUDA, PyTorch, or model files differ; the source-controlled package is the immutable original evidence.

## App Configuration

The DreamGenClone app talks to Qwen with normal HTTP ComfyUI requests. It does not create an SSH connection itself.

In Model Manager, create an image-only ComfyUI provider pointing to the reachable Qwen URL. Configure an image model with these persisted values and assign it only to `RP Scene Image Editor (Qwen)`:

| Setting | Value |
|---|---|
| Diffusion model | `qwen_image_edit_2511_fp8mixed.safetensors` |
| Text encoder | `qwen_2.5_vl_7b_fp8_scaled.safetensors` |
| VAE | `qwen_image_vae.safetensors` |
| Steps / CFG | `40` / `4` |
| Sampler / Scheduler | `euler` / `simple` |
| Denoise | `1` |
| AuraFlow shift | `3.1` |
| CFGNorm strength | `1` |

The editor path is source-image-only. Begin with a completed scene image, enter one explicit change, and submit the edit. For multi-step transformations, select the latest completed output and submit the next instruction manually.