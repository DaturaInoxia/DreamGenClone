# Krea 2 training worker

Serverless worker that turns a folder of captioned images on the network volume into a Krea 2
character LoRA. One job = one LoRA.

This is the only worker here that does **not** inherit a handler from a stock RunPod base image.
Every other worker extends `runpod/worker-comfyui` and gets its request contract for free; nothing
upstream trains Krea 2 LoRAs, so this image carries its own `handler.py` and its own runtime.

## Contract

Submit to the endpoint's `/run` and poll `/status/{id}` (`/runsync` is unusable — training is far
beyond its 1-minute result retention).

```json
{
  "input": {
    "datasetVolumePath": "/runpod-volume/datasets/dean-v1",
    "outputName": "dean-krea2-v1",
    "epochs": 10,
    "maxTrainSteps": 5
  }
}
```

| Input | Required | Default | Notes |
|---|---|---|---|
| `datasetVolumePath` | yes | — | Directory on the volume of images, each with a same-stem `.txt` caption |
| `outputName` | yes | — | Bare file name; written to `<outputVolumeDir>/<outputName>.safetensors` |
| `epochs` | no | 1 | |
| `maxTrainSteps` | no | uncapped | Caps the run — use this to smoke-test the endpoint cheaply |
| `networkDim` / `networkAlpha` | no | 32 / 32 | |
| `learningRate` | no | 1e-4 | |
| `blocksToSwap` | no | 26 | The value proven on a 16 GB card |
| `seed` | no | 42 | |
| `resolution` | no | `[1024, 1024]` | |
| `outputVolumeDir` | no | `/runpod-volume/loras` | |

Output:

```json
{
  "status": "ok",
  "loraPath": "/runpod-volume/loras/dean-krea2-v1.safetensors",
  "loraSha256": "…",
  "loraBytes": 469315336,
  "datasetImages": 14,
  "gpu": "NVIDIA RTX PRO 6000 Blackwell, 97887 MiB",
  "durationSec": 812.4,
  "stepSeconds": { "cache_latents": 6.1, "cache_text_encoder": 41.0, "train": 760.2 },
  "recipe": { "…": "every effective hyperparameter, echoed back" },
  "logs": { "cacheLatents": "…", "cacheTextEncoder": "…", "train": "…" }
}
```

The handler **refuses to guess**. A missing dataset, an image with no caption, an absent or truncated
model, or a malformed parameter fails the job with a message naming the offending value. It never
substitutes a default to keep going.

## Required network-volume assets

Volume `n5rainij0c` (`DreamGen_Krea2_Training`, 50 GB STANDARD, **EU-RO-1**), staged and
sha256-verified by `helpers/runpod/serverless/krea2-training/download-training-models.sh`:

- `models/diffusion_models/krea2_raw_bf16.safetensors` — 26,283,332,608 B (gated)
- `models/text_encoders/qwen3vl_4b_bf16.safetensors` — 8,875,719,384 B
- `models/vae/qwen_image_vae.safetensors` — 253,806,246 B

### These must be the bf16 weights

musubi **rejects** the ComfyUI fp8 repacks that inference uses:

- DiT: `Layer blocks.0.attn.gate.weight is already in torch.float8_e4m3fn format. --fp8_scaled
  optimization should not be applied.`
- Text encoder: loads with `missing=[]` but `unexpected=['...weight_scale', '...comfy_quant']`

Do not "optimise" the volume by re-staging the fp8 files.

## Deploying

**RunPod GitHub Integration** ("Start from GitHub Repo", repo `DaturaInoxia/DreamGenClone`, branch
`development`) — RunPod clones the repo, builds this Dockerfile, and applies it to the endpoint.
Images are **not** published to GHCR and there is **no** GitHub Actions worker workflow; that is the
established decision on this repo, recorded in `dwpose-worker/Dockerfile`, and it is why
`.github/workflows/` does not exist. The README at `serverless/README.md` still describes the older
GHCR/Actions route and is out of date on this point.

Because a worker mounts the volume at `/runpod-volume` (a pod mounts the *same* volume at
`/workspace` — only the prefix differs), the endpoint must be pinned to **EU-RO-1**: a serverless
endpoint mounting a network volume is restricted to that volume's data center.

## GPU pools and the CUDA constraint

The endpoint takes up to three pools in priority order. `BLACKWELL_96` → `AMPERE_80` → `ADA_24`
gives a 96 GB best case and a 24 GB fallback, all inside EU-RO-1.

`pytorch/pytorch:2.11.0-cuda12.8-cudnn9-runtime` was chosen because cu128 carries kernels for
**sm_80 (A100), sm_89 (RTX 4090) and sm_120 (Blackwell RTX PRO 6000)** — so one image backs every
pool in that cascade. Verify any base-image change against this: a cu12x wheel older than cu128
builds fine and then fails at kernel launch on Blackwell, and Blackwell sm_120 requires torch ≥ 2.7.

Only ~16 of 34 data centers support network volumes, and the cheapest 48 GB class (A40) lives solely
in CA-MTL-1/EU-SE-1, neither of which does — so A40 + this volume is currently impossible.

## Endpoint settings

| Setting | Value | Why |
|---|---|---|
| Active workers | 0 | No idle charge |
| Max workers | 1 | Training saturates the GPU; a second worker buys nothing |
| Idle timeout | 300 s | Model load from the volume is expensive; don't tear down between jobs |
| Execution timeout | ~4 h | **Bounded on purpose.** The max is 7 days — do not use it, or a hung job bills for days |
| Container disk | ≥ 20 GB | Latent/TE caches plus the image layers |

## Cost and speed

The recipe was proven on a 16 GB RTX 5080 at **~47 s/step** and **15,897 of 16,303 MiB** peak VRAM.
Expect materially better on 80/96 GB cards, where `blocks_to_swap` can be reduced. Every result
carries `stepSeconds` and `gpu` precisely so this can be measured rather than estimated.

## Known open items

- `blocksToSwap` defaults to 26, the 16 GB-proven value. On an 80/96 GB card this is leaving speed on
  the table; the first real run should measure and then the default (or the app's value) should be
  updated from evidence, not guesswork.
- Dataset staging is not automated end-to-end: images must reach the volume before the job is
  submitted. The volume has an S3-compatible API and `serverless/s3-volume.py` already exists.
- The handler caches latents and TE outputs into container disk (`/tmp`), not the volume, so a retried
  job re-caches. That is deliberate for a first version — the caches are tiny next to the model load.
