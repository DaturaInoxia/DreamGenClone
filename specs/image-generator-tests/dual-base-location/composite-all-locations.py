"""Build all four composites: 2 poses x 2 locations, using rembg cutouts.

Reads the studio renders from the run dir, composites each pose onto each
location reference, and writes composite + harmonization mask per pair.
"""
from rembg import remove, new_session
from PIL import Image
import numpy as np
import cv2
from pathlib import Path

root = Path(__file__).parent
W, H = 1216, 832
run = root / 'runs/dual-location-20260919-132253'

session = new_session('isnet-general-use')

def cutout(png_path: Path):
    img = Image.open(png_path).convert('RGB')
    cut = remove(img, session=session)
    a = np.array(cut)[..., 3]
    a = np.where(a > 100, a, 0).astype(np.uint8)
    # keep large components only
    num, labels, stats, _ = cv2.connectedComponentsWithStats((a > 0).astype(np.uint8))
    final = np.zeros_like(a)
    for i in range(1, num):
        if stats[i, cv2.CC_STAT_AREA] > 0.005 * W * H:
            final[labels == i] = a[labels == i]
    rgba = np.dstack([np.array(cut)[..., :3], final])
    return Image.fromarray(rgba, 'RGBA')

def composite(rgba: Image.Image, loc_path: Path, out_prefix: str):
    bedroom = Image.open(loc_path).convert('RGB').resize((W, H), Image.Resampling.LANCZOS)
    a = np.array(rgba)[..., 3]
    ys, xs = np.where(a > 0)
    top, bottom = ys.min(), ys.max()
    fig_h = bottom - top
    target_h = int(H * 0.72)
    scale = target_h / fig_h
    new_w, new_h = max(1, int(rgba.width * scale)), max(1, int(rgba.height * scale))
    cut_scaled = rgba.resize((new_w, new_h), Image.Resampling.LANCZOS)
    a_scaled = np.array(cut_scaled)[..., 3]
    m_ys, m_xs = np.where(a_scaled > 0)
    mb, ml, mr = m_ys.max(), m_xs.min(), m_xs.max()
    place_cx = W // 2
    feet_y = int(H * 0.92)
    off_x = place_cx - (ml + mr) // 2
    off_y = feet_y - mb
    canvas = bedroom.convert('RGBA')
    canvas.alpha_composite(cut_scaled, (off_x, off_y))
    out_img = canvas.convert('RGB')
    out_img.save(root / f'refs/{out_prefix}.png')

    hm = np.zeros((H, W), np.uint8)
    hh, ww = a_scaled.shape
    x0, y0 = max(0, off_x), max(0, off_y)
    x1, y1 = min(W, off_x + ww), min(H, off_y + hh)
    hm[y0:y1, x0:x1] = a_scaled[y0 - off_y:y1 - off_y, x0 - off_x:x1 - off_x]
    hm = cv2.dilate((hm > 0).astype(np.uint8) * 255, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (41, 41)))
    hm = cv2.GaussianBlur(hm, (0, 0), 6)
    Image.fromarray(hm).save(root / f'refs/{out_prefix}-mask.png')
    print(f'{out_prefix}: composite + mask')

pose_a_rgba = cutout(run / '03-studio-a/img-flux-openpose-serverless_0.png')
pose_b_rgba = cutout(run / '04-studio-b/img-flux-openpose-serverless_0.png')

for loc in ('bedroom', 'outdoors'):
    loc_path = run / 'refs' / f'locref-{loc}.png'
    composite(pose_a_rgba, loc_path, f'comp-{loc}-a')
    composite(pose_b_rgba, loc_path, f'comp-{loc}-b')
print('all composites done')
