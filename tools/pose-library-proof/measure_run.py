"""Measure a pose-library-proof run with the APPROVED probe, one row per render.

This adds no scoring of its own. It maps each rendered pose back to the pack's own keypoint JSON and
runs ``tools/pose-angle-probe/probe_pose_angle.py`` (which extracts DWPose keypoints from the render
via ComfyUI and scores joint geometry as a percentage of figure height), then collects the results
into one table.

Usage
-----
    python tools/pose-library-proof/measure_run.py ^
        --run specs/image-generator-tests/pose-library-all-fours/runs/<run> ^
        --pack-root pose-packs/openpose-nsfw ^
        --comfy https://comfy.kenacwood.net --max-mean-pct 6
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
PROBE = REPO_ROOT / "tools" / "pose-angle-probe" / "probe_pose_angle.py"


def slug(value: str) -> str:
    """Mirrors PoseLibraryService.Slug, so a preset id can be mapped back to its pack file."""
    builder = ""
    for ch in value.strip().lower():
        if ch.isalnum():
            builder += ch
        elif builder and builder[-1] != "-":
            builder += "-"
    return builder.strip("-")


def build_preset_index(pack_root: Path) -> dict[str, Path]:
    """preset id -> the pack's own keypoint JSON, using the importer's own id rule.

    Accepts either shape: a packs ROOT (one folder per pack, each with pack.json - the app's
    PoseLibrary:PacksRoot, i.e. ``pose-packs``) or a single pack folder (``pose-packs/openpose-nsfw``).
    Getting this wrong is silent: every preset id then fails to match and the run looks unmeasurable.
    """
    if (pack_root / "pack.json").is_file():
        pack_dirs = [pack_root]
    else:
        pack_dirs = sorted(p for p in pack_root.iterdir() if p.is_dir() and (p / "pack.json").is_file())

    if not pack_dirs:
        raise SystemExit(
            f"No pack found under {pack_root}. Point --pack-root at the packs root (one folder per pack) "
            "or at a single pack folder holding pack.json."
        )

    index: dict[str, Path] = {}
    for pack_dir in pack_dirs:
        pack_slug = slug(pack_dir.name)
        for path in sorted(pack_dir.rglob("*.json")):
            if path.name.lower() == "pack.json":
                continue
            relative = path.relative_to(pack_dir).as_posix()
            stem = relative[:-5] if relative.lower().endswith(".json") else relative
            builder = f"pack-{pack_slug}-"
            for ch in stem:
                if ch.isalnum():
                    builder += ch.lower()
                elif builder[-1] != "-":
                    builder += "-"
            index[builder.rstrip("-")[:180]] = path
    return index


def main() -> int:
    parser = argparse.ArgumentParser(description="Measure a pose-library proof run with pose-angle-probe.")
    parser.add_argument("--run", required=True, help="the run directory written by run_pose_proof.py")
    parser.add_argument("--pack-root", default="pose-packs/openpose-nsfw",
                        help="pack root holding one folder per pack (the app's PosesRoot).")
    parser.add_argument("--comfy", default="https://comfy.kenacwood.net", help="ComfyUI base URL")
    parser.add_argument("--max-mean-pct", type=float, default=6.0,
                        help="the bar, in percent of figure height (probe default band)")
    parser.add_argument("--only", default=None, help="optional comma-separated pose stem filter")
    args = parser.parse_args()

    run_dir = Path(args.run)
    if not run_dir.is_absolute():
        run_dir = REPO_ROOT / run_dir
    pack_root = Path(args.pack_root)
    if not pack_root.is_absolute():
        pack_root = REPO_ROOT / pack_root

    manifest_path = run_dir / "manifest.json"
    if not manifest_path.is_file():
        raise SystemExit(f"No manifest at {manifest_path}; run run_pose_proof.py first.")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))

    index = build_preset_index(pack_root)
    only = {part.strip() for part in args.only.split(",")} if args.only else None
    measurements_dir = run_dir / "measurements"
    measurements_dir.mkdir(exist_ok=True)

    rows = []
    for entry in manifest["runs"]:
        pose = entry["pose"]
        if only and pose not in only:
            continue

        candidate = index.get(pose)
        if candidate is None:
            rows.append({"pose": pose, "seed": entry["seed"], "error": "no pack JSON for this preset id"})
            continue

        image = run_dir / entry["outputs"][0]["file"]
        out_path = measurements_dir / f"{pose}__s{entry['seed']}.json"
        command = [
            sys.executable, str(PROBE),
            "--candidate", str(candidate),
            "--plate", str(image),
            "--comfy", args.comfy,
            "--max-mean-pct", str(args.max_mean_pct),
            "--angle", pose,
            "--out", str(out_path),
        ]
        completed = subprocess.run(command, capture_output=True, text=True)
        if not out_path.is_file():
            rows.append({
                "pose": pose,
                "seed": entry["seed"],
                "candidate": str(candidate.relative_to(REPO_ROOT)),
                "error": (completed.stderr or completed.stdout).strip().splitlines()[-1:] or ["probe produced no result"],
            })
            continue

        result = json.loads(out_path.read_text(encoding="utf-8"))
        rows.append({
            "pose": pose,
            "seed": entry["seed"],
            "candidate": str(candidate.relative_to(REPO_ROOT)),
            "image": entry["outputs"][0]["file"],
            "joints": result["joints_compared"],
            "mean": result["mean_error_pct_of_height"],
            "p95": result["p95_error_pct_of_height"],
            "max": result["max_error_pct_of_height"],
            "worst": result["worst_joint"],
            "passed": result["passed"],
        })
        print(
            f"{pose:52} s{entry['seed']}  mean={result['mean_error_pct_of_height']:6.2f}%  "
            f"p95={result['p95_error_pct_of_height']:6.2f}%  joints={result['joints_compared']:2d}  "
            f"{'PASS' if result['passed'] else 'FAIL'}",
            flush=True,
        )

    summary_path = run_dir / "measurements.json"
    summary_path.write_text(json.dumps({
        "maxMeanPct": args.max_mean_pct,
        "comfy": args.comfy,
        "rows": rows,
        "passed": sum(1 for row in rows if row.get("passed")),
        "failed": sum(1 for row in rows if row.get("passed") is False),
        "errors": sum(1 for row in rows if "error" in row),
    }, indent=2), encoding="utf-8")

    print(f"\n{summary_path}")
    if any("error" in row for row in rows):
        print("Rows that could not be measured:")
        for row in rows:
            if "error" in row:
                print(f"  {row['pose']} seed={row['seed']}: {row['error']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
