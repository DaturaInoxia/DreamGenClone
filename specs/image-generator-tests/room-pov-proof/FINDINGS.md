# Room POV proof — can Qwen-Image-2.1 produce consistent views of one room?

**Date:** 2026-10-04 · **Host:** local ComfyUI `http://192.168.0.11:8188` (WOOD-GAME-MAIN, RTX 5080 16 GB)
· **Status:** **SOLVED for Qwen 2.1 — no ControlNet, no LoRA, no pod.** Supplying the requested view's
**depth map as an ordinary reference image** reproduces both the wall *and* the layout: **6/6 runs PASS**.

## The question

The operator's hypothesis: *"I think this can be done with 2.1 and reference images. I need consistent
location images over and over with different POVs."* The prior conclusion from the 360-LoRA work was
negative, so this proof tested 2.1 directly rather than inferring from that. **The hypothesis is correct.**

## Headline result

| Arm | Setup | Result |
|---|---|---|
| **A** | 1 reference → opposite view | **FAIL** — never turned; reproduced the source wall (green 0.00, 4/4 seeds) |
| **B** | 3 wall references | wall right (green 0.34 vs 0.33) but **layout wrong** (r +0.336) |
| **C** | prose only, no image | **FAIL** — green 0.08 |
| **D** | all 4 wall references → corner | **FAIL** — yellow 0.22 dominant, green 0.18 |
| **G1** | source + wall refs + depth map **as image_4** | layout improved (r +0.575) but **wall lost** (green 0.12, blue dominant) |
| **G2** | source + **depth map as image_2** + wall refs + far wall named | **PASS — green 0.35 vs truth 0.33, r +0.567** |
| **G3** | same recipe, **reverse** direction | **PASS — red 0.65, r +0.510** |
| **G4** | same recipe, **two-wall corner** | **PASS — green 0.32 + yellow 0.31 (truth 0.40/0.36), r +0.702** |

**G2 seed sweep: 4/4 PASS** — green 0.35 / 0.35 / 0.33 / 0.35 (truth 0.33), r +0.567 / +0.533 / +0.532 /
+0.590. Stable across seeds, on the app's own configured edit settings (25 steps, CFG 1.0, denoise 1.0),
with no tuning and no host changes.

## The recipe (Qwen 2.1, entirely through native multi-reference)

1. **image_1** = the source view (the POV you already have).
2. **image_2** = the **depth map of the view you want**, rendered from the room's geometry.
3. **image_3, image_4** = reference views of the side walls.
4. **Instruction** names the far wall explicitly and points at the depth map by position: *"The SECOND
   image is a depth map of exactly this view — follow its layout for where the walls stand and where the
   crate sits."*

**Ordering matters, and was measured.** The depth map as **image_2 works; as image_4 it does not** (arm G1:
layout improved to r +0.575 but the wall was lost to blue 0.22 / green 0.12). The reference carrying
*geometry* must sit immediately after the source.

## Why this is the answer

Geometry does **not** have to be carried by prose (measured dead: green 0.08), by a ControlNet (the host's
Qwen ControlNet node is broken — see below), or by a LoRA. It travels through **Qwen 2.1's own native
multi-reference mechanism** — the same mechanism the app already drives. The missing ingredient was
supplying the *requested view's geometry* as a reference, instead of expecting the model to infer the room.

## Supporting findings

1. **A single reference cannot change the viewpoint.** Arm A re-rendered the source view (r **+0.77** with
   the very image it was given) and produced the asked-for wall **0.00, across four seeds**. This is the
   operator's reported symptom, reproduced and measured.
2. **Multiple references can change the viewpoint.** Arm B — given the side walls as extra references —
   produced the correct far wall and **removed the source's red wall entirely (0.00)**.
3. **More references is not better.** All four walls (arm D) did *worse* than three; the corner lost both
   required walls. Reference count is not the lever — *content* is.
4. **Prose cannot carry a room.** The full-prose control produced green 0.08 against a required 0.33.
5. **Shouting louder does not help.** Raising CFG to 3 and 6 with a maximally explicit "turn 180°, show
   the GREEN wall" instruction **regressed** arm B (green 0.34 → 0.05). The plainest instruction at the
   app's own CFG 1.0 was best.

## The Qwen ControlNet route on this host is blocked (recorded; not needed)

`ModelPatchLoader` + `QwenImageDiffsynthControlnet` with `qwen_image_depth_diffsynth_controlnet.safetensors`
(2,266,838,080 B, staged from `Comfy-Org/Qwen-Image-DiffSynth-ControlNets`) fails inside ComfyUI's own code:

```
comfy_extras/nodes_model_patch.py:60, in QwenImageBlockWiseControlNet.process_input_latent_image
    latent_image[:, :16] = comfy.latent_formats.Wan21().process_in(latent_image[:, :16])
RuntimeError: The expanded size of the tensor (1) must match the existing size (16) at
non-singleton dimension 0.  Target sizes: [1, 16, 64, 64].  Tensor sizes: [16, 16, 64, 64]
```

A **Qwen** controlnet path calling `latent_formats.Wan21()` is the defect. Not pursued, because arm G
delivers the geometry through references with no host patch and no new dependency — the better outcome.
`run-room-depth-sdxl.ps1` is retained for an SDXL comparison but is **not** part of the recommended route.

## The instrument

A synthetic room whose geometry is known **exactly**, so every camera view has a computable correct answer.
Walls are colour-coded so a result is scored by *which wall appears where*, not by eye.

```
        NORTH  red    + shelf
   WEST  ................................  EAST
  yellow |                                |  blue
  window |            [crate]             |  table
         |                                |
   SOUTH  ................................  GREEN + picture
```

Room 4×4×3 m, eye 1.6 m, FOV 75°, centre crate as a **second independent landmark**. Two cameras — one at
each end, looking at the other's wall — which is the Dean/Becky "facing each other" case.

- `room.py` renders exact ground truth, the input views, **and exact depth maps** (analytic ray cast).
- `score.py` classifies ground truth and candidate with the **same** hue classifier, then gates on
  (a) every wall the view requires and (b) structure correlation.
- `layout-probe.py` reports each class's centre of mass, to explain a correlation rather than guess.
- **Gate validated by controls:** a perfect input scores PASS (L1 0.000, r +1.000); the real wrong-wall
  render scores FAIL (L1 0.748). The gate is not trivially failing.
- **Reference sets never contain the target view.** The depth map is *geometry*, not appearance, so it
  cannot leak the walls' colours — which is why arm G is inference, not copying.

## What is NOT proven

- That this holds for a **photoreal** location rather than a flat colour diagram.
- That it holds when the depth map must come from a **blockout or panorama crop** the app generates rather
  than from analytic geometry. That is the integration step, not a mechanism question.
- Anything about other samplers, step counts, or the app's alternate reference strategies.

## Corrections made during this proof (each caught by a control, not by inspection)

1. **The ground-truth renderer was horizontally mirrored.** `right = cross(worldUp, forward)` yields the
   *left* vector. Colour shares survived — so the colour scores were valid — but every left/right
   comparison was invalid, and a legitimate 180° view read as "mirrored". Fixed to
   `right = cross(forward, worldUp)`; the whole matrix was re-run, **which is what revealed arm B's
   success that the mirrored run had hidden**.
2. **The first verdict gate was too lenient** — it passed arm D on the dominant wall alone while arm D's
   second required wall (yellow 0.36) was absent and structure correlation was ≈0. The gate now requires
   every wall the view needs **and** a structure pass.
3. **My first conclusion was over-stated.** I initially reported "references do not change the camera" and
   "depth ControlNet is required". Arm B refuted the first; arm G refuted the second.

**Methodological caveat:** a 180° turn and a horizontal mirror place the side walls in the *same*
left/right positions, so structure correlation alone cannot separate "turned around" from "mirrored". The
**far-wall colour** separates them cleanly, which is why the wall test is the primary gate and structure
secondary.

## Real photographs — the operator's shed (one photo + depth)

The synthetic room is flat and colour-named, so the recipe was re-run on **real photographs**: the
operator's own shed, whose front and back are genuine opposite views of one room.

**Setup.** Only the **back photo** as appearance (`image_1`); the **estimated depth of the wanted view**
(front) as `image_2`; the never-photographed entrance wall **invented in prose**. Scored against the real
front photo by `score-photo.py`, whose scale comes from the same two images: the two real views differ by
**+0.189**, and a blurred copy of the candidate (what faithful looks like) is **+0.98**.

| Arm | Setup | vs WANTED | vs GIVEN | Verdict |
|---|---|---|---|---|
| **I1** | back photo + **depth of the wanted view** | **+0.291** | **−0.107** | **MOVED** |
| I1 | seed 777 | **+0.447** | +0.189 | MOVED |
| I1 | seed 31337 | +0.207 | −0.035 | MOVED |
| **I2** | back photo only, prose, **no depth** | +0.143 | **+0.751** | **COPIED the given view** |
| **I3** | back photo + **the WANTED photograph** as a reference | +0.158 | **+0.694** | **COPIED the given view** |
| I4 | depth **and** the wanted photograph | +0.302 | +0.020 | MOVED (photo added nothing) |
| I6 | depth of **both** views | +0.201 | +0.196 | marginal |

### What this establishes

1. **The mechanism transfers to real photographs.** 3/3 seeds moved toward the wanted view, and stopped
   agreeing with the view they were given (I1 seed 20261004 actually goes *negative* against the source,
   −0.107, having clearly stopped copying it).
2. **Depth is the lever; photographs are not.** `I3` handed the model **the actual wanted photograph** as
   a reference and it *still* copied the given view (+0.694). `I4` added that photograph alongside the
   depth and changed nothing (+0.302 vs +0.291). This is the direct answer to *"will reference images do
   it?"* — **only the geometry reference moves the camera**; another appearance image does not.
3. **More geometry is not better either.** Two depth maps (I6, +0.201) did worse than one.
4. **Fidelity is weak on a real interior.** +0.21…+0.45 against a given-vs-wanted baseline of +0.189 and a
   faithful bar of +0.98 — a *plausible dim shed in roughly the right arrangement*, not this shed. The
   synthetic room reached +0.53…+0.70 and passed; a real cluttered interior does not.
5. **And this is the favourable case.** The depth came from the wanted photograph *itself*. In the app the
   depth would come from a blockout or a panorama crop — less exact — so **+0.3 is an upper bound** for
   this route on a real room as it stands.

### The daylight/`lighting` trap — measured, and it costs real fidelity

The first real-photo run lost the windows and the sunlight entirely. **The cause was the prompt, not the
model.** My instruction said *"cold dim blue light"* and mentioned no window, door or daylight:

| image | mean | p95 | bright% (>150) | Blue−Red |
|---|---|---|---|---|
| BACK (real) | 73.7 | **211.9** | **13.6%** | −15.5 |
| FRONT (real) | 77.8 | 182.8 | **10.0%** | −17.9 |
| I1 — *"cold dim **blue** light"* | **35.3** | **88.8** | **0.11%** | **+9.0** |
| I7 — same depth, daylight restored | 32.9 | **131.5** | **4.3%** | **−11.0** |
| W1 — bench → entrance | 36.0 | 154.8 | 5.2% | −11.7 |
| W2 — bench → far end | 73.8 | **250.8** | **17.6%** | −12.1 |

- Bright pixels collapsed **13.6% → 0.11%** and p95 **212 → 89**: the windows were gone.
- The image went **blue** (−15.5 → **+9.0**): the model obeyed the word "blue" literally.
- Restoring an explicit daylight clause brought them back (**4.3%**, p95 **131**, colour **−11.0**) and
  raised fidelity **+0.291 → +0.442 on the identical seed**.
- W2 reached **17.6% bright / p95 251**, i.e. **brighter than the real photographs** — the sunlight comes
  back completely. Residual dimness elsewhere is only because "dim" was left at the front of the prompt.

**Consequence for the app:** `frozenState.lighting` prose is not a description, it is an instruction.
*"Thinning blue light from the last of the day; dim inside the shed"* — the value observed in this
session earlier — will suppress windows and re-tint the room. It is transcribed into the prompt verbatim.

### Views from the workbench, both ways

| View | vs WANTED | vs GIVEN | Verdict |
|---|---|---|---|
| W1 — bench → entrance | **+0.453** | +0.086 | MOVED |
| W2 — bench → far end | **+0.304** | +0.034 | MOVED |

W1's **+0.453** is the best real-photo number in the test.

**Honest limitation:** no photograph exists *from* the workbench, so there is no ground truth at that
position, and the depth supplied was the **photo positions'** depth (FRONT-depth for the entrance
direction, BACK-depth for the far end). These are therefore the two real views with bench-height framing,
**not genuinely new camera positions**; the scores mostly measure how faithfully the depth was followed.
A true new position needs a depth map for that position — from a blockout or a panorama crop.

**Open confound — depth-map tone tracks output exposure, inversely:**

| | depth mean / p50 | output mean |
|---|---|---|
| W1 (given FRONT-depth) | 85.8 / **79** | **36.2** |
| W2 (given BACK-depth) | 58.1 / **26** | **73.9** |

Same room, same light prose, but the two depth maps differ hugely in tone (p50 79 vs 26) and the outputs
land at opposite exposures. One run per condition, so this is **not isolated** — but depth maps should be
**tone-normalised before being supplied**, and the near/far convention should be asserted rather than
assumed. (Evidence it is *not* an inverted convention: the layout-structure proof fed DepthAnythingV2
output straight into SDXL depth ControlNet and measured IoU 0.96.)

### Denoise is NOT a preservation knob on this graph — measured, and the opposite of the obvious guess

The obvious theory is that lowering `denoise` preserves the source (it is the usual img2img knob). **On this
graph it does the reverse**, and the effect is monotonic — measured against the actual photograph:

| denoise | r vs the source photo | mean pixel diff /255 | what came out |
|---|---|---|---|
| **1.0** (the app's setting) | **+0.994** | **4.85** | **essentially the photograph** — only 0.84% of pixels changed |
| 0.7 | −0.029 | 60.09 | a completely different image |
| 0.5 | −0.032 | 58.56 | a completely different image |
| 0.35 | −0.040 | — | a completely different image |

So partial denoise does not give a controlled partial edit here: it leaves the sampler mid-schedule on a
latent that its schedule did not produce, which is out of distribution for this model and yields garbage.
**`denoise` must stay 1.0.** (The command `D_denoise1` was `--instruction "…add a man and a woman… keep
everything else exactly as it is"`; the model returned the photograph nearly untouched, so it also shows
that an instruction asking for no change plus a change request is resolved in favour of *no change*.)

**This also disposes of the "keep the photo, change the viewpoint" hope.** There is no denoise value that
preserves the source *and* moves the camera, because at 1.0 the model simply does what the instruction
asks — "keep everything" returns the photo, "the camera has moved" invents a room. **Changing what the
prompt asks for is the whole lever; denoise is not.**

### The structure-correlation metric does NOT measure identity — the operator's eye refuted it

**2026-10-04.** The 16-channel stack (`qwen_image_edit_2511_fp8mixed` + `qwen_2.5_vl_7b` +
`qwen_image_vae` + `TextEncodeQwenImageEditPlus`) scored **+0.624** against the requested real
photograph — far above every earlier generative result (+0.291 for the same idea on 2.1). The
operator looked at the output and said, flatly, **"the sheet images aren't my shed at all."**

They are right, and the number is the problem. `score-photo.py`'s `structure()` reduces both images
to 256×256 greyscale and correlates. **"A dim shed interior with ribbed tin walls, a bright doorway
at one end and clutter along the floor" scores high under that metric while being a completely
different building.** The metric measures *gross luminance layout*, and for this room layout is
almost entirely generic — so a wrong shed passes.

This is the **same error class as the synthetic room** (whose identity *was* its geometry): the
instrument was chosen so that the wrong answer passes it. It follows that:

| quantity | what it can actually tell you |
|---|---|
| structure correlation | whether the gross layout is plausible — nothing about identity |
| the two real views differ by +0.189 | a floor, not a target |
| **the operator's judgement** | **the only instrument here that measures identity** |

**Consequence: the generative route is closed for reproducing a SPECIFIC real room.** Not "needs
tuning" — generation re-draws every pixel from noise and only *biases* toward the source; no
setting makes it copy. It will always produce *a* shed. Keeping *the* shed requires its actual
pixels, i.e. a capture (photograph / 360 → deterministic reprojection) or a reconstruction.
Any future claim about identity must be settled by looking, not by this metric.

### Where this belongs: reference creation, not the render

The recipe produces **a view**, and it is far better suited to one of the two jobs:

| job | needs | fits? |
|---|---|---|
| **Creating a location's reference set** | the *same room* from angles you do not have, mutually consistent | **YES** — this is what depth-as-reference does |
| **Rendering a moment** | characters placed into a room that already exists and must not be reinvented | **NO** — it invents (it produced a tractor that is not in the shed) |

So: use it at **stage 0** to fill the location container's missing named views, then **review, approve and
name** them like any other candidate; the render then binds those *approved* views and must not re-imagine
the room at all.

**The catch that makes the approval gate load-bearing:** a generated reference becomes that location's
**identity**, so an invented tractor or open door propagates into every scene that binds it. Ranking for
filling a reference set: **(1)** photograph the missing angle — exact and free; **(2)** generate with a
depth reference — consistent but inventive, keep only what passes review.

### Why the synthetic room did better (likely, untested)

The flat room offered **nameable landmarks**: the winning arm could be *told* "the far wall is the GREEN
wall", which pins wall identity independently of geometry. A real shed has no such names. The
corresponding next test — naming the wanted view's distinctive features in the prose, which is exactly
what the app's moment enrichment already produces — **has not been run.**

## Reproduce

```powershell
python room.py artifacts/tmp/room-proof/gt --size 1024

# the passing recipe (arm G2)
helpers/local-comfyui-host/run-qwen21-native-edit.ps1 `
  -SourceImage artifacts/tmp/room-proof/gt/S_to_N.png `
  -References @('artifacts/tmp/room-proof/gt/N_to_S-depth.png',
                'artifacts/tmp/room-proof/gt/S_to_E.png',
                'artifacts/tmp/room-proof/gt/S_to_W.png') `
  -Instruction 'The camera has moved to the far end of the room and now looks back toward the near end. The far wall facing the camera is the GREEN wall with a framed picture on it. The SECOND image is a depth map of exactly this view - follow its layout for where the walls stand and where the crate sits.' `
  -Seed 20261004 -Resolution 1024 -Denoise 1.0 -OutDir artifacts/tmp/room-proof/runs/G2
```
