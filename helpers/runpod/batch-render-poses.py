"""Batch-render every single-person OpenPose JSON in the pose pack to a flat,
stable-named folder of 1024x1024 skeleton PNGs for the pod's ComfyUI input dir.

This is prep for a pose-picker UI: each output gets a deterministic name and is
listed in manifest.json (name -> source JSON, category, resolution, number).

Naming: `{category}__{NNN}.png`; if two source files collide on that name
(same category + number at different resolutions), fall back to
`{category}__{resolution}__{NNN}.png`.

Usage:
  python helpers/runpod/batch-render-poses.py [pack_dir] [out_dir]

Defaults:
  pack_dir = helpers/runpod/openposeNSFWPosePackage_final
  out_dir  = artifacts/tmp/openpose-render
"""
import json
import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw

CANVAS = 1024
MARGIN = 48

BODY_PAIRS = [
    (1, 0), (0, 14), (14, 16), (0, 15), (15, 17),   # face
    (1, 2), (2, 3), (3, 4),                          # right arm
    (1, 5), (5, 6), (6, 7),                          # left arm
    (1, 8), (8, 9), (9, 10),                         # right leg
    (1, 11), (11, 12), (12, 13),                     # left leg
]
BODY_COLORS = {
    (1, 0): (0, 0, 255), (0, 14): (0, 0, 255), (14, 16): (0, 0, 255),
    (0, 15): (0, 0, 255), (15, 17): (0, 0, 255),
    (1, 2): (0, 255, 255), (2, 3): (0, 255, 255), (3, 4): (0, 255, 255),
    (1, 5): (255, 255, 0), (5, 6): (255, 255, 0), (6, 7): (255, 255, 0),
    (1, 8): (255, 0, 0), (8, 9): (255, 0, 0), (9, 10): (255, 0, 0),
    (1, 11): (255, 0, 255), (11, 12): (255, 0, 255), (12, 13): (255, 0, 255),
}
HAND_PAIRS = [
    (0, 1), (1, 2), (2, 3), (3, 4),
    (0, 5), (5, 6), (6, 7), (7, 8),
    (0, 9), (9, 10), (10, 11), (11, 12),
    (0, 13), (13, 14), (14, 15), (15, 16),
    (0, 17), (17, 18), (18, 19), (19, 20),
]


def load_person(path):
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    people = data.get("people", [])
    if not people:
        raise ValueError(f"No people in {path}")
    return people[0]


def fit(person):
    """Scale+translate all keypoints so the person fits the canvas."""
    body = person.get("pose_keypoints_2d", [])
    pts = [(body[3 * i], body[3 * i + 1]) for i in range(len(body) // 3)
           if body[3 * i + 2] > 0.1]
    if not pts:
        raise ValueError("No valid body keypoints")
    min_x = min(p[0] for p in pts)
    max_x = max(p[0] for p in pts)
    min_y = min(p[1] for p in pts)
    max_y = max(p[1] for p in pts)
    w = max(max_x - min_x, 1.0)
    h = max(max_y - min_y, 1.0)
    scale = min((CANVAS - 2 * MARGIN) / w, (CANVAS - 2 * MARGIN) / h)
    dx = (CANVAS - w * scale) / 2 - min_x * scale
    dy = (CANVAS - h * scale) / 2 - min_y * scale

    out = {}
    for key in ("pose_keypoints_2d", "hand_left_keypoints_2d", "hand_right_keypoints_2d"):
        arr = person.get(key, [])
        new = []
        for i in range(len(arr) // 3):
            x, y, c = arr[3 * i], arr[3 * i + 1], arr[3 * i + 2]
            new += [x * scale + dx, y * scale + dy, c]
        out[key] = new
    return out


def render(person, out_png):
    img = Image.new("RGB", (CANVAS, CANVAS), (0, 0, 0))
    d = ImageDraw.Draw(img)

    body = person.get("pose_keypoints_2d", [])
    for a, b in BODY_PAIRS:
        pa, pb = a * 3, b * 3
        if pa + 2 < len(body) and pb + 2 < len(body):
            xa, ya, ca = body[pa], body[pa + 1], body[pa + 2]
            xb, yb, cb = body[pb], body[pb + 1], body[pb + 2]
            if ca > 0.1 and cb > 0.1:
                d.line([(xa, ya), (xb, yb)], fill=BODY_COLORS.get((a, b), (200, 200, 200)), width=4)
    for i in range(len(body) // 3):
        x, y, c = body[3 * i], body[3 * i + 1], body[3 * i + 2]
        if c > 0.1:
            r = 5
            d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 255, 255))

    for hand_key in ("hand_left_keypoints_2d", "hand_right_keypoints_2d"):
        hand = person.get(hand_key, [])
        if len(hand) < 63:
            continue
        for a, b in HAND_PAIRS:
            xa, ya, ca = hand[3 * a], hand[3 * a + 1], hand[3 * a + 2]
            xb, yb, cb = hand[3 * b], hand[3 * b + 1], hand[3 * b + 2]
            if ca > 0.1 and cb > 0.1:
                d.line([(xa, ya), (xb, yb)], fill=(120, 120, 120), width=2)
        for i in range(21):
            x, y, c = hand[3 * i], hand[3 * i + 1], hand[3 * i + 2]
            if c > 0.1:
                d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=(200, 200, 200))

    img.save(out_png)


def parse_source(json_path, pack_dir):
    """Derive category, resolution, number from a pack-relative JSON path."""
    rel = json_path.relative_to(pack_dir)
    parts = rel.parts  # (category, resolution, filename.json)
    category = parts[0]
    resolution = parts[1] if len(parts) >= 3 else "flat"
    m = re.search(r"(\d+)\.json$", rel.name)
    number = int(m.group(1)) if m else 0
    return category, resolution, number


def main():
    pack_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("helpers/runpod/openposeNSFWPosePackage_final")
    out_dir = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("artifacts/tmp/openpose-render")
    out_dir.mkdir(parents=True, exist_ok=True)

    sources = sorted(pack_dir.rglob("*.json"))
    if not sources:
        print(f"No JSON files found under {pack_dir}")
        return

    manifest = []
    used_names = {}

    for src in sources:
        category, resolution, number = parse_source(src, pack_dir)
        base = f"{category}__{number:03d}.png"
        name = base
        if base in used_names and used_names[base] != str(src):
            name = f"{category}__{resolution}__{number:03d}.png"
        used_names[name] = str(src)

        out_png = out_dir / name
        try:
            person = fit(load_person(src))
            render(person, out_png)
        except ValueError as e:
            print(f"SKIP {src.relative_to(pack_dir)}: {e}")
            continue
        manifest.append({
            "name": name,
            "category": category,
            "resolution": resolution,
            "number": number,
            "source": str(src.relative_to(pack_dir)).replace("\\", "/"),
        })

    manifest.sort(key=lambda e: (e["category"], e["number"], e["resolution"]))
    manifest_path = out_dir / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")

    print(f"Rendered {len(manifest)} poses -> {out_dir}")
    print(f"Manifest: {manifest_path}")


if __name__ == "__main__":
    main()
