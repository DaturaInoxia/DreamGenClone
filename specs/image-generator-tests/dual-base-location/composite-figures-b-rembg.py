"""Composite via rembg (U2Net) person segmentation - the reliable approach.

rembg with isnet-general-use / u2net segments the figures from the studio
backdrop regardless of backdrop texture. No thresholds, no flood fills.
"""
from rembg import remove, new_session
from PIL import Image
import numpy as np
import cv2
from pathlib import Path

root = Path(__file__).parent
W, H = 1216, 832

figures = Image.open(root / 'proofs/figures-studio-b-v6-white/img-flux-openpose-serverless_0.png').convert('RGB')
bedroom = Image.open(root / 'proofs/locref-open-floor/img-flux-openpose-serverless_0.png').convert('RGB').resize((W, H), Image.Resampling.LANCZOS)

session = new_session('isnet-general-use')
cut = remove(figures, session=session)  # RGBA

alpha = np.array(cut)[..., 3]
# harden the soft alpha a little: keep the figure, drop faint backdrop halos
alpha = np.where(alpha > 100, alpha, 0).astype(np.uint8)

# keep the two largest components
num, labels, stats, _ = cv2.connectedComponentsWithStats((alpha > 0).astype(np.uint8))
final_a = np.zeros_like(alpha)
kept = 0
for i in range(1, num):
    if stats[i, cv2.CC_STAT_AREA] > 0.01 * W * H:
        final_a[labels == i] = alpha[labels == i]
        kept += 1
print(f'kept {kept} figure components')
alpha = final_a

ys, xs = np.where(alpha > 0)
top, bottom = ys.min(), ys.max()
fig_h = bottom - top

target_h = int(H * 0.72)
scale = target_h / fig_h
new_w, new_h = int(figures.width * scale), int(figures.height * scale)
cut_scaled = cut.resize((new_w, new_h), Image.Resampling.LANCZOS)
alpha_scaled = np.array(cut_scaled)[..., 3]

m_ys, m_xs = np.where(alpha_scaled > 0)
mb, ml, mr = m_ys.max(), m_xs.min(), m_xs.max()

place_cx = W // 2
feet_y = int(H * 0.92)
off_x = place_cx - (ml + mr) // 2
off_y = feet_y - mb

canvas = bedroom.convert('RGBA')
canvas.alpha_composite(cut_scaled, (off_x, off_y))
composite = canvas.convert('RGB')
composite.save(root / 'refs/composite-base-b-v2.png')

# harmonization mask from the placed alpha
hm = np.zeros((H, W), np.uint8)
a_crop = alpha_scaled
hh, ww = a_crop.shape
x0, y0 = max(0, off_x), max(0, off_y)
x1, y1 = min(W, off_x + ww), min(H, off_y + hh)
hm[y0:y1, x0:x1] = a_crop[y0 - off_y:y1 - off_y, x0 - off_x:x1 - off_x]
hm = cv2.dilate((hm > 0).astype(np.uint8) * 255, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (41, 41)))
hm = cv2.GaussianBlur(hm, (0, 0), 6)
Image.fromarray(hm).save(root / 'refs/harmonize-mask-base-b-v2.png')

print('composite:', root / 'refs/composite-base-b-v2.png')
print('mask     :', root / 'refs/harmonize-mask-base-b-v2.png')
