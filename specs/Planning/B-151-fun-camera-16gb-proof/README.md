# Proof plan — Wan 2.2 Fun-Camera on 16GB (camera-move → location frames)

> **This is a handoff document for a separate session. Do not execute here.**
>
> Status: plan written 2026-10-04, awaiting a dedicated proof session.
> Purpose: prove that **Wan 2.2 Fun-Camera** (the model behind `WanCameraEmbedding` /
> `WanCameraImageToVideo`) can be made to run on the local **16GB RTX 5080**, and that it produces
> a **consistent camera move** whose extracted frames can serve as multi-POV location images
> (B-148 Option I).
>
> Background and sources: `../B-148-location-creation-from-moment/RESEARCH-minimax-m3-t2v.md`.
> The wider option set: `../B-148-location-creation-from-moment/OPTIONS-consistent-location.md`
> Option I. **This is now its own backlog item: B-151.**

---

## 0. The question, stated precisely

Two separate risks must be retired, in order:

1. **VRAM risk** — can the 14B Fun-Camera model be made to fit and run on 16GB at all?
2. **Consistency risk** — if it runs, do the extracted frames actually show the *same room from a
   controlled camera trajectory* (not a drifting re-invention)?

Both must pass for Option I to be worth building. If only the first fails, the producer defaults to
Wan2.2-TI2V-5B / I2V (text-prompted camera). If only the second fails, the option is dead.

**Scope guard (the session's hard-won lesson):** this proof measures **camera trajectory and
inter-frame consistency**, NOT identity preservation. A generated video cannot preserve a real
room's identity; do not test that and do not claim it. Test it on a scene whose *consistency* can be
objectively judged.

---

## 1. Environment facts (verified 2026-10-04 — do not re-derive)

| fact | value |
|---|---|
| host | `WOOD-GAME-MAIN` @192.168.0.11, ComfyUI **0.37.1**, RTX **5080 16GB**, 64GB RAM |
| SSH | `ssh -i "$env:USERPROFILE\.ssh\dgcomfy_ed25519" wood-game-main\kenac@192.168.0.11` (batch mode works) |
| already on disk | `wan2.2_vae.safetensors`, `umt5_xxl_fp8_e4m3fn_scaled.safetensors`, `CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors`, `Wan2.2_LightX2V_high_n54vv.safetensors` / `_low_`, `wan2.2_ti2v_5B_fp16.safetensors`, `Wan2.2_I2V_High_R1` / `_Low_R1` |
| camera nodes present | `WanCameraEmbedding` (`camera_pose` + `fx/fy/cx/cy` + `speed`), `WanCameraImageToVideo` (`start_image`, `camera_conditions`, `length`) |
| related Wan control nodes | `WanFunControlToVideo`, `Wan22FunControlToVideo`, `WanMoveTrackToVideo`, `WanTrackToVideo`, `WanFirstLastFrameToVideo`, `WanImageToVideo` |
| **missing** | **`wan2.2_fun_camera_high_noise_14B_*.safetensors` + `wan2.2_fun_camera_low_noise_14B_*.safetensors`** — must be downloaded |
| official VRAM anchor | ComfyUI test on RTX 4090D **24GB**, 640×640, 81 frames: `fp8_scaled` = **84% VRAM (~20GB peak)**; +4-step LoRA = 89%. Source: https://docs.comfy.org/tutorials/video/wan/wan2-2-fun-camera |

**Hard implication:** at fp8 the 14B peaks ~20GB → **will not fit 16GB**. The whole point of
Phase A is to find the quant/offload/resolution combination that gets it under 16GB.

---

## 2. Phase A — VRAM feasibility (this is the gate)

Stop here if none of these fit. Try in this order (cheapest first):

| step | what to do | expected |
|---|---|---|
| A1 | **fp8 + ComfyUI block-swap offload** (host has 64GB RAM). Load `fp8_scaled` with offload, 640×640, short length (e.g. 25–49 frames) | may still OOM at 16GB — measure, don't guess |
| A2 | **Reduce resolution and length.** Fun-Camera is trained at low res; try 512 or 480 short edge and the minimum frame count the trajectory needs | the real lever on peak VRAM |
| A3 | **4-step LightX2V LoRA** (already on disk) to cut sampling steps | cuts time, not peak — but frees room to raise res |
| A4 | **Hunt for a low-bit quant.** Search HF for `wan2.2 fun camera GGUF / nf4 / int4 / nvfp4`. **Unverified to exist** — this is an open question to answer, not an assumption | if a GGUF exists, it is the decisive win |
| A5 | If no quant exists, **produce one** (ComfyUI can GGUF-quant via `ComfyUI-GGUF` if the pack is installed, else add it) | last resort; only if A1–A4 fail |

**Record for every attempt:** resolution, frame count, peak VRAM (from ComfyUI log or `nvidia-smi`),
seconds-to-first-frame, frames/sec. Put numbers in `artifacts/tmp/fun-camera-vram/` (git-ignored).

**A-pass threshold:** a single configuration that completes a camera-move video **without OOM** and
in a time the operator would tolerate for one location (rough guide: minutes, not hours).

---

## 3. Phase B — the generation proof (only if A passes)

### 3.1 Test scene

Use the **synthetic ground-truth room** (`specs/image-generator-tests/room-pov-proof/room.py`) — it
renders exact perspective views and has four **distinctly coloured walls** (N=red+shelf, E=blue+table,
S=green+picture, W=yellow+window). This is the *right* use of the synthetic room: it cannot prove
identity of a real room, but it **can** prove camera motion — did the camera actually pan from the
red wall to the green wall when asked?

Alternative/complement: the operator's shed photos, but only to eyeball realism — **not** to score
identity.

### 3.2 The test

1. Render the synthetic room's **yaw-0 view** as the `start_image`.
2. Run `WanCameraImageToVideo` with a `WanCameraEmbedding` trajectory — start with the simplest
   deterministic presets (pan left / pan right / zoom), then a combined 4-wall tour if presets work.
3. Extract frames at known yaw points along the clip.

### 3.3 Success criteria

- **Camera obeyed:** the frame at "pan left 90°" shows the **blue wall** (E), not the red wall again.
  Compare against `room.py` ground-truth renders at the same yaw — a *wall-colour* check, which is a
  legitimate trajectory test (unlike the greyscale-structure metric, which measured nothing real).
- **Frames agree with each other:** the same objects (shelf, table, picture, window, crate) appear at
  consistent positions across consecutive frames — no morphing shelf, no re-rolled room.
- **No seam artifact** if a full 360 tour is attempted.

**Instrumentation warning (from `../B-148-location-creation-from-moment/FINDINGS-location-images.md` §0):** do **not** re-use
`score-photo.py`'s greyscale structure correlation as the acceptance test — it passed a completely
different shed. For the synthetic room, the wall-colour classifier in `score.py` is the meaningful
instrument. For anything else, the operator's eye is the instrument.

---

## 4. Phase C — what the proof must NOT claim

- ❌ "It preserves the identity of a real room." It does not — it is still generation.
- ❌ "Frames are ready to use without review." Every extracted frame still needs approval + naming
  (B-148 FR-B148-05/06) before it becomes a bindable location reference.

The proof's output is a **capability finding**, not a product feature.

---

## 5. Go / no-go

| outcome | decision |
|---|---|
| A passes, B passes | **Go** — record the working config (res/quant/offload) and add Fun-Camera as a producer for B-148 Option I |
| A passes, B fails (camera drifts / re-invents) | **No-go for Fun-Camera** — Option I defaults to Wan2.2-5B/I2V text-prompted camera |
| A fails (no 16GB config) | **No-go on 16GB** — same default; note it needs a bigger GPU |
| A or B passes only partially | record exactly what did and didn't work, with numbers |

---

## 6. Deliverables of the proof session

1. `artifacts/tmp/fun-camera-vram/` — the VRAM matrix (config → peak → time → fps).
2. `artifacts/tmp/fun-camera-proof/` — the generated clips, extracted frames, and comparison sheets
   (ground-truth vs extracted at matched yaw).
3. A written verdict appended to this package: `RESEARCH-fun-camera-16gb.md` with the go/no-go, the
   working config, and the measurement table.

---

## 7. Handoff notes (for the separate session)

- **Do not touch RP engine code.** This is a proof against the local ComfyUI host; no app changes.
- **Do not re-litigate "MiniMax M3".** That is a different item (B-150); this proof is Wan 2.2
  Fun-Camera.
- **Do not re-derive the environment facts** in §1 — they were verified this session.
- **Read `../B-148-location-creation-from-moment/RESEARCH-minimax-m3-t2v.md` §3 first** — it has the checkpoint names, the HF repo, and the
  source links for the camera nodes.
- **The two checkpoints needed** are in `Comfy-Org/Wan_2.2_ComfyUI_Repackaged` on HuggingFace, plus
  the optional LightX2V 4-step LoRAs (already on disk). Confirm the exact filenames on HF before
  downloading; the research only established the `wan2.2_fun_camera_high_noise_14B` /
  `low_noise_14B` family names.
