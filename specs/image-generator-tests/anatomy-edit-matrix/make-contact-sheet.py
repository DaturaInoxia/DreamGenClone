"""Build one labelled contact sheet so a whole comparison set can be reviewed in a single image.

Rows are filled left-to-right, top-to-bottom; each cell keeps its source filename as its label, so a
comparison set is readable without opening a dozen files.

Usage:
  make-contact-sheet.py <out.png> <cols> <caption> <file1> <file2> ...
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

if len(sys.argv) < 5:
    raise SystemExit("usage: make-contact-sheet.py <out.png> <cols> <caption> <file...>")

out_path = sys.argv[1]
cols = int(sys.argv[2])
caption = sys.argv[3]
files = sys.argv[4:]

CELL_W, CELL_H = 360, 400
LABEL_H = 34
HEADER_H = 56 if caption else 0
PAD = 10
BG = (24, 24, 28)
LABEL_BG = (40, 40, 48)
TEXT = (235, 235, 240)

rows = (len(files) + cols - 1) // cols
width = PAD + cols * (CELL_W + PAD)
height = HEADER_H + PAD + rows * (CELL_H + LABEL_H + PAD)

sheet = Image.new("RGB", (width, height), BG)
draw = ImageDraw.Draw(sheet)


def load_font(size):
    for candidate in (r"C:\Windows\Fonts\arial.ttf", r"C:\Windows\Fonts\segoeui.ttf"):
        if os.path.exists(candidate):
            try:
                return ImageFont.truetype(candidate, size)
            except OSError:
                pass
    return ImageFont.load_default()


font_label = load_font(15)
font_header = load_font(24)

if caption:
    draw.text((PAD, 16), caption, fill=TEXT, font=font_header)

for index, path in enumerate(files):
    row, col = divmod(index, cols)
    x = PAD + col * (CELL_W + PAD)
    y = HEADER_H + PAD + row * (CELL_H + LABEL_H + PAD)

    draw.rectangle([x, y, x + CELL_W, y + CELL_H + LABEL_H], fill=LABEL_BG)

    try:
        image = Image.open(path).convert("RGB")
    except Exception as error:  # keep the sheet usable even if one file is unreadable
        draw.text((x + 8, y + 8), f"UNREADABLE: {error}", fill=(255, 120, 120), font=font_label)
        continue

    image.thumbnail((CELL_W - 8, CELL_H - 8))
    sheet.paste(image, (x + (CELL_W - image.size[0]) // 2, y + (CELL_H - image.size[1]) // 2))

    label = os.path.basename(path).replace(".png", "")
    draw.text((x + 8, y + CELL_H + 6), label, fill=TEXT, font=font_label)

sheet.save(out_path)
print(f"{out_path}  {sheet.size[0]}x{sheet.size[1]}  cells={len(files)}")
