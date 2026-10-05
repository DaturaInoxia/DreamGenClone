# RESEARCH — MiniMax H3 on the local 16GB ComfyUI host

**Item:** B-150 (investigation)
**Status:** External research landed 2026-10-04; **local measurement on the RTX 5080 is PENDING**.
**Scope:** MiniMax H3 as a **general local video-creation producer** on the 16GB host (T2V / I2V /
first-last-frame / reference). **MiniMax-only** — Wan 2.2 is a separate parallel track, not
combined here.
**Question this phase answers:** Can MiniMax H3 run on *local ComfyUI*, and what does it take to
run it on a 16GB host?

---

## TL;DR

- **Can H3 run on local ComfyUI? Yes — natively.** H3 support is in **ComfyUI core** (not a
  third-party node package). `MiniMaxH3ImageToVideo` (T2V + first/last-frame I2V) and
  `MiniMaxH3ReferenceToVideo` (omni-reference) ship in core from **ComfyUI 0.30.0+**; the official
  file host is `Comfy-Org/MiniMax-H3`. Local **text-to-video is fully supported** with open weights.
- **For our 16GB RTX 5080 (Blackwell), the documented path is the 12–16GB ComfyUI tier:**
  pruned `Q4_K_M` GGUF (10.64 GiB) **or** pruned `nvfp4` (11.67 GiB) DiT + TE `Q2_K` (7.91 GiB) +
  fp8mix VAE pair. This confirms "it can be hosted on 16GB" — with exact sizes; the local
  measurement (speed / VRAM headroom) is what remains.
- **Camera control is a capability note, not a disqualifier here:** H3 has **no
  camera-pose/trajectory/intrinsics control** — camera movement is reference-video imitation +
  natural language only. This only matters for the location-frame use case; for general video
  creation it is not a blocker.

---

## 1. "By VRAM and hardware" — the authoritative table

Primary source: `MiniMax-AI/awesome-minimax-h3-integration`, README §"By VRAM and hardware"
(<https://github.com/MiniMax-AI/awesome-minimax-h3-integration>). Quoted verbatim in source order:

| Situation | Stack |
|---|---|
| **24 GB, first run** | `pruned_int8_convrot` DiT (19.53 GiB) + TE `nvfp4_awq` (14.61 GiB) + `ComfyUI-MiniMaxH3-Easy` |
| **24 GB, want speed** | Above + `TE-Speed-MiniMaxH3` + Turbo `v4_step600_ema` at 6–8 steps |
| **22 GB, Turing (sm_75)** | `minimax-h3-turing` — W4A8 mixed + Turbo 4-step (5.7 min/clip baseline; 210 s/clip tuned) |
| **12–16 GB** | Pruned `Q4_K_M` GGUF (**10.64 GiB**) or pruned `nvfp4` (**11.67 GiB**) + TE `Q2_K` (**7.91 GiB**) + fp8mix VAE pair. (`IQ1_S` is 3.78 GiB but quality drops.) |
| **8 GB** | `DiffSynth-Studio` NF4 path — *"Offloading performs most work here; expect slow performance."* |
| **RTX 50-series / Blackwell** | `NVIDIA Sol-Attn` — 1.14–1.44× faster than SageAttention, −37% MLP peak VRAM (measured on 5090); unlocks Blackwell-only hybrid-NVFP4 |
| **H200 / B200** | `OpenVDN/vdn-minimax-h3` (datacenter) |
| **Multi-shot / long video** | `ComfyUI-H3-Motion-Context` (feeds previous block's final frame + audio forward) |
| **Pose / depth / edge control** | `alibaba-pai/MiniMax-H3-Fun-Controlnet-Union` + `ComfyUI-H3-FunControl` (~6.8 GiB control branch: Canny/Depth/HED/MLSD/Pose + inpainting) |

Key points:

- The table is quoted verbatim from the source; its sub-12GB (DiffSynth-Studio) row is **not
  relevant to this 16GB host** and is out of scope here.
- **The 12–16GB ComfyUI tier is exactly confirmed**: 10.64 / 11.67 / 7.91 **GiB** (the brief's
  "~10.6 / ~11.7 / ~7.9 GB" is the same value, GiB vs GB nuance).
- The **RTX 50-series / Blackwell** row is directly applicable to the 5080: `NVIDIA Sol-Attn`
  (−37% MLP peak VRAM, 1.14–1.44× faster than SageAttention on a 5090).

## 2. Host fit (16GB)

The operator's point was that H3 can run on the 16GB host — **confirmed**. The documented ComfyUI
tier for 12–16GB cards is the exact recipe in §1: pruned `Q4_K_M` GGUF **10.64 GiB** or pruned
`nvfp4` **11.67 GiB** DiT + TE `Q2_K` **7.91 GiB** + fp8mix VAE pair (heavy offload, slow).
Sub-12GB is not relevant to this host and is out of scope. The remaining question is **measured
speed and VRAM headroom** on the 5080 (pending — see PENDING section).

## 3. Quantized / community checkpoints (Hugging Face)

All rows from `MiniMax-AI/awesome-minimax-h3-integration` README (Quantized Models / GGUF / Text
encoders / VAE sections). Sizes in GiB as listed.

**FL2VA DiT — low-VRAM quantized variants**

| Publisher | Quant | Size | Link |
|---|---|---|---|
| unsloth | Pruned Q4_K_M | 10.64 GiB | huggingface.co/unsloth/MiniMax-H3-GGUF |
| unsloth | Pruned Q2_K / Q3_K | 6.26 / 8.16 GiB | same |
| unsloth | Pruned Q5_0 / Q6_K / Q8_0 | 12.97 / 15.45 / 19.97 GiB | same |
| MarxistLeninist | Pruned IQ1 | (IQ1_S = 3.78 GiB) | huggingface.co/MarxistLeninist/MiniMax-H3-FL2VA-Pruned-IQ1-GGUF |
| Abiray | Pruned nvfp4 | 11.67 GiB | huggingface.co/Abiray/MiniMax-H3-nvfp4-INT4-INT8-Convrot |
| Abiray | Pruned mixed INT4/INT8 ConvRot | 14.81 GiB | same |
| Merserk | Pruned INT4 ConvRot | 10.56 GiB | huggingface.co/Merserk/MiniMax-H3-INT4-ConvRot |
| rockerBOO | nvfp4 / nvfp4+ConvRot INT8 / INT4 ConvRot | 18.69 / 18.69 / 15.67 GiB | huggingface.co/rockerBOO/minimax-h3-nvfp4 |
| AX1Y2JP | W4A8 ConvRot | 11.68 GiB | huggingface.co/AX1Y2JP/MiniMax-H3-W4A8-ConvRot |
| Kijai | w4a8 mixed | 11.68 GiB | huggingface.co/Kijai/MiniMax-H3-experimental |
| DiffSynth-Studio | NF4 (not pruned) | 15.98 GiB | huggingface.co/DiffSynth-Studio/MiniMax-H3-NF4 |
| WaveCut | OrbitQuant W4A4 | 17.03 GiB | huggingface.co/WaveCut/MiniMax-H3-OrbitQuant-W4A4 |
| Comfy-Org | Pruned int8 convrot (official repackage) | 19.53 GiB | huggingface.co/Comfy-Org/MiniMax-H3 |

**Text encoder (Qwen3-VL-32B) — smallest options**

| Publisher | Quant | Size | Link |
|---|---|---|---|
| realrebelai | **Q2_K — smallest TE published** | 7.91 GiB | huggingface.co/realrebelai/MiniMax-H3_GGUFs |
| Comfy-Org | nvfp4_awq (smallest official; non-Blackwell) | 14.61 GiB | huggingface.co/Comfy-Org/MiniMax-H3 |
| unsloth | Q2_K_M / Q4_K_M | 12.2 / 17.0 GiB | huggingface.co/unsloth/MiniMax-H3-GGUF |

**VAE (the "fp8mix" pair in the 12–16GB recipe)**

| Publisher | Component | Precision | Size | Link |
|---|---|---|---|---|
| dummy9996 | Video VAE | fp8mix | 2.60 GiB | huggingface.co/dummy9996/minimax_h3_vae_fp8 |
| dummy9996 | Audio VAE | bf16 | 289 MiB | same |
| Comfy-Org | Video / Audio VAE | fp16 / fp32 | 4.85 GiB / 577 MiB | huggingface.co/Comfy-Org/MiniMax-H3 |
| Kijai | Video VAE int8 convrot | int8 | 2.95 GiB | huggingface.co/Kijai/MiniMax-H3-experimental |

Warning from the source: `DeepBeepMeep/MiniMax-H3` (quanto-INT8 TE, 24.89 GiB) has **"no
license"**; VDN/PDD quants are not interchangeable with base quants.

## 4. ComfyUI nodes, workflows, and version requirements

**H3 support is native in ComfyUI core — there is no "Kijai ComfyUI-MiniMaxH3" node package.**
Kijai's H3 contributions are the `ComfyUI-SolAttn_triton` attention backend, `Kijai/MiniMax-H3-
experimental` checkpoints, and `ComfyUI-KJNodes` commits (e.g. "Fix MiniMax H3 VRAM Attention",
"Tiny VAE for H3").

Native node names and mode coverage (per <https://docs.comfy.org/tutorials/video/minimax/minimax-h3>):

- **`MiniMaxH3ImageToVideo`** — text-to-video (t2va) and first/last-frame image-to-video (fl2va).
- **`MiniMaxH3ReferenceToVideo`** — reference-to-video (ref2va): images + video + audio.
- **`MiniMaxH3SigmaShift`**, **`MiniMaxLowVRAMAttention`**, **`MiniMaxChunkFeedForward`**,
  **`ModelAttentionBackend`**, **`Apply MiniMax H3 Fun ControlNet`**.

**Local T2V verdict: YES.** All three base modes run fully locally with open weights.

**Version requirements (docs.comfy.org):**

| Feature | ComfyUI min |
|---|---|
| Base T2V / I2V / R2V templates | 0.30.0+ |
| Multiframe Reference | 0.34.0+ |
| Fun ControlNet Union / sparse attention / Model Attention Backend | 0.35.0+ |
| FastH3 | 0.36.0+ |

**Third-party node packages:** `comfyui-minimax-h3-audio-T8` (62 nodes), `ComfyUI-MiniMaxH3-Easy`,
`ComfyUI_H3_MiniMaxH3_Director` (timeline editors), `ComfyUI-H3-FunControl`,
`ComfyUI-H3-Motion-Context`, `ComfyUI-VDN-H3`. **VRAM-optimized example workflows:**
`javawock7618/comfy-MiniMax-H3-workflows` (bundles the low-VRAM acceleration stack).

## 5. Camera control capabilities

**H3 has NO camera-pose/trajectory/intrinsics control.** Camera direction is limited to:

1. **Reference-video imitation** — docs list "Lock a camera move from reference materials" as an
   R2V capability.
2. **Natural-language camera direction** — prompt structure includes "The camera pushes in with
   small amplitude at slow speed…"; community `ComfyUI-MiniMaxDirector` compiles
   "camera = motion type × amplitude × speed".
3. **Fun ControlNet Union** — Canny / Depth / HED / MLSD / **Pose** (+ inpainting) only. "Pose"
   here is **human-body pose, not camera pose**. No extrinsics/intrinsics.

Prompt embeddings like `minimaxh3_kiss_camera` / `bullet_time` are style/motion embeddings, not
camera-parameter control.

**Scope note:** this is a *capability note*, not a disqualifier. It only matters for the
location-frame use case (B-148 Option I), where an explicit camera trajectory is needed; for
general video creation it is not a blocker. That use case is evaluated in its own right; this item
is the general MiniMax video track.

## 6. License

- **MiniMax H3 Community License** (release Aug 2, 2026; <https://huggingface.co/MiniMaxAI/MiniMax-H3/blob/main/LICENSE>):
  - **Excluded Territories = EU, UK, Republic of Korea, United States** (the license applies
    "worldwide, excluding the Excluded Territories").
  - Commercial use allowed within Applicable Territory, but **>US$20M/yr revenue requires prior
    written authorization from MiniMax** (api@minimax.io).
  - §V.3 forbids using H3 outputs to improve other AI models.
- **Paid Comfy local license** (<https://comfy.org/minimax/license.md>): Comfy is the only official
  reseller of MiniMax commercial-use licenses for local hardware. **Professional: from
  $5,000/month** (~46,250 video-seconds, ≤10 users, distilled open-weight versions); **Enterprise**:
  custom, undistilled. Comfy Cloud subscriptions already include commercial rights.

## 7. NSFW handling

**Local H3 has no runtime moderation.** The official repo's "Safety Guardrails" (automated
moderation that blocks "pornographic" content) live in the **hosted H3-Context-IR** preprocessing
system, which is **not open-sourced** — so running H3-Base locally in ComfyUI bypasses that layer
entirely. The remaining gate is the **text encoder**: H3 uses the full pretrained **Qwen3-VL-32B**
(a safety-aligned VLM) as its H3-Encoder, so NSFW prompts are conditioned through it (hence the
community "uncensored" TE below). The DiT + VAEs do not add a refusal layer.

Verified NSFW ecosystem on Hugging Face (all `not-for-all-audiences`):

| Resource | Type | License | Notes |
|---|---|---|---|
| `SexGod1979/PinkCherry-NSFW_MiniMax-H3` (425 likes) | Full NSFW fine-tune of the DiT (T2V/FL2VA) | Apache-2.0 | Use the FL2VA template + euler/simple; pair with lightx2v Turbo LoRA |
| `SexGod1979/AfterMidnight-MiniMax-H3-NSFW` (192 likes) | NSFW LoRA for **Ref2VA** | Apache-2.0 | Two flavors: `sexytime` (strength 1.0, coherent motion) / `softer` (0.8–1.0, more detail). Requires **euler sampler + beta scheduler** or audio breaks |
| `linjian257/qwen3vl_32b_minimax_h3_int8_convrot_uncensored` (51 likes) | **Uncensored** text encoder (int8 convrot) | personal-entertainment-use-only | ~24 GiB — does **not** fit a 16GB card alongside the DiT; not commercial |
| `RunningHubAI/rh-minimax-h3-astro-nsfw-lora` | NSFW LoRA (ComfyUI) | — | small LoRA |

**Practical path for the 16GB host:** the LoRA route rides the same quantized base DiT (§1 12–16GB
recipe), so it fits VRAM. `AfterMidnight` (Ref2VA LoRA) + quantized base + the standard `Q2_K` TE
(7.91 GiB) is the lowest-VRAM NSFW setup. A full PinkCherry fine-tune would need a quantized
reupload (none surfaced in the HF search — only full-weight copies and LoRA extracts), so the LoRA
route is the realistic 16GB option. The ~24 GiB "uncensored" TE is too large for this host.

**License caveat:** PinkCherry / AfterMidnight are tagged Apache-2.0, but they are **derived from
the H3 base**, whose MiniMax H3 Community License still applies — including the US/EU/UK/KR
commercial-use exclusion (§6). The uncensored TE is personal-entertainment-use-only. A finetune's
Apache tag does not waive the base model's terms.

---

## MEASUREMENT — in progress on WOOD-GAME-MAIN (2026-10-04)

Host verified live: **WOOD-GAME-MAIN**, RTX 5080 16 GB (16303 MiB), ComfyUI **0.37.1**, PyTorch
2.14.0+cu130, 64 GB RAM, reachable at `http://192.168.0.11:8188` + SSH (`~/.ssh/dgcomfy_ed25519`).
Native H3 nodes (`MiniMaxH3ImageToVideo`, `MiniMaxH3ReferenceToVideo`, …) are present in core;
`ComfyUI-GGUF` custom node is installed, so GGUF DiT/TE load via the core `UNETLoader`/`CLIPLoader`.

**Chosen 16GB stack (all verified non-gated on HF, placed on the host's `D:\ComfyUI\models\`):**

| Role | File | Size |
|---|---|---|
| DiT (Ref2VA) | `unet/minimax_h3_ref2va_pruned-Q4_K.gguf` (unsloth) | 10.6 GB |
| Text encoder | `clip/qwen3vl-32B-MiniMax-H3-Q2_K.gguf` (realrebelai) | 7.91 GB |
| Video VAE | `vae/minimax_h3_video_vae_int8_convrot.safetensors` (Comfy-Org) | ~3 GB |
| Audio VAE | `vae/minimax_h3_audio_vae_fp32.safetensors` (Comfy-Org) | ~0.6 GB |
| NSFW LoRA | `loras/AfterMidnight_ref2va_h3_sexytime_rank64-v1.2.safetensors` | 1.11 GB |

**Harness:** `helpers/h3-local-host/run-h3-ref2va-proof.py` — converts the official Comfy-Org
`video_minimax_h3_r2v.json` template to API format, swaps in the stack, forces **euler + beta**
(AfterMidnight requirement) and the LoRA switch on. Outputs to `artifacts/tmp/h3-nsfw-proof/`.

**Download note (host has only `curl.exe`):** HF's signed LFS redirect stalls when curl is given
`-C -` (resume) — downloads must use plain `curl -L` (no resume), ~1 MB/s per connection, 5 files
fetched in parallel (~1–1.5 h for ~23 GB).

Still to run before the go/no-go closes:

- **I-1 / I-4:** measured **VRAM peak, time-to-first-frame, seconds-per-frame** on the 5080
  (smoke: 768×768 len 56; then 768p len 124). Sol-Attn MLP-peak claim remains unverified here.
- **I-2 / I-3:** confirm the GGUF DiT + GGUF TE + int8 VAE load on **0.37.1** and produce a clip.
- **I-8 (NSFW):** confirm `AfterMidnight` (Ref2VA LoRA) generates on the Q4_K base at 16GB.
- **I-7:** general video-creation quality at achievable resolutions/lengths.
- **I-5:** *(location-frame use case only)* camera-imitation adequacy.

---

## Sources

1. <https://github.com/MiniMax-AI/awesome-minimax-h3-integration> — "By VRAM and hardware" table, quantized-model tables, ComfyUI nodes, licenses.
2. <https://github.com/MiniMax-AI/MiniMax-H3> — official repo (33B Omni-Transformer, Qwen3-VL-32B encoder, 768p), SGLang 4-GPU examples, license pointer.
3. <https://docs.comfy.org/tutorials/video/minimax/minimax-h3> — native support, version requirements, commercial-license note.
4. <https://docs.comfy.org/tutorials/video/minimax/minimax-h3-native> — `MiniMaxH3ImageToVideo` / `MiniMaxH3ReferenceToVideo` node names.
5. <https://docs.comfy.org/tutorials/video/minimax/minimax-h3-fun-controlnet> — Fun ControlNet Union (Canny/Depth/HED/MLSD/Pose).
6. <https://huggingface.co/MiniMaxAI/MiniMax-H3> — official model card (768p short edge, 24 FPS, 4–15s).
7. <https://huggingface.co/MiniMaxAI/MiniMax-H3/blob/main/LICENSE> — full license text.
8. <https://huggingface.co/Comfy-Org/MiniMax-H3> — official ComfyUI repackage + file list.
9. <https://comfy.org/minimax/license.md> — paid commercial license tiers.
10. Quantized checkpoint repos: `unsloth/MiniMax-H3-GGUF`, `realrebelai/MiniMax-H3_GGUFs`, `Abiray/MiniMax-H3-nvfp4-INT4-INT8-Convrot`, `MarxistLeninist/MiniMax-H3-FL2VA-Pruned-IQ1-GGUF`, `rockerBOO/minimax-h3-nvfp4`, `Merserk/MiniMax-H3-INT4-ConvRot`, `Kijai/MiniMax-H3-experimental`, `DiffSynth-Studio/MiniMax-H3-NF4`, `WaveCut/MiniMax-H3-OrbitQuant-W4A4`, `dummy9996/minimax_h3_vae_fp8`, `lightx2v/Minimax-h3-Turbo` (on huggingface.co).
