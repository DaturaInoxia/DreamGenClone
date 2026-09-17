from PIL import Image, ImageDraw, ImageFilter
from pathlib import Path

out = Path(__file__).parent / 'refs' / 'bedroom-characters-inpaint-mask.png'
mask = Image.new('L', (1216, 832), 0)
d = ImageDraw.Draw(mask)
# Tight, separate human-shaped regions. Keep the bed and floor outside the mask.
def person(cx, top, bottom, shoulder, hip, foot):
	d.ellipse((cx-42, top, cx+42, top+84), fill=255)
	d.polygon([(cx-52, top+72), (cx+52, top+72), (cx+66, hip-35),
			   (cx+34, hip+10), (cx+25, bottom-105), (cx+42, bottom),
			   (cx+8, bottom), (cx, bottom-100), (cx-12, bottom),
			   (cx-48, bottom), (cx-30, bottom-105), (cx-38, hip+10),
			   (cx-66, hip-35)], fill=255)

person(470, 105, 770, 0, 520, 770)
person(735, 115, 770, 0, 525, 770)
# A small feather reduces hard seams without masking the bedroom broadly.
mask = mask.filter(ImageFilter.GaussianBlur(4))
mask.save(out)
print(out)
