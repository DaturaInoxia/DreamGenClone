import os
import cv2
import insightface
from PIL import Image

BASE = "/workspace/comfyui/input/dean"
os.makedirs(BASE, exist_ok=True)

# Convert webp -> png (deterministic, RGB)
for src, dst in [
    ("JA_SNS2_90.webp", "dean_face.png"),
    ("JA_SNS1_08_HQ.webp", "dean_fullbody.png"),
]:
    src_path = os.path.join(BASE, src)
    dst_path = os.path.join(BASE, dst)
    if not os.path.exists(dst_path):
        im = Image.open(src_path).convert("RGB")
        im.save(dst_path)
    im = Image.open(dst_path)
    print(f"{dst}: {im.size[0]}x{im.size[1]}, {os.path.getsize(dst_path)} bytes")

# Face detection with antelopev2 (the PuLID face encoder's detector)
app = insightface.app.FaceAnalysis(
    name="antelopev2",
    root="/workspace/comfyui/models/insightface",
    providers=["CPUExecutionProvider"],
)
app.prepare(ctx_id=0, det_size=(640, 640))

for name in ["dean_face.png", "dean_fullbody.png"]:
    img = cv2.imread(os.path.join(BASE, name))
    if img is None:
        print(f"{name}: FAILED TO READ")
        continue
    faces = app.get(img)
    scores = [round(float(f.det_score), 3) for f in faces]
    print(f"{name}: {len(faces)} face(s) detected, det_score={scores}")
