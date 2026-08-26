# Identity Conditioning Proof — Dependency Manifest

**Date:** 2026-08-26
**Pod:** `7i2mutjmry5tkt` (ComfyUI v0.3.10, PyTorch 2.6.0+cu124)

Installed and verified (node discovery confirmed via `/object_info`, 555 nodes).

## Custom nodes (pinned git revisions)

| Node | Repo | Commit | License |
|---|---|---|---|
| ComfyUI_IPAdapter_plus | cubiq/ComfyUI_IPAdapter_plus | `a0f451a5113cf9becb0847b92884cb10cbdec0ef` | GPL-3.0 |
| PuLID_ComfyUI | cubiq/PuLID_ComfyUI | `93e0c4c226b87b23c0009d671978bad0e77289ff` | Apache-2.0 |
| ComfyUI-Impact-Pack | ltdrdata/ComfyUI-Impact-Pack | `cb0655f9a11ad771b4f6a846f08be29b5b66f0eb` | MIT |
| comfyui_controlnet_aux | Fannovel16/comfyui_controlnet_aux | `e8b689a513c3e6b63edc44066560ca5919c0576e` | Apache-2.0 |

### ComfyUI version compatibility note (recorded for the pin)

Latest Impact Pack (V8.28.3) fails to import on ComfyUI v0.3.10
(`comfy.samplers` lacks `SCHEDULER_HANDLERS`). Pinned Impact Pack to the commit immediately
before the scheduler-API refactor (`7a81d5a` → parent `cb0655f`), which uses the legacy
`SAMPLER_NAMES`/`SCHEDULER_NAMES` API and imports cleanly. Same pin would be required on any
ComfyUI v0.3.10 host.

## Python packages added

`insightface`, `onnxruntime`, `opencv-python-headless`, `facexlib`, plus each node's
`requirements.txt` (IPAdapter_plus uses `pyproject.toml`, deps already present).

## Verified node presence

`IPAdapterUnifiedLoader`, `IPAdapter`, `IPAdapterAdvanced`, `IPAdapterRegionalConditioning`,
`PulidModelLoader`, `ApplyPulid`, `DWPreprocessor`, `RegionalPrompt`, `ImpactControlBridge`,
`FaceDetailer`, plus ComfyUI core `ControlNetLoader`/`ControlNetApplyAdvanced`/`CLIPVisionLoader`.

## Model files (downloaded to `/workspace/comfyui/models`)

Recorded in the model manifest after download + SHA-256. See `model-manifest-2026-08-26.md`.
