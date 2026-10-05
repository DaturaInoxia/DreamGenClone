"""Ground-truth colour-coded room renderer for the 2.1 POV-consistency proof.

Builds a room whose geometry is known EXACTLY, so every camera view has a
computable correct answer. Walls are colour-coded so a result can be scored by
which wall colour appears where -- no eyeballing needed.

Room, looking down (+y is up, north is -z):

        NORTH  red    + shelf
   WEST  ................................  EAST
  yellow |                                |  blue
  window |            [crate]             |  table
         |                                |
   SOUTH  ................................  GREEN + picture

Usage:
  python room.py OUTDIR [--size 1024] [--pano 2048]
"""
import sys, os, json, argparse
import numpy as np
from PIL import Image, ImageDraw, ImageFont

# ---- room geometry (metres) -------------------------------------------------
XMIN, XMAX = -2.0, 2.0
YMIN, YMAX = 0.0, 3.0
ZMIN, ZMAX = -2.0, 2.0          # -z is NORTH, +z is SOUTH
EYE = 1.6                       # camera height at the room centre

WALL = {
    "north": (200, 55, 45),     # red
    "east": (45, 120, 200),     # blue
    "south": (40, 165, 90),     # green
    "west": (235, 195, 30),     # yellow
    "floor": (120, 118, 112),
    "ceiling": (205, 202, 196),
}
SHELF = (95, 60, 30)
TABLE = (150, 105, 55)
FRAME = (245, 245, 245)
CANVAS = (40, 35, 50)
WINDOW = (185, 235, 255)
CRATE_SIDE = (160, 110, 55)
CRATE_TOP = (190, 145, 85)

CRATE = dict(x=-0.45, X=0.45, y=0.0, Y=0.9, z=-0.45, Z=0.45)


def _slab(origin, d, lo, hi):
    """Ray vs axis-aligned box (slab method). Returns (t_enter, t_exit)."""
    t1 = (lo - origin) / np.where(np.abs(d) < 1e-9, 1e-9, d)
    t2 = (hi - origin) / np.where(np.abs(d) < 1e-9, 1e-9, d)
    tmin = np.minimum(t1, t2)
    tmax = np.maximum(t1, t2)
    t_enter = np.maximum(np.maximum(tmin[..., 0], tmin[..., 1]), tmin[..., 2])
    t_exit = np.minimum(np.minimum(tmax[..., 0], tmax[..., 1]), tmax[..., 2])
    return t_enter, t_exit


def render(origin, forward, fov_deg, w, h):
    """Perspective render of the room. Returns (rgb, face_id)."""
    aspect = w / h
    t = np.tan(np.radians(fov_deg) / 2.0)
    u = (np.arange(w) + 0.5) / w * 2 - 1
    v = (np.arange(h) + 0.5) / h * 2 - 1
    uu, vv = np.meshgrid(u, v)

    f = np.asarray(forward, dtype=np.float64)
    f = f / np.linalg.norm(f)
    # right = cross(forward, worldUp). NOT cross(worldUp, forward) - that yields the LEFT vector and
    # mirror-flips every render, which silently invalidates any left/right position comparison.
    r = np.cross(f, np.array([0.0, 1.0, 0.0]))
    if np.linalg.norm(r) < 1e-6:
        r = np.array([1.0, 0.0, 0.0])
    r = r / np.linalg.norm(r)
    up = np.cross(r, f)
    up = up / np.linalg.norm(up)

    d = (f[None, None, :] + uu[..., None] * t * r[None, None, :]
         + vv[..., None] * (t / aspect) * up[None, None, :])
    d = d / np.linalg.norm(d, axis=-1, keepdims=True)
    o = np.asarray(origin, dtype=np.float64)

    planes = []
    for axis, (lo, hi, n_lo, n_hi) in enumerate([
            (XMIN, XMAX, "west", "east"),
            (YMIN, YMAX, "floor", "ceiling"),
            (ZMIN, ZMAX, "north", "south")]):
        for bound, nm in ((lo, n_lo), (hi, n_hi)):
            tt = (bound - o[axis]) / np.where(np.abs(d[..., axis]) < 1e-9, 1e-9, d[..., axis])
            planes.append((tt, nm))

    best_t = np.full((h, w), np.inf)
    best_face = np.full((h, w), "", dtype=object)
    for tt, nm in planes:
        valid = (tt > 1e-4) & (tt < best_t)
        best_t = np.where(valid, tt, best_t)
        best_face = np.where(valid, nm, best_face)

    rgb = np.zeros((h, w, 3), dtype=np.float64)
    hit = o[None, None, :] + best_t[..., None] * d
    hx, hy, hz = hit[..., 0], hit[..., 1], hit[..., 2]

    for face in ("north", "east", "south", "west", "floor", "ceiling"):
        rgb[best_face == face] = WALL[face]

    def uv(face):
        if face in ("north", "south"):
            return (hx - XMIN) / (XMAX - XMIN), (hy - YMIN) / (YMAX - YMIN)
        if face in ("east", "west"):
            return (hz - ZMIN) / (ZMAX - ZMIN), (hy - YMIN) / (YMAX - YMIN)
        return (hx - XMIN) / (XMAX - XMIN), (hz - ZMIN) / (ZMAX - ZMIN)

    # NORTH: shelf (band + two brackets)
    m = best_face == "north"
    u_n, v_n = uv("north")
    rgb[m & (u_n > 0.22) & (u_n < 0.78) & (v_n > 0.52) & (v_n < 0.60)] = SHELF
    for bx in (0.30, 0.70):
        rgb[m & (np.abs(u_n - bx) < 0.03) & (v_n > 0.42) & (v_n < 0.52)] = SHELF

    # EAST: table (top + legs)
    m = best_face == "east"
    u_e, v_e = uv("east")
    rgb[m & (u_e > 0.25) & (u_e < 0.75) & (v_e > 0.48) & (v_e < 0.54)] = TABLE
    for bx in (0.32, 0.68):
        rgb[m & (np.abs(u_e - bx) < 0.035) & (v_e > 0.24) & (v_e < 0.48)] = TABLE

    # SOUTH: framed picture
    m = best_face == "south"
    u_s, v_s = uv("south")
    rgb[m & (u_s > 0.36) & (u_s < 0.64) & (v_s > 0.42) & (v_s < 0.76)] = FRAME
    rgb[m & (u_s > 0.39) & (u_s < 0.61) & (v_s > 0.45) & (v_s < 0.73)] = CANVAS

    # WEST: window with a cross frame
    m = best_face == "west"
    u_w, v_w = uv("west")
    win = m & (u_w > 0.24) & (u_w < 0.68) & (v_w > 0.40) & (v_w < 0.82)
    rgb[win] = WINDOW
    rgb[win & (np.abs(v_w - 0.61) < 0.012)] = FRAME
    rgb[win & (np.abs(u_w - 0.46) < 0.010)] = FRAME

    # centre crate: the nearer hit wins
    ct, cx = _slab(o, d, [CRATE["x"], CRATE["y"], CRATE["z"]],
                   [CRATE["X"], CRATE["Y"], CRATE["Z"]])
    solid = (ct > 1e-4) & (ct < best_t) & (cx > ct)
    if solid.any():
        entry = ct[solid]
        on_y = []
        for axis, (lo, hi) in enumerate([(CRATE["x"], CRATE["X"]), (CRATE["y"], CRATE["Y"]),
                                        (CRATE["z"], CRATE["Z"])]):
            t1 = (lo - o[axis]) / np.where(np.abs(d[..., axis][solid]) < 1e-9, 1e-9,
                                           d[..., axis][solid])
            on_y.append(np.isclose(t1, entry, atol=1e-6))
        rgb[solid] = CRATE_SIDE
        rgb[np.flatnonzero(solid)[on_y[1]]] = CRATE_TOP
        best_face = np.where(solid, "crate", best_face)

    return np.clip(rgb, 0, 255).astype(np.uint8), best_face


def render_equirect(w, h, origin=None):
    """Exact equirect of the room seen from `origin` (default: room centre)."""
    lon = (np.arange(w) + 0.5) / w * 2 * np.pi - np.pi
    lat = (np.arange(h) + 0.5) / h * np.pi - np.pi / 2
    lo, la = np.meshgrid(lon, lat)
    d = np.stack([np.cos(la) * np.sin(lo), np.sin(la), -np.cos(la) * np.cos(lo)], axis=-1)
    o = np.array(origin if origin is not None else (0.0, EYE, 0.0), dtype=np.float64)

    planes = []
    for axis, (lo_b, hi_b, n_lo, n_hi) in enumerate([
            (XMIN, XMAX, "west", "east"), (YMIN, YMAX, "floor", "ceiling"),
            (ZMIN, ZMAX, "north", "south")]):
        for bound, nm in ((lo_b, n_lo), (hi_b, n_hi)):
            tt = (bound - o[axis]) / np.where(np.abs(d[..., axis]) < 1e-9, 1e-9, d[..., axis])
            planes.append((tt, nm))
    best_t = np.full((h, w), np.inf)
    best_face = np.full((h, w), "", dtype=object)
    for tt, nm in planes:
        v = (tt > 1e-4) & (tt < best_t)
        best_t = np.where(v, tt, best_t)
        best_face = np.where(v, nm, best_face)

    rgb = np.zeros((h, w, 3), dtype=np.float64)
    for face in ("north", "east", "south", "west", "floor", "ceiling"):
        rgb[best_face == face] = WALL[face]

    ct, cx = _slab(o, d, [CRATE["x"], CRATE["y"], CRATE["z"]],
                   [CRATE["X"], CRATE["Y"], CRATE["Z"]])
    solid = (ct > 1e-4) & (ct < best_t) & (cx > ct)
    rgb[solid] = CRATE_SIDE
    return np.clip(rgb, 0, 255).astype(np.uint8)


def tile(im, text):
    im = Image.fromarray(np.ascontiguousarray(im))
    d = ImageDraw.Draw(im)
    try:
        f = ImageFont.truetype("arial.ttf", 20)
    except Exception:
        f = ImageFont.load_default()
    bb = d.textbbox((0, 0), text, font=f)
    d.rectangle([0, 0, bb[2] - bb[0] + 14, bb[3] - bb[1] + 12], fill=(0, 0, 0))
    d.text((7, 6), text, fill=(255, 255, 255), font=f)
    return np.asarray(im)


def hstack(imgs, gap=8):
    h = max(i.shape[0] for i in imgs)
    w = sum(i.shape[1] for i in imgs) + gap * (len(imgs) - 1)
    out = np.full((h, w, 3), 25, np.uint8)
    x = 0
    for i in imgs:
        out[0:i.shape[0], x:x + i.shape[1]] = i
        x += i.shape[1] + gap
    return out


def vstack(imgs, gap=8):
    w = max(i.shape[1] for i in imgs)
    h = sum(i.shape[0] for i in imgs) + gap * (len(imgs) - 1)
    out = np.full((h, w, 3), 25, np.uint8)
    y = 0
    for i in imgs:
        out[y:y + i.shape[0], 0:i.shape[1]] = i
        y += i.shape[0] + gap
    return out


def render_depth(origin, forward, fov_deg, w, h):
    """Exact depth map of the room from a camera: near = bright.

    Emitted from the SAME geometry as the colour render, so it is an exact
    geometry control for the view being requested -- not a depth estimate of an
    image the model has already invented.
    """
    aspect = w / h
    t = np.tan(np.radians(fov_deg) / 2.0)
    u = (np.arange(w) + 0.5) / w * 2 - 1
    v = (np.arange(h) + 0.5) / h * 2 - 1
    uu, vv = np.meshgrid(u, v)

    f = np.asarray(forward, dtype=np.float64)
    f = f / np.linalg.norm(f)
    r = np.cross(f, np.array([0.0, 1.0, 0.0]))
    r = r / np.linalg.norm(r)
    up = np.cross(r, f)
    up = up / np.linalg.norm(up)
    d = (f[None, None, :] + uu[..., None] * t * r[None, None, :]
         + vv[..., None] * (t / aspect) * up[None, None, :])
    d = d / np.linalg.norm(d, axis=-1, keepdims=True)
    o = np.asarray(origin, dtype=np.float64)

    best = np.full((h, w), np.inf)
    for axis, (lo, hi) in enumerate([(XMIN, XMAX), (YMIN, YMAX), (ZMIN, ZMAX)]):
        for bound in (lo, hi):
            tt = (bound - o[axis]) / np.where(np.abs(d[..., axis]) < 1e-9, 1e-9, d[..., axis])
            best = np.where((tt > 1e-4) & (tt < best), tt, best)

    ct, cx = _slab(o, d, [CRATE["x"], CRATE["y"], CRATE["z"]],
                   [CRATE["X"], CRATE["Y"], CRATE["Z"]])
    solid = (ct > 1e-4) & (ct < best) & (cx > ct)
    best = np.where(solid, ct, best)

    # near=bright, normalised to the room's diagonal so depth is comparable across views
    diag = float(np.linalg.norm([XMAX - XMIN, YMAX - YMIN, ZMAX - ZMIN]))
    g = np.clip(1.0 - (best / diag), 0.0, 1.0)
    return np.repeat((g * 255).astype(np.uint8)[..., None], 3, axis=2)


VIEWS = {
    # Cameras stand near each end of the room's centre line, looking at the
    # OPPOSITE wall -- which is exactly the Dean/Becky "facing each other" case.
    # Each view therefore contains TWO independent landmarks: the far wall
    # (colour + its object) and the centre crate in the near foreground.
    "S_to_N":  ("south", 0),     # red + shelf, crate in front
    "S_to_E":  ("south", 90),    # blue + table
    "S_to_W":  ("south", -90),   # yellow + window
    "N_to_S":  ("north", 180),   # green + picture, crate in front
    "N_to_E":  ("north", 90),    # blue + table
    "N_to_W":  ("north", -90),   # yellow + window
    "C_NE":    ("center", 45),   # corner: north + east walls
    "C_SW":    ("center", 225),  # corner: south + west walls
}
SPOT = {
    "south": (0.0, EYE, 1.6),
    "north": (0.0, EYE, -1.6),
    "center": (0.0, EYE, 0.0),
}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("outdir")
    p.add_argument("--size", type=int, default=1024)
    p.add_argument("--pano", type=int, default=2048)
    a = p.parse_args()
    os.makedirs(a.outdir, exist_ok=True)

    S = a.size
    stats, tiles = {}, {}

    palette = {**WALL, "shelf": SHELF, "table": TABLE, "frame": FRAME,
               "canvas": CANVAS, "window": WINDOW, "crate": CRATE_SIDE}

    for name, (spot, yaw) in VIEWS.items():
        y, p = np.radians(yaw), 0.0
        fwd = (np.sin(y) * np.cos(p), np.sin(p), -np.cos(y) * np.cos(p))
        rgb, face = render(SPOT[spot], fwd, 75, S, S)
        Image.fromarray(rgb).save(os.path.join(a.outdir, f"{name}.png"))
        Image.fromarray(render_depth(SPOT[spot], fwd, 75, S, S)).save(
            os.path.join(a.outdir, f"{name}-depth.png"))
        flat = rgb.reshape(-1, 3)
        stats[name] = dict(spot=spot, yaw=yaw, share={
            k: round(float((np.abs(flat - np.array(v)).sum(axis=1) < 30).mean()), 4)
            for k, v in palette.items()})
        tiles[name] = tile(rgb, f"{name} spot={spot} yaw={yaw} (ground truth)")

    Image.fromarray(hstack([tiles["S_to_N"], tiles["N_to_S"]])).save(
        os.path.join(a.outdir, "gt-facing-pair.png"))
    Image.fromarray(hstack([tiles["S_to_E"], tiles["S_to_W"]])).save(
        os.path.join(a.outdir, "gt-from-south.png"))
    Image.fromarray(hstack([tiles["N_to_E"], tiles["N_to_W"]])).save(
        os.path.join(a.outdir, "gt-from-north.png"))
    Image.fromarray(hstack([tiles["C_NE"], tiles["C_SW"]])).save(
        os.path.join(a.outdir, "gt-corners.png"))
    Image.fromarray(tile(render_depth(SPOT["north"], (0, 0, 1), 75, S, S),
                         "depth control for N_to_S (near = bright) - exact, from the room geometry")).save(
        os.path.join(a.outdir, "gt-depth-N_to_S.png"))
    for spot in ("south", "north", "center"):
        Image.fromarray(tile(render_equirect(a.pano, a.pano // 2, SPOT[spot]),
                             f"ground-truth equirect from {spot} spot (flat = warped)")).save(
            os.path.join(a.outdir, f"gt-equirect-{spot}.png"))

    with open(os.path.join(a.outdir, "ground-truth.json"), "w") as fh:
        json.dump(stats, fh, indent=2)

    print("room x[%.0f,%.0f] y[%.0f,%.0f] z[%.0f,%.0f] eye=%.1f fov=75" %
          (XMIN, XMAX, YMIN, YMAX, ZMIN, ZMAX, EYE))
    print("north=RED+shelf  east=BLUE+table  south=GREEN+picture  west=YELLOW+window  centre=crate")
    print("spots: south z=+1.6  north z=-1.6  center\n")
    hdr = ["north", "east", "south", "west", "shelf", "table", "frame", "canvas", "window", "crate"]
    print("%-8s %s" % ("view", " ".join("%6s" % c for c in hdr)))
    for name, s in stats.items():
        print("%-8s %s" % (name, " ".join("%6.2f" % s["share"][c] for c in hdr)))
    print("\nwrote to", a.outdir)


if __name__ == "__main__":
    main()
