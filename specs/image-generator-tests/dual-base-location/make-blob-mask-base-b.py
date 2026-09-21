"""Hand-built mask over the gray blob region in the Base B harmonized image.

The blob is a large soft-edged area roughly x in [370, 730], y in [400, 700].
A generous rounded ellipse over that zone, feathered, is enough — the pass must
repaint it as carpet, and carpet surrounds it on all sides.
"""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from pathlib import Path

root = Path(__file__).parent
W, H = 1216, 832
mask = Image.new('L', (W, H), 0)
d = ImageDraw.Draw(mask)
d.ellipse((360, 390, 740, 710), fill=255)
mask = mask.filter(ImageFilter.GaussianBlur(12))
out = root / 'refs/repaint-blob-mask-base-b.png'
mask.save(out)
print('mask:', out)
