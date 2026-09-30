# CASE-23 — the coloured circle SELECTS a region (C2b), it does not CONTAIN the edit

**Date:** 2026-09-29 · **Host:** WOOD-GAME-MAIN local ComfyUI 0.37.1, RTX 5080 · **Model:** Qwen-Image-2.1
(Unet `qwen_image_2.1_int8_convrot`, VAE `qwen_image_2.1_vae_bf16`, 25 steps, cfg 1.0, euler/simple)
**Runner:** `helpers/local-comfyui-host/run-qwen-2-1-proof.ps1`, cells `p0CircleSelect` / `p0CircleSelectControl`
**Annotator:** `helpers/local-comfyui-host/annotate-region-circle.py`

## What was asked

B-129 §C2 lists three routes under "local editing — region selection": coloured circles, painted
annotations, and "separate mask + original image as two inputs (the production form)". C2a (the mask
route) is already PROVEN by CASE-21. The C2b row — "Colored circles named in the instruction" — was
marked UNPROVEN with the note "needs only a prompt test — no graph change". This is that test.

## The design error that came first, and why it matters

The first attempt asked the ring to **contain** an edit: `p0Base` (one white t-shirt), a blue ring drawn
on the shirt, `Inside the blue circle ... change the colour of the t-shirt ... to bright red`.

It "failed" — the whole garment changed and the ring vanished — and its control produced the **same**
image, which reads like a refutation. It is not one. CASE-21 already documents the corollary: on this
graph the source travels as a **reference** latent and the sampler starts fresh, so an **unmasked** 2.1
edit **regenerates the entire frame**. Containment was never available on the circle route, so the run
measured CASE-21's finding again while looking like a verdict on C2b.

**A ring is a SELECTOR. A mask is a CONTAINER. Testing one for the other's job is the mistake.**

The second attempt failed the same way for a different reason: the instruction said "t-shirt" and the
candidate man wore a button-up *shirt*, so the noun had already disambiguated and the ring had nothing
to prove — both runs changed the same person.

## The measurement that works

`genShedTight` is a two-figure render (1216×1216): a man in an olive shirt at the left, a woman in a
grey t-shirt lying on the bench. The ring is drawn on the **man** — deliberately the *less* likely
target — and the noun is deliberately ambiguous ("the garment"), so **only the ring can decide**.

| Run | Source | Instruction (verbatim) | Result |
|---|---|---|---|
| `p0CircleSelect` | `p0CircleMen/source.png` (ring on the man) | `Change the colour of the garment worn by the person inside the blue circle drawn on the image to bright red. Leave the other person as they are.` | **The MAN's shirt turned red.** The woman's t-shirt unchanged; the blue ring was cleaned away |
| `p0CircleSelectControl` | `genShedTight/result_0.png` (clean base) | `Change the colour of the garment worn by the person in this image to bright red. Leave the rest of the image as it is.` | **The WOMAN's t-shirt turned red** — the model's own pick without a ring |

**Verdict: the ring overrode the model's default target and selected the person it enclosed.**

Artifacts:

- `artifacts/tmp/qwen-2-1/p0CircleMen/source.png` (the annotated input)
- `artifacts/tmp/qwen-2-1/p0CircleSelect/result_0.png` (the ringed run)
- `artifacts/tmp/qwen-2-1/p0CircleSelectControl/result_0.png` (the control)

## What this proves, and what it does not

**Proves:** a coloured ring drawn on the source, named in the instruction, **selects which candidate**
the edit applies to — with no graph change, no mask node and no mask the app would have to build. The
annotation is also understood as an annotation: it does not survive into the render.

**Does not prove:**

- **Containment.** The frame is regenerated. In these two runs the unringed person's garment happened to
  stay unchanged, but nothing constrains it. For an edit that must not touch the rest of the picture, the
  C2a masked latent (CASE-21) remains the production form.
- **A painted/brushed region (C2c)** — untested; a ring is a closed selection, a brush stroke is not.
- **Non-circular shapes, multiple disjoint rings in one call, or a ring plus reference images.**
- Anything semantic about edit quality: the verdict is *which person changed*, judged by eye. A pixel
  containment measurement is the wrong instrument for a selection claim — that is what made the first
  attempt look conclusive.

## Reproduce

```powershell
# 1. the ringed source (ring on the MAN, at 10,35,20,20 percent of the frame)
d:/src/DreamGenClone/.venv/Scripts/python.exe helpers/local-comfyui-host/annotate-region-circle.py `
    --input artifacts/tmp/qwen-2-1/genShedTight/result_0.png `
    --output artifacts/tmp/qwen-2-1/p0CircleMen/source.png `
    --rect-pct 10,35,20,20 --colour 0,64,255 --width 10 --inset 20

# 2. the two runs (the host is a LAN box; -ComfyUiUrl defaults to it)
powershell -File helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells p0CircleSelect `
    -TargetImage artifacts/tmp/qwen-2-1/p0CircleMen/source.png
powershell -File helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells p0CircleSelectControl `
    -TargetImage artifacts/tmp/qwen-2-1/genShedTight/result_0.png
```

`-Cells` takes ONE name per invocation.

## Consequence for the app

The two mechanisms compose and both are now evidenced:

- **ring → selection** (cheap, prompt-only, no graph change) — the operator's way of saying *which* thing;
- **masked latent → containment** (CASE-21) — what pins everything outside the edit.

An app region UI that wants both sends the ring for the operator's intent and a mask for containment.
