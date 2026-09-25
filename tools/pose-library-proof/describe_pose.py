"""Describe a pose's verifiable geometry, to ground per-pose prompt wording in FACTS.

Why this exists
---------------
Writing a "canned prompt" for a pose by looking at a stick figure is guesswork: the two renderers use
different limb palettes, so which line is an arm and which is a leg cannot be read off the image. The
keypoints, however, carry the facts that wording actually needs — which joints touch the floor, whether
the head sits above or below the hips, and which way the body extends. This prints those, so a canned
prompt is derived from the reference rather than from an interpretation of a drawing.

Usage
-----
    python tools/pose-library-proof/describe_pose.py <pose.json> [...]
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

JOINTS = [
    "nose", "neck", "r_shoulder", "r_elbow", "r_wrist",
    "l_shoulder", "l_elbow", "l_wrist",
    "r_hip", "r_knee", "r_ankle",
    "l_hip", "l_knee", "l_ankle",
    "r_eye", "l_eye", "r_ear", "l_ear",
]

VISIBILITY_FLOOR = 0.1


def person_of(payload) -> dict:
    """Accepts a bare person dict, the pack's {people:[...], canvas_*} document, or ComfyUI's list form."""
    if isinstance(payload, dict):
        if payload.get("people"):
            return payload["people"][0]
        if "pose_keypoints_2d" in payload:
            return payload
        raise SystemExit(f"A document with keys {sorted(payload)[:6]} carries no person record.")
    for entry in payload:
        if isinstance(entry, dict) and entry.get("people"):
            return entry["people"][0]
    raise SystemExit("No person record found in the document.")


def describe(path: Path) -> None:
    person = person_of(json.loads(path.read_text(encoding="utf-8")))
    body = person.get("pose_keypoints_2d") or []
    if len(body) != len(JOINTS) * 3:
        raise SystemExit(f"{path.name}: expected {len(JOINTS) * 3} body values, found {len(body)}.")

    joints = {
        JOINTS[i]: (body[i * 3], body[i * 3 + 1], body[i * 3 + 2])
        for i in range(len(JOINTS))
    }
    visible = {name: point for name, point in joints.items() if point[2] > VISIBILITY_FLOOR}
    if len(visible) < 6:
        raise SystemExit(f"{path.name}: only {len(visible)} visible joints; nothing to describe.")

    by_y = sorted(visible.items(), key=lambda item: -item[1][1])  # y grows down -> largest is lowest
    by_x = sorted(visible.items(), key=lambda item: item[1][0])

    def y(name: str) -> str:
        return f"{joints[name][1]:7.1f}" if name in visible else "   none"

    print(f"\n=== {path.name} ===")
    print(f"  canvas band   y {min(p[1] for p in visible.values()):7.1f} .. {max(p[1] for p in visible.values()):7.1f}"
          f"   x {min(p[0] for p in visible.values()):7.1f} .. {max(p[0] for p in visible.values()):7.1f}")
    print(f"  lowest joints (contacts): "
          + ", ".join(f"{name}@{point[1]:.0f}" for name, point in by_y[:5]))
    print(f"  highest joints          : "
          + ", ".join(f"{name}@{point[1]:.0f}" for name, point in by_y[-3:]))
    print(f"  leftmost / rightmost    : {by_x[0][0]}@{by_x[0][1][0]:.0f} .. {by_x[-1][0]}@{by_x[-1][1][0]:.0f}")

    if "nose" in visible and "r_hip" in visible and "l_hip" in visible:
        hip_y = (joints["r_hip"][1] + joints["l_hip"][1]) / 2.0
        head_y = joints["nose"][1]
        relation = "ABOVE" if head_y < hip_y else "BELOW"
        print(f"  head is {relation} the hips by {abs(hip_y - head_y):.0f}px "
              f"(nose y={head_y:.0f}, hips y={hip_y:.0f})")

    for chain in (("r_wrist", "r_elbow", "r_shoulder"), ("l_wrist", "l_elbow", "l_shoulder")):
        if all(name in visible for name in chain):
            wrist, elbow, shoulder = (joints[name][1] for name in chain)
            direction = "down" if wrist > shoulder else "up"
            print(f"  {chain[0][0]} arm: wrist is {direction} from the shoulder "
                  f"({wrist:.0f} vs {shoulder:.0f}), elbow at {elbow:.0f}")

    for chain in (("r_ankle", "r_knee", "r_hip"), ("l_ankle", "l_knee", "l_hip")):
        if all(name in visible for name in chain):
            ankle, knee, hip = (joints[name][1] for name in chain)
            print(f"  {chain[0][0][0]} leg: knee y={knee:.0f}, ankle y={ankle:.0f}, hip y={hip:.0f} "
                  f"(ankle {'below' if ankle > knee else 'above'} the knee)")


def main() -> int:
    parser = argparse.ArgumentParser(description="Print a pose's verifiable keypoint facts.")
    parser.add_argument("poses", nargs="+", type=Path)
    args = parser.parse_args()

    for path in args.poses:
        if not path.is_file():
            print(f"missing: {path}", file=sys.stderr)
            continue
        describe(path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
