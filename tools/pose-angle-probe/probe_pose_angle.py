#!/usr/bin/env python3
"""Measure how closely a projected pose agrees with what DWPose reads back from an image.

Why this exists
---------------
An angle produced by projecting the authoring rig is only *known good* once it has been measured against
something that came from a real image. This tool does that measurement and prints the numbers; it never writes
"known-good" itself, and it never decides the bar — the caller declares the tolerance, because a threshold
hidden in a script is a claim nobody agreed to.

What it compares
----------------
Two OpenPose person JSONs:

* the **candidate** — the keypoints the app projected for an angle (``pose-pose-angle emit``);
* the **reference** — keypoints DWPose extracted from a rendered plate in that view, either passed in with
  ``--reference`` or produced here by ``--plate`` plus a reachable ComfyUI.

Scoring is **joint geometry**, never raster overlap. Raster IoU can score a hollow outline as a match and was
already proved invalid for this purpose in B-123's scoping.

The comparison is deliberately scale- and position-invariant, because the two sides come from different figures:
the candidate is the app's rig, the reference is whatever the image model drew. What must agree is the *shape* of
the pose — where each joint sits relative to the others — so both sides are normalised by figure height and
centred on their own bounding box, and every error is reported as a percentage of height.

It also reports the **shoulder span as a fraction of height** for both sides. That is the non-circular part: a
round trip through a plate the candidate itself conditioned will tend to reproduce the candidate, but the angle
itself still has to survive. A profile whose span did not collapse means the render ignored the request.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
import urllib.request
import uuid
from pathlib import Path

# COCO-18 joint names, in OpenPose's order.
JOINTS = [
    "nose", "neck", "r_shoulder", "r_elbow", "r_wrist",
    "l_shoulder", "l_elbow", "l_wrist",
    "r_hip", "r_knee", "r_ankle",
    "l_hip", "l_knee", "l_ankle",
    "r_eye", "l_eye", "r_ear", "l_ear",
]

VISIBILITY_FLOOR = 0.1

# The app's ComfyUI provider URL is a tunnel (https://comfy.kenacwood.net) whose proxy answers 403 to the
# default 'Python-urllib/3.x' agent while accepting curl - verified 2026-09-25 with the same bytes to the
# same URL, only the agent differing (403 vs 200). Every request therefore carries an explicit agent.
USER_AGENT = "pose-angle-probe/1.0 (+curl)"


def unwrap_people(payload, what: str, source: str) -> list[dict]:
    """Finds the person records in an OpenPose document, whatever shape it arrives in.

    Three shapes occur in practice and all three mean the same thing:

    * ``[{canvas_height, canvas_width, people: [...]}]`` — what ComfyUI's OpenposePreprocessor emits, and the
      shape a naive reader mistakes for a single person (the wrapper dict has no ``pose_keypoints_2d``, so the
      joints silently read as empty and the measurement looks like a bad pose);
    * ``{"people": [...]}``;
    * a bare person dict.

    Anything else is refused by name rather than guessed at, because guessing here would report a pose "mismatch"
    that is really a parsing bug.
    """
    if isinstance(payload, dict):
        payload = [payload]

    if not isinstance(payload, list):
        raise SystemExit(f"{what}: '{source}' is a {type(payload).__name__}, not an OpenPose document")

    people: list[dict] = []
    for entry in payload:
        if not isinstance(entry, dict):
            raise SystemExit(f"{what}: '{source}' holds a {type(entry).__name__} where a person was expected")

        if isinstance(entry.get("people"), list):
            people.extend(entry["people"])
        elif "pose_keypoints_2d" in entry:
            people.append(entry)
        else:
            raise SystemExit(
                f"{what}: '{source}' has neither 'people' nor 'pose_keypoints_2d' (keys: "
                f"{sorted(entry.keys())}); this is not an OpenPose person document"
            )

    return people


def load_person(path: Path, what: str) -> dict:
    """Reads one person out of an OpenPose document.

    A document with two people is refused rather than silently truncated: a two-person reference would score
    against the wrong figure and report a large error that looks like a bad projection.
    """
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        raise SystemExit(f"{what}: '{path}' is not valid JSON ({error})")

    people = unwrap_people(data, what, str(path))
    if not people:
        raise SystemExit(f"{what}: '{path}' holds no person")

    if len(people) != 1:
        raise SystemExit(
            f"{what}: '{path}' holds {len(people)} people. A pose comparison is one person against one person; "
            "crop the image or extract a single figure first."
        )

    body = people[0].get("pose_keypoints_2d") or []
    if len(body) != len(JOINTS) * 3:
        raise SystemExit(
            f"{what}: '{path}' has {len(body)} pose_keypoints_2d values; {len(JOINTS)} joints need "
            f"{len(JOINTS) * 3}. A partial extraction cannot be compared joint by joint."
        )

    points = [
        {"name": JOINTS[i], "x": body[i * 3], "y": body[i * 3 + 1], "c": body[i * 3 + 2]}
        for i in range(len(JOINTS))
    ]
    return {"path": str(path), "points": points}


def visible(person: dict) -> list[dict]:
    return [point for point in person["points"] if point["c"] > VISIBILITY_FLOOR]


def normalise(person: dict) -> dict:
    """Scales so figure height is 1.0 and centres on the visible bounding box.

    Height is the vertical extent of the visible body joints, which is the same ruler on both sides without either
    side assuming the other's camera.
    """
    points = visible(person)
    if len(points) < 4:
        raise SystemExit(
            f"'{person['path']}' has only {len(points)} visible body joints; too few to normalise or compare."
        )

    min_x = min(point["x"] for point in points)
    max_x = max(point["x"] for point in points)
    min_y = min(point["y"] for point in points)
    max_y = max(point["y"] for point in points)

    height = max_y - min_y
    if height <= 0:
        raise SystemExit(f"'{person['path']}' has zero height, so it cannot be normalised.")

    centre_x = (min_x + max_x) / 2.0
    centre_y = (min_y + max_y) / 2.0

    return {
        point["name"]: ((point["x"] - centre_x) / height, (point["y"] - centre_y) / height)
        for point in points
    }


def shoulder_span(normalised: dict) -> float | None:
    """Shoulder separation as a fraction of height — the angle's own signature."""
    if "r_shoulder" not in normalised or "l_shoulder" not in normalised:
        return None

    (x0, y0), (x1, y1) = normalised["r_shoulder"], normalised["l_shoulder"]
    return math.hypot(x1 - x0, y1 - y0)


def compare(candidate: dict, reference: dict) -> dict:
    cand = normalise(candidate)
    ref = normalise(reference)

    shared = sorted(set(cand) & set(ref))
    if not shared:
        raise SystemExit("The two poses share no visible joints, so there is nothing to compare.")

    rows = []
    for name in shared:
        (cx, cy), (rx, ry) = cand[name], ref[name]
        rows.append({"joint": name, "error_pct": round(math.hypot(cx - rx, cy - ry) * 100.0, 3)})

    errors = sorted(row["error_pct"] for row in rows)
    count = len(errors)
    mean = sum(errors) / count
    p95 = errors[min(count - 1, int(round(0.95 * (count - 1))))]

    return {
        "joints_compared": count,
        "mean_error_pct_of_height": round(mean, 3),
        "p95_error_pct_of_height": round(p95, 3),
        "max_error_pct_of_height": round(errors[-1], 3),
        "worst_joint": max(rows, key=lambda row: row["error_pct"])["joint"],
        "per_joint": sorted(rows, key=lambda row: -row["error_pct"]),
        "candidate_shoulder_span_pct_of_height": (
            None if shoulder_span(cand) is None else round(shoulder_span(cand) * 100.0, 3)
        ),
        "reference_shoulder_span_pct_of_height": (
            None if shoulder_span(ref) is None else round(shoulder_span(ref) * 100.0, 3)
        ),
    }


def upload_plate(comfy: str, plate: Path) -> str:
    """Copies the plate into ComfyUI's input folder and returns the name the server will use.

    ``LoadImage`` only sees ComfyUI's own input directory, so a plate sitting anywhere else has to be sent first.
    Without this the graph would reference a filename the server has never heard of and fail with a missing-image
    error that looks like a bad plate rather than a missing upload.
    """
    boundary = f"----pose-probe-{uuid.uuid4().hex}"
    data = plate.read_bytes()

    # A unique stored name per run. ComfyUI caches node execution, and a cache hit comes back through /history
    # without the POSE_KEYPOINT output, so re-measuring the same plate would fail with "no person found" even
    # though the measurement had just worked.
    stored_name = f"pose-probe-{uuid.uuid4().hex[:12]}{plate.suffix}"
    body = b"".join([
        f"--{boundary}\r\n".encode(),
        f'Content-Disposition: form-data; name="image"; filename="{stored_name}"\r\n'.encode(),
        b"Content-Type: application/octet-stream\r\n\r\n",
        data,
        b"\r\n",
        f"--{boundary}\r\n".encode(),
        b'Content-Disposition: form-data; name="overwrite"\r\n\r\n',
        b"true\r\n",
        f"--{boundary}--\r\n".encode(),
    ])

    request = urllib.request.Request(
        f"{comfy}/upload/image",
        data=body,
        headers={
            "Content-Type": f"multipart/form-data; boundary={boundary}",
            "User-Agent": USER_AGENT,
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            answer = json.loads(response.read())
    except Exception as error:  # noqa: BLE001 — the server's own answer is the useful part
        raise SystemExit(f"Could not upload the plate to ComfyUI at {comfy}: {error}")

    name = answer.get("name")
    if not name:
        raise SystemExit(f"ComfyUI accepted the upload but did not name the stored file: {answer}")

    subfolder = answer.get("subfolder") or ""
    return f"{subfolder}/{name}" if subfolder else name


def extract_with_dwpose(comfy: str, plate: Path, poll_seconds: float = 2.0, timeout_seconds: float = 300.0) -> dict:
    """Runs DWPose on a plate through ComfyUI and returns the extracted person.

    The node is ``OpenposePreprocessor``, which is the DWPose-based body estimator this server exposes and whose
    keypoints come back as ``openpose_json``. Nothing is guessed about the server: an unreachable host or a refused
    prompt raises with the server's own answer.
    """
    import time

    stored = upload_plate(comfy, plate)
    client_id = str(uuid.uuid4())
    graph = {
        "1": {"class_type": "LoadImage", "inputs": {"image": stored}},        "2": {
            "class_type": "OpenposePreprocessor",
            "inputs": {"image": ["1", 0], "detect_hand": "enable", "detect_body": "enable",
                       "detect_face": "enable", "resolution": 1024},
        },
        # POSE_KEYPOINT is not a terminal output, so the graph needs an image sink for the prompt to be valid.
        "3": {"class_type": "SaveImage", "inputs": {"images": ["2", 0], "filename_prefix": "pose-probe"}},
    }

    payload = json.dumps({"prompt": graph, "client_id": client_id}).encode("utf-8")
    request = urllib.request.Request(
        f"{comfy}/prompt",
        data=payload,
        headers={"Content-Type": "application/json", "User-Agent": USER_AGENT},
    )

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            prompt_id = json.loads(response.read())["prompt_id"]
    except Exception as error:  # noqa: BLE001 — the server's own message is the useful part
        raise SystemExit(f"ComfyUI at {comfy} refused the extraction prompt: {error}")

    deadline = time.time() + timeout_seconds
    while time.time() < deadline:
        time.sleep(poll_seconds)
        # The agent matters here too: a proxy in front of ComfyUI answers 403 to the default Python agent on
        # EVERY call, not just the upload, and an unwrapped urlopen then aborts the measurement.
        history_request = urllib.request.Request(
            f"{comfy}/history/{prompt_id}", headers={"User-Agent": USER_AGENT})
        with urllib.request.urlopen(history_request, timeout=30) as response:
            history = json.loads(response.read())

        entry = history.get(prompt_id)
        if not entry:
            continue

        for output in entry.get("outputs", {}).values():
            raw = output.get("openpose_json")
            if not raw:
                continue

            people = unwrap_people(json.loads(raw[0]), "DWPose", f"{plate} (via {comfy})")
            if not people:
                raise SystemExit("DWPose found no person in the plate, so there is nothing to compare.")
            if len(people) != 1:
                raise SystemExit(
                    f"DWPose found {len(people)} people in the plate. A pose comparison is one person against one "
                    "person; use a plate with a single figure."
                )

            body = people[0].get("pose_keypoints_2d") or []
            if len(body) != len(JOINTS) * 3:
                raise SystemExit(
                    f"DWPose returned {len(body)} body values for the plate's person; {len(JOINTS)} joints need "
                    f"{len(JOINTS) * 3}. The estimator did not resolve a full figure in this plate, so there is "
                    "nothing to compare. Use a plate with an unobstructed full body."
                )

            return {"path": f"{plate} (DWPose)", "points": [
                {"name": JOINTS[i], "x": body[i * 3], "y": body[i * 3 + 1], "c": body[i * 3 + 2]}
                for i in range(len(JOINTS))
            ]}

        status = entry.get("status", {})
        if status.get("status_str") == "error":
            raise SystemExit(f"ComfyUI reported an error running the extraction: {status}")

    raise SystemExit(f"ComfyUI did not return an extraction within {timeout_seconds:0}s.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--candidate", required=True, type=Path, help="Projected keypoints, from the app")
    parser.add_argument("--reference", type=Path, help="DWPose keypoints to compare against")
    parser.add_argument("--plate", type=Path, help="Image to extract the reference from, via ComfyUI")
    parser.add_argument("--comfy", default="http://127.0.0.1:8188", help="ComfyUI base URL")
    parser.add_argument("--max-mean-pct", type=float, required=True,
                        help="The bar: maximum acceptable mean joint error, in percent of figure height")
    parser.add_argument("--out", type=Path, help="Write the measurement table here as JSON")
    parser.add_argument("--angle", help="Label for the angle being measured, recorded in the output")
    args = parser.parse_args()

    if (args.reference is None) == (args.plate is None):
        parser.error("give exactly one of --reference or --plate")

    candidate = load_person(args.candidate, "candidate")
    reference = (
        extract_with_dwpose(args.comfy, args.plate)
        if args.plate is not None
        else load_person(args.reference, "reference")
    )

    result = compare(candidate, reference)
    result["angle"] = args.angle
    result["candidate_path"] = candidate["path"]
    result["reference_path"] = reference["path"]
    result["max_mean_pct"] = args.max_mean_pct
    result["passed"] = result["mean_error_pct_of_height"] <= args.max_mean_pct

    print(json.dumps(result, indent=2))
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")

    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
