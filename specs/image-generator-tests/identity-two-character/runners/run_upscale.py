#!/usr/bin/env python3
"""
4x-UltraSharp upscaler for identity reference images (ComfyUI on a RunPod pod).

Part of the `identity-two-character` test suite. Used to upgrade low-resolution
reference photos (e.g. ~400px face crops) before re-running the proof — the
identity conditioning degrades below ~700px, so 4x upscale is the enhancement
step that was validated to help the angled cells.

Usage:
  python run_upscale.py --images a.jpg b.png --out outdir
  python run_upscale.py --images <dir>/<glob> --out outdir --dry
"""
import argparse, json, os, sys, time, urllib.request, urllib.parse, uuid

DEFAULT_BASE = "https://7i2mutjmry5tkt-3000.proxy.runpod.net"
UA = "Mozilla/5.0 (compatible; DreamGenClone/1.0)"


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
        print("uploaded", filename, r.read().decode()[:80])


def build_workflow(image_name, upscaler="4x-UltraSharp.pth", prefix="up"):
    return {
        "1": {"class_type": "LoadImage", "inputs": {"image": image_name}},
        "2": {"class_type": "UpscaleModelLoader", "inputs": {"model_name": upscaler}},
        "3": {"class_type": "ImageUpscaleWithModel",
              "inputs": {"upscale_model": ["2", 0], "image": ["1", 0]}},
        "4": {"class_type": "SaveImage", "inputs": {"filename_prefix": prefix, "images": ["3", 0]}},
    }


def post(base, path, body):
    req = urllib.request.Request(base + path, data=json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json", "User-Agent": UA})
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            return json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        detail = e.read().decode() if e.readable() else ""
        print("HTTP", e.code, detail[:3000])
        raise


def get_json(base, path):
    req = urllib.request.Request(base + path, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.loads(resp.read().decode())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--images", nargs="+", required=True, help="Local image paths to upscale")
    ap.add_argument("--out", required=True, help="Output directory")
    ap.add_argument("--base", default=DEFAULT_BASE)
    ap.add_argument("--upscaler", default="4x-UltraSharp.pth")
    ap.add_argument("--dry", action="store_true")
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)

    for local in args.images:
        stem = os.path.splitext(os.path.basename(local))[0]
        filename = os.path.basename(local)
        if args.dry:
            print(f"[dry] would upscale {filename} -> {args.out}/{stem}_4x.png")
            continue

        upload_image(args.base, filename, local)
        workflow = build_workflow(filename, upscaler=args.upscaler, prefix=f"up_{stem}")
        resp = post(args.base, "/prompt", {"prompt": workflow, "client_id": f"up-{stem}"})
        if resp.get("node_errors"):
            print("NODE_ERRORS:", json.dumps(resp["node_errors"], indent=2)[:1500])
            sys.exit(1)
        prompt_id = resp.get("prompt_id")
        print(f"{stem}: prompt_id {prompt_id}")

        deadline = time.time() + 240
        while time.time() < deadline:
            time.sleep(4)
            try:
                hist = get_json(args.base, f"/history/{prompt_id}")
            except Exception:
                continue
            if prompt_id in hist:
                entry = hist[prompt_id]
                st = entry.get("status", {}).get("status_str")
                if st != "success":
                    print("STATUS:", st, json.dumps(entry.get("status", {}))[:400])
                    sys.exit(1)
                for node_out in entry.get("outputs", {}).values():
                    for img in node_out.get("images", []):
                        fn = img["filename"]
                        url = f"{args.base}/view?filename={urllib.parse.quote(fn)}"
                        if img.get("subfolder"): url += f"&subfolder={urllib.parse.quote(img['subfolder'])}"
                        if img.get("type"): url += f"&type={urllib.parse.quote(img['type'])}"
                        local_out = os.path.join(args.out, f"{stem}_4x.png")
                        dl = urllib.request.Request(url, headers={"User-Agent": UA})
                        with urllib.request.urlopen(dl, timeout=120) as r:
                            with open(local_out, "wb") as f:
                                f.write(r.read())
                        print("SAVED:", local_out)
                break
        else:
            print("TIMEOUT")
            sys.exit(1)


if __name__ == "__main__":
    main()
