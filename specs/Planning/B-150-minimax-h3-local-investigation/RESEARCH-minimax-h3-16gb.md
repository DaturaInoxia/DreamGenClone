# RESEARCH — MiniMax H3 on the local 16GB ComfyUI host

**Item:** B-150 (investigation)
**Status:** External research landed 2026-10-04; **local measurement on the RTX 5080 is COMPLETE**
(2026-10-05/06) — stack proven on 16GB, NSFW path proven, a 23-configuration tuning sweep run, and a
**configuration catalog kept complete and re-runnable** (see Tuning sweep below). The best
configuration is **not** considered settled; the catalog exists so any option can be re-tested.
**Next step:** application integration — see [`INTEGRATION-HANDOFF.md`](./INTEGRATION-HANDOFF.md).
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

**Final 16GB stack (all verified non-gated on HF, placed on the host's `D:\ComfyUI\models\`):**

| Role | File | Size |
|---|---|---|
| DiT (Ref2VA) | `diffusion_models/minimax_h3_ref2va_pruned_w4a8_mixed.safetensors` (Kijai) | 10.96 GB |
| Text encoder | `text_encoders/qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors` (Comfy-Org, `device=cpu`) | 14.6 GB |
| Video VAE | `vae/minimax_h3_video_vae_int8_convrot.safetensors` (Comfy-Org) | ~3 GB |
| Audio VAE | `vae/minimax_h3_audio_vae_fp32.safetensors` (Comfy-Org) | ~0.6 GB |
| NSFW LoRA | `loras/AfterMidnight_ref2va_h3_sexytime_rank64-v1.2.safetensors` + `loras/AfterMidnight_ref2va_h3_softer_rank64_v1.safetensors` | 1.11 GB each |

**Harness:** `helpers/h3-local-host/run-h3-ref2va-proof.py` — converts the official Comfy-Org
`video_minimax_h3_r2v.json` template to API format, swaps in the stack, forces **euler + beta**
(AfterMidnight requirement) and the LoRA switch on. Outputs to `artifacts/tmp/h3-nsfw-proof/`.

**⚠️ GGUF is a dead end on this host (measured 2026-10-04).** `ComfyUI-GGUF` is present in
`custom_nodes` and the `gguf` 0.19.0 package is installed, but the node's loader classes
(`UnetLoaderGGUF`/`CLIPLoaderGGUF`) are **not loaded** by the running ComfyUI 0.37.1, and core has
no `gguf` reference in `comfy/`/`nodes.py`. `.gguf` files placed in `diffusion_models/`/
`text_encoders/` never appear in the loader dropdowns. The 16GB stack therefore uses the
**nvfp4 safetensors** DiT + the official **nvfp4_awq** text encoder (`device=cpu` to stay out of
VRAM) — nvfp4 is proven on this host via `krea2_turbo_nvfp4.safetensors` (the app loads all
quantized models with `weight_dtype=default` auto-detect).

**SMOKE TEST — PASS (2026-10-04).** 768×768, 56 frames (~2.3 s), 20 steps, euler/beta, seed 1,
AfterMidnight `sexytime` LoRA strength 1.0:
- **Wall-clock: 322 s (5.4 min)** end-to-end (TE encode on CPU + 20 DiT steps + VAE decode).
- **VRAM:** ~13.6 GB during load, **~15.4 GB peak during sampling** (of 16 GB) — no OOM.
- Output: `artifacts/tmp/h3-nsfw-proof/smoke/smoke_00001_.mp4` (308 KB).

**FULL-RES NSFW RUN — PASS (2026-10-04).** 1344×768 (768p), 124 frames (~5.2 s @24 fps), 20 steps,
euler/beta, seed 1, AfterMidnight `sexytime` LoRA 1.0, real subject reference image
(`6c813ff4-…` scene image), two-person prompt:
- **Wall-clock: 782 s (13.0 min)** end-to-end.
- Output: `artifacts/tmp/h3-nsfw-proof/nsfw-002/nsfw-002_00001_.mp4` (1.72 MB) +
  extracted frames in `…/nsfw-002/frames/`.
- **Speed scaling:** ~5.4 min for 2.3 s @ 768² → ~13 min for 5.2 s @ 768p — roughly linear in
  pixels × frames. A full 15 s 768p clip ≈ **35–40 min**; the 4-step turbo LoRA would cut that
  ~5× if used instead of 20 full steps.

**QUALITY PASS — operator verdict "still looks bad" on the nvfp4 run (2026-10-05).** External
research (Reddit API hard-blocked; recovered via Brave Search snippets + HF discussions) found:
the nvfp4 DiT is implicated by a Blackwell user report ("blurred, slow movements") and the quant
author ("INT4 isn't that great"); the AfterMidnight author recommends `softer` @ 1.0 for
anatomy/detail (and `sexytime` ≤ 0.8); 25–40 steps for high-detail reference shots; the official
6-section Ref2VA prompt format ("make `detailed_description` as detailed and explicit as
possible"); CFG fixed at 1 (no negative prompt). Quality pass run `nsfw-003`:
**w4a8_mixed DiT (Kijai, 10.96 GB) + `softer` LoRA @ 1.0 + 30 steps + 6-section prompt** →
**1085 s (18.1 min)**. Output: `artifacts/tmp/h3-nsfw-proof/nsfw-003/nsfw-003_00001_.mp4` +
frames in `…/nsfw-003/frames/`. (Source URLs in the research agent report, session temp.)

**Download note (host has only `curl.exe`):** HF's signed LFS redirect stalls when curl is given
`-C -` (resume) — downloads must use plain `curl -L` (no resume), ~1 MB/s per connection, 5 files
fetched in parallel (~1–1.5 h for ~23 GB).

**RUN MATRIX (full-res 1344×768, len 124, 24 fps, seed 1, euler/beta, CPU TE, reference image
`6c813ff4-…` two-person scene):**

| Tag | DiT | LoRA | Steps | Wall-clock | Operator verdict |
|---|---|---|---|---|---|
| nsfw-002 | nvfp4 | sexytime @ 1.0 | 20 | 782 s (13.0 min) | "worked but looks horrible" |
| nsfw-003 | w4a8_mixed | softer @ 1.0 | 30 | 1085 s (18.1 min) | "better… body morphed, woman's face missing" |
| nsfw-004 | w4a8_mixed | sexytime @ 0.8 | 40 | 1390 s (23.2 min) | hardcore + follows reference; face / contact-point still weak |
| nsfw-005 | w4a8_mixed | **none** (base) | 40 | 1332 s (22.2 min) | act degraded to an undifferentiated embrace; faces slightly cleaner |

**NSFW verdict (I-8):** AfterMidnight Ref2VA LoRA generates on the 16GB stack (peak ~15.4 GB, no
OOM) and is **necessary** for a defined hardcore act — the no-LoRA run (nsfw-005) regressed the act
to a vague embrace, confirming the LoRA teaches the explicit anatomy/motion the base lacks. The
base model is uncensored-capable but its explicit-act priors are weak.

**General video-creation verdict (I-7):** go on capability, quality-limited at 16GB. The stack
reliably produces 5.2 s 768p clips in 13–23 min. Remaining quality gaps after tuning: (a) the
woman's face in this two-person reference stays angled away / obscured, (b) contact-point anatomy
and limbs still warp (a known AfterMidnight limitation), (c) 768p short edge with no upscale pass.

**Tuning conclusion (loop closed 2026-10-05):** best observed config is **w4a8_mixed DiT +
`sexytime` LoRA @ 0.8 + 40 steps + 6-section explicit prompt**. Two levers were identified but not
run before the operator closed the loop: (1) stack `h3-realism-people` LoRA (trigger `r34l1sm`)
for faces/anatomy; (2) 48–50 steps for high-detail reference shots.

---

## TUNING SWEEP — 2026-10-06 (hardcore + reference-character + quality)

Goal set by the operator: a **hardcore** scene that **uses the reference image**, **uses the
characters from the reference**, at **good quality**. MiniMax H3 only. 19 configurations run
sequentially on the host; all at 1344×768, len 124 (≈5.2 s @ 24 fps), seed 1 unless noted,
euler/beta, CPU text encoder, reference = the operator's two-person scene image `6c813ff4-…`.

### New levers added to the harness

1. **Stacked LoRAs.** `--lora-spec ALIAS[@STRENGTH]`, repeatable, chained as extra
   `LoraLoaderModelOnly` nodes. Two new adapters were fetched to the host:
   - `h3-realism-people.safetensors` — **fal MiniMax-H3-Realism-People**, rank 32, trigger
     `r34l1sm`, intended scale 1.0 (0.6–0.8 lighter), trained specifically for faces/skin/hands
     and declared to work on T2V/I2V/**R2V**.
   - `h3-facial-realism-closeup.safetensors` — prithivMLmods Facial-Realism-CloseUp.
2. **Second reference image.** The official R2V template has two reference slots and the earlier
   proofs dropped the second. `--image2` now fills it. `make-ref-crop.py` derives the crop
   deterministically from **MediaPipe FaceMesh** landmarks (same detector family as the repo's
   canonical eye-validation tool).
3. **Third reference image (optional).** `ref_images` is a `COMFY_AUTOGROW_V3` input, so `--image3`
   adds a slot on demand; the earlier template only ships two.
4. **Vulgar/explicit prompt dialect** (`prompt-v3-explicit.txt`) — the NSFW LoRA is trained on
   explicit vocabulary, so the earlier clinical wording was leaving conditioning on the table.

### Reference-image diagnostic (important)

Running FaceMesh on the operator's reference found **exactly ONE detectable face**
(box ≈ 116×130 px in a 1024² frame, upper-left). The second subject's face is not cleanly
presented in the reference. **A model cannot preserve a face the reference does not show** — this
is a large part of why "could not see the woman's face" persisted across every single-reference
run, and it is a property of the input image, not a tunable.

### Run matrix

| Tag | Prompt | LoRAs | Refs | Steps | Wall-clock | Notes |
|---|---|---|---|---|---|---|
| nsfw-002 | softcore | sexytime@1.0 | scene | 20 | 782 s | superseded |
| nsfw-003 | softcore | softer@1.0 | scene | 30 | 1085 s | superseded |
| nsfw-004 | hardcore | sexytime@0.8 | scene | 40 | 1390 s | prior best, single ref |
| nsfw-005 | hardcore | *(none)* | scene | 40 | 1332 s | base model: act degrades |
| v2-base | v2 + face-lock | sexytime@0.8 | scene | 40 | 1392 s | prompt lever alone |
| v2-real10 | v2 + trigger | sexytime@0.8 + realism@1.0 | scene | 40 | 1396 s | realism LoRA @1.0 |
| v2-real07 | v2 + trigger | sexytime@0.8 + realism@0.7 | scene | 40 | 1062 s | realism LoRA lighter |
| v2-soft-real | v2 + trigger | softer@1.0 + realism@1.0 | scene | 40 | 1064 s | softer flavor + realism |
| v2-facial | v2 + trigger | sexytime@0.8 + realism@1.0 + facial@0.6 | scene | 40 | 1069 s | 3-LoRA stack |
| v2-real10-notrig | v2 (no trigger) | sexytime@0.8 + realism@1.0 | scene | 40 | 1393 s | trigger-placement control |
| v2-steps50 | v2 + trigger | sexytime@0.8 + realism@1.0 | scene | 50 | 1686 s | more steps |
| v2-refmax | v2 + trigger | sexytime@0.8 + realism@1.0 | scene, `ref_size=max` | 40 | 1407 s | bigger ref encode |
| v3-explicit | v3 vulgar | sexytime@0.8 + realism@1.0 | scene | 40 | 1402 s | explicit dialect |
| v3-explicit-soft | v3 vulgar | softer@1.0 + realism@1.0 | scene | 40 | 1069 s | explicit + softer |
| v3-explicit-face | v3 vulgar | sexytime@0.8 + realism@1.0 + facial@0.6 | scene | 40 | 1069 s | explicit + facial |
| dual-scene-head | v3 vulgar | sexytime@0.8 + realism@1.0 | **scene + head crop** | 40 | 1790 s | dual ref |
| dual-scene-subject | v3 vulgar | sexytime@0.8 + realism@1.0 | **scene + subject crop** | 40 | 1784 s | dual ref |
| dual-head-only | v3 vulgar | softer@1.0 + realism@1.0 | subject crop only | 40 | 1402 s | tight single ref |

### Chunk 6 — seed variance, steps, length

| Tag | LoRAs | Refs | Steps/Len | Wall-clock | Notes |
|---|---|---|---|---|---|
| dual-subj-s2 | sexytime@0.8 + realism@1.0 | scene + subject | 40 / 124 | 1788 s | seed 2 |
| dual-subj-s3 | sexytime@0.8 + realism@1.0 | scene + subject | 40 / 124 | 1159 s | seed 3 |
| dual-head-s2 | sexytime@0.8 + realism@1.0 | scene + head | 40 / 124 | 1801 s | seed 2 |
| dual-subj-facial | + facial@0.6 | scene + subject | 40 / 124 | 1802 s | 3 LoRAs + 2 refs |
| dual-subj-steps50 | sexytime@0.8 + realism@1.0 | scene + subject | 50 / 124 | 1443 s | dual ref at 50 steps |
| dual-subj-long | sexytime@0.8 + realism@1.0 | scene + subject | 40 / **192** | 2927 s | **8.0 s clip**, usable length |

### Chunk 7 — third reference slot

`MiniMaxH3ReferenceToVideo.ref_images` is a **`COMFY_AUTOGROW_V3`** input, so it accepts more than
the two slots the official template ships. The harness now supports a third (`--image3`), created
as an extra `LoadImage` wired to `ref_images.ref_image_2`.

| Tag | Refs | Steps/Len | Wall-clock | Notes |
|---|---|---|---|---|
| tri-scene-subj-head | scene + subject + head (3) | 40 / 124 | 2008 s | 3 refs cost ~200 s over 2 |
| dual-subj-s4 | scene + subject (2) | 40 / 124 | 1786 s | seed 4 |
| tri-hero-long | scene + subject + head (3) | 40 / **192** | 3191 s (53 min) | **8.0 s hero clip** |

### Chosen production configuration

```
DiT          minimax_h3_ref2va_pruned_w4a8_mixed.safetensors   (quantized w4a8)
Text encoder qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors, device=cpu
LoRAs        AfterMidnight_ref2va_h3_sexytime @ 0.8
             + h3-realism-people (fal) @ 1.0
References   <Picture 1> scene image  +  MediaPipe subject crop (2 refs)
Prompt       6-section R2V, vulgar/explicit dialect, 'r34l1sm' trigger on line 1
Sampling     euler / beta, CFG 1.0, 40 steps, denoise 1.0, seed 1
Output       1344x768, 124 frames (~5.2 s) — or 192 frames (~8.0 s)
Cost         ~23-30 min per 5.2 s clip; ~53 min per 8.0 s clip
```

Three references remain available (`--image3`) and add ~200 s, but the two-reference setup is the
practical choice; the third did not clearly separate itself from it.

### Honest limits of this result- **Both faces in frame at once is still the weak spot.** This is bounded by the input: the
  reference presents one clean face, and no prompt or LoRA recovers a second face the reference
  never showed. Feeding a reference where both subjects' faces are visible is the real fix.
- **Contact-point anatomy still warps** at the bodies' join — a documented limitation of the
  AfterMidnight adapters ("all genitalia and acts comes out weird"), not a setting.
- **768p ceiling.** H3-Base is open-weights at 768p short edge; there is no local upscale pass in
  this harness, so 1344×768 is the native output. A 2K path would need H3-Regenerate-2K, which is
  **not** open-sourced.
- **What is solid:** the act is unambiguously hardcore, the scene composition follows the
  reference, both subjects are recognisably the reference's two people, and skin/faces are
  markedly better with the fal realism adapter than any single-LoRA configuration.

---

## AUDIO — 2026-10-06 (native synchronized audio)

H3 is omni-modal: **video and audio are generated jointly in one forward pass**, stereo, muxed into
the same MP4. Audio was therefore already being produced by every run above; it had simply never
been inspected (frames cannot show it). Measured with the new approved tool
[`tools/video-audio-inspection/`](../../../tools/video-audio-inspection/README.md).

### What is there

Every clip carries a **stereo AAC track, 32 kHz, 128 kb/s**: 5.17 s on the 124-frame runs, 8.00 s on
the 192-frame runs — i.e. exactly the video duration. This is not a silent stream.

Measured character of three runs:

| Run | RMS | Range | Harmonicity | Flatness | Band energy % |
|---|---|---|---|---|---|
| v3-explicit | −33.9 dBFS | 6.2 dB | 0.847 | 0.016 | sub 2.2 · body 21.7 · **voice 41.6** · pres 24.4 · air 6.8 |
| dual-scene-subject | −34.8 dBFS | 5.7 dB | 0.857 | 0.013 | sub 2.4 · body 19.1 · **voice 42.5** · pres 25.3 · air 6.3 |
| dual-subj-long | −30.5 dBFS | 25.0 dB | 0.856 | 0.004 | sub 0.1 · body 0.3 · **voice 76.1** · pres 21.8 · air 1.7 |

### What that means

- **Moans and vocalisation are present and prominent.** Harmonicity ≈ 0.85 with 42–76 % of energy in
  the 400–2000 Hz vocal-formant band is the signature of **voiced human sound**. Flatness near zero
  confirms tonal/harmonic content rather than noise. The `overall_soundscape` lines asking for moans
  and gasps were honoured.
- **The body/impact band is the gap.** `sub<100 Hz` is 0.1–2.4 % and drops to **0.1 %** on
  `dual-subj-long`, with only 0.3 % at 100–400 Hz. The low, physical weight of the track — skin
  contact, body impact, bed movement — is largely absent; the model defaults to a near-pure
  vocalisation. This is the measurable reason the audio reads as "moans over nothing".
- **The 8 s run has a real arc** (25.0 dB range, ramping −48.5 → −23.5 dBFS) versus the flat 5–6 dB
  of the 5 s runs — longer length gives the audio somewhere to go.

### ⚠️ The audio is far too quiet — a real pipeline defect

Measured as **integrated loudness (LUFS)**, not just RMS. H3's native output is well below any normal
delivery level:

| Clip | Native | After `loudnorm` to −16 LUFS |
|---|---|---|
| `dual-subj-long` | −24.3 LUFS | −17.1 LUFS |
| `dual-scene-subject` | **−30.4 LUFS** | −17.0 LUFS |
| `v3-explicit` | **−29.4 LUFS** | −17.1 LUFS |

For reference: typical speech video sits near −19 LUFS and streaming targets −14 LUFS. So most clips
are **~12 dB too quiet**. They decode correctly and playback fine, but at normal player volume they
are easy to mistake for silence. **Any integration must add a loudness-normalization step**
(`loudnorm=I=-16:TP=-1.5` is a reasonable target) rather than storing H3's raw level. Note that
setting `-ar` explicitly: `loudnorm` otherwise resamples to 96 kHz, which is unnecessary and less
compatible.

### Playback caveat (tooling, not the model)

**Review generated video in a real player (VLC), not the VS Code preview.** VS Code's Electron build
lacks an AAC decoder, so an MP4 whose audio is AAC plays silently there while the same file plays
correctly in VLC. This was misdiagnosed as a container/codec defect on 2026-10-06 and cost a full
detour through six re-encodings before the player was identified. The files were never broken. If a
clip appears silent, **confirm the player before investigating the pipeline.**

### Official prompt rules that were being violated

Read from the ComfyUI H3 guide + MiniMax's own
`VIDEO_PROMPT_WRITING_GUIDE_ref_en.md` / `VIDEO_PROMPT_WRITING_GUIDE_base_en.md`. The R2V rewrite
format is exactly the six sections already in use, but the audio rules were not being followed.
Nine rules, all now fixed in `prompt-v4-audio.txt`:

1. **`non_diegetic_music` must be `N/A`** when there is no audience-only score. We were writing
   `None.`, which is not the documented sentinel. (`overall_soundscape` uses `N/A` only when the clip
   should be completely silent.)
2. **Dialogue does not belong in `overall_soundscape`.** That field covers ambient sound, physical
   action sounds and **non-verbal** human sounds. Spoken lines go in `detailed_description`.
3. **Dialogue must be wrapped in `<d>` tags** with a language tag inside:
   `<Subject 2> (S2) says, <d>[English] Don't stop.</d>`. Everything else — identifying phrase,
   speaker ID, action, delivery — stays **outside** the tags.
4. **Speaker IDs `(S1)`, `(S2)` are required**, assigned once in order of actual vocal event, reused
   at every vocal event. IDs number *speakers*, not subjects; the first voice is `(S1)` even if its
   subject is numbered differently. A speaker who never vocalizes gets **no** ID.
5. **A speaker's first appearance needs identity information** — character type, age, gender,
   on-screen/off, pitch, timbre, speaking rate or accent — so the voice can be established stably.
6. **Bans must be written as positives.** A `BasicGuider` at CFG 1 has no negative branch, so
   `"no text and no watermark"` **adds those words to the description** — it registers text as
   content rather than removing it. This was present in every earlier prompt.
7. **`detailed_description` should run 350–500 words** for generation tasks (dialogue-dense content
   may prioritize the spoken timeline instead). Ours was ~150.
8. **The overall style is declared at the very start of `[Shot 1]`** (e.g. `Live-action, cinematic,
   a medium close-up frames…`). Ours omitted it.
9. **A `[Shot N]` for N>1 requires a strictly increasing cut timestamp** (`At 00:03.500, the camera
   cuts to…`). A continuous shot must therefore stay inside `[Shot 1]` — a second shot label with no
   cut is malformed.

Also noted from the same guides: `overall_soundscape` is **1–4 sentences**; `non_diegetic_music` is
**1–3 sentences** and must describe instrumentation/speed/rhythm/dynamics, **not abstract mood words**;
a line truncated by the end of the clip uses `<cutoff>`; a line crossing a cut uses `<scenetrans>` at
both connection points; and R2V audio references need a retention marker (`fully_copy`,
`partially_copy`, `reference`, `weak_reference`) that matches what the audio actually does.

### Why the body sounds are missing — the actionable diagnosis

The guide's own soundscape vocabulary is **footsteps, fabric movement, impacts, breathing, laughter,
panting**, and every worked example names a **concrete object plus a physical action** ("wet
footsteps", "the soft scrape of a chair", "trays clink softly inside the bakery"). Our earlier
wording described **abstract qualities** instead — "a wet, heavy, low impact sound". The model had no
concrete physical event to render, which is consistent with the measured sub-100 Hz almost-absence.
The revised prompt names the objects and events: *the bed frame knocks against the wall*, *the
mattress compresses and creaks*, *the sheets bunch and drag under her hands*, *skin slaps against
skin*.

### The 10 style embeddings are visual only

`Comfy-Org/MiniMax-H3/embeddings` ships 10 prompt embeddings — `art_is_explosion`, `blooming_flowers`,
`bullet_time`, `dark_magic`, `fire_breath`, `four_seasons`, `kiss_camera`, `spiral_ascent`,
`storm_magic`, `truman_show`. **None is audio-related**, so embeddings are not an audio lever.

### Voice reference (unused lever)

`MiniMaxH3ReferenceToVideo` accepts, via the same autogrow mechanism as images, up to **3 standalone
reference audio clips** (`ref_audios`) plus 3 reference videos with paired soundtracks, and up to
**9 reference images** (not 2). Docs state references can lock "a character's identity, a style, a
motion, a camera move, **or a voice**". So a per-character voice can be pinned by supplying an audio
clip and declaring `reference` (timbre/delivery only) or `fully_copy`/`partially_copy` (reuse the
signal). Not yet exercised in this harness.

### Audio test configurations added (chunk 8)

Prompt `prompt-v4-audio.txt` fixes all nine points above and adds four spoken lines using both
characters' names as a single continuous shot. Configurations `audio-v4`, `audio-v4-long`
(192 frames) and `audio-v4-soft` (softer flavour A/B) are queued in the catalog.

**Not yet run — awaiting operator go-ahead.** The revised prompt validates clean against every rule
above (385-word description, 4-sentence soundscape, style declared, no malformed `[Shot 2]`, four
`<d>` blocks each preceded by a speaker ID, no negative phrasing).



### Findings

- **Three-LoRA stacking works** on the quantized w4a8 DiT (`v2-facial`, `v3-explicit-face`) and
  costs nothing measurable in wall-clock.
- **Dual reference works** and costs ~400 s extra (a second reference has to be encoded) —
  ~30 min/run instead of ~23 min.
- **`r34l1sm` trigger placement is not decisive** in the structured 6-section prompt
  (`v2-real10` vs `v2-real10-notrig`: 1396 s vs 1393 s, visually comparable).
- **Realism LoRA** is the consistent win on skin/faces; `sexytime` remains necessary for a
  defined hardcore act (`nsfw-005` confirmed the base cannot render one).
- **Dual reference (scene + subject crop) is the strongest identity/face configuration found**,
  which is exactly what the one-face diagnostic predicts: it is the only setup that supplies the
  model with extra pixels for the faces.
- **A third reference is accepted** (`ref_images` autogrows) and adds ~200 s, but did not clearly
  beat two — the crop's own pixel budget is already spent at two.
- **Seed variance is large.** The same config ranged 1159 s–1788 s and produced visibly different
  takes; seed choice matters more than several of the parameter levers.
- **`softer` is consistently faster** than `sexytime` (~1065 s vs ~1395 s at identical settings).
- **50 steps** costs ~1686 s for a marginal gain — poor value against 40.
- **192 frames (8.0 s) is valid and works** — ~53 min per clip with three references.

### Chosen production configuration

`w4a8_mixed` DiT · `sexytime@0.8` + `realism@1.0` · **two references (scene + subject crop)** ·
v3 explicit 6-section prompt with `r34l1sm` · 40 steps · euler/beta · CFG 1 · 1344×768 · len 124.


**Open measurement items (not blocking the go/no-go):** Sol-Attn MLP-peak claim unverified here;
local T2V (t2va) and first/last-frame I2V not yet measured end-to-end (only Ref2VA ran); a full
15 s clip ≈ 35–40 min at 20 steps (or ~5× less with the 4-step turbo LoRA, at a quality cost).
I-5 (camera-imitation adequacy) remains unassessed — it only matters for the location-frame use
case, not the general producer track.

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
