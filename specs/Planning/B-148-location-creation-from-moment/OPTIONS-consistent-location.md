# Options for consistent location images

> The implementation options this evidence supports. Companion to
> [`FINDINGS-location-images.md`](FINDINGS-location-images.md) (what was tried) and
> [`FINDINGS-depth-and-models.md`](FINDINGS-depth-and-models.md) (depth specifically).
>
> **Status:** design input for **B-148**. Nothing here is a commitment.

---

## 1. The requirement is really two requirements

They are routinely conflated, and separating them is what makes the options tractable.

| property | question it answers | how it is achieved |
|---|---|---|
| **Consistency** | do the views agree *with each other*? | one source for all views — one latent, or one capture |
| **Identity** | are the views of *this* room? | the room's **own pixels** must be in the result |

**The measured conclusion of this work is a single sentence:**

> **Generation can deliver consistency but not identity. Capture delivers both.**

Generation re-draws every pixel from noise and is only *biased* toward its source — so it can
produce several views that agree with each other while none of them is your shed. Capture carries
the actual pixels, so it cannot drift. Everything below follows from that split.

A second, practical consequence: **a generated view becomes the location's identity.** Whatever is
approved becomes canon and propagates into every moment that binds it. The approval gate is
therefore load-bearing, not a formality.

---

## 2. The design rule (from the operator, 2026-10-04)

> *"it can just be fully text driven the user can decide it is good enough, or if a full panaroma
> 360 location feature is added then this can be used"*

**The plan owns the container, the placement, the approval gate, the naming and the selector. It
must NOT own how an image is produced.**

Concretely: the options below are **candidate sources**, plural and interchangeable. Adding,
improving or removing one must never require changing the flow. This is
`FR-B148-04` in the requirements, and it is currently **violated** by the code — see §6.

---

## 3. The options

### Option A — Photographic capture, fixed views
Shoot the angles you need; upload them as the location's images.

| identity | consistency | coverage | cost | status |
|---|---|---|---|---|
| **exact** | exact (same building) | only the angles you shot | zero tech | ✅ available today |

**When:** a real place you can visit. Highest fidelity of anything on this list, and the least
machinery. If it's reachable, this wins.

### Option B — 360° capture + deterministic reprojection
One 360 shot from a spot → project **any** yaw / pitch / FOV from it.

| identity | consistency | coverage | cost | status |
|---|---|---|---|---|
| **exact** — the same pixels, resampled | exact by construction | every *direction* from **that one spot**; no parallax | low — pure arithmetic, **no model** | ⚠️ viewer math prototyped & validated; **B-139 T1/T2 not built** |

**When:** you want many angles cheaply and can stand in one place. Directly serves the operator's
original phrasing — *"say view of person from x y in the shed"*. One capture per standing position.

### Option C — Multi-view reconstruction (photogrammetry / 3D Gaussian splatting)
~30–60 photos → textured mesh or splat → render from **any** camera, with parallax.

| identity | consistency | coverage | cost | status |
|---|---|---|---|---|
| **exact** | exact | **any camera position** | high — models or an external tool | ⚠️ host has the **nodes but not the models**; external tools viable immediately |

**When:** you want positions you never stood in. This is the industrial answer and the only one
that gives true free-camera movement. Note: interiors are harder than exteriors for photogrammetry
(low texture on flat walls); splatting handles them better; LiDAR skips the problem.

### Option D — Pure text generation
Describe the place; generate it.

| identity | consistency | coverage | cost | status |
|---|---|---|---|---|
| ❌ invented — will not be *your* room | ❌ independently re-rolled each run | unlimited | lowest | ✅ works today |

**When:** the place is **invented by the RP engine** and has no real counterpart — then there is no
ground truth to preserve, and this is legitimate. **This is the operator's own stated fallback:**
*"the user can decide it is good enough."* It must remain available.

### Option E — Generation conditioned on depth / references *(the route tested in this work)*
Supply the source view plus a depth map (or photo) of the target view.

| identity | consistency | coverage | cost | status |
|---|---|---|---|---|
| ❌ **measured: not preserved** | partial | any angle *if* you supply its geometry | low | ⚠️ works for **layout** (+0.291 on 2.1; **+0.624** on the 2511 lineage) — refuted as an identity method by the operator |

**When:** layout matters more than identity — composed frames, blocking, small camera nudges. **Not
for reproducing a specific real room.** And it is circular: supplying the target's depth presupposes
you already know the target's geometry.

### Option F — All views in one generation *(untested)*
Request the whole set in **one** image (e.g. a 2×2 room turnaround), then crop the panels and
approve them individually.

| identity | consistency | coverage | cost | status |
|---|---|---|---|---|
| ❌ invented | ✅ **free** — one latent, one room by construction | a fixed small set | lowest | ⬜ **never attempted** |

**When:** an **invented** location needs several views that agree. This is the strongest untested
idea for that case, because the consistency problem disappears rather than being engineered around.

### Option G — 3D → render → depth-ControlNet photoreal pass *(the hybrid)*
From B or C: render the desired view to **depth + a rough colour pass**, then let a depth ControlNet
paint it photoreal.

| identity | consistency | coverage | cost | status |
|---|---|---|---|---|
| ✅ inherited from the capture | ✅ inherited | any (from B/C) | medium | ⚠️ architecturally sound, untested end-to-end |

**When:** the captured/reconstructed render is too flat or unphotoreal. **This is where depth
legitimately belongs in a consistent-location pipeline — last, not first.**

### Option H — Reuse an approved image as the reference for the next
Bind an accepted view and ask for another angle from it.

| identity | consistency | coverage | cost | status |
|---|---|---|---|---|
| ❌ drifts; the reference carries appearance, **not viewpoint** | ❌ | any | low | ⚠️ measured: it ignores the requested angle and re-rolls the details |

**When:** never as the primary mechanism. A reference image has **no yaw in it**, so "show me the
side" cannot be expressed through that channel at all.

### Option I — Video camera-move → frame extraction
Generate a short **video** that moves the camera through the location, then extract frames as the
location's views. Suggested by the operator 2026-10-04.

| identity | consistency | coverage | cost | status |
|---|---|---|---|---|
| ❌ invented — it is still generation | ✅ **by construction** — one continuous generation, one latent trajectory | **every angle along the path**, unlimited frames | high — video is far costlier per second than a still | ⚠️ local route **armed**; cloud route needs an API key |

**Why this is structurally stronger than option F (one-image turnaround).** A video is a single
latent *trajectory*, not a set of independently-decoded panels. Temporal coherence is not a property
we hope the model has — it is what video generation *is*. So the frames agree with one another along
the entire arc, and you may **sample as many as you want** rather than being limited to the panels
you asked for. Of all the generative routes, this has the best claim to real mutual consistency.

**Why it also answers the camera-angle requirement directly.** The host carries an *explicit* camera
control path — not a text hint:

| node | what it gives |
|---|---|
| `WanCameraEmbedding` | `camera_pose` (named trajectory) + optional `speed`, **`fx`, `fy`, `cx`, `cy`** — focal length and principal point, i.e. genuine intrinsics |
| `WanCameraImageToVideo` | `start_image` + `camera_conditions` + `length` |

That is the "camera angle implementation" the operator described, **already installed**.

**Two routes, and only one is usable today:**

| route | nodes | models | armed? |
|---|---|---|---|
| **local — true camera control** | `WanCameraEmbedding`, `WanCameraImageToVideo` | `wan2.2_fun_camera_high/low_noise_14B` (**not on disk**) | ⚠️ **needs download; ~20GB peak → marginal on 16GB** |
| **local — text-prompted camera only** | `WanImageToVideo`, `WanFirstLastFrameToVideo`, `WanAnimateToVideo` | `Wan2.2_I2V_High/Low_R1`, `wan2.2_ti2v_5B`, `wan2.2_vae`, `Wan2.2_LightX2V_*` | ✅ **yes — local weights present** |
| **cloud** | `ComfyCloudMiniMaxH3TextToVideoNode` / `…ImageToVideoNode` / `…FirstLastFrameToVideoNode`, `MinimaxHailuo03*`, `KlingTextToVideoNode`, `Veo3VideoGenerationNode`, `LumaRay32*` | remote | ❌ **needs an API key; none is configured on the host** (no key found in `comfy.settings.json`, 2026-10-04) |

**⚠️ Model naming (corrected 2026-10-04 by research):** there is no "MiniMax M3" video model —
MiniMax-M3 is a ~428B multimodal *language* model. MiniMax's video line is **Hailuo 01 → 02 → H3**.
The nodes the host exposes as `MinimaxHailuo03*` / `ComfyCloudMiniMaxH3*` are **cloud API** nodes,
and the *local* `MiniMaxH3ImageToVideo` / `MiniMaxH3ReferenceToVideo` nodes (open weights, not
downloaded) are **image-to-video / reference-to-video only** — text-to-video MiniMax on this host is
API-only and no key is configured. Full research with sources:
[`RESEARCH-minimax-m3-t2v.md`](RESEARCH-minimax-m3-t2v.md).

**The model that actually does what was asked:** **Wan 2.2 Fun-Camera** (`wan2.2_fun_camera_*_14B`)
— a Comfy-Org-repackaged fine-tune giving genuine camera intrinsics (`fx/fy/cx/cy`) + Plücker
trajectory embeddings + an I2V starting frame. Apache 2.0, fully local. **VRAM caveat:** ~20GB peak
at 640×640 on a 24GB card → marginal on the 16GB RTX 5080 (needs a low-bit quant, unverified to
exist). The base `Wan2.2_TI2V-5B`/`I2V` on disk fit comfortably but have **no camera adapter** — their
camera moves are text-prompted only.

**Honest limits:**

- **Identity is still invented.** This buys *consistency*, not identity — so it is genuinely
  excellent for **RP-invented** locations and no help at all for a specific real shed.
- **Temporal drift.** Video models morph over long clips. Keep clips short and camera-only.
- **Resolution.** Video frames are typically well below still resolution → extracted backdrops may
  need upscaling before use.
- **Cost and time** are an order of magnitude beyond a still, per usable image produced.
- **360 closure is not guaranteed.** A full-turn clip may not meet at the seam; treat a 360 video as
  unproven until measured on seam continuity, exactly as the 360 LoRA was.

**Relationship:** **B-113** (scene video production — Wan 2.2 uncensored I2V/FLF2V over rendered
keyframes, local ComfyUI) already exists. This option should **consume** B-113, not duplicate it.

**How it enters the flow:** identical to every other source — frames are *derived candidates*. No
special handling. It is covered by `FR-B148-04`, and it is **blocked by the same ingest gap** as
options A / B / C / F / G.

---

## 4. Choosing between them

| if the location is… | use | because |
|---|---|---|
| **real and reachable** | **A**, then **B** (360), then **C** | identity is preserved — it is your pixels |
| **real, reachable, needs free camera movement** | **C** (or an external reconstruction tool now) | only route with parallax |
| **real, but you only have photos** | **A** — shoot more. Do not generate. | generation will not preserve it |
| **invented by the RP engine** | **D**, or **F** for a multi-view set | no ground truth exists to preserve; consistency is what matters |
| **invented, and you want many angles or a camera path** | **I** (video → frames); **F** is the cheap alternative | consistency by construction, plus explicit camera control via `WanCameraEmbedding` |
| **any, and a flat render needs photorealising** | **G** | depth paints geometry you already own |

**Ranked by identity fidelity:** `C > B ≈ A > G > H > I ≈ E > F ≈ D`.
**Ranked by mutual consistency:** `C ≈ B ≈ A` (exact — the same real building) `> I` (one continuous
generation) `> F` (one latent) `> G > E > D ≈ H`.
**Ranked by cost per usable image:** roughly the reverse — `I` is the most expensive, `A` and `D`
the cheapest.

---

## 5. How they compose — and the one gap

These are not exclusive. A location's image set may legitimately contain a photograph (A), a
projected view (B), a reconstructed render (C), and a generated backdrop (D) — because the flow
owns the **container**, not the technique.

**The gate that makes this safe** is approval + naming: an image becomes bindable only when a human
accepted it and named it. That is what stops an invented shed becoming canon by accident.

### The gap (blocking)

`IReferenceBootstrapService.GenerateCandidatesAsync(batchId)` is the **only** way a candidate can
enter a batch. There is no upload, ingest or derive method. So the pipeline today permits **option D
only** — and options A, B, C, F and G have **no way in at all**.

**Closing that gap is the single most important piece of B-148.** Everything in §3 is theoretical
until a candidate can arrive from any source.

---

## 6. How to judge "consistent" — the acceptance question

**Only looking measures identity.** This work produced a metric (greyscale structure correlation)
that scored a *completely different shed* at +0.624, and the operator refuted it on sight. Do not
repeat that mistake.

| instrument | what it may be used for | what it must never be used for |
|---|---|---|
| operator review | **the identity decision** | — |
| **depth-map comparison** | *"is this the same shape of room?"* — a genuine, interpretable layout check | identity |
| greyscale structure correlation | nothing load-bearing | anything about identity |
| pixel-difference vs a real photo | detecting gross change | fidelity claims |

A practical review aid that did work: put the source, the real target (if one exists), and every
candidate side by side in one sheet, labelled, and let the operator judge. See
`artifacts/tmp/depth-controlnet/sheet.png` for the form.

---

## 7. Decisions the design pass must settle

| # | decision |
|---|---|
| O-1 | **Source-agnostic ingest** — the shape of the new service capability, so A/B/C/F/G can all deliver candidates. *(B-148 OQ-4; the blocker.)* |
| O-2 | Whether **generation stays a candidate source at all** for locations, or is restricted to invented places. Recommendation: stays, unrestricted — the operator decides "good enough". |
| O-3 | Does the flow need an explicit **"this is a real place"** signal to guide which candidates are plausible? Recommendation: no — naming and review already carry that judgement. |
| O-4 | Is **B-139 T1/T2** (the equirect viewer) in scope for B-148, or a parallel item? It is a candidate *source*, not a dependency. |
| O-5 | Is **reconstruction (C)** in scope at all, given the host models are unarmed and external tools exist? Recommendation: out of scope — treat output as uploads. |
| O-6 | What is the **acceptance evidence** for a location image set — review only, or review plus a depth-layout check? |
| O-7 | Is **video camera-move (option I)** in scope for B-148, or does it ride on **B-113**? Recommendation: ride on B-113 — B-148 only needs extracted frames to be admissible as derived candidates. |
| O-8 | **Which camera-angle control should the studio expose** — `WanCameraEmbedding` `camera_pose` presets, explicit intrinsics (`fx`/`fy`/`cx`/`cy`), or both? This is the operator's "camera angle implementation" and it is already installed locally. |
| O-9 | Which video route is the target — **local Wan 2.2** (armed today; Fun-Camera for true pose control but VRAM-marginal) or a **cloud** provider (MiniMax H3 / Hailuo / Kling / Veo — needs an API key)? Research confirms there is **no "MiniMax M3" video model** — see `RESEARCH-minimax-m3-t2v.md`. |

---

## 8. What this means for B-148

1. **Nothing here changes the B-148 design.** The container, placement, gate, naming and selector
   are identical whichever options are used — which is exactly why the design rule in §2 exists.
2. **The blocking work is unchanged and singular:** make candidate ingest source-agnostic.
3. **The measured results do not invalidate any option** — they only tell you which option suits
   which kind of location, which is a runtime choice the operator makes.
4. **One correction to inherited assumptions:** B-108's design expands views via
   `IImageEditingClient.EditAsync` — that is option **H**, measured not to preserve identity. It is
   a legitimate *candidate source* but must not be presented as the mechanism for a real place.
