# Sex Slideshow (local ComfyUI — Qwen source-image editor)

A **19-step chained edit sequence**: one scene progressed from clothed standing to a completed act, one
edit per step. Runnable against any editor configuration so two checkpoints/LoRAs can be compared
step-for-step.

## Why it is chained (and why that matters)

Step 1 is a text-to-image render (the editor is an *editor* — it needs a starting image). **Every later
step edits the previous step's output.** That is the whole point: unlike the anatomy matrix, which edits a
fixed base, this harness tests whether an editor can hold two subjects, their wardrobe and their spatial
relationship across **18 successive** edits. Drift, silent re-dressing and identity loss only show up in a
chain.

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
| `run-sex-slideshow.ps1` | The 19-step chained runner (prompts are defined here) |
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
    -RunId 20260912-07-slideshow-v23lora08
```

**Give each configuration its OWN `-RunId`.** Step files are named `stepNN-<slug>.png`, so two
configurations sharing a run folder would overwrite each other's steps and manifest.

## The 19 steps

| # | Step | # | Step |
|---|---|---|---|
| 1 | man + woman standing (T2I base) | 11 | woman turns away from camera |
| 2 | face each other | 12 | woman lowers her jeans |
| 3 | woman on hands and knees | 13 | woman bends over |
| 4 | man lowers pants, soft penis | 14 | man positions at entry |
| 5 | woman's hand, penis erect | 15 | tip penetrates |
| 6 | woman positions to take it in mouth | 16 | half penetrates |
| 7 | tip in mouth | 17 | fully penetrates |
| 8 | half in mouth | 18 | man withdraws |
| 9 | all in mouth | 19 | man ejaculates onto her |
| 10 | woman stands facing him | | |

The full instruction text for every step is recorded in each run's `manifest.json`, so a run is
self-describing even if the prompts here change later.

## Failure behaviour

The chain stops at the first failed step and the manifest records `completed=false` and `failedAtStep`.
Deliberate: later frames would otherwise descend from the wrong source image while looking plausible.

## Verified (2026-09-12, Remix + LoRA @0.8)

Steps 1–3 inspected: step 1 matched the prompt exactly; **steps 2 and 3 preserved both subjects' wardrobe
and barefoot state while executing the pose changes**, i.e. the chain does not silently re-dress. Steps 4+
are explicit and the image-review tool refuses them, so those frames require human review.

## Measured: the backdrop drift is a CFG-1 feedback loop (2026-09-12)

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

**Suggested fix (computed, not yet validated end-to-end).** Use the validated Qwen-Image-Edit-2511 recipe
— **40 steps, CFG 4, euler/simple, denoise 1** (see
`.github/instructions/qwen-image-edit-2511.instructions.md`) — and add `-StabilizeColors`, which
mean-matches the frame fed forward back to step 1 and zeroes the residual walk independently of sampling.
`-AnchorBackground` is complementary. The runner already exposes `-Steps`, `-Cfg`, `-Sampler`,
`-Scheduler` and `-Denoise`, so **no runner code change is required** for this configuration. Note that
raising sampling alone is not sufficient: run `08` (20 steps / CFG 3.5) only halved the drift.

**Not yet validated.** Runs `09`, `10`, `12` (hi-sampling, stabilized, background-anchor) stopped at steps
5-8 with a leftover `_staging-stepN/` and **no `manifest.json`**, i.e. they were interrupted rather than
hitting the runner's fail-and-record path. The mitigations have therefore never been measured end-to-end.

## Anti-drift toolchain

Three deterministic, model-free tools address the backdrop walk. They are complementary, not alternatives:

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
