"""Build a subject-free "empty studio" reference from a frame, for use as an extra edit reference (image2).

Why this exists
---------------
Passing the WHOLE step-1 frame as an anchor made drift worse, not better (measured: backdrop drift 39 vs 33
at step 9, saturation 121 vs 85). The reason is a contradiction: image2 still showed "two people standing
side by side facing the camera" while the step's instruction said e.g. "she is on her hands and knees".
Two conflicting scene references made the model split the difference.

What actually drifts is the ENVIRONMENT (the flat studio backdrop shifts steadily toward magenta -- ~3.7
channel units per link in the measured baseline). So pass only the environment.

Method
------
The backdrop is flat and surrounds the subjects, so for every row the LEFT and RIGHT margins are pure
backdrop. The gap between them is filled with a horizontal blend of the two margin colours. Vertical
structure (dark upper backdrop -> lighter floor) is preserved because each row is filled independently;
every trace of the subjects is removed. No model, no inpainting -- deterministic and cheap.

Usage:
  python make-background-reference.py <src> <dst> [margin_frac]
"""
import sys

from PIL import Image

if len(sys.argv) not in (3, 4):
    raise SystemExit("usage: make-background-reference.py <src> <dst> [margin_frac]")

src, dst = sys.argv[1], sys.argv[2]
margin = float(sys.argv[3]) if len(sys.argv) == 4 else 0.08

with Image.open(src) as image:
    frame = image.convert("RGB")

width, height = frame.size
margin_x = max(1, int(width * margin))
if margin_x * 2 >= width:
    raise SystemExit("margin too wide for this image")

out = frame.copy()
src_px = frame.load()
out_px = out.load()
span = max(1, width - 2 * margin_x - 1)

for y in range(height):
    left = src_px[margin_x - 1, y]
    right = src_px[width - margin_x, y]
    for x in range(margin_x, width - margin_x):
        t = (x - margin_x) / span
        out_px[x, y] = (
            int(round(left[0] + (right[0] - left[0]) * t)),
            int(round(left[1] + (right[1] - left[1]) * t)),
            int(round(left[2] + (right[2] - left[2]) * t)),
        )

out.save(dst)
print(f"{src} -> {dst} (subject-free studio reference, margin {margin:.0%} each side)")
