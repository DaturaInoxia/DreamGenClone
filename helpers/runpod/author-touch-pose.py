import argparse
import json
import math
import sys
from pathlib import Path


MAN_INDEX = 0
WOMAN_INDEX = 1
RIGHT_WRIST_INDEX = 4
LEFT_ELBOW_INDEX = 6
LEFT_WRIST_INDEX = 7
TARGET_MAN_RIGHT_WRIST = (130.0, 980.0)
TARGET_MAN_LEFT_ELBOW = (390.0, 650.0)
TARGET_MAN_LEFT_WRIST = (420.0, 900.0)
MAN_LEFT_HAND_ROTATION_DEGREES = -135.0
MAN_LEFT_HAND_SCALE = 0.55
TARGET_LEFT_ELBOW = (900.0, 690.0)
TARGET_LEFT_WRIST = (890.0, 850.0)
LEFT_HAND_ROTATION_DEGREES = -145.0
LEFT_HAND_SCALE = 0.55
TARGET_CONTACT_WRIST = (465.0, 815.0)


def set_body_point(keypoints: list[float], index: int, point: tuple[float, float]) -> None:
    offset = index * 3
    keypoints[offset] = point[0]
    keypoints[offset + 1] = point[1]
    keypoints[offset + 2] = 1.0


def transform_hand(
    keypoints: list[float],
    target_wrist: tuple[float, float],
    rotation_degrees: float,
    scale: float,
) -> None:
    if len(keypoints) != 63:
        raise ValueError(f"Expected 63 hand values, found {len(keypoints)}")

    source_wrist = (keypoints[0], keypoints[1])
    radians = math.radians(rotation_degrees)
    cosine = math.cos(radians)
    sine = math.sin(radians)

    for offset in range(0, len(keypoints), 3):
        delta_x = keypoints[offset] - source_wrist[0]
        delta_y = keypoints[offset + 1] - source_wrist[1]
        rotated_x = delta_x * cosine - delta_y * sine
        rotated_y = delta_x * sine + delta_y * cosine
        keypoints[offset] = target_wrist[0] + rotated_x * scale
        keypoints[offset + 1] = target_wrist[1] + rotated_y * scale
        keypoints[offset + 2] = 1.0


def clear_hand(keypoints: list[float]) -> None:
    if len(keypoints) != 63:
        raise ValueError(f"Expected 63 hand values, found {len(keypoints)}")
    for offset in range(0, len(keypoints), 3):
        keypoints[offset] = 0.0
        keypoints[offset + 1] = 0.0
        keypoints[offset + 2] = 0.0


def author_pose(source_path: Path, output_json_path: Path, output_image_path: Path) -> None:
    frames = json.loads(source_path.read_text(encoding="utf-8"))
    if len(frames) != 1:
        raise ValueError(f"Expected one pose frame, found {len(frames)}")

    frame = frames[0]
    people = frame.get("people", [])
    if len(people) != 2:
        raise ValueError(f"Expected two people, found {len(people)}")

    man = people[MAN_INDEX]
    man_body = man.get("pose_keypoints_2d", [])
    if len(man_body) != 54:
        raise ValueError(f"Expected 54 man body values, found {len(man_body)}")

    set_body_point(man_body, RIGHT_WRIST_INDEX, TARGET_MAN_RIGHT_WRIST)
    set_body_point(man_body, LEFT_ELBOW_INDEX, TARGET_MAN_LEFT_ELBOW)
    set_body_point(man_body, LEFT_WRIST_INDEX, TARGET_MAN_LEFT_WRIST)
    transform_hand(
        man.get("hand_left_keypoints_2d", []),
        TARGET_MAN_LEFT_WRIST,
        MAN_LEFT_HAND_ROTATION_DEGREES,
        MAN_LEFT_HAND_SCALE,
    )
    clear_hand(man.get("hand_right_keypoints_2d", []))

    woman = people[WOMAN_INDEX]
    body = woman.get("pose_keypoints_2d", [])
    if len(body) != 54:
        raise ValueError(f"Expected 54 woman body values, found {len(body)}")

    set_body_point(body, RIGHT_WRIST_INDEX, TARGET_CONTACT_WRIST)
    transform_hand(
        woman.get("hand_right_keypoints_2d", []),
        TARGET_CONTACT_WRIST,
        rotation_degrees=0.0,
        scale=1.0,
    )
    set_body_point(body, LEFT_ELBOW_INDEX, TARGET_LEFT_ELBOW)
    set_body_point(body, LEFT_WRIST_INDEX, TARGET_LEFT_WRIST)
    transform_hand(
        woman.get("hand_left_keypoints_2d", []),
        TARGET_LEFT_WRIST,
        LEFT_HAND_ROTATION_DEGREES,
        LEFT_HAND_SCALE,
    )

    output_json_path.parent.mkdir(parents=True, exist_ok=True)
    output_json_path.write_text(json.dumps(frames, indent=2) + "\n", encoding="utf-8")

    extension_src = Path(
        "/workspace/comfyui/custom_nodes/comfyui_controlnet_aux/src"
    )
    sys.path.insert(0, str(extension_src))
    from custom_controlnet_aux.dwpose import decode_json_as_poses, draw_poses

    poses, _, height, width = decode_json_as_poses(frame)
    image = draw_poses(
        poses,
        height,
        width,
        draw_body=True,
        draw_hand=True,
        draw_face=True,
        xinsr_stick_scaling=True,
    )

    import cv2

    output_image_path.parent.mkdir(parents=True, exist_ok=True)
    if not cv2.imwrite(str(output_image_path), image):
        raise RuntimeError(f"Failed to write pose image: {output_image_path}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source_json", type=Path)
    parser.add_argument("output_json", type=Path)
    parser.add_argument("output_image", type=Path)
    args = parser.parse_args()
    author_pose(args.source_json, args.output_json, args.output_image)


if __name__ == "__main__":
    main()