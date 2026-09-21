"""Shift and scale the canvas-matched pose + its derived mask so the two
figures stand on the lower-center carpet (below window level, plain wall
behind), keeping the room's identity features (window, bed, lamps) outside the
masked region.
"""
import cv2
import numpy as np
from PIL import Image
from pathlib import Path

root = Path(__file__).parent
W, H = 1216, 832

# --- pose ---
pose = Image.open(root / 'refs/pose-face-to-face-1216x832.png').convert('RGB')
# Scale up slightly and push down so feet land on the lower carpet; figures
# occupy the lower ~70% of frame, wall/window zone stays above their heads.
scale = 1.12
sw, sh = int(W * scale), int(H * scale)
pose_big = pose.resize((sw, sh), Image.Resampling.BICUBIC)
left = (W - sw) // 2
top = H - sh          # anchor to bottom: heads stay low, feet near bottom edge
canvas = Image.new('RGB', (W, H), (0, 0, 0))
canvas.paste(pose_big, (left, top))
pose_out = root / 'refs/pose-face-to-face-low-1216x832.png'
canvas.save(pose_out)

# --- mask (same transform on the tight mask) ---
mask = Image.open(root / 'refs/mask-face-to-face-tight-1216x832.png').convert('L')
mask_big = mask.resize((sw, sh), Image.Resampling.BICUBIC)
mcanvas = Image.new('L', (W, H), 0)
mcanvas.paste(mask_big, (left, top))
mask_out = root / 'refs/mask-face-to-face-low-1216x832.png'
mcanvas.save(mask_out)

print('pose:', pose_out)
print('mask:', mask_out)
