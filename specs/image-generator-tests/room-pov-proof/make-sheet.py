"""Build the room-POV proof contact sheet: ground truth vs what each arm produced."""
import os, sys, glob
import numpy as np
from PIL import Image, ImageDraw, ImageFont

GT = r"D:\src\DreamGenClone\artifacts\tmp\room-proof\gt"
RUNS = r"D:\src\DreamGenClone\artifacts\tmp\room-proof\runs"
TH = 340


def load(p):
    return np.asarray(Image.open(p).convert("RGB"))


def thumb(im):
    s = TH / im.shape[0]
    return np.asarray(Image.fromarray(im).resize((max(1, int(im.shape[1] * s)), TH), Image.LANCZOS))


def label(im, text, colour=(0, 0, 0)):
    im = Image.fromarray(np.ascontiguousarray(im.astype(np.uint8)))
    d = ImageDraw.Draw(im)
    try:
        f = ImageFont.truetype("arial.ttf", 17)
    except Exception:
        f = ImageFont.load_default()
    lines = text.split("\n")
    w = max(d.textbbox((0, 0), ln, font=f)[2] for ln in lines) + 14
    h = sum(d.textbbox((0, 0), ln, font=f)[3] + 5 for ln in lines) + 10
    d.rectangle([0, 0, w, h], fill=colour)
    y = 5
    for ln in lines:
        d.text((7, y), ln, fill=(255, 255, 255), font=f)
        y += d.textbbox((0, 0), ln, font=f)[3] + 5
    return np.asarray(im)


def hstack(imgs, gap=8):
    h = max(i.shape[0] for i in imgs)
    w = sum(i.shape[1] for i in imgs) + gap * (len(imgs) - 1)
    out = np.full((h, w, 3), 25, np.uint8)
    x = 0
    for i in imgs:
        out[0:i.shape[0], x:x + i.shape[1]] = i
        x += i.shape[1] + gap
    return out


def vstack(imgs, gap=8):
    w = max(i.shape[1] for i in imgs)
    h = sum(i.shape[0] for i in imgs) + gap * (len(imgs) - 1)
    out = np.full((h, w, 3), 25, np.uint8)
    y = 0
    for i in imgs:
        out[y:y + i.shape[0], 0:i.shape[1]] = i
        y += i.shape[0] + gap
    return out


def first(arm):
    hits = sorted(glob.glob(os.path.join(RUNS, arm, "*.png")))
    return load(hits[0]) if hits else None


GREEN, RED, GREY = (25, 90, 45), (120, 35, 30), (45, 45, 50)

row1 = [label(thumb(load(os.path.join(GT, "S_to_N.png"))), "SOURCE (image_1)\nred wall + crate", GREY),
        label(thumb(load(os.path.join(GT, "N_to_S-depth.png"))), "DEPTH MAP (image_2)\ngeometry of the view asked for", (30, 50, 90)),
        label(thumb(load(os.path.join(GT, "N_to_S.png"))), "WHAT WAS ASKED FOR\ngreen wall + picture", GREEN),
        label(thumb(first("G2_depth_as_ref_second")), "ARM G2: PASS\ngreen 0.35 vs 0.33, r +0.57\n4/4 seeds PASS", GREEN)]

row2 = [label(thumb(first("G4_corner")), "ARM G4: corner, PASS\nBOTH walls: green 0.32, yellow 0.31\nr +0.70", GREEN),
        label(thumb(first("A")), "ARM A: no depth reference\nFAIL - reproduced the source, green 0.00\n(4/4 seeds)", RED),
        label(thumb(first("D")), "ARM D: refs without geometry\nFAIL - yellow 0.22 dominant, green 0.18", RED),
        label(thumb(first("C")), "ARM C: prose only\nFAIL - green 0.08", RED)]

sheet = vstack([hstack(row1), hstack(row2)])
out = os.path.join(RUNS, "proof-sheet.png")
Image.fromarray(sheet).save(out)
print("wrote", out)
