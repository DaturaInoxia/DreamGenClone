#!/usr/bin/env python3
"""Focused Dean 1-person validation driver.

Runs the sfw-identity-1p-dean cell (text + ip, matched seed) against the current
refs/multiangle/dean_front.png so we can verify IP-Adapter output matches the
clean v6 front without paying for the full 39-variant consolidated suite.

Supports an IP-Adapter WEIGHT A/B: pass --weights w1,w2,... to render the ip
variant at several face weights with the SAME seed so only weight varies
(0.8 is the PLUS FACE default that showed asymmetric/too-wide eyes).

Outputs to runs/<stamp>-deanv6-front/ (images + prompts).

Usage:
  python run_dean_1p_focused.py [--dry-run]
  python run_dean_1p_focused.py --weights 0.8,0.7,0.65,0.6 --seed 92576021
"""
import argparse, datetime, json, os, random, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import run_biglust_ab as R  # noqa: E402  (reuse proven builders/executor)

DEAN_PROMPT = (
    "photo (medium), 8k, high quality, cinematic, portrait of one adult man with "
    "short brown hair, stubble, blue eyes, and broad shoulders, wearing a t-shirt, "
    "standing facing the camera, neutral expression, soft studio lighting, 35mm "
    "lens, sharp focus, highly detailed"
)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--seed", type=int, default=None, help="fixed seed (default: random)")
    ap.add_argument("--weights", default="0.8",
                    help="comma-separated IP-Adapter face weights to A/B (default: 0.8)")
    args = ap.parse_args()
    weights = [float(w.strip()) for w in args.weights.split(",") if w.strip()]

    headers = {"Authorization": "Bearer " + R.read_api_key()}

    ref = R.find_local(R.REF_DIR, "dean_front")
    if not ref:
        print("MISSING dean_front ref in refs/multiangle")
        sys.exit(1)
    print(f"using ref: {ref} ({os.path.getsize(ref)} bytes)")

    stamp = datetime.datetime.now().strftime("%Y-%m-%d_%H%M%S")
    run_name = f"{stamp}-deanv7-front"
    run_dir = os.path.join(R.RUN_ROOT, run_name)
    images_out = os.path.join(run_dir, "images")
    prompts_out = os.path.join(run_dir, "prompts")
    if not args.dry_run:
        os.makedirs(images_out, exist_ok=True)
        os.makedirs(prompts_out, exist_ok=True)

    seed = args.seed if args.seed is not None else random.randint(1, 2**31 - 1)
    results = []

    # -text variant (prose identity only, no refs) as baseline
    text_wf = R.build_text_workflow("sfw-identity-1p-dean", seed, DEAN_PROMPT)
    R.execute_variant("sfw-identity-1p-dean-text", "sfw-identity", "neutral portrait",
                      seed, DEAN_PROMPT, text_wf, [], images_out, prompts_out,
                      run_dir, headers, results, args.dry_run)

    # -ip variants (same prompt + same seed + IP-Adapter dean_front) at each weight
    for weight in weights:
        ip_wf = R.build_single_ip_workflow("sfw-identity-1p-dean", seed,
                                           os.path.basename(ref), DEAN_PROMPT,
                                           weight=weight)
        wlabel = str(weight).replace(".", "")
        v_id = f"sfw-identity-1p-dean-ip-w{wlabel}" if len(weights) > 1 else "sfw-identity-1p-dean-ip"
        R.execute_variant(v_id, "sfw-identity", f"neutral portrait (ip weight {weight})",
                          seed, DEAN_PROMPT, ip_wf, [ref], images_out, prompts_out,
                          run_dir, headers, results, args.dry_run)

    if args.dry_run:
        return

    print("\n=== SUMMARY ===")
    for r in results:
        print(f"  {r['result']} {r['id']} (seed {r['seed']})")
    manifest = {
        "suite": "deanv6-front-focused",
        "model": R.CHECKPOINT,
        "runDir": run_dir,
        "generatedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "endpointId": R.ENDPOINT_ID,
        "note": "Focused sfw-identity-1p-dean text-vs-ip after v6 front swap "
                "(dean_front = Real-ESRGAN upscaled clean PNG). Matched seed.",
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
