"""Composite the studio figures onto the open-floor bedroom reference.

Steps:
1. Load studio figures (plain gray backdrop) and bedroom reference (1216x832).
2. Build a figure mask by removing the near-uniform backdrop (color distance
   from backdrop median + flood-fill from borders), then clean with morphology.
3. Scale figures so their feet land on the bedroom's open carpet, centered
   slightly below the middle of the frame.
4. Paste with feathered alpha; save composite + harmonization mask (feathered
   figure alpha dilated, for a light masked KSampler pass).
"""
import cv2
import numpy as np
from PIL import Image, ImageFilter
from pathlib import Path

root = Path(__file__).parent
W, H = 1216, 832

figures = np.array(Image.open(root / 'proofs/figures-studio-b-v4/img-flux-openpose-serverless_0.png').convert('RGB'))
bedroom = Image.open(root / 'proofs/locref-open-floor/img-flux-openpose-serverless_0.png').convert('RGB').resize((W, H), Image.Resampling.LANCZOS)

# --- figure mask: backdrop is a flat light gray; distance from its median color ---
bg_color = np.median(figures[::20, ::20].reshape(-1, 3), axis=0)
dist = np.linalg.norm(figures.astype(np.float32) - bg_color, axis=2)
raw = (dist > 55).astype(np.uint8) * 255

# remove small specks only; NO large close (it bridges backdrop gradient noise)
raw = cv2.morphologyEx(raw, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))

# keep the two largest components only (kills backdrop gradient noise)
num, labels, stats, _ = cv2.connectedComponentsWithStats(raw)
comp_mask = np.zeros_like(raw)
areas = sorted(range(1, num), key=lambda i: stats[i, cv2.CC_STAT_AREA], reverse=True)[:2]
for i in areas:
    comp_mask[labels == i] = 255

# fill internal holes bounded by strong figure edges only
comp_mask = cv2.morphologyEx(comp_mask, cv2.MORPH_CLOSE, np.ones((15, 15), np.uint8))

ys, xs = np.where(comp_mask > 0)
top, bottom, left, right = ys.min(), ys.max(), xs.min(), xs.max()
fig_h = bottom - top

# --- placement: feet on the open carpet, center of frame ---
target_h = int(H * 0.72)          # figures occupy ~72% of frame height
scale = target_h / fig_h
figs_scaled = cv2.resize(figures, None, fx=scale, fy=scale, interpolation=cv2.INTER_LANCZOS4)
mask_scaled = cv2.resize(comp_mask, None, fx=scale, fy=scale, interpolation=cv2.INTER_LINEAR)

m_ys, m_xs = np.where(mask_scaled > 0)
mt, mb, ml, mr = m_ys.min(), m_ys.max(), m_xs.min(), m_xs.max()

# place figure center-x at frame center; feet (mask bottom) at 92% of frame height
# keep full body visible: target height accounts for feet margin inside frame
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

# feather the alpha edge for blending
alpha = cv2.GaussianBlur(alpha, (0, 0), 2.5)
composite = (src * alpha[..., None] + canvas.astype(np.float32) * (1 - alpha[..., None])).astype(np.uint8)

Image.fromarray(composite).save(root / 'refs/composite-base-b.png')

# harmonization mask: dilated, feathered figure region
hmask = cv2.dilate((alpha > 0.02).astype(np.uint8) * 255, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (41, 41)))
hmask = cv2.GaussianBlur(hmask, (0, 0), 6)
Image.fromarray(hmask).save(root / 'refs/harmonize-mask-base-b.png')

print('composite:', root / 'refs/composite-base-b.png')
print('mask     :', root / 'refs/harmonize-mask-base-b.png')
