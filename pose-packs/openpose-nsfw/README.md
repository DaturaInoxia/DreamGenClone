# OpenPose NSFW Pose Package (single-character)

Source: Civitai "OpenPose, NSFW pose package. (total 525 poses)" — model 297881, version 574236
("Final"), published 2024-06-15, by **dickccchen761**. Also mirrored on civarchive.com/models/297881.
This is the extracted pack; the distributed archive is
`openposeNSFWPosePackage_final.zip` (66.63 MB), sha256
`814ef6e3dff2b3437607d859428da274ac92be85e4410fae100398e786974fb4`.

**Licence: CreativeML Open RAIL-M** (declared on the model page). It permits use, modification and
redistribution, and it carries **use-based restrictions** (Attachment A of that licence) that travel with
the material — anyone redistributing this pack must pass those restrictions on. The Civitai version also
links an addendum (`civitai.com/models/license/574236`); it has not been read, so treat that as an open
detail rather than a cleared one. Recorded as verified 2026-09-24 in `pack.json`.

- All files are **single-person** OpenPose JSON (472 files, 0 two-person files).
- Each JSON = 1 person: `pose_keypoints_2d` (54 floats = 18 COCO keypoints) plus
  `hand_left_keypoints_2d` / `hand_right_keypoints_2d` (63 floats = 21 keypoints each).
- Resolutions in folder names: `512512`, `512768`, `768512` (SD 1.5-era source canvases).
- The author's own folder tally sums to ~525 poses while the extracted pack holds 472 JSON files. The
  seven known-good/verified entries all resolve, so nothing depended on the missing ones — noted rather
  than silently reconciled.

## Selected combo for standing-man + kneeling-woman fellatio

| Role | File | Geometry (source canvas) |
|---|---|---|
| Man (standing) | `NSFW_standing/512768/NSFW_standing028.json` | nose 148 → hip 550 → ankle 1015; upright standing |
| Woman (kneeling, bent) | `NSFW_Kneeling/512768/NSFW_Kneeling017.json` | nose 241, hip 554, knee≈ankle≈645; head dropped low |

To use: merge the two single-person JSONs into one 2-person frame and render a
1024×1024 OpenPose skeleton PNG. See `helpers/runpod/merge-openpose-pair.py`.
