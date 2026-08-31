#!/usr/bin/env python3
"""
Pull the LATEST approved identity-pack face views for Becky and Dean from the app
database and stage them as the canonical multiangle refs.

This is the source-of-truth refresh for:
    specs/image-generator-tests/identity-two-character/refs/multiangle/

It replaces the old ad-hoc `--pull-v4` staging. For each character it:
  1. finds the latest APPROVED CharacterImageIdentityPacks row,
  2. reads its Face assets (SceneImageReferenceAssets, AssetKind='Face'),
  3. copies each asset file from the scene-image root into refs/multiangle/ with
     the canonical name <char>_<view>.<ext>.

Views map: Front -> front, ThreeQuarterLeft -> 34l, ThreeQuarterRight -> 34r,
           ProfileLeft -> profl, ProfileRight -> profr.

Read-only against the DB; only writes under refs/multiangle/.

Usage:
  python pull_latest_refs.py
  python pull_latest_refs.py --db <path> --root <scene-image-root>
"""
import argparse, hashlib, json, os, shutil, sqlite3, sys

HERE = os.path.dirname(os.path.abspath(__file__))   # .../identity-two-character
SUITE = HERE                                          # this script lives in the suite dir
OUT_DIR = os.path.join(SUITE, "refs", "multiangle")

REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))  # repo root
DEFAULT_DB = os.path.join(REPO, "DreamGenClone.Web", "data", "dreamgenclone.dev.db")
DEFAULT_ROOT = os.path.join(REPO, "DreamGenClone.Web", "data", "scene-images")

# Campground Intimacy characters (stable profile ids for this suite).
CHARACTERS = {
    "becky": "f58f959a-8050-4388-a219-99d2df3446a1",
    "dean":  "faee1ec0-1cf3-459e-97d2-ad59717c41ba",
}

VIEW_STEM = {
    "Front":            "front",
    "ThreeQuarterLeft": "34l",
    "ThreeQuarterRight": "34r",
    "ProfileLeft":      "profl",
    "ProfileRight":     "profr",
}

LATEST_PACK_SQL = """
    SELECT Id, Version FROM CharacterImageIdentityPacks
    WHERE CharacterProfileId = ? AND Status = 'Approved'
    ORDER BY Version DESC LIMIT 1
"""

FACE_ASSETS_SQL = """
    SELECT FaceView, FileRelativePath FROM SceneImageReferenceAssets
    WHERE IdentityPackId = ? AND AssetKind = 'Face' AND IsApproved = 1
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--db", default=DEFAULT_DB)
    ap.add_argument("--root", default=DEFAULT_ROOT)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    conn = sqlite3.connect(f"file:{args.db}?mode=ro", uri=True)
    conn.row_factory = sqlite3.Row
    c = conn.cursor()

    os.makedirs(OUT_DIR, exist_ok=True)
    report = []
    for char, profile_id in CHARACTERS.items():
        pack = c.execute(LATEST_PACK_SQL, (profile_id,)).fetchone()
        if not pack:
            print(f"[{char}] no approved pack — SKIP")
            continue
        pack_id, version = pack["Id"], pack["Version"]
        print(f"[{char}] latest approved pack v{version} ({pack_id})")

        faces = c.execute(FACE_ASSETS_SQL, (pack_id,)).fetchall()
        by_view = {r["FaceView"]: r["FileRelativePath"] for r in faces}
        missing_views = [v for v in VIEW_STEM if v not in by_view]
        if missing_views:
            print(f"  WARN: missing views {missing_views}")

        for view, stem in VIEW_STEM.items():
            rel = by_view.get(view)
            if not rel:
                continue
            src = os.path.join(args.root, rel.replace("/", os.sep))
            ext = os.path.splitext(src)[1] or ".png"
            dst = os.path.join(OUT_DIR, f"{char}_{stem}{ext}")
            if not os.path.exists(src):
                print(f"  MISSING on disk: {src}")
                report.append({"char": char, "view": view, "status": "missing"})
                continue
            if args.dry_run:
                print(f"  would copy {src} -> {dst}")
            else:
                # Remove any stale same-stem file with a different extension (e.g. old .jpg).
                for old in os.listdir(OUT_DIR):
                    base, old_ext = os.path.splitext(old)
                    if base == f"{char}_{stem}" and old_ext.lower() != ext.lower():
                        os.remove(os.path.join(OUT_DIR, old))
                        print(f"  removed stale {old}")
                shutil.copy2(src, dst)
                sha = hashlib.sha256(open(dst, "rb").read()).hexdigest()
                print(f"  copied {os.path.basename(src)} -> {os.path.basename(dst)}  sha256={sha[:16]}…")
            report.append({"char": char, "view": view, "status": "ok" if os.path.exists(dst) or args.dry_run else "missing",
                           "file": os.path.basename(dst)})

    conn.close()
    if args.dry_run:
        print("\nDRY RUN — no files written.")
    else:
        print(f"\nDone. Refs staged in {OUT_DIR}")


if __name__ == "__main__":
    main()
