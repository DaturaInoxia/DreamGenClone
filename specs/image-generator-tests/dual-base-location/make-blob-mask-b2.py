"""Build a mask over the gray blob left of the man in the Base B composite.

The blob spans roughly x in [300, 620], y in [370, 690]. A generous feathered
ellipse over that zone; the pass repaints it as carpet (denoise 0.8).
"""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from pathlib import Path

root = Path(__file__).parent
W, H = 1216, 832
mask = Image.new('L', (W, H), 0)
d = ImageDraw.Draw(mask)
d.ellipse((300, 360, 760, 720), fill=255)
mask = mask.filter(ImageFilter.GaussianBlur(12))
out = root / 'refs/blob-mask-base-b.png'
mask.save(out)
print('mask:', out)
