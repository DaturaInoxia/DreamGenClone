"""Self-test for pin-background.py on a synthetic fixture -- no real imagery involved.

Reproduces the two conditions that make the mask non-trivial:
  1. the backdrop has a VERTICAL GRADIENT, so a single-colour test would fail, and
  2. an interior subject patch is the SAME COLOUR as the (drifted) backdrop -- the dark-shirt-on-dark-
     backdrop case. A pure colour test would erase it; border connectivity must keep it.

Writes its fixture and outputs to the git-ignored artifacts/tmp/pinbg/, and exits non-zero on failure.

Run:
  d:/src/DreamGenClone/.venv/Scripts/python.exe specs/image-generator-tests/sex-slideshow/validate-pin-background.py
"""
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image

HARNESS = Path(__file__).resolve().parent
REPO_ROOT = HARNESS.parents[2]
TOOL = HARNESS / "pin-background.py"
OUT = REPO_ROOT / "artifacts" / "tmp" / "pinbg"

WIDTH, HEIGHT = 400, 300
TOLERANCE = 18.0
FEATHER = 3.0


def backdrop(y):
    value = 40.0 + 35.0 * y / (HEIGHT - 1)
    return np.array([value, value, value + 5.0], dtype=np.float32)


def build(drift):
    image = np.zeros((HEIGHT, WIDTH, 3), dtype=np.float32)
    for y in range(HEIGHT):
        image[y, :, :] = backdrop(y)
    if drift is not None:
        image += drift[None, None, :]
    return image


DRIFT = np.array([60.0, 20.0, 40.0], dtype=np.float32)

reference = build(None)

# The input is the same scene with the backdrop drifted toward magenta (the measured failure mode).
# The garment drifts WITH the scene, so the shirt is expressed relative to the DRIFTED backdrop -- that
# is what makes this a real test of border connectivity rather than of the colour threshold.
source = build(DRIFT)

SHIRT = (140, 110, 260, 200)   # x0, y0, x1, y1
OUTLINE = 3
shirt_color = backdrop(155) + DRIFT                          # matches the drifted row backdrop
outline_color = shirt_color - np.array([35.0, 35.0, 35.0])   # 35 > tolerance -> seals the region
for y in range(SHIRT[1], SHIRT[3]):
    for x in range(SHIRT[0], SHIRT[2]):
        on_outline = (x < SHIRT[0] + OUTLINE or x >= SHIRT[2] - OUTLINE
                      or y < SHIRT[1] + OUTLINE or y >= SHIRT[3] - OUTLINE)
        source[y, x, :] = outline_color if on_outline else shirt_color

SHIRT_INTERIOR = (SHIRT[0] + OUTLINE, SHIRT[1] + OUTLINE, SHIRT[2] - OUTLINE, SHIRT[3] - OUTLINE)

SKIN = (40, 200, 110, 260)
source[SKIN[1]:SKIN[3], SKIN[0]:SKIN[2], :] = (205.0, 130.0, 95.0)

OUT.mkdir(parents=True, exist_ok=True)
reference_path = OUT / "reference.png"
source_path = OUT / "source.png"
fixed_path = OUT / "fixed.png"
Image.fromarray(np.clip(np.rint(reference), 0, 255).astype(np.uint8)).save(reference_path)
Image.fromarray(np.clip(np.rint(source), 0, 255).astype(np.uint8)).save(source_path)

result = subprocess.run(
    [sys.executable, str(TOOL), str(reference_path), str(source_path), str(fixed_path),
     "--tolerance", str(TOLERANCE), "--feather", str(FEATHER)],
    capture_output=True,
    text=True,
)
print(result.stdout.strip())
if result.returncode != 0:
    print(result.stderr.strip())
    raise SystemExit(f"tool failed with {result.returncode}")

with Image.open(fixed_path) as image:
    fixed = np.asarray(image.convert("RGB"), dtype=np.float32)
with Image.open(reference_path) as image:
    expected = np.asarray(image.convert("RGB"), dtype=np.float32)

failures = []


def check(name, condition, detail):
    print(f"  {'PASS' if condition else 'FAIL'}  {name}: {detail}")
    if not condition:
        failures.append(name)


print("--- pin-background self-test ---")

# 1. The drifted backdrop is replaced by the reference backdrop wherever the mask fired.
before = source[:30, :30].reshape(-1, 3).mean(axis=0)
after = fixed[:30, :30].reshape(-1, 3).mean(axis=0)
want = expected[:30, :30].reshape(-1, 3).mean(axis=0)
check("backdrop drift removed", np.abs(after - want).max() < 3.0,
      f"({before[0]:.0f},{before[1]:.0f},{before[2]:.0f}) -> ({after[0]:.0f},{after[1]:.0f},{after[2]:.0f}), "
      f"reference ({want[0]:.0f},{want[1]:.0f},{want[2]:.0f})")

# 2. The backdrop-COLOURED interior survives -- the whole reason for border connectivity.
x0, y0, x1, y1 = SHIRT_INTERIOR
shirt_before = source[y0:y1, x0:x1].reshape(-1, 3).mean(axis=0)
shirt_after = fixed[y0:y1, x0:x1].reshape(-1, 3).mean(axis=0)
shirt_ref = expected[y0:y1, x0:x1].reshape(-1, 3).mean(axis=0)
check("backdrop-coloured interior kept", np.abs(shirt_after - shirt_before).max() < 3.0,
      f"before ({shirt_before[0]:.0f},{shirt_before[1]:.0f},{shirt_before[2]:.0f}) -> "
      f"after ({shirt_after[0]:.0f},{shirt_after[1]:.0f},{shirt_after[2]:.0f}) "
      f"[would have become ({shirt_ref[0]:.0f},{shirt_ref[1]:.0f},{shirt_ref[2]:.0f}) if erased]")

# 3. The foreground patch is untouched (sampled INSIDE the feathered seam).
INSET = int(FEATHER) + 6
sx0, sy0, sx1, sy1 = SKIN[0] + INSET, SKIN[1] + INSET, SKIN[2] - INSET, SKIN[3] - INSET
skin_before = source[sy0:sy1, sx0:sx1].reshape(-1, 3).mean(axis=0)
skin_after = fixed[sy0:sy1, sx0:sx1].reshape(-1, 3).mean(axis=0)
check("foreground preserved", np.abs(skin_after - skin_before).max() < 1.0,
      f"({skin_before[0]:.0f},{skin_before[1]:.0f},{skin_before[2]:.0f}) -> "
      f"({skin_after[0]:.0f},{skin_after[1]:.0f},{skin_after[2]:.0f}) (inset {INSET}px past the seam)")

# 4. The backdrop's vertical gradient survives (it is not flattened to one colour).
top = fixed[5, 200].copy()
bottom = fixed[HEIGHT - 5, 5].copy()
check("backdrop gradient preserved", abs(float(bottom[0] - top[0])) > 20.0,
      f"top {top[0]:.0f} -> bottom {bottom[0]:.0f}")

print(f"--- {'ALL PASS' if not failures else 'FAILURES: ' + ', '.join(failures)} ---")
raise SystemExit(1 if failures else 0)
