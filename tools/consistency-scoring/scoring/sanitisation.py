"""Heuristic screen for silently sanitised image renders, not ground truth.

This screen helps stop silently-sanitised renders entering the baseline. A human
verdict overrides the heuristic result.
"""

import numpy as np
from PIL import Image


# Broad RGB and YCbCr bounds for a conservative skin-tone pixel screen.
SKIN_TONE_RGB_BOUNDS = ((95, 255), (40, 240), (20, 200))
SKIN_TONE_YCBCR_BOUNDS = ((60, 240), (77, 135), (125, 190))
SKIN_TONE_MIN_RED_GREEN_DIFFERENCE = 15

# Conservative screen threshold, tuned later; human verdict overrides per B-111 P8.
LOW_SKIN_FRACTION_THRESHOLD = 0.01


def score_sanitisation(render_path: str) -> dict:
    with Image.open(render_path) as image:
        rgb = np.asarray(image.convert("RGB"), dtype=np.uint8)
        ycbcr = np.asarray(image.convert("YCbCr"), dtype=np.uint8)

    rgb_mask = np.logical_and.reduce(
        [
            rgb[:, :, index] >= bounds[0]
            for index, bounds in enumerate(SKIN_TONE_RGB_BOUNDS)
        ]
        + [
            rgb[:, :, index] <= bounds[1]
            for index, bounds in enumerate(SKIN_TONE_RGB_BOUNDS)
        ]
    )
    rgb_mask &= (
        rgb[:, :, 0].astype(np.int16) - rgb[:, :, 1].astype(np.int16)
        >= SKIN_TONE_MIN_RED_GREEN_DIFFERENCE
    )
    ycbcr_mask = np.logical_and.reduce(
        [
            ycbcr[:, :, index] >= bounds[0]
            for index, bounds in enumerate(SKIN_TONE_YCBCR_BOUNDS)
        ]
        + [
            ycbcr[:, :, index] <= bounds[1]
            for index, bounds in enumerate(SKIN_TONE_YCBCR_BOUNDS)
        ]
    )
    skin_fraction = float(np.mean(rgb_mask & ycbcr_mask))

    return {
        "suspected_sanitised": skin_fraction < LOW_SKIN_FRACTION_THRESHOLD,
        "signals": {"skin_fraction": skin_fraction},
    }