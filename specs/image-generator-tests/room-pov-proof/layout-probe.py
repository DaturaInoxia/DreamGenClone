"""Diagnose WHERE the walls land, to explain a negative structure correlation.

A negative correlation with correct wall colours usually means the room came out
mirrored or rearranged. This measures each class's centre of mass and its
left/right split for both ground truth and candidate.

Usage: python layout-probe.py GROUNDTRUTH_DIR VIEW_NAME CANDIDATE.png
"""
import sys, os
import numpy as np
from PIL import Image

DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, DIR)
import importlib.util
_s = importlib.util.spec_from_file_location("sc", os.path.join(DIR, "score.py"))
sc = importlib.util.module_from_spec(_s)
sys.modules["sc"] = sc
_s.loader.exec_module(sc)


def probe(im, name):
    im = np.asarray(Image.fromarray(im).convert("RGB").resize((384, 384), Image.LANCZOS))
    shares = sc.classify(im)
    # rebuild the class map to get positions
    h, w = im.shape[:2]
    cls = np.full((h, w), -1, dtype=int)
    labels = sc.CLASSES

    # recompute the same classification but keep the map
    f = im.astype(np.float32) / 255.0
    mx, mn = f.max(axis=2), f.min(axis=2)
    v = mx
    s = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    d = np.maximum(mx - mn, 1e-6)
    hh = np.zeros_like(mx)
    m = (mx == r) & (d > 1e-6)
    hh[m] = ((g - b)[m] / d[m]) % 6
    m = (mx == g) & (d > 1e-6)
    hh[m] = (b - r)[m] / d[m] + 2
    m = (mx == b) & (d > 1e-6)
    hh[m] = (r - g)[m] / d[m] + 4
    hh = (hh * 60) % 360

    name_map = np.full((h, w), "grey", dtype=object)
    name_map[v < 0.16] = "dark"
    lit = v >= 0.16
    gr = lit & (s < 0.18)
    name_map[gr & (v > 0.72)] = "white"
    name_map[gr & (v <= 0.72)] = "grey"
    sat = lit & (s >= 0.18)
    for lo, hi, nm in [(0, 15, "red"), (345, 361, "red"), (15, 45, "brown"), (45, 70, "yellow"),
                       (70, 160, "green"), (160, 200, "cyan"), (200, 260, "blue"), (260, 345, "purple")]:
        name_map[sat & (hh >= lo) & (hh < hi)] = nm
    return name_map, shares


def centroid(map_, cls):
    m = map_ == cls
    n = m.sum()
    if n == 0:
        return None
    ys, xs = np.nonzero(m)
    return (xs.mean() / map_.shape[1], ys.mean() / map_.shape[0], n / map_.size)


def main():
    gt_dir, view, cand = sys.argv[1], sys.argv[2], sys.argv[3]
    gt = np.asarray(Image.open(os.path.join(gt_dir, f"{view}.png")).convert("RGB"))
    cd = np.asarray(Image.open(cand).convert("RGB"))

    gm, _ = probe(gt, "truth")
    cm, _ = probe(cd, "cand")

    print("%-8s %-28s %-28s" % ("class", "GROUND TRUTH  (cx, cy, share)", "CANDIDATE  (cx, cy, share)"))
    for cls in ["red", "green", "yellow", "blue", "cyan", "brown", "white"]:
        a, b = centroid(gm, cls), centroid(cm, cls)
        fa = "  --" if a is None else "%.2f %.2f %.2f" % a
        fb = "  --" if b is None else "%.2f %.2f %.2f" % b
        if a is None and b is None:
            continue
        # mirroring test: a left/right flip moves cx to 1-cx
        note = ""
        if a is not None and b is not None:
            if abs(a[0] - b[0]) > 0.15:
                mirrored = abs((1.0 - a[0]) - b[0]) < abs(a[0] - b[0])
                note = "  <== MIRRORED" if mirrored else "  <== moved"
        print("%-8s %-28s %-28s%s" % (cls, fa, fb, note))

    print("\ncx=0 is the LEFT edge, cy=0 is the TOP. 'MIRRORED' means the candidate's feature")
    print("sits where a left-right flip of the ground truth would put it.")


if __name__ == "__main__":
    main()
