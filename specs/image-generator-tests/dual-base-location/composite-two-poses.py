"""Build the four dual-base composites (2 poses x N locations) with rembg cutouts.

Parameterized replacement for `composite-all-locations.py` (which hard-coded the run dir and
the serverless output filename). The segmentation/placement math is unchanged - it is the
proven algorithm from FINDINGS-FLUX-OPENPOSE-PIPELINE.md:

    1. rembg (isnet-general-use) person segmentation, no thresholds
    2. keep components larger than 0.5% of the canvas
    3. scale the figures to 72% of frame height, feet at 92%, centred horizontally
    4. write the composite AND the harmonization mask (figure alpha, dilated 41px, blurred 6)

For each location it writes, into --out-dir:
    comp-<location>-a.png / comp-<location>-a-mask.png
    comp-<location>-b.png / comp-<location>-b-mask.png

Usage:
    python composite-two-poses.py --run-dir specs/.../runs/<run> \
        --studio-a <path to pose A studio render> \
        --studio-b <path to pose B studio render> \
        --locations bedroom,outdoors
"""
import argparse
from pathlib import Path

import cv2
import numpy as np
from PIL import Image
from rembg import new_session, remove

W, H = 1216, 832
# Canvas geometry (proven values - do not tune without a new proof).
TARGET_FIGURE_FRACTION = 0.72
FEET_Y_FRACTION = 0.92
MIN_COMPONENT_FRACTION = 0.005
MASK_DILATE_PX = 41
MASK_BLUR_SIGMA = 6


def cutout(png_path: Path, session):
    """Return the figures as RGBA with the studio backdrop removed."""
    img = Image.open(png_path).convert("RGB")
    cut = remove(img, session=session)
    alpha = np.array(cut)[..., 3]
    alpha = np.where(alpha > 100, alpha, 0).astype(np.uint8)

    # Keep only substantial components: drops faint backdrop halos and stray specks.
    num, labels, stats, _ = cv2.connectedComponentsWithStats((alpha > 0).astype(np.uint8))
    final = np.zeros_like(alpha)
    kept = 0
    for i in range(1, num):
        if stats[i, cv2.CC_STAT_AREA] > MIN_COMPONENT_FRACTION * W * H:
            final[labels == i] = alpha[labels == i]
            kept += 1
    if kept == 0:
        raise RuntimeError(f"rembg found no figure components in {png_path}")
    print(f"  {png_path.name}: kept {kept} figure component(s)")
    return Image.fromarray(np.dstack([np.array(cut)[..., :3], final]), "RGBA")


def composite(rgba: Image.Image, location: Path, out_prefix: Path):
    """Place the figures on the location reference and write composite + mask."""
    background = Image.open(location).convert("RGB").resize((W, H), Image.Resampling.LANCZOS)

    alpha = np.array(rgba)[..., 3]
    ys, _ = np.where(alpha > 0)
    fig_h = ys.max() - ys.min()
    scale = (H * TARGET_FIGURE_FRACTION) / fig_h

    new_w = max(1, int(rgba.width * scale))
    new_h = max(1, int(rgba.height * scale))
    scaled = rgba.resize((new_w, new_h), Image.Resampling.LANCZOS)
    a_scaled = np.array(scaled)[..., 3]

    m_ys, m_xs = np.where(a_scaled > 0)
    off_x = (W // 2) - (m_xs.min() + m_xs.max()) // 2
    off_y = int(H * FEET_Y_FRACTION) - m_ys.max()

    canvas = background.convert("RGBA")
    canvas.alpha_composite(scaled, (off_x, off_y))
    canvas.convert("RGB").save(out_prefix.with_suffix(".png"))

    # Harmonization mask = the placed figure alpha, so the room is never re-noised.
    mask = np.zeros((H, W), np.uint8)
    x0, y0 = max(0, off_x), max(0, off_y)
    x1, y1 = min(W, off_x + new_w), min(H, off_y + new_h)
    mask[y0:y1, x0:x1] = a_scaled[y0 - off_y : y1 - off_y, x0 - off_x : x1 - off_x]
    mask = cv2.dilate(
        (mask > 0).astype(np.uint8) * 255,
        cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (MASK_DILATE_PX, MASK_DILATE_PX)),
    )
    mask = cv2.GaussianBlur(mask, (0, 0), MASK_BLUR_SIGMA)
    Image.fromarray(mask).save(str(out_prefix) + "-mask.png")
    print(f"  {out_prefix.name}: composite + mask  (offset {off_x},{off_y})")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--run-dir", required=True, type=Path, help="run directory holding refs/ and the studio stages")
    parser.add_argument("--studio-a", required=True, type=Path, help="pose A studio render (figures on white)")
    parser.add_argument("--studio-b", required=True, type=Path, help="pose B studio render (figures on white)")
    parser.add_argument("--locations", default="bedroom,outdoors", help="comma-separated location names (refs/locref-<name>.png)")
    parser.add_argument("--location-dir", type=Path, default=None, help="where locref-<name>.png live (default <run-dir>/refs)")
    parser.add_argument("--out-dir", type=Path, default=None, help="output dir (default <run-dir>/05-composites)")
    parser.add_argument("--model", default="isnet-general-use", help="rembg model")
    args = parser.parse_args()

    location_dir = args.location_dir or (args.run_dir / "refs")
    out_dir = args.out_dir or (args.run_dir / "05-composites")
    out_dir.mkdir(parents=True, exist_ok=True)

    for p in (args.studio_a, args.studio_b):
        if not p.is_file():
            raise SystemExit(f"studio render not found: {p}")

    session = new_session(args.model)
    print("cutting out figures")
    pose_a = cutout(args.studio_a, session)
    pose_b = cutout(args.studio_b, session)

    print("compositing")
    for name in [s.strip() for s in args.locations.split(",") if s.strip()]:
        loc_ref = location_dir / f"locref-{name}.png"
        if not loc_ref.is_file():
            raise SystemExit(f"location reference not found: {loc_ref}")
        composite(pose_a, loc_ref, out_dir / f"comp-{name}-a")
        composite(pose_b, loc_ref, out_dir / f"comp-{name}-b")

    print(f"done -> {out_dir}")


if __name__ == "__main__":
    main()
