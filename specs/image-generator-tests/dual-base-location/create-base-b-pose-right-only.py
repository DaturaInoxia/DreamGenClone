"""Base B skeleton, attempt 2: mirror the ENTIRE right strip of the canvas.

The right figure (body + head/face keypoints) lives entirely in the right half
of the Base A canvas, separated from the left figure by a black gap. Find the
gap column (between left figure's right edge and right figure's left edge),
mirror everything right of the gap midpoint, and paste back. This guarantees
the face is mirrored together with the body — the previous attempt missed the
face because component analysis split the face from the body.
"""
import cv2
import numpy as np
from PIL import Image
from pathlib import Path

root = Path(__file__).parent
W = 1216

src = np.array(Image.open(root / 'refs/pose-face-to-face-1216x832.png').convert('RGB'))
gray = cv2.cvtColor(src, cv2.COLOR_RGB2GRAY)

# column occupancy with a LOW threshold (dark blue neck is dim)
col_has_ink = (gray > 8).any(axis=0)

# find the gap between the two figures
ink_cols = np.where(col_has_ink)[0]
left_max = ink_cols[ink_cols < W // 2].max()          # left figure's right edge
right_min = ink_cols[ink_cols >= W // 2].min()        # right figure's left edge
split = (left_max + right_min) // 2
print(f'left figure ends x={left_max}, right figure starts x={right_min}, split at x={split}')

out = src.copy()
strip = src[:, split:].copy()
mirrored = strip[:, ::-1].copy()

# After mirroring, translate the mirrored figure so a clear gap (~80px) opens
# between the figures. The mirrored ink starts at strip col m_left; placing it
# at split + 1 + GAP guarantees the figures never touch, so the studio render
# has two cleanly separated people and the composite mask stays clean.
GAP = 80
mgray = cv2.cvtColor(mirrored, cv2.COLOR_RGB2GRAY)
m_ink_cols = np.where((mgray > 8).any(axis=0))[0]
m_left = m_ink_cols.min()
dx = m_left - (1 + GAP)
shifted = np.zeros_like(mirrored)
if dx < mirrored.shape[1]:
    shifted[:, :mirrored.shape[1] - dx] = mirrored[:, dx:]
out[:, split:] = shifted

dst = root / 'refs/pose-base-b-1216x832.png'
Image.fromarray(out).save(dst)
print(f'mirrored ink started at strip col {m_left}, shifted left by {dx} (gap={GAP}px)')
print('pose:', dst)
