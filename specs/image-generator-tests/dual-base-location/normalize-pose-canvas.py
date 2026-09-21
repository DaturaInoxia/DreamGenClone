"""Normalize a DWPose canvas to the exact FLUX generation canvas (1216x832).

Stretch-to-fill on purpose: the OpenPose skeleton must occupy the same relative
positions in the control image as the bodies should occupy in the render, so the
XlabsSampler bicubic resize becomes a 1:1 mapping instead of an aspect-corrected
fit that leaves letterboxing for FLUX to crop into.
"""
from PIL import Image
from pathlib import Path

root = Path(__file__).parent
source = root / 'proofs/dwpose-step02-facing-each-other/pose-dwpose-serverless-comfy_0.png'
out = root / 'refs/pose-face-to-face-1216x832.png'

pose = Image.open(source).convert('RGB')
pose = pose.resize((1216, 832), Image.Resampling.BICUBIC)
out.parent.mkdir(parents=True, exist_ok=True)
pose.save(out)
print('pose:', out)
