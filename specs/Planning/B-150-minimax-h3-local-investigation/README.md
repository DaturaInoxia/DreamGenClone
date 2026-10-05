# B-150 — MiniMax H3 / Hailuo local video on 16GB — investigation brief

**State:** `new` — investigation item
**Created:** 2026-10-04
**Origin:** split from B-148 Option I at the operator's request, on the claim:
*"it can be hosted on 16G, there are other checkpoints (or whatever they are called) and ComfyUI
workflows that can handle it."*
**Scope:** MiniMax H3 as a **general local video-creation producer** on the 16GB host — **MiniMax
only** (Wan 2.2 is a separate parallel track, not combined here).

---

## 1. What to determine

A measured **go/no-go** for using **MiniMax H3 (open weights)** as a **general local video-creation
producer** on the 16GB ComfyUI host — text-to-video, image / first-last-frame-to-video, and
reference-to-video. Camera-move → extracted location frames (B-148 Option I) is **one use case,
not the focus**.

## 2. The operator's claim, restated as testable hypotheses

1. **"It can be hosted on 16GB."** → Find which H3 checkpoints (GGUF / nvfp4 / convrot / pruned)
   actually load on a 16GB RTX 5080, at what speed, at what resolution/length. (External research
   already confirms the documented 12–16GB ComfyUI tier: `Q4_K_M` GGUF 10.64 GiB or `nvfp4`
   11.67 GiB DiT + TE `Q2_K` 7.91 GiB + fp8mix VAE. Only the local measurement remains.)
2. **"There are other checkpoints … that can handle it."** → Enumerate the H3 quantisation
   landscape and the ComfyUI workflows that consume them. (Landed in the research doc.)
3. **Local text-to-video.** → Confirmed native in ComfyUI core (`MiniMaxH3ImageToVideo` t2va mode,
   0.30.0+); the open question is only whether it runs end-to-end on this 0.37.1 host.
4. **General video-creation quality.** → Assess H3 as a producer for arbitrary scenes (T2V / I2V /
   first-last-frame / reference), not just the location use case.

## 3. Already-established evidence (do not re-derive)

From `../B-148-location-creation-from-moment/RESEARCH-minimax-m3-t2v.md` (2026-10-04), plus
external research in `RESEARCH-minimax-h3-16gb.md` (2026-10-04):

- **"MiniMax M3" does not exist as a video model.** M3 is a ~428B multimodal *language* model
  (`MiniMax-AI/MiniMax-M3`, arXiv:2606.13392). The video line is **Hailuo 01 → 02 → H3**
  (`minimax.io/blog/minimax-h3`).
- **H3 is open-weights at 768p short edge** (H3-Base). H3-Context-IR and H3-Regenerate-2K are
  **not** open-sourced. `MiniMax-AI/MiniMax-H3`.
- **Size:** 33B DiT + 32B text-encoder. Official SGLang example uses **4 GPUs**.
- **A community 12–16GB recipe exists:** pruned `Q4_K_M` GGUF (10.64 GiB) or `nvfp4` DiT
  (11.67 GiB) + `Q2_K` encoder (7.91 GiB) + fp8mix VAE, heavy offload, slow.
  `MiniMax-AI/awesome-minimax-h3-integration` "By VRAM and hardware" table.
- **Host node inventory (verified 2026-10-04):** local `MiniMaxH3ImageToVideo` +
  `MiniMaxH3ReferenceToVideo` (+ `AddGuide`/`FunControlNetApply`/`SigmaShift`/`EmptyMiniMaxH3LatentAV`)
  exist; **no H3 weights are downloaded**. External research confirms `MiniMaxH3ImageToVideo`
  natively covers **text-to-video (t2va)** and first/last-frame I2V, so the separate
  `MinimaxHailuo03TextToVideoNode` / `ComfyCloudMiniMaxH3TextToVideoNode` API nodes are **not**
  required for local T2V — to be confirmed on this host during measurement (I-3).
- **No camera-pose/trajectory/intrinsics control in H3.** The official node has no camera
  parameters; H3's camera move is **reference-video imitation + natural language**.
- **Licensing:** H3 is a custom Community License (commercial use needs a paid MiniMax license
  via Comfy); it excludes US/EU/UK/KR from the free grant. Assessed for H3 on its own.

## 4. What this item must investigate (open)

| # | question |
|---|---|
| I-1 | Which **quantised H3 checkpoints** fit 16GB with usable speed? (GGUF `Q4_K_M` / `nvfp4` / pruned convrot variants — download sizes, VRAM peak, seconds-per-frame.) |
| I-2 | Which **ComfyUI workflows** actually run those on a 16GB card, and do they run on **0.37.1**? |
| I-3 | Confirm **local text-to-video** runs end-to-end on this 0.37.1 host (native `MiniMaxH3ImageToVideo` t2va mode) — no API key needed. |
| I-4 | **Real measurement on the RTX 5080 (Blackwell):** VRAM peak, time-to-first-frame, frames/sec. Does Sol-Attn reduce the MLP peak meaningfully (~37% claimed, unverified)? |
| I-5 | *(Location-frame use case only)* Can H3's **reference-video camera imitation** substitute for explicit camera control? H3 has no camera-pose/trajectory control. |
| I-6 | **License / cost:** does the Community License + paid commercial tier block use here? |
| I-7 | **General video-creation capability:** assess output quality/usefulness for arbitrary scenes (T2V / I2V / first-last-frame / reference) at resolutions/lengths achievable on 16GB. |
| I-8 | **NSFW:** confirm the AfterMidnight LoRA (Ref2VA) and/or an NSFW T2V LoRA generate on the quantized base at 16GB, using the standard `Q2_K` TE (the ~24 GiB uncensored TE does not fit 16GB). |

## 5. The decision this feeds

If MiniMax H3 passes, it becomes **the local MiniMax video producer** — a general-purpose
text/image/reference-to-video capability on the 16GB host, usable by any consumer (including, but
not limited to, B-148 Option I's camera-move → location frames). If it fails (VRAM / speed /
quality), the MiniMax track is closed on its own merits.

**MiniMax-only track.** Wan 2.2 is evaluated separately and in parallel (B-151 / B-113); this item
does not weigh H3 against it.

## 6. Deliverable

A `RESEARCH-minimax-h3-16gb.md` in this directory: measured VRAM/speed numbers on the 5080,
the checkpoint/workflow list, the local T2V/I2V/R2V verdict, a general video-creation capability
assessment, and a final go/no-go. No implementation until this lands and is reviewed.

## 7. Relationship to neighbouring items

- **B-148** — one consumer (Option I camera-move → location frames); not the scope of this item.
- **B-151 / B-113** — Wan 2.2 producer (parallel, separate track — not combined here).
- **B-112** — FLUX local host + app integration (precedent for adding a host-side model).
- **B-139** — 360 capture/reprojection (the non-generative route, unaffected).
