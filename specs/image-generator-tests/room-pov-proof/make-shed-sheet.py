"""Contact sheet for the real-photo (shed) test, including the daylight fix and the bench views."""
import os, glob
import numpy as np
from PIL import Image, ImageDraw, ImageFont

A = r"D:\src\DreamGenClone\DreamGenClone.Web\data\scene-images\assets"
S = r"D:\src\DreamGenClone\artifacts\tmp\room-proof\shed"
BACK = os.path.join(A, "7fa647c55d1d44ccac159e1225501943.png")
FRONT = os.path.join(A, "1fb98a0edf4b4bb7a744d187519841f8.png")
TH = 330

GREEN, RED, GREY, BLUE = (25, 90, 45), (120, 35, 30), (45, 45, 50), (30, 50, 90)


def load(p):
    return np.asarray(Image.open(p).convert("RGB"))


def thumb(im):
    s = TH / im.shape[0]
    return np.asarray(Image.fromarray(im).resize((max(1, int(im.shape[1] * s)), TH), Image.LANCZOS))


def label(im, text, colour=GREY):
    im = Image.fromarray(np.ascontiguousarray(im.astype(np.uint8)))
    d = ImageDraw.Draw(im)
    try:
        f = ImageFont.truetype("arial.ttf", 15)
    except Exception:
        f = ImageFont.load_default()
    lines = text.split("\n")
    w = max(d.textbbox((0, 0), ln, font=f)[2] for ln in lines) + 12
    h = sum(d.textbbox((0, 0), ln, font=f)[3] + 5 for ln in lines) + 10
    d.rectangle([0, 0, w, h], fill=colour)
    y = 5
    for ln in lines:
        d.text((6, y), ln, fill=(255, 255, 255), font=f)
        y += d.textbbox((0, 0), ln, font=f)[3] + 5
    return np.asarray(im)


def first(sub):
    hits = sorted(glob.glob(os.path.join(S, sub, "*.png")))
    return load(hits[0]) if hits else None


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


row1 = [label(thumb(load(BACK)), "REAL back photo\nbright 13.6%  B-R -15.5", GREY),
        label(thumb(first("I1_back_plus_depth")), "I1: my prompt said\n'cold DIM BLUE light'\nwindows gone: bright 0.11%\nand it went BLUE (B-R +9.0)", RED),
        label(thumb(first("I7_depth_with_light")), "I7: same depth, daylight\nput back in the prompt\nbright 4.3%, p95 89->131\nB-R -11.0 (cast fixed)", GREEN),
        label(thumb(load(FRONT)), "REAL front photo\nbright 10.0%  B-R -17.9", GREY)]

row2 = [label(thumb(first("W1_bench_to_entrance")), "W1: FROM THE WORKBENCH\nlooking toward the entrance\nwanted +0.45", GREEN),
        label(thumb(first("W2_bench_to_far_end")), "W2: FROM THE WORKBENCH\nthe other way (far end)\nwanted +0.30, bright 17.6%", GREEN),
        label(thumb(first("I2_back_no_depth")), "I2 CONTROL: no depth\nCOPIED the given view", RED),
        label(thumb(first("I3_back_plus_frontphoto")), "I3: the WANTED PHOTO\nas a reference - copied anyway", RED)]

out = os.path.join(S, "shed-proof-sheet.png")
Image.fromarray(vstack([hstack(row1), hstack(row2)])).save(out)
print("wrote", out)
