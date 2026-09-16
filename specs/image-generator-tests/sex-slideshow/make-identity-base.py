#!/usr/bin/env python3
"""
Identity-conditioned base renderer for the sex-slideshow harness.

Renders the step-1 base image with regional IP-Adapter using the multiangle
identity pack (Dean = man on left, Becky = woman on right). Designed for the
side-profile "facing each other" base pose, so it defaults to the profile refs
(profl / profr) that match the curated pack views.

Usage (called from run-sex-slideshow.ps1):
  python make-identity-base.py \
      --dean-ref specs/image-generator-tests/refs/dean/v8/profl.png \
      --becky-ref specs/image-generator-tests/refs/becky/v5/profl.png \
      --dean-mask specs/image-generator-tests/identity-two-character/masks/c6_left.png \
      --becky-mask specs/image-generator-tests/identity-two-character/masks/c6_right.png \
      --prompt "..." --seed 6601 --outdir ... --prefix slide...

Refs are resolved via identity_refs.py when --resolve-refs is passed (recommended).
"""

import argparse
import json
import os
import sys
import time
import urllib.request
import urllib.error

HERE = os.path.dirname(os.path.abspath(__file__))
TESTS_ROOT = os.path.dirname(HERE)
REPO = os.path.dirname(os.path.dirname(TESTS_ROOT))

if TESTS_ROOT not in sys.path:
    sys.path.insert(0, TESTS_ROOT)
import identity_refs as REF  # noqa: E402


def upload_image(comfy_url: str, filepath: str, upload_name: str) -> str:
    """Upload a local image to ComfyUI using proper multipart/form-data."""
    import http.client
    import mimetypes
    import os as _os

    boundary = "----WebKitFormBoundary7MA4YWxkTrZu0gW"
    content_type = f"multipart/form-data; boundary={boundary}"

    with open(filepath, "rb") as f:
        file_data = f.read()

    mime_type = mimetypes.guess_type(filepath)[0] or "image/png"

    body = bytearray()
    body.extend(f"--{boundary}\r\n".encode())
    body.extend(
        f'Content-Disposition: form-data; name="image"; filename="{upload_name}"\r\n'.encode()
    )
    body.extend(b"Content-Type: " + mime_type.encode() + b"\r\n\r\n")
    body.extend(file_data)
    body.extend(b"\r\n")
    body.extend(f"--{boundary}--\r\n".encode())

    parsed = comfy_url.rstrip("/")
    host_port = parsed.replace("http://", "").replace("https://", "")
    path = "/upload/image"

    conn = http.client.HTTPSConnection(host_port, timeout=120) if parsed.startswith("https") else http.client.HTTPConnection(host_port, timeout=120)
    headers = {"Content-Type": content_type}
    conn.request("POST", path, body=bytes(body), headers=headers)
    resp = conn.getresponse()
    result = json.loads(resp.read().decode())
    conn.close()
    return result["name"]


def build_workflow(
    dean_ref: str,
    becky_ref: str,
    dean_mask: str,
    becky_mask: str,
    positive: str,
    negative: str,
    seed: int,
    width: int,
    height: int,
    checkpoint: str,
    steps: int,
    cfg: float,
    sampler: str,
    scheduler: str,
    dean_weight: float = 0.8,
    becky_weight: float = 0.6,
) -> dict:
    """Build the regional IP-Adapter workflow (pattern from identity-two-character c6)."""
    wf = {
        "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": checkpoint}},
        "5": {"class_type": "EmptyLatentImage", "inputs": {"width": width, "height": height, "batch_size": 1}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": positive, "clip": ["4", 1]}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"text": negative, "clip": ["4", 1]}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {"images": ["8", 0], "filename_prefix": "identity-base"}},
        "10": {"class_type": "IPAdapterUnifiedLoader", "inputs": {"model": ["4", 0], "preset": "PLUS FACE (portraits)"}},
        # Load refs (will be overwritten by uploaded names at runtime)
        "11": {"class_type": "LoadImage", "inputs": {"image": os.path.basename(dean_ref)}},
        "12": {"class_type": "LoadImage", "inputs": {"image": os.path.basename(becky_ref)}},
        "13": {"class_type": "LoadImageMask", "inputs": {"image": os.path.basename(dean_mask), "channel": "red"}},
        "14": {"class_type": "LoadImageMask", "inputs": {"image": os.path.basename(becky_mask), "channel": "red"}},
        "20": {
            "class_type": "IPAdapter",
            "inputs": {
                "model": ["10", 0],
                "ipadapter": ["10", 1],
                "image": ["11", 0],
                "weight": dean_weight,
                "weight_type": "standard",
                "start_at": 0.0,
                "end_at": 1.0,
                "attn_mask": ["13", 0],
            },
        },
        "21": {
            "class_type": "IPAdapter",
            "inputs": {
                "model": ["20", 0],
                "ipadapter": ["10", 1],
                "image": ["12", 0],
                "weight": becky_weight,
                "weight_type": "standard",
                "start_at": 0.0,
                "end_at": 1.0,
                "attn_mask": ["14", 0],
            },
        },
        "3": {
            "class_type": "KSampler",
            "inputs": {
                "seed": seed,
                "steps": steps,
                "cfg": cfg,
                "sampler_name": sampler,
                "scheduler": scheduler,
                "denoise": 1.0,
                "model": ["21", 0],
                "positive": ["6", 0],
                "negative": ["7", 0],
                "latent_image": ["5", 0],
            },
        },
    }
    return wf


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--comfy-url", default="http://192.168.0.16:8188")
    parser.add_argument("--dean-ref", help="Path to Dean's profile ref (e.g. .../dean/v8/profr.png for facing-each-other base)")
    parser.add_argument("--becky-ref", help="Path to Becky's profile ref")
    parser.add_argument("--dean-mask", required=True)
    parser.add_argument("--becky-mask", required=True)
    parser.add_argument("--prompt", required=True)
    parser.add_argument("--negative", default="")
    parser.add_argument("--seed", type=int, default=6601)
    parser.add_argument("--width", type=int, default=1216)
    parser.add_argument("--height", type=int, default=832)
    parser.add_argument("--checkpoint", default="bigLust_v16.safetensors")
    parser.add_argument("--steps", type=int, default=30)
    parser.add_argument("--cfg", type=float, default=5.0)
    parser.add_argument("--sampler", default="dpmpp_2m_sde")
    parser.add_argument("--scheduler", default="karras")
    parser.add_argument("--outdir", required=True)
    parser.add_argument("--prefix", default="slide")
    parser.add_argument("--resolve-refs", action="store_true", help="Resolve refs via identity_refs.py instead of literal paths")
    args = parser.parse_args()

    os.makedirs(args.outdir, exist_ok=True)

    dean_ref = args.dean_ref
    becky_ref = args.becky_ref

    if args.resolve_refs:
        # Resolve the active pack versions and use the profile views that match the side-profile "facing each other" base pose.
        # Dean (left) shows his right profile to camera → profr
        # Becky (right) shows her left profile to camera → profl
        dean_ref = REF.resolve_view("dean", "profr") or REF.resolve_view("dean", "front")
        becky_ref = REF.resolve_view("becky", "profl") or REF.resolve_view("becky", "front")
        if not dean_ref or not becky_ref:
            raise RuntimeError("Could not resolve identity refs via identity_refs.py")

    # Upload the three images we need (refs + masks)
    base = args.comfy_url.rstrip("/")
    dean_name = upload_image(base, dean_ref, "dean_ref.png")
    becky_name = upload_image(base, becky_ref, "becky_ref.png")
    dean_mask_name = upload_image(base, args.dean_mask, "dean_mask.png")
    becky_mask_name = upload_image(base, args.becky_mask, "becky_mask.png")

    wf = build_workflow(
        dean_ref=dean_ref,
        becky_ref=becky_ref,
        dean_mask=args.dean_mask,
        becky_mask=args.becky_mask,
        positive=args.prompt,
        negative=args.negative,
        seed=args.seed,
        width=args.width,
        height=args.height,
        checkpoint=args.checkpoint,
        steps=args.steps,
        cfg=args.cfg,
        sampler=args.sampler,
        scheduler=args.scheduler,
    )

    # Patch the LoadImage nodes with the uploaded filenames
    wf["11"]["inputs"]["image"] = dean_name
    wf["12"]["inputs"]["image"] = becky_name
    wf["13"]["inputs"]["image"] = dean_mask_name
    wf["14"]["inputs"]["image"] = becky_mask_name

    payload = {"prompt": wf, "client_id": f"sex-slideshow-base-{int(time.time())}"}
    req = urllib.request.Request(
        f"{base}/prompt",
        data=json.dumps(payload).encode(),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=30) as resp:
        result = json.loads(resp.read().decode())
    if "error" in result:
        raise RuntimeError(f"ComfyUI rejected prompt: {result['error']}")
    prompt_id = result["prompt_id"]
    print(f"queued: {prompt_id}")

    # Poll for completion
    deadline = time.time() + 900
    while time.time() < deadline:
        time.sleep(2)
        with urllib.request.urlopen(f"{base}/history/{prompt_id}", timeout=30) as resp:
            history = json.loads(resp.read().decode())
        if prompt_id in history and history[prompt_id].get("outputs"):
            break
    else:
        raise RuntimeError("Render did not complete in time")

    # Download the saved image
    images = history[prompt_id]["outputs"]["9"]["images"]
    img = images[0]
    view_url = f"{base}/view?filename={img['filename']}&subfolder={img.get('subfolder','')}&type={img.get('type','output')}"
    with urllib.request.urlopen(view_url, timeout=60) as resp:
        data = resp.read()

    out_name = f"{args.prefix}_identity_base_{img['filename']}"
    out_path = os.path.join(args.outdir, out_name)
    with open(out_path, "wb") as f:
        f.write(data)
    print(f"OUTPUT: {out_path}")


if __name__ == "__main__":
    main()
