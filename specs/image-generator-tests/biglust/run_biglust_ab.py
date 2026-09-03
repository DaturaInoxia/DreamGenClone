#!/usr/bin/env python3
"""
BigLust unified A/B runner (text-identity vs ip-identity).

Runs the SAME prompts from TEST-MATRIX-PROMPTS.json (the source of the good
2026-09-01_114950 run) twice for every identity cell, in ONE dated run:

    <id>-text.png  -> plain T2I (prose-only identity, NO IP-Adapter refs)
    <id>-ip.png    -> SAME prompt + SAME seed, but with the IP-Adapter refs
                      (single ref for 1-person; dual refs + regional masks for 2-person)

Non-identity cells (stock-nsfw: identity == null) run only the -text variant (there
is no identity to condition on). Identity cells get both variants with a MATCHED seed,
so any difference between -text and -ip is attributable to the identity mechanism.

Refs come from the VERSIONED identity-pack archive (refs/<char>/<version>/) resolved by
identity_refs.py — the active pack per character is pinned in specs/image-generator-tests/refs/versions.json,
so changing one value re-points the whole run at a different approved pack. Masks live in
identity-two-character/masks/.

Outputs are SOURCE-CONTROLLED (specs/image-generator-tests/), dated for cross-run
comparison:

    specs/image-generator-tests/biglust/runs/<yyyy-MM-dd_HHmmss>-ab/
      images/<id>-text.png, <id>-ip.png
      prompts/<id>-text.json, <id>-ip.json   (frozen workflows)
      manifest.json                            (per-variant id/suite/pose/seed/prompt/jobId/sha)

Usage:
  python run_biglust_ab.py --dry-run
  python run_biglust_ab.py [--label v1]
"""
import argparse, base64, datetime, hashlib, json, os, random, re, sys, time, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))                 # .../biglust
TESTS_ROOT = os.path.dirname(HERE)                                 # .../specs/image-generator-tests
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))    # repo root
RUN_ROOT = os.path.join(HERE, "runs")

if TESTS_ROOT not in sys.path:
    sys.path.insert(0, TESTS_ROOT)
import identity_refs as REF  # noqa: E402  (versioned identity-pack source; refs/versions.json)

PROMPTS_JSON = os.path.join(REPO, "specs", "image-generator-tests", "TEST-MATRIX-PROMPTS.json")
ID_SUITE = os.path.join(REPO, "specs", "image-generator-tests", "identity-two-character")
MASKS_DIR = os.path.join(ID_SUITE, "masks")

ENDPOINT_ID = "yhae6ihkabyb0o"   # GitHub-Integration BigLust endpoint (was ovwnwol2o30grn)
BASE = f"https://api.runpod.ai/v2/{ENDPOINT_ID}"
CHECKPOINT = "bigLust_v16.safetensors"
NEGATIVE = ""

# ---- identity -> ref / mask mapping (same as run_biglust_identity.py, proven) ----

# 2-person position masks: (dean_mask, becky_mask)
POS_MASKS = {
    "missionary":  ("c4_top.png", "c4_bottom.png"),
    "doggy":       ("c1_left.png", "c1_right.png"),
    "cowgirl":     ("c4_bottom.png", "c4_top.png"),
    "fellatio":    ("c4_top.png", "c4_bottom.png"),
    "cunnilingus": ("c4_bottom.png", "c4_top.png"),
}
# 2-person position refs: (dean_ref, becky_ref) - angle-matched to how the head appears
POS_REFS = {
    "missionary":  ("dean_front", "becky_front"),
    "doggy":       ("dean_front", "becky_profl"),
    "cowgirl":     ("dean_front", "becky_front"),
    "fellatio":    ("dean_front", "becky_profl"),
    "cunnilingus": ("dean_profl", "becky_front"),
}
# SFW standing 2-person (side by side): dean left / becky right
SFW2P_MASKS = ("c1_left.png", "c1_right.png")
SFW2P_REFS = ("dean_front", "becky_front")
# solo pose -> ref override (per run_biglust_identity.py solo mapping)
SOLO_REF_OVERRIDES = {"upskirt": "becky_34r"}

# Multi-angle SFW cells (the unique identity-ma set from run_biglust_identity.py, c1-c6 only):
# angle-matched Dean/Becky refs + per-cell regional masks, IP-Adapter only. The SFW 2-person
# text composition baseline is already covered by sfw-identity-2p-text, so no -text variant here.
# (cell_id, dean_stem, becky_stem, dean_mask, becky_mask, strength_a, strength_b, prompt)
MA_CELLS = [
    ("identity-ma-c1", "dean_front", "becky_front", "c1_left.png", "c1_right.png", 0.8, 0.6,
     "photo (medium), 8k, high quality, cinematic, 35mm shot of two people standing side by side "
     "facing the camera, the man on the left, the woman on the right, full body, casual summer "
     "clothes at a campground"),
    ("identity-ma-c2", "dean_34r", "becky_34l", "c2_left.png", "c2_right.png", 0.8, 0.6,
     "photo (medium), 8k, high quality, cinematic, 35mm shot of a man and a woman facing each other "
     "closely, the man on the left facing right, the woman on the right facing left, "
     "full body, campground"),
    ("identity-ma-c3", "dean_34r", "becky_34l", "c3_left.png", "c3_right.png", 0.8, 0.6,
     "photo (medium), 8k, high quality, cinematic, 35mm shot of a man and a woman embracing, "
     "the man on the left facing right, the woman on the right facing left, full body, campground"),
    ("identity-ma-c4", "dean_front", "becky_front", "c4_top.png", "c4_bottom.png", 0.8, 0.6,
     "photo (medium), 8k, high quality, cinematic, 35mm shot of exactly two people, one man and one "
     "woman, the man standing behind the seated woman, the man upper, the woman lower, "
     "full body, campground"),
    ("identity-ma-c5", "dean_front", "becky_front", "c5_left.png", "c5_right.png", 0.8, 0.6,
     "photo (medium), 8k, high quality, cinematic, 35mm shot of a man behind a woman, two-shot, "
     "the man on the left, the woman on the right, full body, campground"),
    ("identity-ma-c6", "dean_profl", "becky_profl", "c6_left.png", "c6_right.png", 0.9, 0.7,
     "photo (medium), 8k, high quality, cinematic, 35mm side profile two-shot of exactly two people, "
     "a man on the left and a woman on the right, both facing left in profile, "
     "full body, campground"),
]


# When set (a real run), each resolved ref is flattened to a unique '<char>_<view>'
# copy so multi-character uploads keep distinct LoadImage names (the versioned pack
# folders all use the same view filename, e.g. front.png, under dean/ and becky/).
_STAGE_DIR = None


def resolve(stem):
    """Resolve a '<char>_<view>' identity ref to the ACTIVE pack file; exit on missing."""
    try:
        p = REF.resolve_ref(stem)
        return REF.stage(stem, _STAGE_DIR) if _STAGE_DIR else p
    except RuntimeError as e:
        print("MISSING REF:", e)
        sys.exit(1)


def read_api_key():
    env_file = os.path.join(REPO, "helpers", "runpod", ".runpod-env.ps1")
    with open(env_file, encoding="utf-8") as f:
        for line in f:
            m = re.search(r'\$env:RUNPOD_API_KEY\s*=\s*"([^"]+)"', line)
            if m:
                return m.group(1)
    raise RuntimeError("RUNPOD_API_KEY not found in " + env_file)


def build_text_workflow(cell_id, seed, prompt):
    """Plain T2I - same graph as biglust-t2i.json (no IP-Adapter)."""
    return {
        "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": CHECKPOINT}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["4", 1]}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["4", 1]}},
        "5": {"class_type": "EmptyLatentImage", "inputs": {"width": 832, "height": 1216, "batch_size": 1}},
        "3": {"class_type": "KSampler",
              "inputs": {"model": ["4", 0], "positive": ["6", 0], "negative": ["7", 0],
                         "latent_image": ["5", 0], "seed": seed, "steps": 50, "cfg": 7.0,
                         "sampler_name": "dpmpp_2m_sde", "scheduler": "sgm_uniform", "denoise": 1.0}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {"filename_prefix": cell_id + "-text", "images": ["8", 0]}},
    }


def build_single_ip_workflow(cell_id, seed, ref_name, prompt, weight=0.8):
    return {
        "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": CHECKPOINT}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["4", 1]}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["4", 1]}},
        "5": {"class_type": "EmptyLatentImage", "inputs": {"width": 832, "height": 1216, "batch_size": 1}},
        "10": {"class_type": "IPAdapterUnifiedLoader", "inputs": {"model": ["4", 0], "preset": "PLUS FACE (portraits)"}},
        "11": {"class_type": "LoadImage", "inputs": {"image": ref_name}},
        "12": {"class_type": "IPAdapter",
               "inputs": {"model": ["10", 0], "ipadapter": ["10", 1], "image": ["11", 0],
                          "weight": weight, "weight_type": "standard", "start_at": 0.0, "end_at": 1.0}},
        "3": {"class_type": "KSampler",
              "inputs": {"model": ["12", 0], "positive": ["6", 0], "negative": ["7", 0],
                         "latent_image": ["5", 0], "seed": seed, "steps": 50, "cfg": 7.0,
                         "sampler_name": "dpmpp_2m_sde", "scheduler": "sgm_uniform", "denoise": 1.0}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {"filename_prefix": cell_id + "-ip", "images": ["8", 0]}},
    }


def build_multi_ip_workflow(cell_id, seed, dean_ref, becky_ref, left_mask, right_mask, prompt,
                            strength_a=0.8, strength_b=0.6):
    return {
        "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": CHECKPOINT}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["4", 1]}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["4", 1]}},
        "5": {"class_type": "EmptyLatentImage", "inputs": {"width": 832, "height": 1216, "batch_size": 1}},
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
                         "latent_image": ["5", 0], "seed": seed, "steps": 50, "cfg": 7.0,
                         "sampler_name": "dpmpp_2m_sde", "scheduler": "sgm_uniform", "denoise": 1.0}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {"filename_prefix": cell_id + "-ip", "images": ["8", 0]}},
    }


def execute_variant(v_id, suite_id, pose, seed, prompt, wf, files, images_out, prompts_out,
                    run_dir, headers, results, dry_run):
    """Submit one workflow variant and record it. Shared by the AB matrix cells and the
    multi-angle identity-ma cells so everything lands in the SAME run folder."""
    if dry_run:
        print(f"WOULD-RUN {v_id} (suite={suite_id} seed={seed}) -> {run_dir}")
        return
    prompt_path = os.path.join(prompts_out, f"{v_id}.json")
    with open(prompt_path, "w", encoding="utf-8") as f:
        f.write(json.dumps(wf, indent=2))
    print(f"\n=== {v_id} : {pose} (seed {seed}) ===")
    images = []
    for fpath in files:
        images.append({"name": os.path.basename(fpath), "image": b64_image(fpath)})
        print(f"    ref/mask: {os.path.basename(fpath)}")
    try:
        job = submit(wf, images, headers)
    except Exception as e:
        print(f"    SUBMIT ERROR: {e}")
        results.append({"id": v_id, "suite": suite_id, "pose": pose, "variant": v_id.rsplit("-", 1)[-1],
                        "seed": seed, "result": "FAIL", "jobId": "", "image": "", "sha256": ""})
        return
    job_id = job["id"]
    print(f"    submitted {job_id}")
    st = poll(job_id, headers)
    img_path = ""
    sha = ""
    result = "FAIL"
    if st.get("status") == "COMPLETED" and st.get("output", {}).get("images"):
        data = st["output"]["images"][0].get("data", "")
        if data:
            if data.startswith("data:"):
                data = data.split(",", 1)[1]
            raw = base64.b64decode(data)
            out_file = os.path.join(images_out, f"{v_id}.png")
            with open(out_file, "wb") as f:
                f.write(raw)
            img_path = os.path.relpath(out_file, REPO).replace("\\", "/")
            sha = hashlib.sha256(raw).hexdigest().upper()
            print(f"    SAVED: {img_path}")
            result = "PASS"
    print(f"    {result} (status={st.get('status')})")
    results.append({"id": v_id, "suite": suite_id, "pose": pose, "variant": v_id.rsplit("-", 1)[-1],
                    "seed": seed, "prompt": prompt, "result": result,
                    "jobId": job_id, "image": img_path, "sha256": sha})


def b64_image(path):
    with open(path, "rb") as f:
        data = f.read()
    ext = os.path.splitext(path)[1].lower().lstrip(".")
    mime = "image/jpeg" if ext in ("jpg", "jpeg") else "image/png"
    return f"data:{mime};base64,{base64.b64encode(data).decode()}"


def post_json(url, body, headers):
    req = urllib.request.Request(url, data=json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json", **headers})
    with urllib.request.urlopen(req, timeout=120) as resp:
        return json.loads(resp.read().decode())


def get_json(url, headers):
    req = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(req, timeout=120) as resp:
        return json.loads(resp.read().decode())


def submit(workflow, images, headers):
    body = {"input": {"workflow": workflow, "images": images}}
    return post_json(BASE + "/run", body, headers)


def poll(job_id, headers):
    deadline = time.time() + 600
    while time.time() < deadline:
        time.sleep(10)
        st = get_json(BASE + f"/status/{job_id}", headers)
        print(f"    poll {st.get('status')}")
        if st.get("status") in ("COMPLETED", "FAILED", "CANCELLED", "TIMED_OUT"):
            return st
    return {"status": "TIMEOUT"}


def rseed():
    return random.randint(1, 2**31 - 1)


def main():
    global _STAGE_DIR
    ap = argparse.ArgumentParser()
    ap.add_argument("--label", default="")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    headers = {"Authorization": "Bearer " + read_api_key()}

    ref_src = REF.source_report()
    print("Identity ref source (refs/versions.json):")
    for _char, _info in ref_src.items():
        print(f"  {_char} -> {_info['dir']}  (pack {_info['version']})")

    stamp = datetime.datetime.now().strftime("%Y-%m-%d_%H%M%S")
    run_name = f"{stamp}-ab" + (f"-{args.label}" if args.label else "")
    run_dir = os.path.join(RUN_ROOT, run_name)
    images_out = os.path.join(run_dir, "images")
    prompts_out = os.path.join(run_dir, "prompts")

    with open(PROMPTS_JSON, encoding="utf-8") as f:
        matrix = json.load(f)

    if not args.dry_run:
        os.makedirs(images_out, exist_ok=True)
        os.makedirs(prompts_out, exist_ok=True)
        _STAGE_DIR = os.path.join(
            os.environ.get("DG_TMP", os.path.join(REPO, "artifacts", "tmp")),
            "biglust-refs", stamp)
        os.makedirs(_STAGE_DIR, exist_ok=True)

    results = []

    for suite in matrix["suites"]:
        for cell in suite["cells"]:
            cid = cell["id"]
            identity = cell.get("identity")   # None | "dean" | "becky" | "dean+becky"
            pose = cell.get("pose", "")
            prompt = cell["prompt"]
            seed = rseed()

            # Always run the -text variant.
            text_wf = build_text_workflow(cid, seed, prompt)
            variants = [("text", text_wf, [])]

            # If the cell carries identity, also run the -ip variant with the SAME seed.
            if identity:
                if identity == "dean+becky":
                    # 2-person: refs + regional masks by pose.
                    if cid == "sfw-identity-2p" or "neutral" in pose or "side by side" in prompt:
                        dr_s, br_s = SFW2P_REFS
                        dm, bm = SFW2P_MASKS
                    else:
                        # map pose name out of the id (e.g. stock-nsfw-identity-missionary)
                        pname = None
                        for p in POS_MASKS:
                            if p in cid:
                                pname = p
                                break
                        if pname is None:
                            print(f"WARN: no pose map for {cid}, defaulting to c1"); pname = "missionary"
                        dr_s, br_s = POS_REFS[pname]
                        dm, bm = POS_MASKS[pname]
                    dr = resolve(dr_s)
                    br = resolve(br_s)
                    wf = build_multi_ip_workflow(cid, seed, dr, br, dm, bm, prompt)
                    files = [dr, br,
                             os.path.join(MASKS_DIR, dm), os.path.join(MASKS_DIR, bm)]
                else:
                    # 1-person: dean or becky (solo pose ref override if any).
                    stem = "dean_front" if identity == "dean" else "becky_front"
                    for solo_stem, override in SOLO_REF_OVERRIDES.items():
                        if solo_stem in cid:
                            stem = override
                            break
                    ref = resolve(stem)
                    wf = build_single_ip_workflow(cid, seed, os.path.basename(ref), prompt)
                    files = [ref]
                variants.append(("ip", wf, files))

            for variant, wf, files in variants:
                v_id = f"{cid}-{variant}"
                execute_variant(v_id, suite["id"], pose, seed, prompt, wf, files,
                                images_out, prompts_out, run_dir, headers, results, args.dry_run)

    # Multi-angle SFW cells (the unique identity-ma set, c1-c6 only): angle-matched Dean/Becky refs
    # + per-cell regional masks, IP-Adapter only (no -text; SFW 2-person text is sfw-identity-2p-text).
    for cid, dean_stem, becky_stem, dm, bm, sa, sb, ma_prompt in MA_CELLS:
        dr = resolve(dean_stem)
        br = resolve(becky_stem)
        v_id = f"{cid}-ip"
        seed = rseed()
        wf = build_multi_ip_workflow(v_id, seed, dr, br, dm, bm, ma_prompt,
                                     strength_a=sa, strength_b=sb)
        execute_variant(v_id, "multiangle-sfw", "multiangle composition", seed, ma_prompt, wf,
                        [dr, br, os.path.join(MASKS_DIR, dm), os.path.join(MASKS_DIR, bm)],
                        images_out, prompts_out, run_dir, headers, results, args.dry_run)

    if args.dry_run:
        return

    print("\n=== SUMMARY ===")
    for r in results:
        print(f"  {r['result']} {r['id']} (seed {r['seed']})")
    manifest = {
        "suite": "biglust-ab",
        "model": matrix.get("model"),
        "runDir": run_dir,
        "generatedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "endpointId": ENDPOINT_ID,
        "refSource": ref_src,
        "note": "text = prose-only identity (no refs); ip = same prompt + same seed + IP-Adapter refs. Matched seeds make text-vs-ip a clean A/B.",
        "cells": results,
    }
    with open(os.path.join(run_dir, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
    print(f"\nmanifest -> {os.path.join(run_dir, 'manifest.json')}")
    failed = [r for r in results if r["result"] == "FAIL"]
    if failed:
        print(f"RESULT: {len(failed)} FAILED ({', '.join(r['id'] for r in failed)})")
        sys.exit(1)
    print(f"RESULT: all {len(results)} variants passed.")


if __name__ == "__main__":
    main()
