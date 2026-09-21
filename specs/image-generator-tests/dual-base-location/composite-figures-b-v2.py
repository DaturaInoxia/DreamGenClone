"""Composite v2 for Base B: extract figures but EXCLUDE the backdrop blob.

The v4 studio figures' legs touch, so the largest connected component includes
backdrop between/below them. Fix: keep the two largest components whose aspect
ratio is human-like (tall), and drop wide/short components (backdrop patches).
Then also cut anything below the figures' feet line.
"""
import cv2
import numpy as np
from PIL import Image
from pathlib import Path

root = Path(__file__).parent
W, H = 1216, 832

figures = np.array(Image.open(root / 'proofs/figures-studio-b-v6-white/img-flux-openpose-serverless_0.png').convert('RGB'))
bedroom = Image.open(root / 'proofs/locref-open-floor/img-flux-openpose-serverless_0.png').convert('RGB').resize((W, H), Image.Resampling.LANCZOS)

# White backdrop: flood-fill from the borders across near-white pixels.
# Everything reachable from the edges through near-white is backdrop; the
# figures are interior islands. Texture dips (darker than the band) become
# small interior islands which are removed by the height filter afterwards.
gray_img = cv2.cvtColor(figures, cv2.COLOR_RGB2GRAY)
near_white = (gray_img >= 225).astype(np.uint8)
h_, w_ = near_white.shape
ffmask = np.zeros((h_ + 2, w_ + 2), np.uint8)
floodable = near_white.copy()
for sx in range(0, w_, 40):
    for sy in (0, h_ - 1):
        if floodable[sy, sx] == 1:
            cv2.floodFill(floodable, ffmask, (sx, sy), 2)
for sy in range(0, h_, 40):
    for sx in (0, w_ - 1):
        if floodable[sy, sx] == 1:
            cv2.floodFill(floodable, ffmask, (sx, sy), 2)
backdrop = (floodable == 2).astype(np.uint8) * 255
comp_mask = cv2.bitwise_not(backdrop)
comp_mask = cv2.morphologyEx(comp_mask, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
comp_mask = cv2.morphologyEx(comp_mask, cv2.MORPH_CLOSE, np.ones((15, 15), np.uint8))

num, labels, stats, _ = cv2.connectedComponentsWithStats(comp_mask)
final = np.zeros_like(comp_mask)
kept = 0
for i in range(1, num):
    x, y, w2_, h2_, area = stats[i]
    if h2_ > H * 0.4:
        final[labels == i] = 255
        kept += 1
print(f'kept {kept} figure components')
comp_mask = final
# The figures may merge into one component where their edges touch; accept 1
# merged component as long as it spans most of the canvas width (both figures).
xs_ink = np.where(comp_mask.max(axis=0) > 0)[0]
span = xs_ink.max() - xs_ink.min() if len(xs_ink) else 0
if kept == 0 or span < W * 0.3:
    raise RuntimeError(f'figure mask invalid: kept={kept}, span={span}')

# close small internal gaps only (no bridging between figures)
comp_mask = cv2.morphologyEx(comp_mask, cv2.MORPH_CLOSE, np.ones((15, 15), np.uint8))

ys, xs = np.where(comp_mask > 0)
top, bottom = ys.min(), ys.max()
fig_h = bottom - top

target_h = int(H * 0.72)
scale = target_h / fig_h
figs_scaled = cv2.resize(figures, None, fx=scale, fy=scale, interpolation=cv2.INTER_LANCZOS4)
mask_scaled = cv2.resize(comp_mask, None, fx=scale, fy=scale, interpolation=cv2.INTER_LINEAR)

m_ys, m_xs = np.where(mask_scaled > 0)
mb, ml, mr = m_ys.max(), m_xs.min(), m_xs.max()

place_cx = W // 2
feet_y = int(H * 0.92)
off_x = place_cx - (ml + mr) // 2
off_y = feet_y - mb

canvas = np.array(bedroom)
alpha = np.zeros((H, W), np.float32)
src = np.zeros((H, W, 3), np.float32)

h2, w2 = mask_scaled.shape
x0, y0 = max(0, off_x), max(0, off_y)
x1, y1 = min(W, off_x + w2), min(H, off_y + h2)
sx0, sy0 = x0 - off_x, y0 - off_y
sx1, sy1 = sx0 + (x1 - x0), sy0 + (y1 - y0)

roi_alpha = mask_scaled[sy0:sy1, sx0:sx1].astype(np.float32) / 255.0
alpha[y0:y1, x0:x1] = roi_alpha
src[y0:y1, x0:x1] = figs_scaled[sy0:sy1, sx0:sx1].astype(np.float32)

alpha = cv2.GaussianBlur(alpha, (0, 0), 2.5)
composite = (src * alpha[..., None] + canvas.astype(np.float32) * (1 - alpha[..., None])).astype(np.uint8)

Image.fromarray(composite).save(root / 'refs/composite-base-b-v2.png')

hmask = cv2.dilate((alpha > 0.02).astype(np.uint8) * 255, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (41, 41)))
hmask = cv2.GaussianBlur(hmask, (0, 0), 6)
Image.fromarray(hmask).save(root / 'refs/harmonize-mask-base-b-v2.png')

print('composite:', root / 'refs/composite-base-b-v2.png')
print('mask     :', root / 'refs/harmonize-mask-base-b-v2.png')
