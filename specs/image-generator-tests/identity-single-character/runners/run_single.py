#!/usr/bin/env python3
"""
Single-character identity render runner (Dean).

Part of the `identity-single-character` test suite. This is the ORIGINAL proof
that started the identity investigation: one character (Dean) rendered with a
single reference face via IP-Adapter "PLUS FACE (portraits)" (or PuLID), which
validated the single-character identity path end-to-end before the two-character
matrix was attempted.

Parameterized like the two-character runners: --base pod URL, --out, --ref-dir,
--mechanism (ipadapter | pulid), --seed, --strength.

Usage:
  python run_single.py --mechanism ipadapter --seed 20256
  python run_single.py --mechanism pulid --seed 20256
  python run_single.py --mechanism ipadapter --seed 20256 --dump-json
"""
import argparse, json, os, sys, time, urllib.request, urllib.parse, uuid

HERE = os.path.dirname(os.path.abspath(__file__))
SUITE = os.path.dirname(HERE)
DEFAULT_OUT = os.path.join(os.environ.get("DG_TMP", r"d:\src\DreamGenClone\artifacts\tmp"),
                           "images", "identity-single-character")
DEFAULT_REF_DIR = os.path.join(SUITE, "refs")

UA = "Mozilla/5.0 (compatible; DreamGenClone/1.0)"

POSITIVE = ("a young man, head and shoulders portrait, looking at the camera, "
            "neutral expression, studio lighting, photorealistic, sharp focus")
NEGATIVE = ("deformed, bad anatomy, extra fingers, extra limbs, blurry, low quality, "
            "cartoon, anime, illustration, painting, sketch, watermark, text, "
            "oversaturated, plastic skin")


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


def build_workflow_ipadapter(seed, dean_ref, checkpoint="juggernautXL_ragnarok.safetensors",
                             strength=0.8):
    return {
        "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": checkpoint}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": POSITIVE, "clip": ["4", 1]}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["4", 1]}},
        "5": {"class_type": "EmptyLatentImage", "inputs": {"width": 1024, "height": 1024, "batch_size": 1}},
        "10": {"class_type": "IPAdapterUnifiedLoader", "inputs": {"model": ["4", 0], "preset": "PLUS FACE (portraits)"}},
        "11": {"class_type": "LoadImage", "inputs": {"image": os.path.basename(dean_ref)}},
        "12": {"class_type": "IPAdapter",
               "inputs": {"model": ["10", 0], "ipadapter": ["10", 1], "image": ["11", 0],
                          "weight": strength, "weight_type": "standard",
                          "start_at": 0.0, "end_at": 1.0}},
        "3": {"class_type": "KSampler",
              "inputs": {"model": ["12", 0], "positive": ["6", 0], "negative": ["7", 0],
                         "latent_image": ["5", 0], "seed": seed, "steps": 30, "cfg": 5.0,
                         "sampler_name": "dpmpp_2m_sde", "scheduler": "karras", "denoise": 1.0}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {"filename_prefix": "dg_ipadapter_dean_single", "images": ["8", 0]}},
    }


def build_workflow_pulid(seed, dean_ref, checkpoint="juggernautXL_ragnarok.safetensors"):
    return {
        "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": checkpoint}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": POSITIVE, "clip": ["4", 1]}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["4", 1]}},
        "5": {"class_type": "EmptyLatentImage", "inputs": {"width": 1024, "height": 1024, "batch_size": 1}},
        "10": {"class_type": "PulidModelLoader", "inputs": {"pulid_file": "ip-adapter_pulid_sdxl_fp16.safetensors"}},
        "11": {"class_type": "LoadImage", "inputs": {"image": os.path.basename(dean_ref)}},
        "13": {"class_type": "PulidInsightFaceLoader", "inputs": {"provider": "CPU"}},
        "14": {"class_type": "PulidEvaClipLoader", "inputs": {}},
        "12": {"class_type": "PulidEvaClip",
               "inputs": {"model": ["4", 0], "eva_clip": ["14", 0], "pulid": ["10", 0],
                          "image": ["11", 0], "insightface": ["13", 0], "weight": 1.0,
                          "start_at": 0.0, "end_at": 1.0}},
        "15": {"class_type": "IPAdapterUnifiedLoader",
               "inputs": {"model": ["12", 0], "preset": "PLUS FACE (portraits)"}},
        "16": {"class_type": "IPAdapter",
               "inputs": {"model": ["15", 0], "ipadapter": ["15", 1], "image": ["11", 0],
                          "weight": 0.8, "weight_type": "standard", "start_at": 0.0, "end_at": 1.0}},
        "3": {"class_type": "KSampler",
              "inputs": {"model": ["16", 0], "positive": ["6", 0], "negative": ["7", 0],
                         "latent_image": ["5", 0], "seed": seed, "steps": 30, "cfg": 5.0,
                         "sampler_name": "dpmpp_2m_sde", "scheduler": "karras", "denoise": 1.0}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {"filename_prefix": "dg_pulid_dean_single", "images": ["8", 0]}},
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


def run_one(base, mechanism, seed, dean_ref, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    upload_image(base, os.path.basename(dean_ref), dean_ref)
    if mechanism == "ipadapter":
        workflow = build_workflow_ipadapter(seed, dean_ref)
        prefix = "dg_ipadapter_dean_single"
    else:
        workflow = build_workflow_pulid(seed, dean_ref)
        prefix = "dg_pulid_dean_single"

    payload = {"prompt": workflow, "client_id": f"single-{mechanism}-{seed}"}
    resp = post(base, "/prompt", payload)
    if resp.get("node_errors"):
        print("NODE_ERRORS:", json.dumps(resp["node_errors"], indent=2))
        sys.exit(1)
    prompt_id = resp.get("prompt_id")
    print("prompt_id:", prompt_id)

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
                print("STATUS:", st, json.dumps(entry.get("status", {}))[:500])
                sys.exit(1)
            for node_out in entry.get("outputs", {}).values():
                for img in node_out.get("images", []):
                    fn = img["filename"]
                    url = f"{base}/view?filename={urllib.parse.quote(fn)}"
                    if img.get("subfolder"): url += f"&subfolder={urllib.parse.quote(img['subfolder'])}"
                    if img.get("type"): url += f"&type={urllib.parse.quote(img['type'])}"
                    local = os.path.join(out_dir, f"{mechanism}-dean-single_s{seed}.png")
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
    ap.add_argument("--ref-dir", default=DEFAULT_REF_DIR)
    ap.add_argument("--mechanism", choices=["ipadapter", "pulid"], default="ipadapter")
    ap.add_argument("--seed", type=int, default=20256)
    ap.add_argument("--strength", type=float, default=0.8)
    ap.add_argument("--dump-json", action="store_true")
    args = ap.parse_args()

    dean_ref = find_ref(args.ref_dir, "dean_face")
    if dean_ref is None:
        print(f"MISSING ref dean_face in {args.ref_dir}")
        sys.exit(1)

    if args.dump_json:
        wf = (build_workflow_ipadapter(args.seed, dean_ref, strength=args.strength)
              if args.mechanism == "ipadapter" else build_workflow_pulid(args.seed, dean_ref))
        print(json.dumps({"prompt": wf, "client_id": f"single-{args.mechanism}-{args.seed}"}, indent=2))
        return

    run_one(args.base, args.mechanism, args.seed, dean_ref, args.out)


if __name__ == "__main__":
    main()
