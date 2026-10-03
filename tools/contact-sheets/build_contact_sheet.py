#!/usr/bin/env python
"""Build labeled contact sheets from a JSON spec.

Why this exists: proof verdicts in this repo were being called from downscaled chat previews, which produced
three wrong calls in the Qwen-2.1 explicit-anatomy programme (2026-10-02). A contact sheet renders the arms
side by side at a fixed tile size with the cell+arm burned into the image, so a verdict can be pinned to a
pixel region and re-checked later. Generated contact sheets are also what makes a render programme reviewable
by the operator instead of by the agent's description.

Spec format (JSON):

    {
      "sheets": [
        {
          "name": "sheet-1-example",
          "title": "TITLE DRAWN AT THE TOP OF THE SHEET",
          "rows": [
            [ {"path": "a.png", "label": "arm A"}, {"path": "b.png", "label": "arm B"} ],
            [ ["c.png", "arm C"], ["d.png", "arm D"] ]
          ]
        }
      ]
    }

- `rows` is a list of rows; each entry is `[path, label]` or `{"path":..., "label":...}`.
- An empty entry ("", "") leaves a blank tile slot.
- `name` is the output file stem; defaults to `sheet-N`.
- A missing file renders as a red placeholder instead of aborting the sheet.

Run with the repo venv:
    d:/src/DreamGenClone/.venv/Scripts/python.exe tools/contact-sheets/build_contact_sheet.py \
        --spec artifacts/tmp/qwen21-nsfw-lora/sheet-spec.json \
        --out-dir artifacts/tmp/qwen21-nsfw-lora/sheets

Outputs are written to `--out-dir` (use a git-ignored path such as artifacts/tmp/...).
"""
import argparse
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

TILE = 700
LABEL_H = 46
PAD = 10


def font(size: int):
    for name in ("arial.ttf", "segoeui.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except Exception:
            continue
    return ImageFont.load_default()


def normalize(entry):
    """Accept [path, label] or {"path":..., "label":...}; return (path, label)."""
    if isinstance(entry, dict):
        return str(entry.get("path", "")), str(entry.get("label", ""))
    if isinstance(entry, (list, tuple)):
        path = str(entry[0]) if len(entry) > 0 else ""
        label = str(entry[1]) if len(entry) > 1 else ""
        return path, label
    return str(entry), ""


def build(sheet, out_dir: Path, tile: int, fmt: str, quality: int, label_h: int):
    rows = [[normalize(e) for e in row] for row in sheet.get("rows", [])]
    if not rows:
        raise SystemExit(f"sheet '{sheet.get('name')}' has no rows")
    cols = max(len(r) for r in rows)
    w = cols * tile + (cols + 1) * PAD
    h = 60 + len(rows) * (tile + label_h + PAD) + PAD
    img = Image.new("RGB", (w, h), (18, 18, 20))
    draw = ImageDraw.Draw(img)
    title = sheet.get("title", "")
    draw.text((PAD, 16), title, fill=(255, 255, 255), font=font(26))
    f = font(max(12, int(tile * 0.028)))

    y = 60
    for row in rows:
        x = PAD
        for path, label in row:
            if not path:
                x += tile + PAD
                continue
            p = Path(path)
            if p.is_file():
                im = Image.open(p).convert("RGB").resize((tile, tile), Image.LANCZOS)
                img.paste(im, (x, y))
            else:
                draw.rectangle([x, y, x + tile, y + tile], outline=(150, 50, 50))
                draw.text((x + 8, y + 8), f"MISSING\n{path}", fill=(220, 90, 90), font=f)
            draw.text((x + 4, y + tile + 12), label, fill=(180, 220, 255), font=f)
            x += tile + PAD
        y += tile + label_h + PAD

    out_dir.mkdir(parents=True, exist_ok=True)
    name = sheet.get("name") or f"sheet-{len(list(out_dir.glob('sheet-*')))+1}"
    ext = ".jpg" if fmt == "jpeg" else ".png"
    dest = out_dir / f"{name}{ext}"
    if fmt == "jpeg":
        img.save(dest, format="JPEG", quality=quality, optimize=True)
    else:
        img.save(dest, format="PNG")
    print(f"{dest}  {img.width}x{img.height}  {dest.stat().st_size/1e6:.2f} MB")


def main():
    ap = argparse.ArgumentParser(description="Build labeled contact sheets from a JSON spec.")
    ap.add_argument("--spec", required=True, help="path to the JSON spec")
    ap.add_argument("--out-dir", required=True, help="output directory (use a git-ignored path)")
    ap.add_argument("--tile", type=int, default=TILE, help=f"tile size in px (default {TILE})")
    ap.add_argument("--label-height", type=int, default=LABEL_H, help="label band height")
    ap.add_argument("--format", choices=("png", "jpeg"), default="png", help="png for review, jpeg for committing")
    ap.add_argument("--quality", type=int, default=90, help="jpeg quality (default 90)")
    args = ap.parse_args()

    spec = json.loads(Path(args.spec).read_text(encoding="utf-8"))
    sheets = spec["sheets"] if isinstance(spec, dict) and "sheets" in spec else [spec]
    for s in sheets:
        build(s, Path(args.out_dir), args.tile, args.format, args.quality, args.label_height)


if __name__ == "__main__":
    sys.exit(main())
