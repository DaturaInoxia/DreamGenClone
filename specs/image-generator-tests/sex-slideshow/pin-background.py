"""Pin a frame's flat studio backdrop back to a reference frame's backdrop.

WHY THIS EXISTS
  A chained edit run feeds each frame forward as the next link's reference, so loss accumulates. The
  measured failure mode is NOT blur -- it is a monotonic COLOUR WALK on the flat studio backdrop.
  Measured on the 19-step sex-slideshow (2026-09-12):

    step01  backdrop (17,19,21)   edge_std 10.28
    step19  backdrop (99,56,86)   edge_std 14.91   <- +82/255 on the worst channel, and edge_std RISES

  Because the damage is chromatic, the cure is chromatic. The two existing tools correct STATISTICS
  (stabilize-color.py matches channel means; make-background-reference.py rebuilds a clean backdrop to
  anchor with). This tool instead puts the reference's backdrop back EXACTLY, pixel for pixel, which is
  possible because a studio backdrop is flat, uniform and spatially known.

HOW THE MASK WORKS (and why it is not a colour test)
  The subjects wear dark grey t-shirts against a dark grey backdrop, so "is this pixel backdrop-coloured?"
  alone would punch holes straight through the clothing. A pixel is treated as background only if it is
  BOTH backdrop-coloured AND CONNECTED TO THE IMAGE BORDER (flood fill from the frame edge). Interior
  regions that merely happen to match the backdrop colour are therefore kept.

  Per row the backdrop colour is estimated from that row's two margins, so the backdrop's vertical
  structure (darker upper wall -> lighter floor) is respected instead of assumed flat.

WHY IT IS SAFE TO APPLY EVERY LINK
  It is deterministic and idempotent: applying it to its own output is a no-op. A learned fixer (an
  upscaler, a tiled refiner) is neither, so applying one every link can introduce its own drift. This
  one cannot accumulate.

Known limitation: a subject's cast SHADOW is darker than the backdrop, so it reads as foreground and its
backdrop is left as-is. Feathering softens the seam; widen --tolerance if a soft shadow is being split.

Usage:
  pin-background.py <reference> <input> <output> [--margin 0.08] [--tolerance 18] [--feather 3]

  reference  frame whose backdrop is the target (e.g. the run's step01)
  input      frame to fix
  output     path to write (PNG)
  --margin   fraction of width taken as pure backdrop at each side (default 0.08)
  --tolerance max per-channel distance from the row's backdrop colour to still count as backdrop
              (default 18)
  --feather  gaussian radius, in pixels, applied to the mask edge (default 3; 0 disables)
"""
import sys

import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

CROSS = np.array([[0, 1, 0], [1, 1, 1], [0, 1, 0]], dtype=bool)


def parse_args(argv):
    if len(argv) < 3:
        raise SystemExit(
            "usage: pin-background.py <reference> <input> <output> "
            "[--margin 0.08] [--tolerance 18] [--feather 3]"
        )
    reference, source, output = argv[0], argv[1], argv[2]

    margin, tolerance, feather = 0.08, 18.0, 3.0
    rest = argv[3:]
    if len(rest) % 2:
        raise SystemExit(f"missing value for {rest[-1]}")
    for flag, value in zip(rest[0::2], rest[1::2]):
        number = float(value)
        if flag == "--margin":
            margin = number
        elif flag == "--tolerance":
            tolerance = number
        elif flag == "--feather":
            feather = number
        else:
            raise SystemExit(f"unknown flag {flag}")
    return reference, source, output, margin, tolerance, feather


def load(path):
    with Image.open(path) as image:
        return np.asarray(image.convert("RGB"), dtype=np.float32)


def corners(image):
    """Mean RGB of the four corner boxes -- the same probe measure-chain-degradation.py uses."""
    height, width = image.shape[:2]
    box_w, box_h = max(1, width // 10), max(1, height // 10)
    boxes = (
        (0, 0, box_w, box_h),
        (width - box_w, 0, width, box_h),
        (0, height - box_h, box_w, height),
        (width - box_w, height - box_h, width, height),
    )
    means = [image[y0:y1, x0:x1].reshape(-1, 3).mean(axis=0) for x0, y0, x1, y1 in boxes]
    return np.mean(means, axis=0)


def main():
    reference_path, source_path, output_path, margin, tolerance, feather = parse_args(sys.argv[1:])

    source = load(source_path)
    reference = load(reference_path)
    height, width = source.shape[:2]

    margin_x = max(1, int(width * margin))
    if margin_x * 2 >= width:
        raise SystemExit("margin too wide for this image")

    if reference.shape[:2] != (height, width):
        reference = np.asarray(
            Image.fromarray(reference.astype(np.uint8)).resize((width, height), Image.Resampling.LANCZOS),
            dtype=np.float32,
        )

    # --- target backdrop: the reference's own backdrop, rebuilt subject-free but gradient-preserving ---
    ref_left = reference[:, margin_x - 1, :]
    ref_right = reference[:, width - margin_x, :]
    blend = np.linspace(0.0, 1.0, width, dtype=np.float32)[None, :, None]
    target_backdrop = ref_left[:, None, :] * (1.0 - blend) + ref_right[:, None, :] * blend

    # --- mask: backdrop-coloured AND connected to the image border ---
    source_row = (source[:, margin_x - 1, :] + source[:, width - margin_x, :]) / 2.0
    distance = np.max(np.abs(source - source_row[:, None, :]), axis=2)
    backdrop_like = distance <= tolerance

    labels, component_count = ndimage.label(backdrop_like, structure=CROSS)
    if not component_count:
        raise SystemExit("no backdrop-coloured region found; raise --tolerance")

    border_labels = set()
    for edge in (labels[0, :], labels[-1, :], labels[:, 0], labels[:, -1]):
        border_labels.update(np.unique(edge).tolist())
    border_labels.discard(0)
    if not border_labels:
        raise SystemExit("backdrop is not connected to the border; raise --tolerance")

    mask = np.isin(labels, list(border_labels)).astype(np.uint8) * 255
    mask_image = Image.fromarray(mask)
    if feather > 0:
        mask_image = mask_image.filter(ImageFilter.GaussianBlur(radius=feather))
    alpha = np.asarray(mask_image, dtype=np.float32)[:, :, None] / 255.0

    combined = source * (1.0 - alpha) + target_backdrop * alpha
    fixed = np.clip(np.rint(combined), 0, 255).astype(np.uint8)
    Image.fromarray(fixed).save(output_path)

    replaced = float((alpha > 0.5).mean())
    before = corners(source)
    after = corners(fixed.astype(np.float32))
    target = corners(target_backdrop)

    print(
        f"pinned {source_path} -> {output_path} | backdrop "
        f"({before[0]:.0f},{before[1]:.0f},{before[2]:.0f}) -> "
        f"({after[0]:.0f},{after[1]:.0f},{after[2]:.0f}) "
        f"(reference ({target[0]:.0f},{target[1]:.0f},{target[2]:.0f})) | "
        f"{replaced:.1%} of frame replaced | components {component_count} | "
        f"enclosing {len(border_labels)} border-connected"
    )


if __name__ == "__main__":
    main()
