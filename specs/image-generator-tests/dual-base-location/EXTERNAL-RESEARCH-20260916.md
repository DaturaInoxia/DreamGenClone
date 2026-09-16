# Dual-base location proof — external research record

**Date:** 2026-09-16  
**Purpose:** Establish the first externally grounded proof route for location-consistent, identity-consistent Base A/Base B generation before application integration.

## Confirmed from external primary sources

### FLUX structural conditioning

Black Forest Labs' official `black-forest-labs/flux` repository documents:

- `FLUX.1 Canny [dev]` as a structural-conditioning model.
- `FLUX.1 Depth [dev]` as a structural-conditioning model.
- `FLUX.1 Canny [dev] LoRA` and `FLUX.1 Depth [dev] LoRA` variants.
- The official sampling implementation encodes a control image with a Canny or Depth image encoder before denoising.
- The official implementation supports local ComfyUI use and CPU offloading.

Primary sources:

- https://github.com/black-forest-labs/flux/blob/main/docs/structural-conditioning.md
- https://github.com/black-forest-labs/flux/blob/main/src/flux/cli_control.py
- https://github.com/black-forest-labs/flux/blob/main/src/flux/sampling.py
- https://github.com/black-forest-labs/flux/blob/main/model_cards/FLUX.1-dev.md

### FLUX image editing and references

The official Kontext model card states that `FLUX.1 Kontext [dev]` supports image editing and character/style/object reference without fine-tuning. It is a benchmark candidate, not yet accepted as the portable route.

Primary sources:

- https://github.com/black-forest-labs/flux/blob/main/docs/image-editing.md
- https://github.com/black-forest-labs/flux/blob/main/model_cards/FLUX.1-kontext-dev.md
- https://github.com/black-forest-labs/flux/blob/main/src/flux/cli_kontext.py

### ComfyUI compatibility

Official ComfyUI source contains Flux model loading, Flux-specific ControlNet support, `LoadImage`, `Canny`, `ControlNetLoader`, `DiffControlNetLoader`, `ControlNetApplyAdvanced`, and `UNETLoader`. Compatibility still depends on the exact model/control weight format and graph; a generic SDXL ControlNet cannot be attached to FLUX.

Primary source:

- https://github.com/Comfy-Org/ComfyUI/blob/master/comfy/ldm/flux/controlnet.py
- https://github.com/Comfy-Org/ComfyUI/blob/master/comfy/controlnet.py
- https://github.com/Comfy-Org/ComfyUI/blob/master/nodes.py

### Exact FLUX ControlNet candidate

The external research identified a concrete candidate that is more actionable than the generic
BFL model-card names: the XLabs-AI `x-flux` project publishes FLUX.1-dev Canny and Depth
ControlNets, including the V3 artifacts used by its reference CLI:

- Canny: repository `xlabs-ai/flux-controlnet-canny-v3`, file
	`flux-canny-controlnet-v3.safetensors`.
- Depth: repository `xlabs-ai/flux-controlnet-depth-v3`, file
	`flux-depth-controlnet-v3.safetensors`.

The same project documents the exact control settings and demonstrates `flux-dev` with Canny and
Depth at 1024x1024, 25 steps, `true_gs` around 3.5, and control guidance. Its implementation
uses a FLUX-specific ControlNet architecture and annotators; these are not SDXL ControlNet files.

Primary sources:

- https://github.com/XLabs-AI/x-flux/blob/main/README.md
- https://github.com/XLabs-AI/x-flux/blob/main/src/flux/xflux_pipeline.py
- https://github.com/XLabs-AI/x-flux/blob/main/src/flux/controlnet.py
- https://github.com/XLabs-AI/x-flux/blob/main/src/flux/util.py

ComfyUI's current source contains a compatible FLUX ControlNet loader path for the XLabs/MistoLine
format (`load_controlnet_flux_xlabs_mistoline`) and a dedicated Flux ControlNet implementation.
That makes a standard ComfyUI API graph plausible, but the exact installed ComfyUI version and
worker node inventory still require a live `/object_info` or proof-job validation.

### IP-Adapter identity conditioning

The official IP-Adapter project documents SDXL image prompting and structural generation by combining IP-Adapter with ControlNet. The ComfyUI IPAdapter Plus project documents masked attention regions and multiple regional conditioning parameters. This establishes the SDXL identity route, not FLUX identity compatibility.

Primary sources:

- https://github.com/tencent-ailab/IP-Adapter
- https://github.com/cubiq/ComfyUI_IPAdapter_plus

## Critical correction

The first FLUX proof must not invent a graph by attaching an SDXL ControlNet to a FLUX checkpoint. The BFL structural models are dedicated FLUX models or FLUX-compatible LoRA variants. The exact ComfyUI node graph and model files must be validated before the proof is run.

## First proof gate

1. Confirm the exact ComfyUI graph for the XLabs FLUX.1 Canny V3 artifact. The initial provisional graph was deliberately not executed because it incorrectly assumed a generic `ControlNetLoader` filename and could have mixed incompatible model formats. The provisional graph must be corrected to `flux-canny-controlnet-v3.safetensors` after the file is installed.
2. Use the existing bedroom location reference.
3. Run a FLUX Canny or Depth structural-only case on RunPod.
4. Use a fixed prompt, seed, dimensions, model file, and control strength.
5. Capture the complete ComfyUI API workflow and output.
6. Record execution status and provider logs.
7. Reject the route if the graph is not reproducible on the local ComfyUI version or if model memory cannot fit the local 16 GB target.

Only after structural control passes should Dean/Becky identity references be added.

## Candidate order

1. XLabs FLUX.1 Canny V3 over the existing local-portable `flux1-dev-fp8` base.
2. XLabs FLUX.1 Depth V3 over the same base.
3. SDXL Juggernaut/BigLust + SDXL ControlNet + regional IP-Adapter as an independently scored comparison.
4. FLUX.1 Kontext as a quality benchmark; it is not accepted unless it passes the local portability gate.

## Acceptance boundary

A successful RunPod render alone is not a feature proof. The final route requires:

- location structure score;
- Dean identity score;
- Becky identity score;
- Base A/Base B orientation score;
- later-editor preservation score;
- exact workflow and model hashes;
- local ComfyUI replay;
- measured local VRAM and execution evidence.
