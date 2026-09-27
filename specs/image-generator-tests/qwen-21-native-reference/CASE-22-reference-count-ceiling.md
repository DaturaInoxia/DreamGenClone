# CASE-22 — Reference-count ceiling on Qwen-Image-2.1 (measured 2026-09-25)

**Question:** how many references does 2.1 actually honour? B-129 §2.1 rows C1c/C1d/C1e depend on 6, 5
and 10 references; the app's model rows declare `MaxReferences: 16`; and **nothing above three
references had ever been run.**

**Answer: slots up to 6 are consumed, including mid-list. At 10 the pose reference stops being honoured
— whichever slot it sits in — and the declared `MaxReferences: 16` is not supported.**

## Method — the skeleton as a slot detector

An OpenPose skeleton in a reference slot drives the pose decisively (CASE-05: a kneeling skeleton produced
a kneeling figure while the prompt still said "standing", same seed). So the skeleton is a **slot
consumption detector**: hold the prompt fixed and move the skeleton to position N. The figure either
kneels (slot N was consumed) or stands (it was dropped).

Every rung uses the **identical** prompt, which explicitly asks for the *opposite* of the skeleton:

> Photorealistic full-body photograph of one woman **standing upright** and facing the camera, her arms
> relaxed at her sides, her feet flat on the ground, plain neutral grey studio background, even soft
> lighting, natural skin texture, sharp focus, shot on 85mm.

Fillers are **face** references, deliberately not location references: an empty-room reference takes over
the composition (measured, CASE-20), which would make "no figure" uninterpretable. Seed 20260925
throughout; 25 steps; cfg 1; euler/simple.

## Result

| Cell | Refs | Skeleton at slot | Wall clock | Verdict |
|---|---|---|---|---|
| `refsSlot2` (control) | 2 | 2 | 35.4 s | **KNEELING** — the probe reads |
| `refsSlot6` | 6 | 6 | 126 s | **KNEELING** — slot 6 consumed |
| `refsSlot10` | 10 | 10 | 261.6 s | **STANDING** — pose ignored, **anatomy broken (extra arms)** |
| `refsSlot10First` (discriminator) | 10 | 1 | 256.6 s | **STANDING** — pose ignored, anatomy clean |
| `refsSlot6` @ `resolution 0` | 6 | 6 | 111 s | **KNEELING** — `resolution: 0` works |

### Visual verdict, per image

| Image | Verdict |
|---|---|
| `22-refs-02-slot2-PASS.png` | **PASS** — kneeling, both knees apart, arms raised overhead, despite the prompt asking for standing. Identity = Becky (frontal ref). |
| `22-refs-06-slot6-PASS.png` | **PASS** — the same kneeling pose. |
| `22-refs-10-slot10-FAIL.png` | **FAIL** — figure **standing** (feet flat, knees straight) so the skeleton did not take; **and the anatomy is corrupted**: a second pair of arms hangs at the sides while the raised arms belong to nobody. |
| `22-refs-10-slot1-FAIL.png` | **FAIL** — figure **standing** with the skeleton at slot 1 too, so this is not a position limit. Anatomy is **clean**, so the corruption above is an *ordering* artifact. |
| `22-refs-06-res0-PASS.png` | **PASS** — kneeling, clean anatomy, `resolution: 0`. |

## What each observation establishes

1. **Slots up to 6 are consumed, including mid-list.** The 6-reference run also copied the **garment**
   from the reference at **slot 5** (white tee, rolled jeans, and the left-calf tree tattoo that only
   exists in that accepted body render). So a mid-list reference carried content, which is the first
   positive evidence for the wardrobe/try-on row.
2. **10 references dilutes the pose reference regardless of position.** Skeleton last → standing;
   skeleton first → standing. Position is not the variable; **count** is. The practical ceiling for a
   *pose-carrying* composition is therefore **≤ 6**, not 10 and certainly not 16.
3. **Anatomy corruption at high reference counts is order-dependent.** With five face references before
   the skeleton the figure grew extra arms; with the skeleton first the anatomy was clean. So ordering
   is a *robustness* control at high counts, not only a placement control.
4. **Identity and wardrobe survive where pose does not.** At 10 references the face and the garment still
   transferred correctly — it is specifically the pose signal that degrades.
5. **`resolution: 0` is accepted and works.** The node's tooltip ("0 keeps each reference at its own
   size") is live: the run rendered in 111 s vs 126 s at 1024, with the pose and identity intact. The
   app currently **requires a positive budget**, so it is blocking a working node capability. This is an
   app-policy defect, not a model limit.
6. **Cost scales superlinearly.** 2 refs → 35.4 s, 6 → 126 s, 10 → 262 s: the 10-reference run costs
   **~7.4×** the 2-reference run. Latency, not capability, may end up being the binding constraint.

## Consequences for the app and for B-129 §2.1

- **Correct `MaxReferences`.** The declared `16` is unsupported; 10 is the official cap and it does not
  hold for pose-carrying compositions either. Treat **6 as the validated budget** for compositions that
  need a pose reference, and re-measure before claiming more.
- **Do not offer "generate all the pose library" style batches at 10+ references.** At 6 the cost is
  already 2 minutes per image.
- **`resolution: 0` should be an exposed option** (a persisted per-model value), since the node supports
  it and it is faster here.
- **C1c/C1d move from UNPROVEN to PARTLY** (slots are consumed and a mid-list reference transfers
  content) — but a genuine 6-person group portrait and a 5-input try-on are still not measured.
- **C1e (10-reference interior assembly) is refuted as a reliable route** for now.

## Not measured

- A genuine N-person group portrait, and N-subject try-on (only slot *consumption* was measured).
- Whether the pose ceiling is 7, 8 or 9 — only 2, 6 and 10 were sampled.
- 11–16 references (deliberately skipped: 10 already degrades, and cost is superlinear).
- Adult content, and any interaction with `grow_mask_by` / region masking.

## Reproduce

```powershell
foreach ($c in 'refsSlot2','refsSlot6','refsSlot10','refsSlot10First') {
    powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 `
        -Cells $c -Seed 20260925
}
# resolution: 0 into a separate output root so it does not overwrite the 1024 run
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 `
    -Cells refsSlot6 -Seed 20260925 -Resolution 0 -OutRoot artifacts/tmp/qwen-2-1-res0
```

Host: ComfyUI 0.37.1, RTX 5080, `qwen_image_2.1_int8_convrot.safetensors` +
`qwen3vl_8b_int8_convrot.safetensors` + `qwen_image_2.1_vae_bf16.safetensors`.
