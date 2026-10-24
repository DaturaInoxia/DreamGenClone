# 085 — The seam is back, on both "Extend this image" and "Confine": the host's boundary band is 8 px, not one

**Session:** 2026-10-17 · **State:** implemented, build green, targeted edit suite 171 passed
**Scope:** `ImageRegionMaskEngine.BuildCompositeAlpha` (the composite ramp) and the feather bound both operation records enforce.
**Related:** 083 (the composite this corrects), 084 (outpaint routing), CASE-24, CASE-25, B-130.

---

## Report

Operator, on an Asset Studio image, referencing produced image `fa58872d7f4245ac8d6a23631d57578d`:

> "edit extend this image and confine have visible seams in the rendered image"

Two operations, one symptom: a vertical line where the edit was confined.

---

## Analysis

### 1. The evidence had to be recovered from the host

`fa58872d…` was deleted from the studio at 22:38:44 (its `SceneAssetImages` row survived, the PNG did not), so the
outpaint's **host render** was pulled from the local ComfyUI history (`qwen21-edit_00269_.png`, 1280x1024) and the
composite was replayed on it. The run was: source `f733b1e7…` (1024x1024), `Outpaint Right 25%`, `grow 8`, `feather 32`
(`SceneAssetImageEditCompilationAttempts.6f59da7c…`), so the mask's white edge is x = 1024 − 8 − 32 = **984**.

### 2. The mask edge is an 8–10 px smear, not a one-pixel step

Render minus source luminance, tree rows (y 0–400), columns 980…995:

| x | 980 | 982 | 983 | 984 | 985 | 986 | 987 | 988 | 989 | 990 | 991 | 992 | 993 | 994 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Δ | −7.7 | −2.7 | +6.4 | **+25.5** | **+55.6** | **+59.1** | **+69.8** | **+76.3** | **+79.5** | **+78.6** | **+80.7** | **+63.8** | **+28.9** | +3.4 |

A mask-less render of the same source (`qwen21-edit_00270`, the whole-frame "Centre the clothesline" edit) has no such
band at 984 (flat 24–26 luminance), so the band is the **pinned/generated boundary**, not the model's content. The
mechanism is the one 083 identified — `VAEEncodeForInpaint` flattens the white area to 0.5 grey and confines through a
latent one eighth of the frame's size — but the decoded edge is a whole latent cell wide (8 px, plus interpolation),
and it is the grey fill bleeding out, which is why it is *bright*.

083 assumed the artefact was a one-pixel step and therefore started the composite ramp **at** the mask edge. The ramp
0→255 therefore multiplied the smear back in at 6 %, 13 %, 19 %, 25 %, 31 % … — up to ~+25 luminance of a +80 band in
dark foliage. That is the line the operator saw; it runs around every confined rectangle, which is why **confine**
showed one too.

---

## Resolution

`ImageRegionMaskEngine.BuildCompositeAlpha` now shortens the ramp by the host's boundary band:

```csharp
var ramp = region.FeatherPixels - MediaEditRegionOperation.HostBoundaryBandPixels;   // 8 px
```

so the alpha is **0 across the last 8 px of the mask's white area** and the fade occupies the rest of the feather,
still reaching full opacity at the drawn+grow rectangle. Mask geometry is unchanged (white = drawn + grow + feather),
so the host still has its margin; the band it smears in is simply handed back as the source byte for byte.

`MediaEditRegionOperation.HostBoundaryBandPixels = 8` is the single named definition of that band (the same VAE block
size the handler already models as `MaxEncodeCropPixels`), and both operation records now **refuse a feather that fits
inside it** by name — `RegionFeatherPixels` must be above 8 — rather than silently degrading to a hard edge. The editor
panel shows the bound on the feather control and reports it in the region block reason.

---

## Evidence

Replaying the composite on the recovered render, column-step outliers in the trees (local median ≈ 14):

| alpha | top column steps, x 950…1059 |
|---|---|
| current (ramp from the mask edge) | 38.3 @ 992, 35.5 @ 984, 32.2 @ 983, 24.9 @ 993 |
| shortened ramp (8 px band) | 16.9 @ 963, 16.1 @ 970, 15.5 @ 969 — all the source's own texture edges |

Visually the bright line through the foliage is gone; the crop that showed a continuous vertical streak now matches the
surrounding trees.

Tests (`DreamGenClone.Tests/RolePlay`, targeted filter: **171 passed / 0 failed**):

- `ImageRegionMaskEngineTests.TheAlphaIsZeroAcrossTheHostsBoundaryBandAtTheMasksEdge` — the 8 px inside the mask's
  white edge are transparent while the mask is still white there.
- `ImageRegionMaskEngineTests.TheAlphaIsOpaqueInsideTheRegionAndFadesToNothingBeforeTheMasksEdge` — the ramp is
  `feather − 8` and ends one boundary band short of the edge.
- `MediaEditRegionOperationTests` / `MediaEditOutpaintOperationTests` — a feather at or below the band is refused by
  name, alongside the existing zero/negative cases.
- `MediaEditImageEditingJobHandlerTests.ConfinedRun_…` and `OutpaintRun_KeepsTheNewStripAndFadesIntoTheOriginal` — the
  produced PNGs are read pixel by pixel: the render owns the editable rectangle, the ramp mixes, and everything past
  the ramp equals the source exactly.

---

## Files

- `DreamGenClone.Web/Application/RolePlay/Editing/MediaEditOperations.cs` — `HostBoundaryBandPixels`, the feather bound
  in both `Validate` methods, and the parameter docs.
- `DreamGenClone.Web/Application/RolePlay/Editing/ImageRegionMaskEngine.cs` — the shortened ramp and its rationale.
- `DreamGenClone.Web/Components/Editing/ImageEditWorkspace.razor` — the feather control's minimum, help text, and the
  three settings checks.
- Tests: `ImageRegionMaskEngineTests`, `MediaEditRegionOperationTests`, `MediaEditOutpaintOperationTests`,
  `MediaEditImageEditingJobHandlerTests`, `MediaEditCompilationServiceTests`,
  `QwenSceneImageEditPromptCompilerTests`.

## Left open

Nothing on this defect. The operator's configured `RegionFeatherPixels = 32` is already above the band, so the next
confine or extend run needs no settings change.
