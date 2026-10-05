# h3-local-host — MiniMax H3 (B-150) local video proof harness

Proof harness that runs **MiniMax H3** (open weights) as a general local video-creation producer
on WOOD-GAME-MAIN (RTX 5080 16GB, ComfyUI 0.37.1, `http://192.168.0.11:8188`), and specifically
proves the **NSFW** path via the `AfterMidnight` Ref2VA LoRA. Same proof-first discipline as
`helpers/wan-local-host/`.

> **Safety boundary:** committed proof cells stay in the **implied / softcore-adult** register
> (same precedent as the Wan harness). The pipeline is uncensored-capable; the committed proof
> assets stay tasteful. See the B-150 research doc for the model's licensing constraints.

## Stack (16GB, all non-gated HF files, on host `D:\ComfyUI\models\`)

| Role | File | Size |
|---|---|---|
| DiT (Ref2VA) | `unet/minimax_h3_ref2va_pruned-Q4_K.gguf` (unsloth) | 10.6 GB |
| Text encoder | `clip/qwen3vl-32B-MiniMax-H3-Q2_K.gguf` (realrebelai) | 7.91 GB |
| Video VAE | `vae/minimax_h3_video_vae_int8_convrot.safetensors` (Comfy-Org) | ~3 GB |
| Audio VAE | `vae/minimax_h3_audio_vae_fp32.safetensors` (Comfy-Org) | ~0.6 GB |
| NSFW LoRA | `loras/AfterMidnight_ref2va_h3_sexytime_rank64-v1.2.safetensors` | 1.11 GB |

The graph is the official Comfy-Org `video_minimax_h3_r2v.json` template, converted UI→API at
runtime, with the DiT/TE/LoRA swapped in, **euler + beta** sampling (required by the AfterMidnight
card), and the LoRA switch forced on.

## Run

```powershell
# smoke test (small, short) — verifies the stack loads on 16GB and measures speed
python helpers/h3-local-host/run-h3-ref2va-proof.py --image artifacts/tmp/h3-nsfw-proof/ref-smoke.png --tag smoke

# real NSFW proof (768p, ~5s)
python helpers/h3-local-host/run-h3-ref2va-proof.py --image <subject.png> --width 1344 --height 768 --length 124 --steps 20 --tag nsfw-001

# just inspect the generated API-format workflow (no submission)
python helpers/h3-local-host/run-h3-ref2va-proof.py --image x.png --dump-only
```

Outputs go to git-ignored `artifacts/tmp/h3-nsfw-proof/<tag>/` (mp4 + `run-manifest.json` with the
measured wall-clock seconds).

## Notes

- **Valid H3 lengths** are ≈ 56 / 124 / 192 frames (24 fps) — the official template's duration
  expression produces `round(5s*24) adjusted ≡ 5 (mod 17)`; use 56 for smoke, 124 for a 5 s clip.
- **Downloads:** the host has only `curl.exe` (no `hf`/`aria2c`). HF's signed LFS redirect stalls
  when curl uses `-C -` (resume), so downloads use plain `curl -L` (no resume) — see the B-150
  research doc for the download notes.
- **LoRA-on-quantized:** the AfterMidnight LoRA is applied onto the Q4_K GGUF DiT. If that merge
  fails on the host, the fallback is the `nvfp4` safetensors DiT
  (`Abiray/MiniMax-H3-nvfp4-INT4-INT8-Convrot` → `MiniMax_H3_Ref2VA_pruned_nvfp4.safetensors`).

Related: `specs/Planning/B-150-minimax-h3-local-investigation/README.md` +
`RESEARCH-minimax-h3-16gb.md`; `helpers/wan-local-host/` (Wan 2.2 proof precedent, separate track).
