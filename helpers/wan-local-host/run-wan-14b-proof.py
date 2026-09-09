"""Uncensored Wan 2.2 I2V-A14B (rzgar) proof runner against the local ComfyUI host.

Drives the native two-stage I2V graph (WanImageToVideo + two KSamplerAdvanced:
high-noise expert 0..split, low-noise expert split..N; optional LoraLoaderModelOnly
per expert) and records mp4 + start/mid/end frames + a run-manifest per cell.

Self-contained: no dependency on artifacts/tmp scaffolding. Outputs to the
git-ignored out dir (default artifacts/tmp/wan-proof).
"""
import argparse, json, time, os, sys, uuid, urllib.request, urllib.parse, cv2

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = 'http://192.168.0.16:8188'
DEFAULT_OUT = os.path.join('artifacts', 'tmp', 'wan-proof')

LORAS = {
    'lightx2v': ('Wan2.2_LightX2V_high_n54vv.safetensors', 'Wan2.2_LightX2V_low_n54vv.safetensors', 1.0),
    'cubeyai': ('CubeyAI-GeneralN-High.safetensors', 'CubeyAI-GeneralN-Low.safetensors', 0.55),
}


def build_graph(cfg, seed, lora_high, lora_low, lora_strength):
    nodes = {
        "84": {"class_type": "CLIPLoader", "inputs": {"clip_name": "umt5_xxl_fp8_e4m3fn_scaled.safetensors", "type": "wan", "device": "default"}},
        "90": {"class_type": "VAELoader", "inputs": {"vae_name": "wan_2.1_vae.safetensors"}},
        "93": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["84", 0], "text": cfg["positive"]}},
        "89": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["84", 0], "text": cfg["negative"]}},
        "56": {"class_type": "LoadImage", "inputs": {"image": cfg["_img"]}},
        "98": {"class_type": "WanImageToVideo", "inputs": {
            "positive": ["93", 0], "negative": ["89", 0], "vae": ["90", 0],
            "width": cfg["_w"], "height": cfg["_h"], "length": cfg.get("length", 49), "batch_size": 1,
            "start_image": ["56", 0]}},
        "95": {"class_type": "UNETLoader", "inputs": {"unet_name": "Wan2.2_I2V_High_R1.safetensors", "weight_dtype": "default"}},
        "96": {"class_type": "UNETLoader", "inputs": {"unet_name": "Wan2.2_I2V_Low_R1.safetensors", "weight_dtype": "default"}},
    }
    hi, lo = ["95", 0], ["96", 0]
    if lora_high:
        nodes["101"] = {"class_type": "LoraLoaderModelOnly", "inputs": {"model": hi, "lora_name": lora_high, "strength_model": lora_strength}}
        hi = ["101", 0]
    if lora_low:
        nodes["102"] = {"class_type": "LoraLoaderModelOnly", "inputs": {"model": lo, "lora_name": lora_low, "strength_model": lora_strength}}
        lo = ["102", 0]
    nodes["104"] = {"class_type": "ModelSamplingSD3", "inputs": {"model": hi, "shift": cfg.get("shift", 5.0)}}
    nodes["103"] = {"class_type": "ModelSamplingSD3", "inputs": {"model": lo, "shift": cfg.get("shift", 5.0)}}
    steps, split = cfg["steps"], cfg["split"]
    nodes["86"] = {"class_type": "KSamplerAdvanced", "inputs": {
        "model": ["104", 0], "add_noise": "enable", "noise_seed": seed, "steps": steps, "cfg": 3.5,
        "sampler_name": "euler", "scheduler": "simple",
        "positive": ["98", 0], "negative": ["98", 1], "latent_image": ["98", 2],
        "start_at_step": 0, "end_at_step": split, "return_with_leftover_noise": "enable"}}
    nodes["85"] = {"class_type": "KSamplerAdvanced", "inputs": {
        "model": ["103", 0], "add_noise": "disable", "noise_seed": seed, "steps": steps, "cfg": 3.5,
        "sampler_name": "euler", "scheduler": "simple",
        "positive": ["98", 0], "negative": ["98", 1], "latent_image": ["86", 0],
        "start_at_step": split, "end_at_step": steps, "return_with_leftover_noise": "disable"}}
    nodes["87"] = {"class_type": "VAEDecode", "inputs": {"samples": ["85", 0], "vae": ["90", 0]}}
    nodes["94"] = {"class_type": "CreateVideo", "inputs": {"images": ["87", 0], "fps": 16}}
    nodes["108"] = {"class_type": "SaveVideo", "inputs": {
        "video": ["94", 0], "filename_prefix": f"video/wan_proof/{cfg['id']}", "format": "auto", "codec": "auto"}}
    return nodes


def upload_image(local_path):
    img = cv2.imread(local_path)
    h, w = img.shape[:2]
    area = 640 * 1024
    scale = min(1.0, (area / (w * h)) ** 0.5)
    nw, nh = max(64, int(w * scale // 16 * 16)), max(64, int(h * scale // 16 * 16))
    boundary = b'----dgwan' + uuid.uuid4().hex.encode()
    with open(local_path, 'rb') as f:
        data = f.read()
    parts = [
        f"--{boundary.decode()}\r\nContent-Disposition: form-data; name=\"image\"; filename=\"{os.path.basename(local_path)}\"\r\nContent-Type: image/png\r\n\r\n".encode() + data + b"\r\n",
        f"--{boundary.decode()}\r\nContent-Disposition: form-data; name=\"overwrite\"\r\n\r\ntrue\r\n".encode(),
        f"--{boundary.decode()}--\r\n".encode(),
    ]
    req = urllib.request.Request(BASE + "/upload/image", data=b"".join(parts),
                                 headers={"Content-Type": f"multipart/form-data; boundary={boundary.decode()}"})
    with urllib.request.urlopen(req, timeout=120) as r:
        res = json.load(r)
    return res.get("name"), nw, nh


def api(path, payload=None, timeout=60):
    if payload is not None:
        data = json.dumps(payload).encode()
        req = urllib.request.Request(BASE + path, data=data, headers={"Content-Type": "application/json"})
    else:
        req = urllib.request.Request(BASE + path)
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.load(r)


def run_cell(cell, seed, out_root, lora_high, lora_low, lora_strength, timeout_s):
    cid = cell["id"]
    outdir = os.path.join(out_root, cid)
    os.makedirs(outdir, exist_ok=True)
    name, w, h = upload_image(cell["start_image"])
    cell["_img"], cell["_w"], cell["_h"] = name, w, h
    prompt = build_graph(cell, seed, lora_high, lora_low, lora_strength)
    print(f"[{cid}] submitting {w}x{h} len={cell.get('length',49)} steps={cell['steps']} split={cell['split']} seed={seed}")
    pid = api("/prompt", {"prompt": prompt, "client_id": str(uuid.uuid4())}).get("prompt_id")
    if not pid:
        print(f"[{cid}] no prompt_id"); return None
    print(f"[{cid}] prompt_id={pid}")
    start = time.time()
    while time.time() - start < timeout_s:
        h = api("/history/" + pid)
        rec = h.get(pid) or {}
        st = rec.get('status', {})
        if st.get('completed') or st.get('status_str') == 'success':
            dt = time.time() - start
            print(f"[{cid}] COMPLETED in {dt:.0f}s")
            files = []
            for o in (rec.get('outputs') or {}).values():
                for key in ('gifs', 'images', 'video', 'files'):
                    files.extend(o.get(key) or [])
            meta = {"cell": cid, "status": "completed", "seconds": round(dt, 1), "prompt_id": pid}
            for it in files:
                fn, sub, typ = it.get('filename'), it.get('subfolder', ''), it.get('type', 'output')
                url = f"{BASE}/view?filename={urllib.parse.quote(fn)}&subfolder={urllib.parse.quote(sub)}&type={typ}"
                dest = os.path.join(outdir, f"{cid}.mp4")
                urllib.request.urlretrieve(url, dest)
                meta["video"] = dest
                meta["view_url"] = url
                # extract start/mid/end frames
                cap = cv2.VideoCapture(dest)
                total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
                d = total / max(cap.get(cv2.CAP_PROP_FPS), 1.0)
                frames = {}
                for sec, tag in [(0.15, 't00'), (d * 0.5, 'tmid'), (max(0.1, d - 0.3), 'tend')]:
                    cap.set(cv2.CAP_PROP_POS_MSEC, int(sec * 1000))
                    ok, fr = cap.read()
                    if ok:
                        p = os.path.join(outdir, f"{cid}_{tag}.png")
                        cv2.imwrite(p, fr)
                        frames[tag] = p
                cap.release()
                meta["frames"] = frames
            with open(os.path.join(outdir, "run-manifest.json"), 'w', encoding='utf-8') as f:
                json.dump(meta, f, indent=2)
            return meta
        if st.get('status_str') == 'error':
            print(f"[{cid}] JOB ERROR: {json.dumps(rec)[:2000]}"); return None
        time.sleep(10)
    print(f"[{cid}] TIMEOUT"); return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--cell', help='cell id to run (default: all)')
    ap.add_argument('--seed', type=int, default=20260908)
    ap.add_argument('--lora', choices=['none', 'lightx2v', 'cubeyai'], default='none')
    ap.add_argument('--steps', type=int, default=None)
    ap.add_argument('--split', type=int, default=None)
    ap.add_argument('--out', default=DEFAULT_OUT)
    ap.add_argument('--cells-file', default=os.path.join(HERE, 'prompts-unfiltered.json'))
    ap.add_argument('--timeout', type=int, default=5400)
    a = ap.parse_args()

    with open(a.cells_file, encoding='utf-8') as f:
        CELLS = json.load(f)['cells']

    lora_high = lora_low = None
    lora_strength = 1.0
    if a.lora != 'none':
        lora_high, lora_low, lora_strength = LORAS[a.lora]
    print(f"lora={a.lora} ({lora_high} / {lora_low}) strength={lora_strength}")

    cells = [c for c in CELLS if not a.cell or c['id'] == a.cell]
    results = []
    for c in cells:
        c = dict(c)
        if a.steps:
            c['steps'] = a.steps
            c['split'] = a.split or a.steps // 2
        m = run_cell(c, a.seed, a.out, lora_high, lora_low, lora_strength, a.timeout)
        results.append(m)
    print(json.dumps(results, indent=2))


if __name__ == '__main__':
    main()
