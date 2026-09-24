# `proofs-local/` — dual-base-location graphs for the LOCAL ComfyUI host

These are the same sampling graphs the serverless RunPod proofs used, ported to the
**local ComfyUI host** (`https://comfy.kenacwood.net`, WOOD-GAME-MAIN RTX 5080).
RunPod was the proof-of-concept; local is the target.

## What changed vs `../proofs/`

Exactly one mechanical thing — the FLUX loader — plus the CLIP source.

| | Pod graphs (`../proofs/`) | Local graphs (here) |
|---|---|---|
| Diffusion model | `CheckpointLoaderSimple` with `ckpt_name = flux1-dev-fp8.safetensors` (the pod volume served it as a checkpoint) | `UNETLoader` with `unet_name = flux1-dev-fp8.safetensors` (the local host has it under `models/diffusion_models/`) |
| Text encoders | `CLIPTextEncode.clip = [4, 1]` (CheckpointLoaderSimple output 1) | `CLIPTextEncode.clip = [2, 0]` (`DualCLIPLoader`, which the pod graphs also contained but did not use) |
| VAE | `VAELoader` (`ae.safetensors`) | unchanged |
| Samplers | `XlabsSampler` / `KSampler` | unchanged — same seeds, steps, denoise, strengths |
| Prompts / canvas | — | unchanged, verbatim |

Nothing else differs: the graphs are byte-identical apart from the loader nodes, so a local
render is comparable to the serverless proof.

## Files

| File | Provenance |
|---|---|
| `flux-figures-A.workflow.json` | `../proofs/studio-a.workflow.json` (pose A: facing each other) |
| `flux-figures-B.workflow.json` | `../proofs/figures-studio-b.workflow.json` (pose B: both facing right, white backdrop) |
| `harmonize-A.workflow.json` | `../proofs/harmonize-face-to-face.workflow.json` (denoise 0.35, seed 20260925) |
| `harmonize-B.workflow.json` | `../proofs/harmonize-base-b.workflow.json` (denoise 0.40, seed 20260927) |

The two `harmonize-*` files carry the literal placeholder `SCENE_PROMPT` in node 6 — the
orchestrator substitutes the per-location scene prompt into a copy written inside the run
directory (`06-harmonized-<loc>-<pose>/workflow.json`), so the prompt actually used is recorded
with the render.

Image manifests (`images.json`) are **not** committed here: they depend on the run directory and
are generated per job by `run-local-dual-location.ps1` next to the workflow copy.

## Host prerequisite

The figure stage needs the XLabs FLUX runtime (`LoadFluxControlNet`, `ApplyFluxControlNet`,
`XlabsSampler`) plus the OpenPose controlnet checkpoint. Neither exists on a stock ComfyUI
install. Run `helpers/local-comfyui-host/provision-xlabs-flux.ps1` **on the ComfyUI host**, then
restart ComfyUI.

`helpers/local-comfyui-host/run-local-proof.ps1` fails fast — before queueing anything — listing
every node class the target host does not provide, so a missing XLabs install is reported as a
provisioning problem rather than a render failure.

## Running

```powershell
powershell -ExecutionPolicy RemoteSigned `
  -File specs/image-generator-tests/dual-base-location/run-local-dual-location.ps1
```

See `../FINDINGS.md` and `../FINDINGS-FLUX-OPENPOSE-PIPELINE.md` for why the pipeline is shaped
this way (pixel-space compositing, masked harmonization, rembg extraction, pose canvas = canvas).
