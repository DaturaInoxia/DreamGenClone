#!/usr/bin/env python3
"""
Shared resolver for the versioned identity reference packs.

Identity proof runners (BigLust suite) source their character reference images
from a VERSIONED archive:

    specs/image-generator-tests/refs/<character>/<version>/<view>.png
        e.g. refs/dean/v7/front.png, refs/becky/v4/profl.png

The ACTIVE version per character is pinned in refs/versions.json (one value per
character). Changing a value there re-points every identity runner at a
different approved pack WITHOUT editing any runner.

Legacy ref stems ("dean_front", "becky_34l", "dean_face", ...) are resolved
against the active version folder, so runners can keep their existing
per-cell stem naming and only change where the image comes from.

Runners should:
  - import this module (add specs/image-generator-tests to sys.path),
  - call resolve_ref(stem) instead of scanning a flat refs/ or refs/multiangle/ dir,
  - persist ref_source() into their run manifest so a run records exactly which
    pack version folder produced its images.

Usage:
  python identity_refs.py            # print the active source report (for quick checks)
"""
import json
import os
import shutil

HERE = os.path.dirname(os.path.abspath(__file__))        # .../specs/image-generator-tests
REPO = os.path.dirname(os.path.dirname(HERE))            # repo root
REFS_ROOT = os.path.join(HERE, "refs")
VERSIONS_FILE = os.path.join(REFS_ROOT, "versions.json")

# Canonical view file-stems inside a pack version folder.
VIEWS = ("front", "34l", "34r", "profl", "profr")
EXTS = (".png", ".jpg", ".jpeg", ".webp")

# Legacy view alias -> canonical file stem.
VIEW_ALIASES = {"face": "front"}


def load_versions():
    """Return {character: version_folder}, ignoring '_'-prefixed metadata keys."""
    if not os.path.exists(VERSIONS_FILE):
        raise RuntimeError(f"Identity pack version pointer not found: {VERSIONS_FILE}")
    with open(VERSIONS_FILE, encoding="utf-8") as f:
        data = json.load(f)
    return {k: v for k, v in data.items() if not str(k).startswith("_")}


def version_for(char):
    """Active pack version folder for a character (from refs/versions.json)."""
    versions = load_versions()
    char = char.lower()
    if char not in versions:
        raise RuntimeError(
            f"No identity pack version configured for character '{char}' in {VERSIONS_FILE}")
    return versions[char]


def version_dir(char):
    """Directory holding a character's active pack views: refs/<char>/<version>/."""
    return os.path.join(REFS_ROOT, char.lower(), version_for(char))


def resolve_view(char, view):
    """Absolute path of <view> for <char>'s active pack, or None if not present."""
    stem = VIEW_ALIASES.get(view, view)
    directory = version_dir(char)
    for ext in EXTS:
        p = os.path.join(directory, stem + ext)
        if os.path.exists(p):
            return p
    return None


def resolve_ref(stem):
    """Resolve a legacy '<char>_<view>' ref stem against the active pack.

    E.g. resolve_ref("dean_front") -> .../refs/dean/v7/front.png
    Raises RuntimeError if the character has no configured version or the file
    is missing, so runners fail fast instead of silently running with no identity.
    """
    stem = stem.lower()
    if "_" not in stem:
        raise RuntimeError(f"Not a '<char>_<view>' ref stem: {stem!r}")
    char, view = stem.split("_", 1)
    p = resolve_view(char, view)
    if p is None:
        raise RuntimeError(
            f"No {view} ref for '{char}' (pack {version_for(char)}): expected a file under "
            f"{version_dir(char)} — run pull_latest_refs / stage refs first")
    return p


def stage(stem, stage_dir):
    """Ensure a flattened, unique copy '<stem><ext>' of the active pack file exists
    in stage_dir and return its path.

    The versioned folders store the SAME view filename under every character
    (refs/dean/v7/front.png vs refs/becky/v4/front.png are both 'front.png'), so
    multi-character runners that address uploaded images by basename must flatten
    each ref to '<char>_<view>.<ext>' (e.g. dean_front.png, becky_profl.png) to
    keep LoadImage names distinct. Safe to call repeatedly; staging is idempotent.
    """
    src = resolve_ref(stem)
    ext = os.path.splitext(src)[1] or ".png"
    dst = os.path.join(stage_dir, stem + ext)
    if not os.path.exists(dst):
        os.makedirs(stage_dir, exist_ok=True)
        shutil.copy2(src, dst)
    return dst


def source_report(chars=None, relative=True):
    """Active pack source per character, for persisting into a run manifest.

    Returns e.g.:
      {"dean":  {"version": "v7", "dir": "refs/dean/v7",
                 "refs": {"front": "refs/dean/v7/front.png", "34l": ..., ...}},
       "becky": {...}}
    """
    versions = load_versions()
    chars = chars or list(versions)
    report = {}
    for char in sorted(c.lower() for c in chars):
        if char not in versions:
            continue
        directory = version_dir(char)
        refs = {}
        for view in VIEWS:
            p = resolve_view(char, view)
            if p:
                refs[view] = os.path.relpath(p, REPO).replace("\\", "/") if relative else p
        entry = {"version": versions[char],
                 "dir": os.path.relpath(directory, REPO).replace("\\", "/") if relative else directory}
        if refs:
            entry["refs"] = refs
        report[char] = entry
    return report


def main():
    report = source_report()
    print(f"Identity ref source ({os.path.relpath(VERSIONS_FILE, REPO)}):")
    for char, info in report.items():
        print(f"  {char}  ->  {info['dir']}  (pack {info['version']})")
        for view, path in info.get("refs", {}).items():
            print(f"       {view:6} {path}")


if __name__ == "__main__":
    main()
