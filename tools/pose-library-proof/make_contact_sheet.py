"""Build a labelled contact sheet for a pose-library-proof run, for the visual review pass.

The per-render adherence numbers come from the approved probe (see ``measure_run.py``); a sheet is
how a human actually spots the failures the numbers cannot name (an extra limb, a fallen-over body,
a figure that sat up). Do not skip it and do not rubber-stamp it.

Usage
-----
    python tools/pose-library-proof/make_contact_sheet.py --run <run dir> [--out <png>] [--cols 4]
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageDraw

REPO_ROOT = Path(__file__).resolve().parents[2]


def main() -> int:
    parser = argparse.ArgumentParser(description="Contact sheet for a pose-library proof run.")
    parser.add_argument("--run", required=True, help="the run directory written by run_pose_proof.py")
    parser.add_argument("--out", help="output PNG (defaults to <run>/contact-sheet.png)")
    parser.add_argument("--cols", type=int, default=4)
    parser.add_argument("--cell", type=int, default=360)
    parser.add_argument("--skeletons", help="optional folder of pose skeletons to show beside each render")
    args = parser.parse_args()

    run_dir = Path(args.run)
    if not run_dir.is_absolute():
        run_dir = REPO_ROOT / run_dir

    manifest = json.loads((run_dir / "manifest.json").read_text(encoding="utf-8"))
    out_path = Path(args.out) if args.out else run_dir / "contact-sheet.png"
    if not out_path.is_absolute():
        out_path = REPO_ROOT / out_path

    entries = sorted(manifest["runs"], key=lambda r: (r["pose"], r["seed"]))
    if not entries:
        raise SystemExit("The manifest records no runs.")

    skeleton_dir = Path(args.skeletons) if args.skeletons else None
    if skeleton_dir and not skeleton_dir.is_absolute():
        skeleton_dir = REPO_ROOT / skeleton_dir

    cell, cols = args.cell, args.cols
    label_h = 30
    rows = (len(entries) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + label_h)), (22, 22, 24))
    draw = ImageDraw.Draw(sheet)

    for index, entry in enumerate(entries):
        r, c = divmod(index, cols)
        top = r * (cell + label_h)
        render = Image.open(run_dir / entry["outputs"][0]["file"]).convert("RGB")

        poses = [render]
        if skeleton_dir:
            skeleton_path = skeleton_dir / entry["skeleton"]
            if skeleton_path.is_file():
                poses.insert(0, Image.open(skeleton_path).convert("RGB"))

        slot = (cell - 6) // len(poses)
        for slot_index, image in enumerate(poses):
            image = image.copy()
            image.thumbnail((slot - 4, cell - 8))
            x = c * cell + 3 + slot_index * slot + (slot - image.width) // 2
            y = top + 4 + (cell - image.height) // 2
            sheet.paste(image, (x, y))

        draw.text((c * cell + 5, top + cell + 6), f"{entry['pose'][-30:]}  s{entry['seed']}", fill=(235, 235, 235))

    out_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out_path)
    print(f"wrote {out_path}  ({len(entries)} render(s), {cols} column(s))")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
