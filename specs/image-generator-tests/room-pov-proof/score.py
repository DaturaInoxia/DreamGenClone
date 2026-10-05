"""Score a candidate room render against ground truth.

Classifies BOTH images with the SAME hue-based classifier, so the comparison is
apples-to-apples and tolerant of realistic shading (an exact RGB match would
fail on any photographic render).

Reports:
  * per-hue-class pixel share, ground truth vs candidate
  * the wall-colour error (which walls are present, and how wrong the shares are)
  * structure correlation against the exact ground-truth render

Usage:
  python score.py GROUNDTRUTH_DIR VIEW_NAME CANDIDATE.png
"""
import sys, os, json, colorsys
import numpy as np
from PIL import Image, ImageFilter

DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, DIR)
import importlib.util
_spec = importlib.util.spec_from_file_location("room", os.path.join(DIR, "room.py"))
room = importlib.util.module_from_spec(_spec)
sys.modules["room"] = room
_spec.loader.exec_module(room)

CLASSES = ["red", "brown", "yellow", "green", "cyan", "blue", "purple",
           "white", "grey", "dark"]


def classify(im):
    """Hue-classify every pixel. Returns {class: share}."""
    rgb = np.asarray(Image.fromarray(im).convert("RGB").resize((256, 256), Image.LANCZOS),
                     dtype=np.float32) / 255.0
    mx = rgb.max(axis=2)
    mn = rgb.min(axis=2)
    v = mx
    s = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    d = np.maximum(mx - mn, 1e-6)
    h = np.zeros_like(mx)
    m = (mx == r) & (d > 1e-6)
    h[m] = ((g - b)[m] / d[m]) % 6
    m = (mx == g) & (d > 1e-6)
    h[m] = (b - r)[m] / d[m] + 2
    m = (mx == b) & (d > 1e-6)
    h[m] = (r - g)[m] / d[m] + 4
    h = (h * 60) % 360

    out = np.full(mx.shape, "grey", dtype=object)
    out[v < 0.16] = "dark"
    lit = v >= 0.16
    greyish = lit & (s < 0.18)
    out[greyish & (v > 0.72)] = "white"
    out[greyish & (v <= 0.72)] = "grey"
    sat = lit & (s >= 0.18)
    for lo, hi, name in [(0, 15, "red"), (345, 361, "red"), (15, 45, "brown"),
                         (45, 70, "yellow"), (70, 160, "green"), (160, 200, "cyan"),
                         (200, 260, "blue"), (260, 345, "purple")]:
        out[sat & (h >= lo) & (h < hi)] = name
    return {c: float((out == c).mean()) for c in CLASSES}


def structure(im, size=256):
    g = np.asarray(Image.fromarray(im).convert("L").resize((size, size), Image.LANCZOS),
                   dtype=np.float32) / 255.0
    g = g - g.mean()
    sd = g.std()
    return g / sd if sd > 1e-6 else g


def corr(a, b):
    return float((a * b).mean())


def main():
    gt_dir, view, candidate = sys.argv[1], sys.argv[2], sys.argv[3]
    gt = np.asarray(Image.open(os.path.join(gt_dir, f"{view}.png")).convert("RGB"))

    with open(os.path.join(gt_dir, "ground-truth.json")) as fh:
        meta = json.load(fh)[view]

    cand = np.asarray(Image.open(candidate).convert("RGB"))
    if cand.shape[0] != gt.shape[0] or cand.shape[1] != gt.shape[1]:
        cand = np.asarray(Image.fromarray(cand).resize((gt.shape[1], gt.shape[0]), Image.LANCZOS))

    c_gt, c_cd = classify(gt), classify(cand)
    r = corr(structure(gt), structure(cand))

    # Which wall did the camera expect, and which did it get?
    walls = ["red", "blue", "green", "yellow"]
    expect = max(walls, key=lambda w: c_gt[w])
    got = max(walls, key=lambda w: c_cd[w])
    print("view %s  (spot=%s yaw=%d)" % (view, meta["spot"], meta["yaw"]))
    print("  expected dominant wall : %s (share %.2f)" % (expect, c_gt[expect]))
    print("  candidate dominant wall: %s (share %.2f)" % (got, c_cd[got]))
    print("  WALL MATCH             : %s" % ("YES" if got == expect else "NO"))
    print("  structure correlation  : %+.3f" % r)
    print()
    print("  %-8s %8s %8s %8s" % ("class", "truth", "cand", "delta"))
    err = 0.0
    for c in CLASSES:
        d = c_cd[c] - c_gt[c]
        err += abs(d)
        flag = "  <<<" if abs(d) > 0.12 else ""
        print("  %-8s %8.3f %8.3f %+8.3f%s" % (c, c_gt[c], c_cd[c], d, flag))
    # Honest gate. Matching only the DOMINANT wall is far too lenient: a corner view needs TWO
    # walls, and arm D was called "wall present" while its second required wall (yellow 0.36) was
    # absent and structure correlation was ~0. Require every wall the view actually needs.
    required = [w for w in sorted(walls, key=lambda w: -c_gt[w])[:2] if c_gt[w] > 0.15]
    ok_walls = all(c_cd[w] >= 0.5 * c_gt[w] for w in required)
    ok_struct = r > 0.5
    print("\n  required walls (truth > 0.15): %s" % ", ".join(
        "%s %.2f->%.2f%s" % (w, c_gt[w], c_cd[w], "" if c_cd[w] >= 0.5 * c_gt[w] else " MISSING")
        for w in required))
    print("  walls reproduced: %s   structure: %s" % ("YES" if ok_walls else "NO",
                                                      "OK" if ok_struct else "FAILED"))
    print("  VERDICT: %s" % ("PASS - room reproduced" if (ok_walls and ok_struct)
                             else "FAIL - room NOT reproduced"))


if __name__ == "__main__":
    main()
