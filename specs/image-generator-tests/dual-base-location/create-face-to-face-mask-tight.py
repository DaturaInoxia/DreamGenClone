import cv2
import numpy as np
from pathlib import Path

root = Path(__file__).parent
pose_path = root / 'refs' / 'pose-face-to-face-1216x832.png'
out_path = root / 'refs' / 'mask-face-to-face-tight-1216x832.png'
pose = cv2.imread(str(pose_path), cv2.IMREAD_COLOR)
if pose is None:
    raise FileNotFoundError(pose_path)

# Skeleton pixels only, then keep the two figures as SEPARATE components.
gray = cv2.cvtColor(pose, cv2.COLOR_BGR2GRAY)
seed = (gray > 20).astype(np.uint8) * 255
seed = cv2.morphologyEx(seed, cv2.MORPH_CLOSE, np.ones((15, 15), np.uint8))

num, labels = cv2.connectedComponents(seed)
if num < 3:
    raise RuntimeError(f'expected 2 separate figures, got {num - 1} components')

# Tight dilation per figure: enough to cover a body, little enough to spare the room.
kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (35, 35))
mask = np.zeros_like(seed)
for i in range(1, num):
    comp = ((labels == i).astype(np.uint8)) * 255
    mask = cv2.bitwise_or(mask, cv2.dilate(comp, kernel, iterations=1))

mask = cv2.GaussianBlur(mask, (0, 0), 3)
cv2.imwrite(str(out_path), mask)
print(out_path)
