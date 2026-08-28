#!/usr/bin/env python3
"""
2-person pack position test runner.

Part of the `identity-two-character` suite. Submits the pre-generated position
workflows (positions/prompts/*.json — Dean+Becky regional IP-Adapter) to a live
ComfyUI pod and downloads each result into a PER-RUN output folder, so separate
runs stay separate and comparable.

Every run writes to a fresh timestamped folder under --out (default:
<suite>/positions/runs/<yyyyMMdd-HHmmss>/ — source-controlled). Pass --label to
add a human-readable suffix to the run folder.

Usage:
  python run_positions.py --base https://<pod>-3000.proxy.runpod.net
  python run_positions.py --base ... --label v3-refs --seeds 1001 1002
  python run_positions.py --base ... --position juggernaut-nsfw-missionary-test
  python run_positions.py --list
"""
import argparse, datetime, json, os, sys, time, urllib.request, urllib.parse, uuid

HERE = os.path.dirname(os.path.abspath(__file__))       # .../identity-two-character/runners
SUITE = os.path.dirname(HERE)                            # .../identity-two-character
PROMPTS_DIR = os.path.join(SUITE, "positions", "prompts")
MASKS_DIR = os.path.join(SUITE, "masks")
REFS_DIR = os.path.join(SUITE, "refs")          # canonical faces (dean_face/becky_face)
PACK_REFS_DIR = os.path.join(SUITE, "refs", "multiangle")  # full 5-view face packs

# Runs are source-controlled evidence (committed), NOT under ignored artifacts/tmp.
# Each run gets its own timestamped folder so separate runs stay comparable.
DEFAULT_RUN_ROOT = os.path.join(SUITE, "positions", "runs")

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
        print("  uploaded", filename, r.read().decode()[:80])


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


def run_one(base, prompt_obj, out_dir):
    """Submit one position workflow; download its result PNG into out_dir."""
    workflow = prompt_obj["prompt"]
    # Gather the ref + mask files this workflow needs. Refs may come from the
    # full face packs (refs/multiangle) or the canonical refs/ folder.
    ref_files = []
    for nid in ("11", "12"):  # LoadImage dean/becky
        fn = workflow[nid]["inputs"]["image"]
        p = os.path.join(PACK_REFS_DIR, fn)
        if not os.path.exists(p):
            p = os.path.join(REFS_DIR, fn)
        ref_files.append(p)
    mask_files = []
    for nid in ("13", "14"):  # LoadImageMask per-character
        mask_files.append(os.path.join(MASKS_DIR, workflow[nid]["inputs"]["image"]))

    for p in ref_files + mask_files:
        if not os.path.exists(p):
            print(f"  MISSING local file: {p}")
            sys.exit(1)
        upload_image(base, os.path.basename(p), p)

    payload = {"prompt": workflow, "client_id": prompt_obj.get("client_id", f"pos-{uuid.uuid4().hex[:8]}")}
    resp = post(base, "/prompt", payload)
    if resp.get("node_errors"):
        print("  NODE_ERRORS:", json.dumps(resp["node_errors"], indent=2)[:2000])
        return None
    prompt_id = resp.get("prompt_id")
    print(f"  prompt_id: {prompt_id}")

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
                print("  STATUS:", st, json.dumps(entry.get("status", {}))[:600])
                return None
            saved = []
            for node_out in entry.get("outputs", {}).values():
                for img in node_out.get("images", []):
                    fn = img["filename"]
                    url = f"{base}/view?filename={urllib.parse.quote(fn)}"
                    if img.get("subfolder"): url += f"&subfolder={urllib.parse.quote(img['subfolder'])}"
                    if img.get("type"): url += f"&type={urllib.parse.quote(img['type'])}"
                    stem = os.path.splitext(os.path.basename(fn))[0]
                    local = os.path.join(out_dir, f"{stem}.png")
                    dl = urllib.request.Request(url, headers={"User-Agent": UA})
                    with urllib.request.urlopen(dl, timeout=120) as r:
                        with open(local, "wb") as f:
                            f.write(r.read())
                    saved.append(local)
                    print("  SAVED:", local)
            return saved
    print("  TIMEOUT")
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="https://ncsmze3anko7w2-3000.proxy.runpod.net",
                    help="ComfyUI pod base URL (the migrated proof pod by default)")
    ap.add_argument("--out", default=None, help="Run root; a timestamped subfolder is created per run")
    ap.add_argument("--label", default=None, help="Optional label suffix for the run folder")
    ap.add_argument("--position", default=None, help="Run only this position id")
    ap.add_argument("--list", action="store_true", help="List available position workflows and exit")
    args = ap.parse_args()

    if not os.path.isdir(PROMPTS_DIR):
        print(f"no positions dir: {PROMPTS_DIR} (run dump_positions.py first)")
        sys.exit(1)
    positions = sorted(fn for fn in os.listdir(PROMPTS_DIR) if fn.endswith(".json"))
    if args.list:
        print("Available 2-person pack position workflows:")
        for fn in positions:
            print("  ", fn)
        sys.exit(0)

    if args.position:
        positions = [args.position + ".json" if not args.position.endswith(".json") else args.position]
    positions = [p for p in positions if os.path.exists(os.path.join(PROMPTS_DIR, p))]
    if not positions:
        print("No position workflows found.")
        sys.exit(1)

    run_root = args.out or DEFAULT_RUN_ROOT
    ts = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    run_dir = os.path.join(run_root, ts + (f"-{args.label}" if args.label else ""))
    os.makedirs(run_dir, exist_ok=True)
    print(f"RUN DIR: {run_dir}")
    print(f"RUNNING {len(positions)} positions against {args.base}")

    results = {}
    for fn in positions:
        with open(os.path.join(PROMPTS_DIR, fn), encoding="utf-8") as f:
            obj = json.load(f)
        print(f"\n=== {fn} ===")
        saved = run_one(args.base, obj, run_dir)
        results[fn] = saved

    with open(os.path.join(run_dir, "run-manifest.json"), "w", encoding="utf-8") as f:
        json.dump({"base": args.base, "label": args.label, "timestamp": ts,
                   "results": results}, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"\nDONE. Run manifest: {os.path.join(run_dir, 'run-manifest.json')}")


if __name__ == "__main__":
    main()
