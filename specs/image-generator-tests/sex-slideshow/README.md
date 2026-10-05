# Sex Slideshow (local ComfyUI — Qwen source-image editor)

A **19-step edit sequence** with a clean base scene rendered once at step 1, runnable against any editor
configuration so two checkpoints/LoRAs can be compared step-for-step, in **two modes**:

- **Chained (default).** Every step edits the *previous step's output*. Drift, clothing continuity and the
  ability to hold two subjects accumulate across 18 successive edits — this is the mode that measures
  whether an editor can carry a scene state forward, and it is the mode the `-FromBase` runs are read
  against.
- **`-FromBase`.** Every step edits the *step-1 output*. No frame descends from another edited frame, so
  each state is reached in one hop and state continuity has to be carried by the prompt instead.

Both modes are first-class; they answer different questions and are recorded in the manifest as
`chained = true|false`.

## Why the base starts in the side-profile "facing each other" pose

The identity pack contains **five curated angle-specific references** (front, 3/4L, 3/4R, profileL, profileR).
When the base render uses regional IP-Adapter + per-angle masks, the model can only condition on the
reference whose angle matches the requested pose. Starting the base facing the camera would force the
model to synthesize profile faces from the front ref on step 2; starting the base already facing each
other lets the curated profile refs (`*_profl`, `*_profr`) be used directly on the T2I base. Every
subsequent `-FromBase` edit inherits that stronger identity signal.

## The two modes, and why both are kept

Step 1 is a text-to-image render (the editor is an *editor* — it needs a starting image). Each prompt
describes the complete target state, including pose, spatial arrangement, clothing, and any accumulated
state such as visible facial residue, so either mode can reach any state.

**Chained** feeds each edit into the next, which is the only way to see whether the editor *holds* a
scene: state survives or decays across links, and drift is measurable. Earlier merged-checkpoint runs
proved the cost — the flat studio background develops a starfield-like pattern even when the subjects
remain usable.

**`-FromBase`** removes that recursive degradation by giving every edit the same step-1 source. It is the
right mode when the question is "which configuration renders each state best", because no frame inherits
another frame's errors.

Neither replaces the other: a `-FromBase` run cannot tell you where a chain breaks, and a chained run
cannot attribute a bad frame to the prompt versus the previous frame. Run both when the editor changes.

## Why it lives here

Scripts in `artifacts/tmp/` are git-ignored and so are neither reproducible nor reviewable. The runner is
tracked here and the run output is tracked too, under `runs/<RunId>/` — a slideshow whose frames are not
committed cannot be reviewed or diffed against a later run. Use `-OutDir`-style overrides for scratch work.

## Prompting rules (learned from `anatomy-edit-matrix` — do not relax these)

- **Clothing/pose ACTION phrasing only. Never `"add <body part>"`.** Told to "add", the editor leaves the
  clothing untouched and stamps the anatomy *on top of* it (verified: anatomy renders *above the waistband*
  on a still-closed fly). Instruct what the clothing or pose *does*.
- **Spell out the target state, not the delta.** `"the tip of the penis is inside her vagina"`,
  `"half…"`, `"the entire…"` — progressive states read more reliably than "push it further".
- **Restate the clothing state every step**, so the chain cannot silently re-dress a subject. The
  description of steps 1–3 confirmed wardrobe survived three edits; that is only true because each prompt
  restates it.
- **Male anatomy rides on the editor LoRA.** The v23 checkpoint alone renders a female-biased groin form,
  so the LoRA supplies the anatomy prior. Keep `"correct male anatomy, natural integration with his body"`.
- **Seed pinned across a run** so two configurations are comparable step-for-step.

## Layout

| File | Purpose |
|---|---|
| `run-sex-slideshow.ps1` | The 19-step runner. Chained by default; `-FromBase` edits every step from step 1 instead. `-GraphLabel` names the renderer in the manifest, and the edit/base runners are recorded there too. |
| `runs/<RunId>/` | `stepNN-<slug>.png`, `manifest.json`, `slideshow.png` — **tracked** output |

It reuses, rather than duplicates: `anatomy-edit-matrix/make-base-image.ps1` (step 1 T2I) and the
app-faithful edit runner `helpers/local-comfyui-host/run-local-aio-edit-proof.ps1` for the merged-checkpoint
(Qwen-Image-Edit-2511) family. Both are overridable — `-BaseRunner` / `-EditRunner` — which is how the
Qwen-Image-2.1 runs below are driven.

## Usage (from the repo root)

```powershell
# one configuration = one complete 19-step chain in its own run folder
& specs/image-generator-tests/sex-slideshow/run-sex-slideshow.ps1 `
    -Prefix remixlora08 `
    -Checkpoint 'qwenImageEditRemix_aioV20.safetensors' `
    -LoraName 'QwenEdit2511_AllIncludedGay_v2.safetensors' -LoraStrength 0.8 `
    -RunId 20260912-06-slideshow-remixlora08

& specs/image-generator-tests/sex-slideshow/run-sex-slideshow.ps1 `
    -Prefix v23lora08 `
    -Checkpoint 'Qwen-Rapid-AIO-NSFW-v23.safetensors' `
    -LoraName 'QwenEdit2511_AllIncludedGay_v2.safetensors' -LoraStrength 0.8 `
  -RunId 20260912-07-slideshow-v23lora08 -FromBase
```

**Give each configuration its OWN `-RunId`.** Step files are named `stepNN-<slug>.png`, so two
configurations sharing a run folder would overwrite each other's steps and manifest.

### Qwen-Image-2.1 (native editor)

The 2.1 family loads a UNETLoader artifact plus a separate text encoder and VAE, so it needs the
purpose-built runners (`helpers/local-comfyui-host/run-qwen21-native-t2i.ps1` /
`run-qwen21-native-edit.ps1`); the merged-checkpoint runner cannot drive it. Both are driven through the
same harness, and the sampling values below are the app's configured values for the two local 2.1 rows —
nothing is invented here:

```powershell
# CHAINED — each step edits the previous frame. -VarySeedPerStep is REQUIRED for 2.1 (noise replay).
& specs/image-generator-tests/sex-slideshow/run-sex-slideshow.ps1 `
    -Prefix chain21 -RunId 20261003-01-slideshow-21-chain `
    -BaseRunner helpers/local-comfyui-host/run-qwen21-native-t2i.ps1 `
    -EditRunner helpers/local-comfyui-host/run-qwen21-native-edit.ps1 `
    -BaseCheckpoint qwen_image_2.1_int8_convrot.safetensors `
    -Checkpoint qwen_image_2.1_int8_convrot.safetensors -LoraName '' `
    -Steps 25 -Cfg 1 -Sampler euler -Scheduler simple `
    -VarySeedPerStep `
    -GraphLabel 'Qwen-Image-2.1 native (app-configured) | 25 steps cfg 1 euler/simple | no LoRA | distinct seed per link'

# FROM-BASE — every step edits step 1, same seed, same prompts
& specs/image-generator-tests/sex-slideshow/run-sex-slideshow.ps1 `
    -Prefix frombase21 -RunId 20261003-02-slideshow-21-frombase -FromBase `
    -BaseRunner helpers/local-comfyui-host/run-qwen21-native-t2i.ps1 `
    -EditRunner helpers/local-comfyui-host/run-qwen21-native-edit.ps1 `
    -BaseCheckpoint qwen_image_2.1_int8_convrot.safetensors `
    -Checkpoint qwen_image_2.1_int8_convrot.safetensors -LoraName '' `
    -Steps 25 -Cfg 1 -Sampler euler -Scheduler simple `
    -GraphLabel 'Qwen-Image-2.1 native (app-configured) | 25 steps cfg 1 euler/simple | no LoRA'
```

Notes for a 2.1 run:

- `-Checkpoint` / `-BaseCheckpoint` are the harness's model labels; for this family they must name the
  2.1 UNET artifact and the runners reject anything else rather than render a different model.
- `-LoraName ''` is the app's configured state for the 2.1 editor (`ImageEditorLoraName` is blank). The
  scene-LoRA catalog in Model Manager is opt-in *per render*; the app applies none by default.
- **`-VarySeedPerStep` is not optional for a 2.1 chain.** A pinned seed makes 2.1 replay the same noise
  draw at every link and the frames collapse into noise by the third edit; a distinct seed per link keeps
  all nineteen clean. See "Root cause and fix" above. `-FromBase` runs are stable either way, but they look
  like near-copies under a pinned seed for the same reason.
- The edit graph returns the frame at its pixel budget rather than the source's exact size: a 1216x832
  step 1 comes back **1248x832** at resolution budget 1024.
- `-GraphLabel` exists so the manifest states the renderer that actually ran. It defaults to the
  merged-checkpoint wording, which would be wrong for a 2.1 run.

## The 20 target states

| # | Step | # | Step |
|---|---|---|---|
| 1 | man + woman standing (T2I base) | 11 | woman stands; man's jeans remain down; residue remains on her face |
| 2 | face each other | 12 | woman on right, man on left, woman facing away |
| 3 | woman on knees | 13 | woman faces away; jeans pulled to thighs; shirt remains on |
| 4 | man lowers jeans, soft penis | 14 | woman bends over; man behind; both jeans down; shirts remain on |
| 5 | woman's hand, penis erect | 15 | man positions at entry from behind |
| 6 | woman positions to take it in mouth | 16 | tip penetrates from behind |
| 7 | tip in mouth | 17 | half penetrates from behind |
| 8 | half in mouth | 18 | fully penetrates from behind |
| 9 | all in mouth | 19 | man withdraws from behind |
| 10 | man ejaculates onto woman's face | 20 | man ejaculates onto buttocks/lower back; shirt remains on |

The full instruction text for every step is recorded in each run's `manifest.json`, so a run is
self-describing even if the prompts here change later.

## Failure behaviour

The run stops at the first failed step and the manifest records `completed=false` and `failedAtStep`.
In `-FromBase` mode, a later frame never descends from an earlier edited frame; it always uses the
recorded step-1 base source.

## Verified (2026-09-12, Remix + LoRA @0.8)

Steps 1–3 inspected: step 1 matched the prompt exactly; **steps 2 and 3 preserved both subjects' wardrobe
and barefoot state while executing the pose changes**, i.e. the chain does not silently re-dress. Steps 4+
are explicit and the image-review tool refuses them, so those frames require human review.

## Measured: chained mode has a CFG-1 feedback loop (2026-09-12)

`measure-chain-degradation.py` on the committed runs. The four corner boxes are flat studio backdrop in
every frame, so their RGB directly probes the reported "the background starts to go different colours,
and it gets worse with each edit".

| Run | Config | backdrop step01 -> last | max channel drift | saturation |
|---|---|---|---|---|
| `20260912-06` Remix+LoRA | 8 steps / CFG 1 | (17,19,21) -> (99,56,86) | 82/255 | 50 -> 120 |
| `20260912-07` v23+LoRA | 8 steps / CFG 1 | (17,19,21) -> (77,27,46) | 60/255 | 50 -> 153 |
| `20260912-08` Remix+LoRA | 20 steps / CFG 3.5 (10 steps only) | (17,19,21) -> (65,51,67) | 48/255 | 50 -> 87 |

- The walk is **monotonic** (~3.7-4.6 channel units per link, never reversing) — the signature of a
  feedback loop, not a one-off bad render.
- The damage is **chromatic, not structural**: `edge_std` RISES (10.28 -> 14.91) and KB grows +55%, so this
  is not blur or detail loss; the subject/pose/wardrobe instructions held for all 19 steps.
- It is a function of **chain length, not the model**: the same prompt and seed applied as a *single* edit
  produces no drift at all (control recorded in `stabilize-color.py`).
- Drift **direction is checkpoint-dependent** (Remix -> magenta, v23 -> red) with the *same* LoRA, so the
  model+LoRA pair sets the bias that the loop then amplifies.

**Cause.** `KSampler.cfg` maps to `true_cfg_scale`. At CFG 1 the sampler has no negative-branch pressure,
so each frame's small colour cast becomes the next link's conditioning and is amplified; 8 steps also
leaves each link too little room to re-converge.

**Superseded recommendation.** The earlier suggested fix was to use the validated Qwen-Image-Edit-2511 recipe
— **40 steps, CFG 4, euler/simple, denoise 1** (see
`.github/instructions/qwen-image-edit-2511.instructions.md`) — and add `-StabilizeColors`, which
mean-matches the frame fed forward back to step 1 and zeroes the residual walk independently of sampling.
`-AnchorBackground` is complementary. The runner already exposes `-Steps`, `-Cfg`, `-Sampler`,
`-Scheduler` and `-Denoise`, so **no runner code change is required** for this configuration. Note that
raising sampling alone is not sufficient: run `08` (20 steps / CFG 3.5) only halved the drift.

**Historical note.** Runs `09`, `10`, `12` (hi-sampling, stabilized, background-anchor) stopped at steps
5-8 with a leftover `_staging-stepN/` and **no `manifest.json`**, i.e. they were interrupted rather than
hitting the runner's fail-and-record path. The mitigations have therefore never been measured end-to-end.

## Anti-drift toolchain (legacy chained mode)

Three deterministic, model-free tools address the backdrop walk. They are complementary, not alternatives:

For normal scene generation, prefer `-FromBase`; it prevents the feedback loop instead of repairing each
edited frame after the fact. The tools below remain useful for analyzing or salvaging legacy chained runs,
and `pin-background.py` is only appropriate when the source really has a flat, border-connected backdrop.

| Tool | What it does | Role |
|---|---|---|
| `pin-background.py` | Puts the **reference's backdrop back exactly**, pixel for pixel. The subject mask is recovered by **border connectivity** (backdrop-coloured AND connected to the image edge), so a garment that merely matches the backdrop colour is not erased; per-row margins preserve the backdrop's vertical gradient. | Strongest fix. Needs a flat backdrop and a clean reference frame. |
| `stabilize-color.py` | Mean-offset (default) or mean+std colour transfer of the fed-forward frame back to the reference. | Cheapest fix; corrects global statistics, not their spatial distribution. |
| `make-background-reference.py` | Builds a subject-free backdrop from a frame, for use as the `-AnchorBackground` image2. | Anchors the environment *inside* the model instead of correcting it afterwards. |

```powershell
.venv/Scripts/python.exe specs/image-generator-tests/sex-slideshow/pin-background.py <reference> <input> <output> [--margin 0.08] [--tolerance 18] [--feather 3]
```

**Upscaling does not fix this.** The damage is chromatic, not resolutional — `edge_std` RISES across the
run, so the frames are not getting soft, and an upscaler inherits (or amplifies) a colour cast rather than
removing it. All three tools above are deterministic and idempotent (applying one to its own output is a
no-op), which is what makes them safe to apply on **every** link; a learned step is not, and can add its
own bias each link. If an upscaler is ever used, it must run *before* the final colour/backdrop fix — the
deterministic correction has to be the last operation before the frame is fed forward.

Known limitation of `pin-background.py`: a subject's cast **shadow** is darker than the backdrop, so it
reads as foreground and its backdrop is left as-is. Feathering softens the seam; widen `--tolerance` if a
soft shadow is being split.

## Verified (2026-10-03, Qwen-Image-2.1 native — both modes)

Two runs, same seed 6601, same prompts, same renderer; only the mode differs:
`runs/20261003-01-slideshow-21-chain` (`chained = true`) and
`runs/20261003-02-slideshow-21-frombase` (`-FromBase`, `chained = false`).

The recipe is the app's own configured values for the two local 2.1 rows — editor **25 steps / CFG 1 /
euler+simple / denoise 1 / resolution budget 1024 / no editor LoRA**, base **30 steps / CFG 3 /
er_sde+beta** — driven by `helpers/local-comfyui-host/run-qwen21-native-{t2i,edit}.ps1`. Nothing was tuned
for the run.

| | Chained | From-base |
|---|---|---|
| Frames that are still a picture | 1, 3, 4 | all 19 |
| Blur-residual σ (high-frequency noise) | 4.8 → **36.2** at step 5, then 38–43 | 4.3–5.3 throughout |
| Backdrop corner σ (flat studio grey) | 3.9 → **66** at step 5, then ~84 | ~3.9 throughout |
| Frame mean brightness | 60 → 118 | ~59 |

- **Chained 2.1 collapsed at step 5** in this first pair of runs, before the seed policy was understood:
  from there every frame is bright noise and the *flat studio backdrop alone* carries σ≈84. Steps 5–20 are
  not usable frames. The reason is not the model, the prompt or the envelope — see "Root cause and fix"
  below; these two runs pinned one seed across every link.
- **From-base 2.1 never degrades**, so the collapse is a property of the chained configuration, not of the
  2.1 edit graph. Adherence looked loose here — each frame differs from step 1 by only 5–17% of pixels
  (max-channel diff > 25) — which the seed finding below also explains, rather than CFG 1 alone.
- Superseded conclusion: it is **not** necessary to re-qualify `ImageEditorSteps/Cfg/Sampler/Scheduler` or
  to add a LoRA to make chaining work. Correct the seed policy first (`-VarySeedPerStep`), because the
  pinned seed — not the envelope — was what destroyed these runs.

### Root cause and fix (2026-10-03): "noise replay"

The collapse is **not** a limit of 2.1's editing — it is the chain reusing one seed. On the 2.1 native
graph the sampler's `latent_image` comes from `TextEncodeQwenImage21`, and that output is an **empty latent
sized to image_1** ("any other size shifts the edit"), so at `denoise 1` the source image enters the render
through the **conditioning only** and every link starts from a *seed-derived noise draw*. Reusing one seed
across a chain replays the identical noise draw at every link; the sampler then compounds it.

This is the documented "noise replay" failure:

- Diffusers issue **#14824** — a Qwen-adjacent contributor `naykun`: *"the same seed is being used for both
  the initial image generation and the subsequent editing… If the output of the edit step is also exactly
  1024, it means the initial noise is completely identical, and the dynamic scheduler will compute the same
  sigma values. This exacerbates the problem we refer to as 'noise replay'."*
  https://github.com/huggingface/diffusers/issues/14824
- ComfyUI issue **#16607** — "any edit → edit → edit workflow (using a previous output as the next input)
  becomes unusable", resolved by a Diffusers quote and *"Simply change the seed if you run into the same
  thing and it will be fine"* … *"Changing the seed fixed it immediately."*
  https://github.com/Comfy-Org/ComfyUI/issues/16607

**Measured on this host** (blur-residual σ, high-frequency noise, 0–255 luma; clean ≈ 4–5). Same host,
model, graph and prompts; only the seed policy changes:

| Chain, 4 SFW links | link 1 | link 2 | link 3 | link 4 |
|---|---|---|---|---|
| **Pinned seed** (the harness default) | 5.09 | **12.81** | **36.75** | — (run collapses) |
| **Distinct seed per link** (`-VarySeedPerStep`) | 5.10 | 4.56 | 4.25 | 4.00 |
| Distinct seed — flat backdrop σ | 3.77 | 3.43 | 3.25 | 2.96 |

Controls that rule out the other explanations (all pinned-seed, all collapse identically):

| Control | link 1 | link 2 | link 3 |
|---|---|---|---|
| Explicit prompts (`20261003-01`) | 4.77 | **12.88** | **36.19** |
| `-Resolution 0` (no resize at any link) | 4.93 | **14.76** | **38.28** |
| **SFW** prompts (arms folded → hair tuck → look left) | 5.09 | **12.81** | **36.75** |
| From the **official template's own sample photo** | 4.23 | **15.85** | **37.54** |
| **denoise 0.5** | **11.06** | **30.48** | **33.74** |

So prompt content, the base frame, the resize and the denoise are all irrelevant; the seed policy is the
variable.

- **Use `-VarySeedPerStep` for any chained 2.1 run.** It gives each edit `Seed + stepN` and never the run
  seed, so no link can replay its source's noise. The flag is off by default because a pinned seed is what
  makes two configurations comparable step-for-step — comparability and chainability are in tension here,
  and this is the switch that trades one for the other.
- It also explains the **weak `-FromBase` edits**: with the pinned seed every from-base edit replayed the
  noise the step-1 base was generated with, which is the documented trigger for a "haloed near-copy"
  (edit ignored) rather than a real edit — consistent with the 5–17% changed-pixel figure above.
- The app's envelope **is** the official one: the official template `image_qwen_image_2_1_image_edit.json`
  ships `KSampler` = 25 steps / CFG 1 / `euler` / `simple` / denoise 1, `resolution` 1024,
  `QwenImage21Cache` between the UNET and the sampler, source at `images.image_1`. Its notes say "cfg: keep
  1 for the Qwen Image 2.1 official path" and "steps: … about 40-50 with euler", and that `resolution` is a
  total pixel budget whose official default is 1024.
- Note what Qwen actually advertises: **one-pass** multi-reference composition (up to 10 refs), one-pass
  storyboard generation, and — for *local/masked* edits only — "successive changes while preserving the
  rest of the scene… assembled into simple animations" (https://qwen.ai/blog?id=qwen-image-2.1). Chained
  full-frame editing is not an advertised workflow, but it does work once the seed policy is right.
- Not tested here: the official template's optional **prompt-enhancer** pass
  (`qwen3.5_9b_qwen_image_2.1_pe_i2i.int8_convrot.safetensors`, `thinking` on) — not installed on this host.
- Also unrelated but real: ComfyUI issue **#16435**, a deterministic 1024-resolved-grid attention/VAE-splice
  artifact (992/1056 are clean), still open; it would show on link 1 rather than compounding.

**Full 19-link runs with the guard on** — `runs/20261003-04-slideshow-21-chain-varseed` (chained) and
`runs/20261003-05-slideshow-21-frombase-varseed` (`-FromBase`). Chained σ stays **3.35–5.40** with backdrop
σ **2.5–4.4** across all nineteen links, and every link still changes the frame
(mean|diff| vs the previous link 1.9–8.1), so the guard fixes the collapse.

**But the guard does not make chaining free.** `measure-chain-degradation.py` on the same runs:

| Metric over 19 steps | Chained (varseed) | From-base (varseed) |
|---|---|---|
| `edge_std` (sharpness / high-frequency energy) | 17.46 → **11.69 (−33%)** | 17.46 → 16.96 (−2.8%) |
| backdrop RGB (flat studio grey) | (61,57,55) → **(27,21,14)**, drift **41/255** | (61,57,55) → (59,56,53), drift **2/255** |
| saturation | 35.2 → **122.5** | 35.2 → 39.3 |
| `gray_std` (contrast) | 25.47 → 17.80 | 25.47 → 27.88 |

The backdrop walk is **monotonic** (~2.2 channel units per link) and the frame softens steadily: the same
feedback-loop signature the merged checkpoints showed in 2026-09-12, just slower. It happens because every
link is a *full re-render* (denoise 1 from a fresh noise draw, conditioned on the previous frame), so each
link's small restyle bias becomes the next link's input. Independent one-hop edits from a fixed base show
essentially nothing (2/255).

So: **the collapse was the harness's pinned seed; the slow drift is real chaining accumulation.** For
chained 2.1 work, budget the number of links, or re-anchor every few links, or repair each link
deterministically (`stabilize-color.py` / `pin-background.py`, see the anti-drift toolchain below).
The pinned-seed pair (`20261003-01` / `20261003-02`) is kept as the failure evidence, not as a recipe.

Validate it without any imagery (synthetic fixture, both hard cases — a gradient backdrop and an interior
patch matching the drifted backdrop colour):

```powershell
.venv/Scripts/python.exe specs/image-generator-tests/sex-slideshow/validate-pin-background.py
```
