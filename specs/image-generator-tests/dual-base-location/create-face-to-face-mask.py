import cv2
import numpy as np
from pathlib import Path

root = Path(__file__).parent
pose_path = root / 'refs' / 'pose-face-to-face-1216x832.png'
out_path = root / 'refs' / 'mask-face-to-face-1216x832.png'
pose = cv2.imread(str(pose_path), cv2.IMREAD_COLOR)
if pose is None:
    raise FileNotFoundError(pose_path)
# DWPose/OpenPose canvas is black; retain colored skeleton pixels.
gray = cv2.cvtColor(pose, cv2.COLOR_BGR2GRAY)
seed = (gray > 20).astype(np.uint8) * 255
# Connect joints and expand limbs into person-shaped inpaint regions.
seed = cv2.morphologyEx(seed, cv2.MORPH_CLOSE, np.ones((31, 31), np.uint8))
mask = cv2.dilate(seed, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (65, 65)), iterations=1)
mask = cv2.GaussianBlur(mask, (0, 0), 4)
cv2.imwrite(str(out_path), mask)
print(out_path)
