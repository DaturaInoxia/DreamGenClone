"""Create the Base B pose skeleton: mirror the Base A canvas horizontally.

Base A: two figures facing each other (left figure faces right, right figure
faces left). Mirroring the full canvas flips both directions: now both face
right, side-by-side, not touching - exactly the Base B requirement.
"""
from PIL import Image
from pathlib import Path

root = Path(__file__).parent
src = Image.open(root / 'refs/pose-face-to-face-1216x832.png').convert('RGB')
out = src.transpose(Image.FLIP_LEFT_RIGHT)
dst = root / 'refs/pose-base-b-1216x832.png'
out.save(dst)
print('pose:', dst)
