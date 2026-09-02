#!/usr/bin/env python3
"""
BigLust identity-conditioned matrix runner (serverless).

Runs the identity cells against the IP-Adapter-enabled BigLust endpoint
(img-biglust-serverless, official worker-comfyui contract: input.workflow +
input.images -> output.images). Each run is DATED for cross-run comparison:

    specs/image-generator-tests/biglust/runs/<yyyy-MM-dd_HHmmss>-identity/
      images/<cellId>.png
      prompts/<cellId>.json        (frozen workflow)
      manifest.json

Cells:
  - identity-single-character: IP-Adapter PLUS FACE, one ref, one person.
  - multiangle: regional IP-Adapter, angle-matched refs (Dean/Becky) + masks.
  - position: regional IP-Adapter, Dean+Becky front refs + masks, NSFW positions (A/B vs stock).
  - solo: IP-Adapter PLUS FACE, one ref, one person NSFW solo poses (Becky=female, Dean=male).

Refs are the DB-pulled latest identity packs (refs/multiangle/); masks live in
identity-two-character/masks/. See pull_latest_refs.py for the ref source.

Usage:
  python run_biglust_identity.py --dry-run
  python run_biglust_identity.py [--label v4]
"""
import argparse, base64, datetime, hashlib, json, os, random, re, sys, time, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))       # .../biglust
REPO = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))  # repo root
RUN_ROOT = os.path.join(HERE, "runs")

ID_SUITE = os.path.join(REPO, "specs", "image-generator-tests", "identity-two-character")
REF_DIR = os.path.join(ID_SUITE, "refs", "multiangle")
MASKS_DIR = os.path.join(ID_SUITE, "masks")

ENDPOINT_ID = "yhae6ihkabyb0o"  # 2026-09-01: recreated via GitHub Integration (was ovwnwol2o30grn)
BASE = f"https://api.runpod.ai/v2/{ENDPOINT_ID}"
CHECKPOINT = "bigLust_v16.safetensors"

# BigLust v1.6 best practice: no negative prompt. The author's own example images use
# an empty negative (or a tiny "score_1,score_2,score_3" tag); per user direction we run empty.
NEGATIVE = ""

# multiangle cells: cell -> (dean_ref_stem, becky_ref_stem, prompt)
CELLS = {
    "c1": ("dean_front", "becky_front",
           "photo (medium), 8k, high quality, cinematic, 35mm shot of two people standing side by side "
           "facing the camera, the man on the left, the woman on the right, full body, "
           "casual summer clothes at a campground"),
    "c2": ("dean_34r", "becky_34l",
           "photo (medium), 8k, high quality, cinematic, 35mm shot of a man and a woman facing each other "
           "closely, the man on the left facing right, the woman on the right facing left, "
           "full body, campground"),
    "c3": ("dean_34r", "becky_34l",
           "photo (medium), 8k, high quality, cinematic, 35mm shot of a man and a woman embracing, "
           "the man on the left facing right, the woman on the right facing left, full body, campground"),
    "c4": ("dean_front", "becky_front",
           "photo (medium), 8k, high quality, cinematic, 35mm shot of exactly two people, one man and "
           "one woman, the man standing behind the seated woman, the man upper, the woman lower, "
           "full body, campground"),
    "c5": ("dean_front", "becky_front",
           "photo (medium), 8k, high quality, cinematic, 35mm shot of a man behind a woman, two-shot, "
           "the man on the left, the woman on the right, full body, campground"),
    "c6": ("dean_profl", "becky_profl",
           "photo (medium), 8k, high quality, cinematic, 35mm side profile two-shot of exactly two people, "
           "a man on the left and a woman on the right, both facing left in profile, "
           "full body, campground"),
}

# Per-cell IP-Adapter strength overrides (Dean, Becky). PLUS FACE is frontal-optimized,
# so the pure-profile cell gets a stronger nudge (c6 fix attempt; see review notes).
STRENGTH_OVERRIDES = {"c6": (0.9, 0.7)}

VIEW_STEM_TO_NAME = {
    "front": "front", "34l": "three-quarter left", "34r": "three-quarter right",
    "profl": "profile left", "profr": "profile right",
}


def find_local(ref_dir, stem):
    for ext in (".png", ".jpg", ".jpeg", ".webp"):
        p = os.path.join(ref_dir, stem + ext)
        if os.path.exists(p):
            return p
    return None


def read_api_key():
    env_file = os.path.join(REPO, "helpers", "runpod", ".runpod-env.ps1")
    with open(env_file, encoding="utf-8") as f:
        for line in f:
            m = re.search(r'\$env:RUNPOD_API_KEY\s*=\s*"([^"]+)"', line)
            if m:
                return m.group(1)
    raise RuntimeError("RUNPOD_API_KEY not found in " + env_file)


def build_single_workflow(cell_id, seed, ref_name, prompt):
    return {
        "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": CHECKPOINT}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["4", 1]}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["4", 1]}},
        "5": {"class_type": "EmptyLatentImage", "inputs": {"width": 832, "height": 1216, "batch_size": 1}},
        "10": {"class_type": "IPAdapterUnifiedLoader", "inputs": {"model": ["4", 0], "preset": "PLUS FACE (portraits)"}},
        "11": {"class_type": "LoadImage", "inputs": {"image": ref_name}},
        "12": {"class_type": "IPAdapter",
               "inputs": {"model": ["10", 0], "ipadapter": ["10", 1], "image": ["11", 0],
                          "weight": 0.8, "weight_type": "standard", "start_at": 0.0, "end_at": 1.0}},
        "3": {"class_type": "KSampler",
              "inputs": {"model": ["12", 0], "positive": ["6", 0], "negative": ["7", 0],
                         "latent_image": ["5", 0], "seed": seed, "steps": 50, "cfg": 7.0,
                         "sampler_name": "dpmpp_2m_sde", "scheduler": "sgm_uniform", "denoise": 1.0}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {"filename_prefix": cell_id, "images": ["8", 0]}},
    }


def build_multi_workflow(cell_id, seed, dean_ref, becky_ref, left_mask, right_mask, prompt,
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
        "9": {"class_type": "SaveImage", "inputs": {"filename_prefix": cell_id, "images": ["8", 0]}},
    }


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
    ap = argparse.ArgumentParser()
    ap.add_argument("--label", default="")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    api_key = read_api_key()
    headers = {"Authorization": f"Bearer {api_key}"}

    stamp = datetime.datetime.now().strftime("%Y-%m-%d_%H%M%S")
    run_name = f"{stamp}-identity" + (f"-{args.label}" if args.label else "")
    run_dir = os.path.join(RUN_ROOT, run_name)
    images_out = os.path.join(run_dir, "images")
    prompts_out = os.path.join(run_dir, "prompts")

    # ---- define the cells ----
    cells = []
    # single-character (one ref, one person)
    dean_front = find_local(REF_DIR, "dean_front")
    becky_front = find_local(REF_DIR, "becky_front")
    sd_dean = rseed()
    cells.append(("identity-1p-dean", "single", sd_dean,
                  build_single_workflow("identity-1p-dean", sd_dean, os.path.basename(dean_front),
                                        "photo (medium), 8k, high quality, cinematic, a young man, head and "
                                        "shoulders portrait, looking at the camera, wearing a plain crew-neck "
                                        "t-shirt, no collar, broad shoulders, short hair, stubble, neutral "
                                        "expression, studio lighting, sharp focus"),
                  [dean_front]))
    sd_becky = rseed()
    cells.append(("identity-1p-becky", "single", sd_becky,
                  build_single_workflow("identity-1p-becky", sd_becky, os.path.basename(becky_front),
                                        "photo (medium), 8k, high quality, cinematic, a young woman, head and "
                                        "shoulders portrait, looking at the camera, wearing a casual top, "
                                        "neutral expression, studio lighting, sharp focus"),
                  [becky_front]))
    # multiangle (angle-matched refs + masks)
    for cell in ("c1", "c2", "c3", "c4", "c5", "c6"):
        dean_stem, becky_stem, prompt = CELLS[cell]
        base_cell = cell[:-1] if cell.endswith("m") else cell
        left_mask, right_mask = f"{base_cell}_left.png", f"{base_cell}_right.png"
        if base_cell == "c4":
            left_mask, right_mask = "c4_top.png", "c4_bottom.png"
        dl = find_local(REF_DIR, dean_stem)
        bl = find_local(REF_DIR, becky_stem)
        if not dl or not bl:
            print(f"MISSING ref for {cell}: {dean_stem}/{becky_stem}")
            sys.exit(1)
        cell_id = f"identity-ma-{cell}"
        sa, sb = STRENGTH_OVERRIDES.get(cell, (0.8, 0.6))
        sd = rseed()
        wf = build_multi_workflow(cell_id, sd, dl, bl, left_mask, right_mask, prompt,
                                  strength_a=sa, strength_b=sb)
        cells.append((cell_id, "multiangle", sd, wf,
                      [dl, bl, os.path.join(MASKS_DIR, left_mask), os.path.join(MASKS_DIR, right_mask)]))

    # extra: waist-up SIDE-VIEW fellatio, both faces visible (profile refs, Dean upper / Becky lower).
    dl_f = find_local(REF_DIR, "dean_profl")
    bl_f = find_local(REF_DIR, "becky_profl")
    if dl_f and bl_f:
        sd_f = rseed()
        wf_f = build_multi_workflow(
            "identity-ma-fellatio-side", sd_f, dl_f, bl_f,
            "c4_top.png", "c4_bottom.png",
            "photo (medium), 8k, high quality, cinematic, 35mm waist-up shot, single continuous image, "
            "a man standing in profile and a woman kneeling in front of him performing fellatio on him, "
            "both faces clearly visible, correct anatomy, natural skin texture, soft warm light, "
            "sharp focus, highly detailed",
            strength_a=0.8, strength_b=0.7)
        cells.append(("identity-ma-fellatio-side", "multiangle", sd_f, wf_f,
                      [dl_f, bl_f, os.path.join(MASKS_DIR, "c4_top.png"), os.path.join(MASKS_DIR, "c4_bottom.png")]))

    # NSFW identity positions (Dean+Becky IP-Adapter). Same prompts as the stock T2I positions,
    # so identity vs non-identity is a clean A/B (only the refs differ). Masks are starting
    # defaults (top/bottom for vertical compositions, left/right for horizontal); validate on review.
    pos_prompts = {
        "missionary": "photo (medium), 8k, high quality, cinematic, explicit sex scene, one adult man and one adult woman on a bed, the man on top face to face in missionary position, his erect penis penetrating her vagina, full genital contact, correct penis and vagina anatomy, both fully nude, natural skin texture, soft warm light, 35mm lens, sharp focus, highly detailed",
        "doggy": "photo (medium), 8k, high quality, cinematic, explicit sex scene, one adult man and one adult woman on a bed, the woman on her hands and knees with her back arched, the man kneeling behind her, his erect penis visibly entering her vagina from behind, clear penetration, correct penis and vagina anatomy, both fully nude, natural skin texture, soft warm light, 35mm lens, sharp focus, highly detailed",
        "cowgirl": "photo (medium), 8k, high quality, cinematic, explicit sex scene, one adult man and one adult woman on a bed, the man lying on his back, the woman straddling him on top facing him in cowgirl position, lowering herself onto his erect penis, full genital contact, correct penis and vagina anatomy, both fully nude, natural skin texture, soft warm light, 35mm lens, sharp focus, highly detailed",
        "fellatio": "photo (medium), 8k, high quality, cinematic, explicit sex scene, one adult man and one adult woman, the woman kneeling in front of the standing man taking his erect penis into her mouth, fellatio oral sex, clear oral contact, correct penis anatomy, both fully nude, natural skin texture, soft warm light, 35mm lens, sharp focus, highly detailed",
        "cunnilingus": "photo (medium), 8k, high quality, cinematic, explicit sex scene, one adult man and one adult woman, the woman lying on her back with her legs open, the man positioned between her legs performing cunnilingus on her vagina, clear oral contact, correct anatomy, both fully nude, natural skin texture, soft warm light, 35mm lens, sharp focus, highly detailed",
    }
    pos_masks = {  # (dean_mask, becky_mask)
        "missionary": ("c4_top.png", "c4_bottom.png"),
        "doggy":      ("c1_left.png", "c1_right.png"),
        "cowgirl":    ("c4_bottom.png", "c4_top.png"),
        "fellatio":   ("c4_top.png", "c4_bottom.png"),
        "cunnilingus": ("c4_bottom.png", "c4_top.png"),
    }
    # Angle-matched refs per position. PLUS FACE is frontal-optimized, so we pick the
    # ref whose face orientation matches how that character's head actually appears:
    #   front = facing camera, 34l/34r = three-quarter, profl/profr = side profile.
    # (e.g. doggy -> the woman faces away/head to the side, not frontal; fellatio -> the
    # kneeling woman's head is in profile; cunnilingus -> the man's head is down in profile.)
    pos_refs = {  # (dean_ref, becky_ref)
        "missionary":  (dean_front, becky_front),
        "doggy":       (dean_front, find_local(REF_DIR, "becky_profl")),
        "cowgirl":     (dean_front, becky_front),
        "fellatio":    (dean_front, find_local(REF_DIR, "becky_profl")),
        "cunnilingus": (find_local(REF_DIR, "dean_profl"), becky_front),
    }
    for pos in ("missionary", "doggy", "cowgirl", "fellatio", "cunnilingus"):
        dm, bm = pos_masks[pos]
        dr, br = pos_refs[pos]
        if not dr or not br:
            print(f"MISSING position ref for {pos}")
            sys.exit(1)
        cid = f"identity-pos-{pos}"
        sd = rseed()
        wf = build_multi_workflow(cid, sd, dr, br, dm, bm, pos_prompts[pos],
                                  strength_a=0.8, strength_b=0.7)
        cells.append((cid, "position", sd, wf,
                      [dr, br, os.path.join(MASKS_DIR, dm), os.path.join(MASKS_DIR, bm)]))

    # solo NSFW identity (one ref, one person) — Becky = female, Dean = male.
    solo_cells = [
        ("identity-solo-becky-down-top", becky_front,
         "photo (medium), 8k, high quality, cinematic, one adult woman, top pulled down to expose her "
         "bare breasts, facing the camera, natural skin texture, soft warm light"),
        ("identity-solo-becky-upskirt", find_local(REF_DIR, "becky_34r"),
         "photo (medium), 8k, high quality, cinematic, one adult woman in a short skirt, low-angle "
         "upskirt view revealing her pelvis, looking over her shoulder, soft warm light"),
        ("identity-solo-becky-masturbation", becky_front,
         "photo (medium), 8k, high quality, cinematic, one adult woman fully nude on her back with her "
         "legs parted, touching herself between her legs, masturbation, correct anatomy, soft warm light"),
        ("identity-solo-dean-non-erect", dean_front,
         "photo (medium), 8k, high quality, cinematic, one adult man fully nude in a confident standing "
         "pose, flaccid non-erect penis, full body, correct anatomy, soft warm light"),
        ("identity-solo-dean-erect", dean_front,
         "photo (medium), 8k, high quality, cinematic, one adult man fully nude in a confident standing "
         "pose with an erect penis, full body, correct anatomy, soft warm light"),
        ("identity-solo-dean-cumshot", dean_front,
         "photo (medium), 8k, high quality, cinematic, one adult man fully nude masturbating with his "
         "hand on his erect penis, ejaculating cumshot, correct anatomy, soft warm light"),
    ]
    for cid, ref, prompt in solo_cells:
        sd = rseed()
        wf = build_single_workflow(cid, sd, os.path.basename(ref), prompt)
        cells.append((cid, "solo", sd, wf, [ref]))

    if args.dry_run:
        for cid, kind, sd, wf, files in cells:
            print(f"WOULD-RUN {cid} ({kind}, seed {sd}) -> {run_dir}")
            for f in files:
                print(f"    image: {os.path.basename(f)}")
        print(f"\nDRY RUN — {len(cells)} cells, no jobs submitted.")
        return

    os.makedirs(images_out, exist_ok=True)
    os.makedirs(prompts_out, exist_ok=True)

    results = []
    for cid, kind, sd, wf, files in cells:
        print(f"\n=== {cid} ({kind}, seed {sd}) ===")
        # freeze workflow
        with open(os.path.join(prompts_out, f"{cid}.json"), "w", encoding="utf-8") as f:
            json.dump(wf, f, indent=2)
        images = [{"name": os.path.basename(p), "image": b64_image(p)} for p in files]
        for im in images:
            print(f"    ref/mask: {im['name']} ({len(im['image'])} chars b64)")
        try:
            sub = submit(wf, images, headers)
        except Exception as e:
            print("    SUBMIT ERROR:", e)
            results.append({"id": cid, "suite": kind, "seed": sd, "result": "FAIL", "jobId": "", "image": "", "sha256": ""})
            continue
        job_id = sub.get("id")
        print(f"    submitted {job_id}")
        st = poll(job_id, headers)
        img_rel, sha = "", ""
        if st.get("status") == "COMPLETED":
            outs = (st.get("output") or {}).get("images") or []
            if outs and outs[0].get("data"):
                raw = outs[0]["data"]
                if raw.startswith("data:"):
                    raw = raw.split(",", 1)[1]
                out_file = os.path.join(images_out, f"{cid}.png")
                with open(out_file, "wb") as f:
                    f.write(base64.b64decode(raw))
                sha = hashlib.sha256(open(out_file, "rb").read()).hexdigest()
                img_rel = os.path.relpath(out_file, REPO).replace("\\", "/")
                print(f"    SAVED: {img_rel}")
                result = "PASS"
            else:
                print("    COMPLETED but no output image")
                result = "FAIL"
        else:
            print(f"    FAIL status={st.get('status')}")
            result = "FAIL"
        results.append({"id": cid, "suite": kind, "seed": sd, "result": result, "jobId": job_id,
                        "image": img_rel, "sha256": sha})

    manifest = {
        "suite": "biglust-identity-matrix",
        "model": f"BigLust v1.6 ({CHECKPOINT}, IP-Adapter PLUS FACE)",
        "runDir": os.path.relpath(run_dir, REPO).replace("\\", "/"),
        "generatedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat().replace("+00:00", "Z"),
        "cells": results,
    }
    with open(os.path.join(run_dir, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)

    print("\n=== SUMMARY ===")
    for r in results:
        print(f"  {r['result']:4} {r['id']} (seed {r['seed']})")
    failed = [r for r in results if r["result"] == "FAIL"]
    print(f"\nRESULT: {len(results) - len(failed)}/{len(results)} passed; manifest -> {manifest['runDir']}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
