# Depth — what it is, which models can consume it, and where it belongs here

> Companion to [`FINDINGS-location-images.md`](FINDINGS-location-images.md) (what was tried) and
> [`OPTIONS-consistent-location.md`](OPTIONS-consistent-location.md) (what to build).
>
> Verified 2026-10-03 → 2026-10-04 on the `WOOD-GAME-MAIN` ComfyUI host.

---

## 1. What a depth map is

A greyscale image where **each pixel's brightness is the distance from the camera to the surface at
that pixel.** Near = bright, far = dark.

**That is the entire content.** A depth map contains no colour, no material, no texture and no
object identity. In the depth panel of the operator's shed you cannot tell the tractor from the wall
behind it — only that one is nearer.

### The one-sentence rule

> **Depth carries where things are. It carries nothing about what they look like.**

Everything that follows is a consequence of that sentence.

---

## 2. What depth legitimately does

Depth is one of the most-used control signals in image generation. Its real uses, all of which share
one property — **the appearance is meant to be invented:**

| use | what depth pins | what the model supplies |
|---|---|---|
| Re-generate an image with a new style / lighting / materials | the composition | the new look |
| Transfer a pose or silhouette onto different content | the shape | the new subject |
| **3D → photoreal** — build a scene, render depth from a chosen camera, let the model paint it | the geometry you own | the pixels you cannot paint |
| Shape-guided outpainting / injoin | seam geometry | the extension |
| Object insertion / relighting with correct occlusion | the depth ordering | the inserted object |

**Depth is a tool for *reinterpretation*.** That is its purpose, not a limitation of it.

### Therefore: what depth must not be used for

- ❌ **Reproducing a specific real room from a new angle while keeping its identity.** The room is
  the thing that must not be re-invented, and re-invention is the mechanism.
- ❌ **Deriving viewpoints of a place you have never seen the geometry of.** See §4.
- ❌ **A 360° walk-around.** Degradation scales with the size of the move (see §5).

---

## 3. Which models can consume depth, and how — the compatibility matrix

There are **two different channels**, and they behave very differently.

| # | route | how depth enters | model family | armed on host | preserves identity? |
|---|---|---|---|---|---|
| 1 | **Depth as an extra reference image** | multi-image conditioning (`images.image_N`, `image1..3`) | Qwen-Image-Edit 2.1 (`TextEncodeQwenImage21`), Qwen-Image-Edit-2511 (`TextEncodeQwenImageEditPlus`) | ✅ | ❌ invents appearance |
| 2 | **Depth as ControlNet** (structural) | per-block geometry patch | **Qwen 16-channel family**: `QwenImageDiffsynthControlnet` + `qwen_image_depth_diffsynth_controlnet.safetensors` | ✅ | ❌ layout only |
| 3 | **Depth as ControlNet** (structural) | standard ControlNet | **SDXL**: `controlnet-depth-sdxl-1.0` + `juggernautXL_ragnarok` / `pony*` / `bigLust_v16` | ✅ **(never tried on the shed)** | ❌ (untested) |
| 4 | Depth in an img2img pass | depth latent blended at partial denoise | SDXL img2img — **note the Qwen graph's denoise is inverted, §5** | ✅ | partial |
| 5 | Depth for a video/camera move | `QwenImageBlockWiseControlNet` is Wan-shaped (16-ch, ×4 patch) | Wan 2.2 I2V family (`Wan2.2_I2V_*`, `wan2.2_ti2v_5B`) | models present | n/a for stills |

### 3.1 The pairing rule — a ControlNet is bound to one model family

A depth ControlNet checkpoint is **not** universal. Two numbers must match the model it attaches to:

1. **latent channel count** (→ row count of `img_in.weight` after the ×4 patchify), and
2. **hidden dimension** (→ `img_in.weight` output size, and every `controlnet_blocks.*` RMS weight).

Measured from the safetensors headers:

| file | `img_in.weight` | hidden dim | latent | verdict |
|---|---|---|---|---|
| `qwen_image_depth_diffsynth_controlnet.safetensors` | **[3072, 64]** | **3072** | 16-ch (60 blocks) | the controlnet itself |
| `qwen_image_edit_2511_fp8mixed.safetensors` | **[3072, 64]** | **3072** | 16-ch | ✅ **exact match — this is its model** |
| `qwen_image_2.1_int8_convrot.safetensors` | **[4096, 64]** | **4096** | **64-ch** | ❌ mismatched on **both** axes |

Confirmed by `supported_models.py` — these are two distinct families:

```
QwenImage    -> latent_formats.Wan21        (16 channels)  ; clip_target detects "qwen25_7b"
QwenImage21  -> latent_formats.QwenImage21  (64 channels)  ; clip_target detects "qwen3vl_8b"
```

**Consequences:**

- **`QwenImage21` (Qwen-Image-2.1) cannot use the staged depth ControlNet. At all.** No patch, prompt
  or strength makes it work — the tensor shapes are wrong. Doing so raises
  `RuntimeError: expanded size of the tensor (1) must match existing size (16) …` from
  `nodes_model_patch.py:60`.
- **`Wan21().process_in(...)` on line 60 of that node is correct for its family**, not a bug. The
  fix for a mismatch is to pair correctly, never to patch that line.
- **The correct pairing was already on the host**: `qwen_image_edit_2511_fp8mixed` +
  `qwen_2.5_vl_7b_fp8_scaled` + `qwen_image_vae` + this controlnet. Zero code changes required.

### 3.2 Depth sources available

| source | gives | limitation |
|---|---|---|
| **DepthAnythingV2** (`depth_anything_v2_vitl.pth`, via `comfyui_controlnet_aux`) | depth of a photograph you already have | same viewpoint only |
| a 360 capture | depth for any direction | one standing position |
| a 3D model / reconstruction | depth from **any** camera | needs the model |
| a Blender blockout | depth from any camera | only as accurate as the blockout |

This repo's tooling: `specs/image-generator-tests/room-pov-proof/extract-depth.ps1`.

**Convention: near = bright.** DepthAnythingV2 emits this, and it is what the SDXL depth ControlNet
expects — feeding it directly achieved a layout-structure IoU of 0.96.

---

## 4. The chicken-and-egg that makes depth circular for this problem

**To supply the depth of the view you *want*, you must already know that room's geometry from that
viewpoint.** And if you knew that, you would essentially have the answer.

Every real-photo experiment in the companion findings supplied the target's **own** depth map —
extracted from the very photograph of the view being requested. That is the answer's geometry,
handed over as a demonstration. It means:

- those scores are an **upper bound**, not an achievable result;
- depth is not a way to *discover* an unseen angle of your shed;
- **depth is a way to *tell* a model the shape of a room you already know.**

Where the geometry legitimately comes from is §3.2's last two rows — a reconstruction or a
blockout. Depth is the bridge *from* geometry you own, not a substitute *for* owning it.

---

## 5. Two measured traps

**Trap 1 — degradation scales with the size of the camera move.** Depth supplies the *shape* of
newly-revealed area and nothing about its appearance. A 10° nudge reveals a sliver → minor
invention. A full walk-around reveals almost everything → almost everything is invented.
**A 360° turnaround is the worst possible case for any depth-based method.**

**Trap 2 — depth tone leaks into exposure, and the Qwen graph's denoise is inverted.**

| denoise (Qwen edit graph) | r vs source | mean diff /255 |
|---|---|---|
| **1.0** | **+0.994** | **4.85** — essentially the source |
| 0.7 | −0.029 | 60.09 — a different image |
| 0.5 | −0.032 | 58.56 |
| 0.35 | −0.040 | — |

The usual img2img intuition ("lower denoise preserves") is **backwards here**; keep denoise at 1.0.
Separately, depth-map tone tracked output exposure **inversely** across runs (FRONT-depth p50 79 →
output mean 36.2; BACK-depth p50 26 → output mean 73.9) — one run per condition, not isolated.
**Normalise depth tone before supplying it.**

---

## 6. Where depth belongs in *this* application

Even though depth cannot solve the consistent-location problem, it has several legitimate homes
here. Ordered by value.

### 6.1 Multi-character composition and blocking — the strongest fit
**B-119 / B-120 / B-126.** Depth ControlNet from a reference is the standard way to fix staging:
where each subject stands, the camera's relationship to the room, the arrangement of the scene. The
existing plan already routes this way — B-120 extracts structure assets (**Depth** / Canny /
OpenPose / Seg) for routes C1/C2 to consume, and both were raised to `high` priority on 2026-09-22.
Appearance *is* meant to be invented in a composed frame, so depth's limitation is **not** a
limitation here. This is exactly the shape of job depth is for.

### 6.2 The "3D → photoreal" last mile
Once a location has real geometry — from reconstruction, a blockout, or a captured 360 — render the
desired view to **depth plus a rough colour pass**, then let a depth ControlNet paint it photoreal.
The room is never invented, because the room came from capture. **This is where depth belongs in a
consistent-location pipeline: last, not first.**

### 6.3 A layout-agreement instrument (a metric that actually means something)
Comparing the **depth maps** of a render against its reference is a meaningful, interpretable
layout check — far better than greyscale structure correlation, which was proven here to pass a
completely different shed. Depth comparison measures *"is this the same shape of room?"*, which is a
real question with a real answer. It still cannot measure identity — only looking can.

### 6.4 Geometry-preserving region edits
**B-133 / B-138.** When restyling or editing a region, depth pins the surrounding layout so the edit
cannot drift the scene's structure.

### 6.5 Complement to pose control
**B-118 / B-128.** Pose skeleton controls the *bodies*; depth controls the *space*. They are
orthogonal and compose.

### 6.6 Small-nudge camera moves
For a genuinely small change of angle, depth-as-reference is usable — the operator's own shed
measurements showed a real effect (+0.291 on 2.1, +0.624 on the 2511 lineage). Treat this as a
convenience for small moves, never as a walk-around.

---

## 7. Summary for a future reader

1. **Depth is geometry, and only geometry.** Identity is never in it.
2. **Use depth when appearance is meant to be invented** — composition, blocking, restyling,
   3D→photoreal. Do not use it when identity must survive.
3. **A depth ControlNet is bound to a model family** by channel count *and* hidden dimension; the
   staged Qwen controlnet serves the **16-channel** lineage (`qwen_image_edit_2511`), never 2.1.
4. **The SDXL depth route is armed and untested** — the cheapest unexplored option (§3, row 3).
5. **Supplying the target's depth presupposes you already know the target's geometry.** That is why
   every depth result here is an upper bound.
6. **Judge identity by looking.** No correlation figure in this repo measures it.
