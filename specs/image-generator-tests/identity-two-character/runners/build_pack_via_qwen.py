#!/usr/bin/env python3
"""
Generate consistent 5-view character packs via Qwen Image Edit 2511.

The identity proof failed because the test refs were DIFFERENT real-world photos
of each person (inconsistent face). This runner fixes that: it takes ONE
high-quality front portrait per character and uses Qwen-edit (a source-image
editor that preserves identity) to create the other 4 views (3/4L, 3/4R, profL,
profR) from that same face — a guaranteed-consistent 5-view pack.

Workflow mirrors the validated qwen-simple-people-edit.json exactly
(specs/image-generator-tests/qwen/prompts/), including the pinned settings:
40 steps, CFG 4, euler/simple, denoise 1, AuraFlow shift 3.1, CFGNorm 1.

Usage:
  python build_pack_via_qwen.py --character dean --front <path> --out <dir>
  python build_pack_via_qwen.py --all --front-dir <dir-with-front-jpgs> --out <dir>
"""
import argparse, json, os, sys, time, urllib.request, urllib.parse, uuid

QWEN = "https://cuth53f1z97dij-3002.proxy.runpod.net"
UA = "Mozilla/5.0 (compatible; DreamGenClone/1.0)"

# The 4 angle variants to generate from the front base, with Qwen edit prompts
# that rotate ONLY the head/pose while preserving identity exactly.
ANGLES = {
    "34l": ("Rotate the person's head and upper body slightly to their left, about "
            "a three-quarter view. Keep the exact same face, hair, facial features, "
            "identity, clothing, and lighting unchanged. Only the head angle changes."),
    "34r": ("Rotate the person's head and upper body slightly to their right, about "
            "a three-quarter view. Keep the exact same face, hair, facial features, "
            "identity, clothing, and lighting unchanged. Only the head angle changes."),
    "profl": ("Rotate the person to a left profile, head turned fully to their left "
              "showing the left side of the face. Keep the exact same face, hair, "
              "facial features, identity, clothing, and lighting unchanged. Only the "
              "head angle changes."),
    "profr": ("Rotate the person to a right profile, head turned fully to their right "
              "showing the right side of the face. Keep the exact same face, hair, "
              "facial features, identity, clothing, and lighting unchanged. Only the "
              "head angle changes."),
}


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
        print("  uploaded", filename, r.read().decode()[:80])


def build_workflow(image_name, prompt, seed):
    """Replicates the validated qwen-simple-people-edit.json graph."""
    return {
        "1": {"class_type": "LoadImage", "inputs": {"image": image_name}},
        "2": {"class_type": "FluxKontextImageScale", "inputs": {"image": ["1", 0]}},
        "3": {"class_type": "KSampler",
              "inputs": {"model": ["14", 0], "positive": ["12", 0], "negative": ["13", 0],
                         "latent_image": ["8", 0], "seed": seed, "steps": 40, "cfg": 4.0,
                         "sampler_name": "euler", "scheduler": "simple", "denoise": 1.0}},
        "4": {"class_type": "UNETLoader",
              "inputs": {"unet_name": "qwen_image_edit_2511_fp8mixed.safetensors", "weight_dtype": "default"}},
        "5": {"class_type": "ModelSamplingAuraFlow", "inputs": {"model": ["4", 0], "shift": 3.1}},
        "6": {"class_type": "TextEncodeQwenImageEditPlus",
              "inputs": {"clip": ["10", 0], "vae": ["11", 0], "image1": ["2", 0], "prompt": prompt}},
        "7": {"class_type": "TextEncodeQwenImageEditPlus",
              "inputs": {"clip": ["10", 0], "vae": ["11", 0], "image1": ["2", 0], "prompt": ""}},
        "8": {"class_type": "VAEEncode", "inputs": {"pixels": ["2", 0], "vae": ["11", 0]}},
        "9": {"class_type": "SaveImage", "inputs": {"images": ["15", 0], "filename_prefix": "qwen-identity-pack"}},
        "10": {"class_type": "CLIPLoader",
               "inputs": {"clip_name": "qwen_2.5_vl_7b_fp8_scaled.safetensors", "type": "qwen_image", "device": "default"}},
        "11": {"class_type": "VAELoader", "inputs": {"vae_name": "qwen_image_vae.safetensors"}},
        "12": {"class_type": "FluxKontextMultiReferenceLatentMethod",
               "inputs": {"conditioning": ["6", 0], "reference_latents_method": "index_timestep_zero"}},
        "13": {"class_type": "FluxKontextMultiReferenceLatentMethod",
               "inputs": {"conditioning": ["7", 0], "reference_latents_method": "index_timestep_zero"}},
        "14": {"class_type": "CFGNorm", "inputs": {"model": ["5", 0], "strength": 1.0}},
        "15": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["11", 0]}},
    }


def post(base, path, body):
    req = urllib.request.Request(base + path, data=json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json", "User-Agent": UA})
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            return json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        detail = e.read().decode() if e.readable() else ""
        print("HTTP", e.code, detail[:4000])
        raise


def get_json(base, path):
    req = urllib.request.Request(base + path, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.loads(resp.read().decode())


def run_edit(base, front_path, angle, prompt, seed, out_dir):
    upload_image(base, os.path.basename(front_path), front_path)
    workflow = build_workflow(os.path.basename(front_path), prompt, seed)
    payload = {"prompt": workflow, "client_id": f"qwen-pack-{angle}-{seed}"}
    resp = post(base, "/prompt", payload)
    if resp.get("node_errors"):
        print("  NODE_ERRORS:", json.dumps(resp["node_errors"], indent=2)[:2000])
        return None
    prompt_id = resp.get("prompt_id")
    print(f"  {angle}: prompt_id {prompt_id}")

    deadline = time.time() + 300
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
                print("  STATUS:", st, json.dumps(entry.get("status", {}))[:600])
                return None
            for node_out in entry.get("outputs", {}).values():
                for img in node_out.get("images", []):
                    fn = img["filename"]
                    url = f"{base}/view?filename={urllib.parse.quote(fn)}"
                    if img.get("subfolder"): url += f"&subfolder={urllib.parse.quote(img['subfolder'])}"
                    if img.get("type"): url += f"&type={urllib.parse.quote(img['type'])}"
                    local = os.path.join(out_dir, f"{angle}.png")
                    dl = urllib.request.Request(url, headers={"User-Agent": UA})
                    with urllib.request.urlopen(dl, timeout=120) as r:
                        with open(local, "wb") as f:
                            f.write(r.read())
                    print("  SAVED:", local)
                    return local
            return None
    print("  TIMEOUT")
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--character", default="character")
    ap.add_argument("--front", default=None, help="Path to the front face (identity anchor)")
    ap.add_argument("--front-dir", default=None, help="Dir with <character>_front.jpg per character (--all mode)")
    ap.add_argument("--out", default=os.path.join("artifacts", "tmp", "qwen-identity-packs"))
    ap.add_argument("--seed", type=int, default=202601)
    ap.add_argument("--all", action="store_true")
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)

    if args.all:
        # front-dir contains <char>_front.jpg; generate all 4 angles for each
        chars = {}
        for fn in os.listdir(args.front_dir):
            if fn.endswith("_front.jpg") or fn.endswith("_front.png"):
                char = fn.replace("_front.jpg", "").replace("_front.png", "")
                chars[char] = os.path.join(args.front_dir, fn)
        for char, front in chars.items():
            char_out = os.path.join(args.out, char)
            os.makedirs(char_out, exist_ok=True)
            # copy front as the anchor
            import shutil
            shutil.copy(front, os.path.join(char_out, "front" + os.path.splitext(front)[1]))
            for angle, prompt in ANGLES.items():
                print(f"\n=== {char} / {angle} ===")
                run_edit(QWEN, front, angle, prompt, args.seed, char_out)
    else:
        if args.front is None:
            print("--front required (or use --all --front-dir)")
            sys.exit(1)
        char_out = os.path.join(args.out, args.character)
        os.makedirs(char_out, exist_ok=True)
        import shutil
        shutil.copy(args.front, os.path.join(char_out, "front" + os.path.splitext(args.front)[1]))
        for angle, prompt in ANGLES.items():
            print(f"\n=== {args.character} / {angle} ===")
            run_edit(QWEN, args.front, angle, prompt, args.seed, char_out)
    print("\nDONE")


if __name__ == "__main__":
    main()
