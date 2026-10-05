"""Score a generated view of a REAL room against the real photographs of it.

No synthetic ground truth exists for a real room, so this scores against the
actual target photograph and reports the source photograph too -- the pair
together say whether the result MOVED to the requested view or merely copied
the view it was given.

Usage: python score-photo.py SOURCE.png TARGET.png CANDIDATE.png
"""
import sys
import numpy as np
from PIL import Image, ImageFilter


def load(p, size=None):
    im = Image.open(p).convert("RGB")
    if size:
        im = im.resize(size, Image.LANCZOS)
    return np.asarray(im)


def structure(im, size=256):
    g = np.asarray(Image.fromarray(im).convert("L").resize((size, size), Image.LANCZOS),
                   dtype=np.float32) / 255.0
    g = g - g.mean()
    sd = g.std()
    return g / sd if sd > 1e-6 else g


def corr(a, b):
    return float((a * b).mean())


def blurred(im, r):
    return np.asarray(Image.fromarray(im).filter(ImageFilter.GaussianBlur(r)))


def main():
    src_p, tgt_p, cand_p = sys.argv[1], sys.argv[2], sys.argv[3]
    tgt = load(tgt_p)
    src = load(src_p, (tgt.shape[1], tgt.shape[0]))
    cand = load(cand_p, (tgt.shape[1], tgt.shape[0]))

    st, ss, sc = structure(tgt), structure(src), structure(cand)

    r_target = corr(sc, st)    # agreement with the REQUESTED view
    r_source = corr(sc, ss)    # agreement with the GIVEN view
    r_base = corr(ss, st)      # how alike the two real views are
    r_faithful = corr(sc, structure(blurred(cand, 3)))  # a blurred copy of itself

    # NOTE: no view names are hardcoded here. An earlier version printed "[back]"/"[front]"
    # regardless of which file was which, which silently mislabels the result whenever the
    # requested view is not the back one.
    print("r(candidate vs REQUESTED view) : %+.3f" % r_target)
    print("r(candidate vs GIVEN view)     : %+.3f" % r_source)
    print()
    print("  scale, from these same two photographs:")
    print("    given vs requested directly (how alike the two real views are) : %+.3f" % r_base)
    print("    a blurred copy of the candidate (what faithful looks like)    : %+.3f" % r_faithful)
    print("    random noise : %+.3f" % corr(sc, structure((np.random.default_rng(0).random(tgt.shape) * 255).astype(np.uint8))))
    print()
    diff = float(np.abs(cand.astype(np.float32) - tgt.astype(np.float32)).mean())
    sdiff = float(np.abs(src.astype(np.float32) - tgt.astype(np.float32)).mean())
    print("mean pixel diff vs requested view : %.1f   (the two real views differ by %.1f)" % (diff, sdiff))
    print()
    moved = r_target > r_base and r_target >= r_source
    copied = r_source > r_target and r_source > r_base
    print("VERDICT: %s" % (
        "MOVED toward the requested view - agrees with the target more than the source does"
        if moved else
        "COPIED the given view - agrees with the source more than the target"
        if copied else
        "NEITHER - agrees with neither real view above the baseline"))
    print("  (must clear the given-vs-requested baseline of %+.3f to count as reproducing the room;"
          % r_base)
    print("   a faithful reproduction would approach %+.3f)" % r_faithful)


if __name__ == "__main__":
    main()
