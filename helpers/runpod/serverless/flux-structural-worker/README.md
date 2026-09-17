# FLUX structural proof worker

Proof-only Serverless worker for the dual-base location experiment.

## Deployment branch

This worker must be built from the tracked deployment branch recorded in
`helpers/runpod/serverless/endpoints.json`. The stock endpoint must not be replaced until the
XLabs image has completed a real pose-only proof.

The worker pins `x-flux-comfyui` to revision
`00328556efc9472410d903639dc9e68a8471f7ac`. The custom node is installed in the image under
`/ComfyUI/custom_nodes`; the network-volume model link is only for the staged checkpoint and is
not a substitute for the image build.

## Required network-volume assets

Place the exact, hash-recorded files under the ComfyUI model folders on network volume `xkslgh6xo0`:

- `models/unet/flux1-dev-fp8.safetensors`
- `models/vae/ae.safetensors`
- `models/clip/t5xxl_fp8_e4m3fn.safetensors`
- `models/clip/clip_l.safetensors`
- `models/controlnet/flux-canny-controlnet-v3.safetensors`

For the FLUX OpenPose experiment, the XLabs runtime is required. The verified
checkpoint is staged once on the volume as:

- `models/controlnet/flux-openpose-controlnet-raulc0399.safetensors`

and linked into the XLabs directory expected by the custom node:

- `models/xlabs/controlnets/flux-openpose-controlnet-raulc0399.safetensors`

The checkpoint is from `raulc0399/flux_dev_openpose_controlnet`, SHA-256
`0401e9fd6a9ae719d5bcaf6825e2fb6a354f84af9341eb7326c11b6027a7a828`, under the
FLUX.1-dev non-commercial license. This is not yet a passing proof or a local
16 GB portability claim.

The Canny artifact is the XLabs-AI `flux-controlnet-canny-v3` artifact. Do not substitute an SDXL ControlNet.

Models must be pre-staged on the persistent volume before deployment, matching the established
Juggernaut/DWPose worker pattern. This worker deliberately performs no runtime model downloads.
The endpoint must not be treated as qualified until the staged model manifest and proof render are
captured.
