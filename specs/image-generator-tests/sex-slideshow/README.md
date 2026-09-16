# Sex Slideshow (local ComfyUI — Qwen source-image editor)

A **20-step multi-reference edit sequence**: one clean base scene is rendered once, then every target
state is edited independently from that same base image. Runnable against any editor configuration so
two checkpoints/LoRAs can be compared step-for-step.

## Why the base starts in the side-profile "facing each other" pose

The identity pack contains **five curated angle-specific references** (front, 3/4L, 3/4R, profileL, profileR).
When the base render uses regional IP-Adapter + per-angle masks, the model can only condition on the
reference whose angle matches the requested pose. Starting the base facing the camera would force the
model to synthesize profile faces from the front ref on step 2; starting the base already facing each
other lets the curated profile refs (`*_profl`, `*_profr`) be used directly on the T2I base. Every
subsequent `-FromBase` edit inherits that stronger identity signal.

## Why it uses the base image for every edit

Step 1 is a text-to-image render (the editor is an *editor* — it needs a starting image). In the
recommended `-FromBase` mode, **every later step edits the step-1 output**, not the previous edit's
output. Each prompt therefore describes the complete target state, including pose, spatial arrangement,
clothing, and any accumulated state such as visible facial residue.

This is intentional. Earlier chained runs proved that feeding an edited frame into the next edit creates
a chromatic feedback loop: the flat studio background develops a starfield-like pattern even when the
subjects remain usable. FromBase removes that recursive image degradation; state continuity is expressed
by the full-state prompt instead.

The legacy chained behavior remains available when `-FromBase` is omitted, but it is diagnostic only and
should not be used to evaluate the maximum number of independent scene states.

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
| `run-sex-slideshow.ps1` | The 20-step runner; use `-FromBase` for independent edits from step 1 |
| `runs/<RunId>/` | `stepNN-<slug>.png`, `manifest.json`, `slideshow.png` — **tracked** output |

It reuses, rather than duplicates: `anatomy-edit-matrix/make-base-image.ps1` (step 1 T2I) and the
app-faithful edit runner `helpers/local-comfyui-host/run-local-aio-edit-proof.ps1`.

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

Validate it without any imagery (synthetic fixture, both hard cases — a gradient backdrop and an interior
patch matching the drifted backdrop colour):

```powershell
.venv/Scripts/python.exe specs/image-generator-tests/sex-slideshow/validate-pin-background.py
```
