from PIL import Image, ImageFilter, ImageDraw
from pathlib import Path

root = Path(__file__).parent
refs = root / 'refs'
source = refs / 'step02-pose-source.png'
if not source.exists():
    source = Path('specs/image-generator-tests/dual-base-location/proofs/dwpose-step02-facing-each-other/pose-dwpose-serverless-comfy_0.png')
pose = Image.open(source).convert('RGB').resize((1216, 832), Image.Resampling.NEAREST)
pose.save(refs / 'pose-step02-normalized-1216x832.png')

mask = Image.new('L', (1216, 832), 0)
d = ImageDraw.Draw(mask)
# Aligned to the extracted two-person pose, preserving head-to-feet visibility.
for cx in (475, 745):
    d.ellipse((cx-52, 95, cx+52, 205), fill=255)
    d.rounded_rectangle((cx-78, 180, cx+78, 520), radius=35, fill=255)
    d.polygon([(cx-70, 470), (cx+70, 470), (cx+48, 700), (cx+35, 790),
               (cx+5, 790), (cx, 700), (cx-5, 790), (cx-35, 790),
               (cx-48, 700)], fill=255)
mask = mask.filter(ImageFilter.GaussianBlur(3))
mask.save(refs / 'bedroom-characters-inpaint-mask-normalized.png')
print('pose:', refs / 'pose-step02-normalized-1216x832.png')
print('mask:', refs / 'bedroom-characters-inpaint-mask-normalized.png')
