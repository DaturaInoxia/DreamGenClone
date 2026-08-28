#!/usr/bin/env python3
"""
IPAdapterFaceID v2 probe runner (regional, two characters).

Part of the `identity-two-character` test suite. Tests the FaceID PLUS V2
alternative mechanism (recorded FAIL on 2026-08-26: different face per angle,
did not match the PLUS FACE baseline). Same parameterization as run_matrix.py.

Usage:
  python run_faceid_probe.py --cell c2 --seed 1001 --strength 0.8 --lora 0.6 --provider CPU
  python run_faceid_probe.py --cell c1 --seed 1001 --dump-json
"""
import argparse, json, os, sys, time, urllib.request, urllib.parse, uuid

HERE = os.path.dirname(os.path.abspath(__file__))
SUITE = os.path.dirname(HERE)
DEFAULT_OUT = os.path.join(os.environ.get("DG_TMP", r"d:\src\DreamGenClone\artifacts\tmp"),
                           "two-character-proof", "outputs-faceid")
DEFAULT_MASKS = os.path.join(SUITE, "masks")
DEFAULT_REF_DIR = os.path.join(SUITE, "refs")

UA = "Mozilla/5.0 (compatible; DreamGenClone/1.0)"

NEGATIVE = ("deformed, bad anatomy, extra limbs, extra legs, four legs, fused legs, extra fingers, "
            "extra arms, missing limbs, malformed hands, malformed feet, misplaced genitals, "
            "blurry genitals, featureless genitals, censored, cartoon, anime, illustration, "
            "painting, sketch, watermark, text, low quality, oversaturated, plastic skin")

CELLS = {
    "c1": ("c1_left.png", "c1_right.png",
           "Photorealistic 35mm shot of two people standing side by side facing the camera, "
           "Dean on the left, Becky on the right, full body, casual summer clothes at a campground"),
    "c2": ("c2_left.png", "c2_right.png",
           "Photorealistic 35mm shot of Dean and Becky facing each other closely, "
           "Dean on the left, Becky on the right, full body, campground"),
    "c3": ("c3_left.png", "c3_right.png",
           "Photorealistic 35mm shot of Dean and Becky embracing, Dean on the left, Becky on the right, "
           "full body, campground"),
    "c4": ("c4_top.png", "c4_bottom.png",
           "Photorealistic 35mm shot of Dean standing behind, Becky seated in front, "
           "Dean upper, Becky lower, full body, campground"),
    "c5": ("c5_left.png", "c5_right.png",
           "Photorealistic 35mm shot of Dean behind, Becky in front, two-shot, "
           "Dean on the left, Becky on the right, full body, campground"),
    "c6": ("c6_left.png", "c6_right.png",
           "Photorealistic 35mm side profile two-shot of Dean on the left and Becky on the right, "
           "both facing right in profile, full body, campground"),
}


def find_ref(ref_dir, stem):
    for ext in (".png", ".jpg", ".jpeg", ".webp"):
        p = os.path.join(ref_dir, stem + ext)
        if os.path.exists(p):
            return p
    return None


def upload_image(base, filename, local_path):
    boundary = "----dg" + uuid.uuid4().hex
    with open(local_path, "rb") as f:
        data = f.read()
    ctype = "image/jpeg" if filename.lower().endswith(".jpg") else "image/png"
    body = (f"--{boundary}\r\n"
            f'Content-Disposition: form-data; name="image"; filename="{filename}"\r\n'
            f"Content-Type: {ctype}\r\n\r\n").encode() + data + f"\r\n--{boundary}--\r\n".encode()
    req = urllib.request.Request(base + "/upload/image", data=body,
                                 headers={"Content-Type": f"multipart/form-data; boundary={boundary}",
                                          "User-Agent": UA})
    with urllib.request.urlopen(req, timeout=120) as r:
        print("uploaded", filename, r.read().decode()[:100])


def build_workflow(cell, seed, dean_ref, becky_ref, masks_dir,
                   checkpoint="juggernautXL_ragnarok.safetensors",
                   strength=0.8, lora=0.6, provider="CPU"):
    left_mask, right_mask, prompt = CELLS[cell]
    return {
        "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": checkpoint}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["4", 1]}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["4", 1]}},
        "5": {"class_type": "EmptyLatentImage", "inputs": {"width": 1024, "height": 1024, "batch_size": 1}},
        "30": {"class_type": "IPAdapterUnifiedLoaderFaceID",
               "inputs": {"model": ["4", 0], "preset": "FACEID PLUS V2",
                          "lora_strength": lora, "provider": provider}},
        "11": {"class_type": "LoadImage", "inputs": {"image": os.path.basename(dean_ref)}},
        "12": {"class_type": "LoadImage", "inputs": {"image": os.path.basename(becky_ref)}},
        "13": {"class_type": "LoadImageMask", "inputs": {"image": left_mask, "channel": "red"}},
        "14": {"class_type": "LoadImageMask", "inputs": {"image": right_mask, "channel": "red"}},
        "20": {"class_type": "IPAdapterFaceID",
               "inputs": {"model": ["30", 0], "ipadapter": ["30", 1], "image": ["11", 0],
                          "weight": strength, "weight_faceidv2": 1.0, "weight_type": "linear",
                          "combine_embeds": "concat", "start_at": 0.0, "end_at": 1.0,
                          "embeds_scaling": "V only", "attn_mask": ["13", 0]}},
        "21": {"class_type": "IPAdapterFaceID",
               "inputs": {"model": ["20", 0], "ipadapter": ["30", 1], "image": ["12", 0],
                          "weight": strength, "weight_faceidv2": 1.0, "weight_type": "linear",
                          "combine_embeds": "concat", "start_at": 0.0, "end_at": 1.0,
                          "embeds_scaling": "V only", "attn_mask": ["14", 0]}},
        "3": {"class_type": "KSampler",
              "inputs": {"model": ["21", 0], "positive": ["6", 0], "negative": ["7", 0],
                         "latent_image": ["5", 0], "seed": seed, "steps": 30, "cfg": 5.0,
                         "sampler_name": "dpmpp_2m_sde", "scheduler": "karras", "denoise": 1.0}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {"filename_prefix": f"faceid_{cell}", "images": ["8", 0]}},
    }


def post(base, path, body):
    req = urllib.request.Request(base + path, data=json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json", "User-Agent": UA})
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            return json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        detail = e.read().decode() if e.readable() else ""
        print("HTTP", e.code, detail[:5000])
        raise


def get_json(base, path):
    req = urllib.request.Request(base + path, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.loads(resp.read().decode())


def run_one(base, cell, seed, dean_ref, becky_ref, masks_dir, out_dir, strength, lora, provider):
    os.makedirs(out_dir, exist_ok=True)
    workflow = build_workflow(cell, seed, dean_ref, becky_ref, masks_dir,
                              strength=strength, lora=lora, provider=provider)
    for local in (dean_ref, becky_ref):
        upload_image(base, os.path.basename(local), local)
    left_mask, right_mask, _ = CELLS[cell]
    for mask in (left_mask, right_mask):
        upload_image(base, mask, os.path.join(masks_dir, mask))

    payload = {"prompt": workflow, "client_id": f"faceid-{cell}-{seed}"}
    resp = post(base, "/prompt", payload)
    if resp.get("node_errors"):
        print("NODE_ERRORS:", json.dumps(resp["node_errors"], indent=2))
        sys.exit(1)
    prompt_id = resp.get("prompt_id")
    print("prompt_id:", prompt_id)

    deadline = time.time() + 420
    while time.time() < deadline:
        time.sleep(5)
        try:
            hist = get_json(base, f"/history/{prompt_id}")
        except Exception:
            continue
        if prompt_id in hist:
            entry = hist[prompt_id]
            st = entry.get("status", {}).get("status_str")
            if st != "success":
                print("STATUS:", st, json.dumps(entry.get("status", {}))[:600])
                sys.exit(1)
            for node_out in entry.get("outputs", {}).values():
                for img in node_out.get("images", []):
                    fn = img["filename"]
                    url = f"{base}/view?filename={urllib.parse.quote(fn)}"
                    if img.get("subfolder"): url += f"&subfolder={urllib.parse.quote(img['subfolder'])}"
                    if img.get("type"): url += f"&type={urllib.parse.quote(img['type'])}"
                    local = os.path.join(out_dir, f"{cell}_s{seed}.png")
                    dl = urllib.request.Request(url, headers={"User-Agent": UA})
                    with urllib.request.urlopen(dl, timeout=120) as r:
                        with open(local, "wb") as f:
                            f.write(r.read())
                    print("SAVED:", local)
            return
    print("TIMEOUT")
    sys.exit(1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="https://7i2mutjmry5tkt-3000.proxy.runpod.net")
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--masks", default=DEFAULT_MASKS)
    ap.add_argument("--ref-dir", default=DEFAULT_REF_DIR)
    ap.add_argument("--cell", choices=list(CELLS))
    ap.add_argument("--seed", type=int, default=1001)
    ap.add_argument("--strength", type=float, default=0.8)
    ap.add_argument("--lora", type=float, default=0.6)
    ap.add_argument("--provider", default="CPU")
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--seeds", nargs="+", type=int, default=[1001])
    ap.add_argument("--dump-json", action="store_true")
    args = ap.parse_args()

    dean_ref = find_ref(args.ref_dir, "dean_face")
    becky_ref = find_ref(args.ref_dir, "becky_face")
    if dean_ref is None or becky_ref is None:
        print(f"MISSING refs in {args.ref_dir}: dean_face/becky_face required")
        sys.exit(1)

    if args.dump_json:
        wf = build_workflow(args.cell, args.seed, dean_ref, becky_ref, args.masks,
                            strength=args.strength, lora=args.lora, provider=args.provider)
        print(json.dumps({"prompt": wf, "client_id": f"faceid-{args.cell}-{args.seed}"}, indent=2))
        return

    cells = list(CELLS) if args.all else [args.cell]
    if args.cell is None and not args.all:
        print("--cell or --all required")
        sys.exit(1)
    for cell in cells:
        for seed in args.seeds:
            run_one(args.base, cell, seed, dean_ref, becky_ref, args.masks, args.out,
                    args.strength, args.lora, args.provider)


if __name__ == "__main__":
    main()
