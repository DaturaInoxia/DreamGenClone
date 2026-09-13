"""Crop a rectangular region from an image (fractions of width/height) and save it.

Used to build the "close" framing variants for the anatomy edit matrix: cropping gives the edit
region a much larger share of the image's pixels, which is one of the hypotheses under test.
Cropping the SAME render means only the framing changes, not the subject.

Column bounds are OPTIONAL. A standing full-body figure only needs a row band at full width, but a
top-down LYING figure has its pelvis near the centre of the frame, so a full-width row band just
slices a stripe across the body — the close framings must narrow the columns as well.

Usage:
  crop-band.py <src> <dst> <top_frac> <bottom_frac> [left_frac] [right_frac]

Examples:
  crop-band.py base-male-far.png base-male-close.png 0.42 0.88
  crop-band.py base-female-lying-far.png base-female-lying-close.png 0.45 0.80 0.30 0.70
"""
import sys

from PIL import Image

if len(sys.argv) not in (5, 7):
    raise SystemExit(
        "usage: crop-band.py <src> <dst> <top_frac> <bottom_frac> [left_frac right_frac]"
    )

src, dst = sys.argv[1], sys.argv[2]
top_frac, bottom_frac = float(sys.argv[3]), float(sys.argv[4])
left_frac = float(sys.argv[5]) if len(sys.argv) == 7 else 0.0
right_frac = float(sys.argv[6]) if len(sys.argv) == 7 else 1.0

image = Image.open(src)
width, height = image.size
top = max(0, min(height - 1, int(height * top_frac)))
bottom = max(top + 1, min(height, int(height * bottom_frac)))
left = max(0, min(width - 1, int(width * left_frac)))
right = max(left + 1, min(width, int(width * right_frac)))
cropped = image.crop((left, top, right, bottom))
cropped.save(dst)
print(
    f"{src} {width}x{height} -> {dst} {cropped.size[0]}x{cropped.size[1]} "
    f"(rows {top}-{bottom}, cols {left}-{right})"
)
