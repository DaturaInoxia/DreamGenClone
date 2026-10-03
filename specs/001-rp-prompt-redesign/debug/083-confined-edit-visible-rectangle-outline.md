# 083 — A confined edit still shows its rectangle: the "Confine this edit to a region" box outline in the render

**Session:** 2026-10-02 · **State:** implemented, build green, RolePlay suite 3407 passed / 4 pre-existing failures
**Scope:** the region mask and the render composite (CASE-25), plus the fail-fast for a zero feather.
**Related:** CASE-21 (masked region), CASE-24 (outpaint), B-130 (ImageEditWorkspace region tools), 081/082 (the same editor).

---

## Report

Operator, after a region edit that *was* confined — the confinement worked, the edge did not:

> "the Confine this edit to a region rectangle outline can still be seen on the rendered image obvious that an edit was
> done, the edit is confined to the box though"

---

## Analysis

### 1. The host discards the feather we paint into the mask

Our mask PNG carried a soft ramp (`ImageRegionMaskEngine.MaskValue`), on the belief that a blurred mask is what
softens an inpaint edge. ComfyUI's own encode node rounds the mask to 0/1 in **both** places it is used
(`nodes.py`, `VAEEncodeForInpaint.encode`; verified against the upstream source, not from memory):

```python
m = (1.0 - mask.round()).squeeze(1)                                     # the pixels path
return ({"samples": t, "noise_mask": (mask_erosion[:,:,:x,:y].round())}, )  # the sampler's mask
```

So the ramp is thrown away, and the area the graph actually confines to is a **hard rectangle = drawn + grow**. The
ramp's 0.5 crossing sat exactly on that rectangle's edge, which is why the feather value had no effect at all.

### 2. Measured on the operator's own run

`SceneImageEditCompilationAttempts.00584266…` (session `63a07750`, region 57.261 %, 60.491 %, 8.341 % × 2 %,
**grow 8, feather 32**) → source `64951899…png` → result `45ff88cd…png`, both 1024×1024:

| measurement | value |
|---|---|
| `\|result − source\|` across the mask edge, centre row | `…127 138 144 67 \| 3 3 4 5…` — stops in **one** pixel |
| image gradient at the mask's right edge (x=680) | **65** (local median 2.0, p99 35.5) |
| image gradient at the mask's bottom edge (y=648) | **59** (local median 2.9, p99 45.2) |

A 32-pixel feather would have spread a 144-unit step over 32 pixels. It did nothing, and the one-pixel step above the
99th percentile of the surrounding image is the outline the operator saw — sitting 8 px outside the drawn box, which is
also why it did not look like *their* rectangle.

---

## Resolution

### The mask is hard, and the softening happens after the render

- `ImageRegionMaskEngine.Build` now paints a hard rectangle whose white area is the drawn region grown by
  `GrowMaskBy + FeatherPixels`. The extra feather is not decoration: the fade has to lie over pixels the host is
  *allowed to generate*, and fading pixels the host pinned would fade the source into itself.
- **New** `ImageRegionMaskEngine.BuildCompositeAlpha`: opaque over the drawn region grown by `GrowMaskBy`, then a
  linear fade to nothing across `FeatherPixels` — reaching zero exactly on the mask's white edge, which is where the
  host's hard step sits. Its 0 is what multiplies that step away.
- `MediaEditImageEditingJobHandler` blends the render back over the frame it started from (the source; the padded
  canvas for an outpaint) through that alpha, after the editor call and before the row is completed. Everything outside
  the fade is handed back **byte for byte** as the source had it, which also removes the faint VAE round-trip halo the
  whole frame carried before (~30 % of the frame differed by 1–3/255).
- One rule covers a region and an outpaint: the alpha always fades *outward* from the editable rectangle, clipped to
  the frame. For an outpaint the outward side of the strip's inner edge is the original content, which is exactly where
  that fade belongs — so there is no per-kind branch.
- Alignment: the host crops a frame it cannot encode whole (a whole number of VAE blocks, taken evenly from the leading
  edge of each axis), so a render can come back a few pixels smaller than the canvas. The composite places it back on
  that centred crop, and refuses by name — rather than shifting silently — when the size is anything else. Both sizes
  are in the message.

### A zero feather is refused by the edit, not by the row

`RegionFeatherPixels = 0` is now refused by `MediaEditRegionOperation.Validate` / `MediaEditOutpaintOperation.Validate`
with a message naming the setting and saying where to change it (`MediaEditCompilationService.EnqueueRunAsync` calls
those before queueing, so the operator sees it in the editor's error alert with no render paid for).

The **settings row** still accepts 0: the same row is round-tripped by flows that never confine anything
(front/body/capture settings), and the defect was in the edit, not in the row. Narrowing the check to the edit is what
keeps this change from breaking those flows.

---

## Evidence

- **Host source**: `VAEEncodeForInpaint.encode` (`mask.round()` twice) — the mechanism above.
- **The measured seam** on the operator's run (table above), from the two PNGs on disk.
- **Tests** (`DreamGenClone.Tests/RolePlay`):
  - `ImageRegionMaskEngineTests` — the mask is hard over drawn+grow+feather; the alpha is opaque at drawn+grow, fades
    to 0 at the mask's edge, fades inward when the region is flush with the frame, survives a feather wider than the
    frame, and a zero feather is refused by name.
  - `MediaEditImageEditingJobHandlerTests.ConfinedRun_BlendsTheRenderBackOverTheSourceThroughTheFeather` — reads the
    produced .png pixel by pixel: the render owns the region, the ramp mixes, and every pixel outside the fade equals
    the source exactly.
  - `MediaEditImageEditingJobHandlerTests.OutpaintRun_KeepsTheNewStripAndFadesIntoTheOriginal` — the same over the
    padded canvas: the new strip is kept whole and the seam fades into the original.
  - `MediaEditImageEditingJobHandlerTests.ConfinedRun_WithoutAFeather_IsRefusedByNameAndMarksTheRow` — refused before
    the editor is called, and the row is marked failed rather than left pending.
- `dotnet test … --filter "FullyQualifiedName~RolePlay"` → **3407 passed, 4 failed**. The 4 are pre-existing failures
  from other in-flight worktree work (3 × `SdxlSceneImagePromptBuilderTests` from a modified
  `SceneImageCompilerSystemPrompts.cs`, 1 × `SceneLoraSelectionWireTests` from a new wire file), unrelated to this
  change; the baseline before it was 3397 passed / the same 4 failed.

---

## Files

- `DreamGenClone.Web/Application/RolePlay/Editing/ImageRegionMaskEngine.cs` — hard mask over drawn + grow + feather;
  new `BuildCompositeAlpha`; `PixelRect` geometry.
- `DreamGenClone.Web/Application/RolePlay/Editing/MediaEditImageEditingJobHandler.cs` — `ReadConfinementAsync`,
  `CompositeConfinedRender`, `AlignRender`, `Blend`; the source is read once before the editor call (the upload
  transport disposes the stream it sends) and the render is blended before completion.
- `DreamGenClone.Web/Application/RolePlay/Editing/MediaEditOperations.cs` — a positive feather is required, by name.
- `DreamGenClone.Infrastructure/RolePlay/ImageWorkflowRepository.cs` — comment records why the row still accepts 0.
- Tests: `ImageRegionMaskEngineTests`, `MediaEditImageEditingJobHandlerTests` (+ `Fixture` source size/colour,
  a size-honest editor stub), `MediaEditRegionOperationTests`, `MediaEditOutpaintOperationTests`,
  `MediaEditCompilationServiceTests`, `ImageWorkflowTemplateServiceTests`.

## Left open

Nothing on this defect. The **outpaint routing** defect found while testing it — an Asset Studio outpaint died with
"An edit run requires the compiled prompt its subject writer prepared" — is fixed in debug record
[084](084-outpaint-dies-with-no-compiled-prompt.md).
