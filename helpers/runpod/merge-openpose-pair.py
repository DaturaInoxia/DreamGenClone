"""Merge two single-person OpenPose JSON poses into one 2-person frame and render
a 1024x1024 OpenPose skeleton PNG for ControlNet conditioning.

Default combo (standing man + kneeling woman fellatio):
  man   = NSFW_standing/512768/NSFW_standing028.json
  woman = NSFW_Kneeling/512768/NSFW_Kneeling017.json

Composition geometry (1024 canvas, y grows down):
  - ground_y = 1000
  - Man scaled so his ankles sit on the ground line, torso centered at man_x.
  - Woman scaled so her nose lands on the man's groin (hip) height and her
    ankles sit on the same ground line; her nose is placed at the man's pelvis x.

Usage:
  python helpers/runpod/merge-openpose-pair.py [man.json] [woman.json] [out.png]
"""
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw

PACK = Path("helpers/runpod/openposeNSFWPosePackage_final")
CANVAS = 1024
GROUND = CANVAS - 24          # 1000
MAN_X = 620                   # man's neck target x (canvas)
MAN_JSON = PACK / "NSFW_standing" / "512768" / "NSFW_standing028.json"
WOMAN_JSON = PACK / "NSFW_Kneeling" / "512768" / "NSFW_Kneeling017.json"
# Curated pose library (source-controlled). Transient scratch outputs should be
# passed as an explicit output path under artifacts/tmp/ instead.
OUT_DEFAULT = Path("specs/image-generator-tests/juggernaut/poses/fellatio-standing/openpose.png")
OUT_FRAME = Path("specs/image-generator-tests/juggernaut/poses/fellatio-standing/frame.json")

# COCO-18 body skeleton pairs (indices into pose_keypoints_2d)
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
# 21-point hand skeleton (wrist 0 -> fingertips)
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


def avg_y(pose, idxs):
    ys = [pose[3 * i + 1] for i in idxs if pose[3 * i + 2] > 0.1]
    return sum(ys) / len(ys) if ys else None


def avg_xy(pose, idxs):
    pts = [(pose[3 * i], pose[3 * i + 1]) for i in idxs if pose[3 * i + 2] > 0.1]
    return (sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts)) if pts else None


def transform(person, scale, dx, dy):
    out = {}
    for key in ("pose_keypoints_2d", "hand_left_keypoints_2d", "hand_right_keypoints_2d"):
        arr = person.get(key, [])
        n = len(arr) // 3
        new = []
        for i in range(n):
            x, y, c = arr[3 * i], arr[3 * i + 1], arr[3 * i + 2]
            new += [x * scale + dx, y * scale + dy, c]
        out[key] = new
    return out


def compose(man, woman):
    man_ankle = avg_y(man["pose_keypoints_2d"], [10, 13])
    man_neck = avg_xy(man["pose_keypoints_2d"], [1])
    if man_ankle is None or man_neck is None:
        raise ValueError("Man pose missing ankles/neck")
    s_m = GROUND / man_ankle
    man_t = transform(man, s_m, MAN_X - man_neck[0] * s_m, 0.0)

    man_hip = avg_xy(man_t["pose_keypoints_2d"], [8, 11])
    if man_hip is None:
        raise ValueError("Man pose missing hips")
    man_hip_x, man_hip_y = man_hip

    w_ankle = avg_y(woman["pose_keypoints_2d"], [10, 13])
    w_nose = avg_xy(woman["pose_keypoints_2d"], [0])
    if w_ankle is None or w_nose is None:
        raise ValueError("Woman pose missing ankles/nose")
    span_w = w_ankle - w_nose[1]
    target_span = GROUND - man_hip_y
    if span_w <= 0 or target_span <= 0:
        raise ValueError("Bad woman geometry")
    s_w = target_span / span_w
    w_nose_x = man_hip_x            # her mouth at his pelvis x
    w_nose_y = man_hip_y            # her mouth at his groin y
    woman_t = transform(woman, s_w, w_nose_x - w_nose[0] * s_w, w_nose_y - w_nose[1] * s_w)
    return man_t, woman_t


def render(people, out_png, out_frame):
    img = Image.new("RGB", (CANVAS, CANVAS), (0, 0, 0))
    d = ImageDraw.Draw(img)

    def draw_skeleton(person):
        body = person.get("pose_keypoints_2d", [])
        # limbs
        for a, b in BODY_PAIRS:
            pa, pb = (a * 3, b * 3)
            if pa + 2 < len(body) and pb + 2 < len(body):
                xa, ya, ca = body[pa], body[pa + 1], body[pa + 2]
                xb, yb, cb = body[pb], body[pb + 1], body[pb + 2]
                if ca > 0.1 and cb > 0.1:
                    d.line([(xa, ya), (xb, yb)], fill=BODY_COLORS.get((a, b), (200, 200, 200)), width=4)
        # joints
        for i in range(len(body) // 3):
            x, y, c = body[3 * i], body[3 * i + 1], body[3 * i + 2]
            if c > 0.1:
                r = 5
                d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 255, 255))
        # hands
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

    for person in people:
        draw_skeleton(person)

    img.save(out_png)
    frame = {"people": people}
    out_frame.write_text(json.dumps(frame, indent=2), encoding="utf-8")
    return out_png


def main():
    man_path = Path(sys.argv[1]) if len(sys.argv) > 1 else MAN_JSON
    woman_path = Path(sys.argv[2]) if len(sys.argv) > 2 else WOMAN_JSON
    out_png = Path(sys.argv[3]) if len(sys.argv) > 3 else OUT_DEFAULT
    man = load_person(man_path)
    woman = load_person(woman_path)
    man_t, woman_t = compose(man, woman)
    saved = render([man_t, woman_t], out_png, OUT_FRAME)
    print(f"Composed {man_path.name} + {woman_path.name}")
    print(f"  PNG:   {saved}")
    print(f"  Frame: {OUT_FRAME}")


if __name__ == "__main__":
    main()
