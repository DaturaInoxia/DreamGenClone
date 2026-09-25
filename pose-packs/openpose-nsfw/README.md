# OpenPose NSFW Pose Package (single-character)

Source: Civitai "OpenPose, NSFW pose package (total 525 poses)" — model 297881
(also mirrored on civarchive.com/models/297881). This is the extracted pack.

- All files are **single-person** OpenPose JSON (472 files, 0 two-person files).
- Each JSON = 1 person: `pose_keypoints_2d` (54 floats = 18 COCO keypoints) plus
  `hand_left_keypoints_2d` / `hand_right_keypoints_2d` (63 floats = 21 keypoints each).
- Resolutions in folder names: `512512`, `512768`, `768512` (SD 1.5-era source canvases).

## Selected combo for standing-man + kneeling-woman fellatio

| Role | File | Geometry (source canvas) |
|---|---|---|
| Man (standing) | `NSFW_standing/512768/NSFW_standing028.json` | nose 148 → hip 550 → ankle 1015; upright standing |
| Woman (kneeling, bent) | `NSFW_Kneeling/512768/NSFW_Kneeling017.json` | nose 241, hip 554, knee≈ankle≈645; head dropped low |

To use: merge the two single-person JSONs into one 2-person frame and render a
1024×1024 OpenPose skeleton PNG. See `helpers/runpod/merge-openpose-pair.py`.
