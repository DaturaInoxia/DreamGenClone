#!/usr/bin/env python3
"""
Multi-angle reference proof runner (Option 1).

Part of the `identity-two-character` test suite. Each cell conditions with the
reference photo whose angle matches the target head angle (a 3/4 ref when the
pose turns the head, etc.), so the angled cells (C2/C3) can hold identity too.

Fully parameterized: --base pod URL, --ref-dir (multiangle refs), --masks, --out,
plus per-actor strength (--strength Dean/A, --strength-b Becky/B).

Usage:
  python run_multiangle.py --list
  python run_multiangle.py --cell c2 --seed 1001 --strength 0.8 --strength-b 0.6
  python run_multiangle.py --cell c3 --seed 1002 --dump-json
"""
import argparse, json, os, sys, time, urllib.request, urllib.parse, uuid

HERE = os.path.dirname(os.path.abspath(__file__))
SUITE = os.path.dirname(HERE)
DEFAULT_OUT = os.path.join(os.environ.get("DG_TMP", r"d:\src\DreamGenClone\artifacts\tmp"),
                           "two-character-proof", "outputs-multiangle")
DEFAULT_MASKS = os.path.join(SUITE, "masks")
DEFAULT_REF_DIR = os.path.join(SUITE, "refs", "multiangle")

UA = "Mozilla/5.0 (compatible; DreamGenClone/1.0)"

NEGATIVE = ("deformed, bad anatomy, extra limbs, extra legs, four legs, fused legs, extra fingers, "
            "extra arms, missing limbs, malformed hands, malformed feet, misplaced genitals, "
            "blurry genitals, featureless genitals, censored, cartoon, anime, illustration, "
            "painting, sketch, watermark, text, low quality, oversaturated, plastic skin")

FACES = [
    "dean_front", "dean_34l", "dean_34r", "dean_profl", "dean_profr",
    "becky_front", "becky_34l", "becky_34r", "becky_profl", "becky_profr",
]

# cell -> (dean_ref_stem, becky_ref_stem, prompt). Angle chosen to match the target head pose.
CELLS = {
    "c1": ("dean_front", "becky_front",
           "Photorealistic 35mm shot of two people standing side by side facing the camera, "
           "Dean on the left, Becky on the right, full body, casual summer clothes at a campground"),
    "c2": ("dean_34r", "becky_34l",
           "Photorealistic 35mm shot of Dean and Becky facing each other closely, "
           "Dean on the left facing right, Becky on the right facing left, full body, campground"),
    "c2m": ("dean_34l", "becky_34r",
            "Photorealistic 35mm shot of Dean and Becky standing back to back, "
            "Dean on the left facing left, Becky on the right facing right, full body, campground"),
    "c3": ("dean_34r", "becky_34l",
           "Photorealistic 35mm shot of Dean and Becky embracing, Dean on the left facing right, "
           "Becky on the right facing left, full body, campground"),
    "c3m": ("dean_34l", "becky_34r",
            "Photorealistic 35mm shot of Dean and Becky embracing, Dean on the left facing left, "
            "Becky on the right facing right, full body, campground"),
    "c4": ("dean_front", "becky_front",
           "Photorealistic 35mm shot of Dean standing behind, Becky seated in front, "
           "Dean upper, Becky lower, full body, campground"),
    "c5": ("dean_front", "becky_front",
           "Photorealistic 35mm shot of Dean behind, Becky in front, two-shot, "
           "Dean on the left, Becky on the right, full body, campground"),
    "c6": ("dean_profl", "becky_profl",
           "Photorealistic 35mm side profile two-shot of Dean on the left and Becky on the right, "
           "both facing left in profile, full body, campground"),
}


def find_local(ref_dir, stem):
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


def build_workflow(cell, seed, dean_ref, becky_ref, left_mask, right_mask, prompt,
                   checkpoint="juggernautXL_ragnarok.safetensors",
                   strength_a=0.8, strength_b=0.8):
    return {
        "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": checkpoint}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["4", 1]}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["4", 1]}},
        "5": {"class_type": "EmptyLatentImage", "inputs": {"width": 1024, "height": 1024, "batch_size": 1}},
        "10": {"class_type": "IPAdapterUnifiedLoader", "inputs": {"model": ["4", 0], "preset": "PLUS FACE (portraits)"}},
        "11": {"class_type": "LoadImage", "inputs": {"image": os.path.basename(dean_ref)}},
        "12": {"class_type": "LoadImage", "inputs": {"image": os.path.basename(becky_ref)}},
        "13": {"class_type": "LoadImageMask", "inputs": {"image": left_mask, "channel": "red"}},
        "14": {"class_type": "LoadImageMask", "inputs": {"image": right_mask, "channel": "red"}},
        "20": {"class_type": "IPAdapter",
               "inputs": {"model": ["10", 0], "ipadapter": ["10", 1], "image": ["11", 0],
                          "weight": strength_a, "weight_type": "standard", "start_at": 0.0, "end_at": 1.0,
                          "attn_mask": ["13", 0]}},
        "21": {"class_type": "IPAdapter",
               "inputs": {"model": ["20", 0], "ipadapter": ["10", 1], "image": ["12", 0],
                          "weight": strength_b, "weight_type": "standard", "start_at": 0.0, "end_at": 1.0,
                          "attn_mask": ["14", 0]}},
        "3": {"class_type": "KSampler",
              "inputs": {"model": ["21", 0], "positive": ["6", 0], "negative": ["7", 0],
                         "latent_image": ["5", 0], "seed": seed, "steps": 30, "cfg": 5.0,
                         "sampler_name": "dpmpp_2m_sde", "scheduler": "karras", "denoise": 1.0}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {"filename_prefix": f"ma_{cell}", "images": ["8", 0]}},
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


def run_one(base, cell, seed, ref_dir, masks_dir, out_dir, strength_a, strength_b):
    os.makedirs(out_dir, exist_ok=True)
    dean_ref, becky_ref, prompt = CELLS[cell]
    base_cell = cell[:-1] if cell.endswith("m") else cell
    left_mask, right_mask = f"{base_cell}_left.png", f"{base_cell}_right.png"
    if base_cell == "c4":
        left_mask, right_mask = "c4_top.png", "c4_bottom.png"

    dean_local = find_local(ref_dir, dean_ref)
    becky_local = find_local(ref_dir, becky_ref)
    if dean_local is None or becky_local is None:
        print(f"MISSING reference: {dean_ref} / {becky_ref} in {ref_dir}")
        sys.exit(1)

    for local in (dean_local, becky_local):
        upload_image(base, os.path.basename(local), local)
    for mask in (left_mask, right_mask):
        upload_image(base, mask, os.path.join(masks_dir, mask))

    workflow = build_workflow(cell, seed, os.path.basename(dean_local), os.path.basename(becky_local),
                              left_mask, right_mask, prompt, strength_a=strength_a, strength_b=strength_b)
    payload = {"prompt": workflow, "client_id": f"multiangle-{cell}-{seed}"}
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
    ap.add_argument("--strength", type=float, default=0.8, help="Dean (left/actor A) weight")
    ap.add_argument("--strength-b", type=float, default=None, help="Becky (right/actor B) weight")
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--seeds", nargs="+", type=int, default=[1001])
    ap.add_argument("--list", action="store_true", help="list expected refs and exit")
    ap.add_argument("--dump-json", action="store_true")
    args = ap.parse_args()

    if args.list:
        print("Expected angle-tagged reference files in", args.ref_dir)
        for stem in FACES:
            found = find_local(args.ref_dir, stem)
            print(("  [OK ] " if found else "  [MISS] ") + stem + " -> " + (found or "NOT PROVIDED"))
        sys.exit(0)

    if args.dump_json:
        dean_ref, becky_ref, prompt = CELLS[args.cell]
        base_cell = args.cell[:-1] if args.cell.endswith("m") else args.cell
        left_mask, right_mask = f"{base_cell}_left.png", f"{base_cell}_right.png"
        if base_cell == "c4":
            left_mask, right_mask = "c4_top.png", "c4_bottom.png"
        dl = find_local(args.ref_dir, dean_ref)
        bl = find_local(args.ref_dir, becky_ref)
        sb = args.strength_b if args.strength_b is not None else args.strength
        wf = build_workflow(args.cell, args.seed, os.path.basename(dl), os.path.basename(bl),
                            left_mask, right_mask, prompt, strength_a=args.strength, strength_b=sb)
        print(json.dumps({"prompt": wf, "client_id": f"multiangle-{args.cell}-{args.seed}"}, indent=2))
        return

    cells = list(CELLS) if args.all else [args.cell]
    if args.cell is None and not args.all:
        print("--cell or --all required")
        sys.exit(1)
    sb = args.strength_b if args.strength_b is not None else args.strength
    for cell in cells:
        for seed in args.seeds:
            run_one(args.base, cell, seed, args.ref_dir, args.masks, args.out, args.strength, sb)


if __name__ == "__main__":
    main()
