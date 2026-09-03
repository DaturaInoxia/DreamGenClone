#!/usr/bin/env python3
"""Focused Dean 1-person validation driver.

Runs the sfw-identity-1p-dean cell (text + ip, matched seed) against Dean's
ACTIVE identity pack front — resolved by identity_refs from refs/versions.json
-> refs/dean/<version>/front.png — so we can verify the IP-Adapter output
matches the clean approved front without paying for the full consolidated suite.
The run folder name and manifest carry the pack version used.

Supports an IP-Adapter WEIGHT A/B: pass --weights w1,w2,... to render the ip
variant at several face weights with the SAME seed so only weight varies
(0.8 is the PLUS FACE default that showed asymmetric/too-wide eyes).

To validate a DIFFERENT Dean pack, change the version in refs/versions.json
(e.g. "v7" -> "v8"); no runner code change is needed.

Outputs to runs/<stamp>-dean-<version>-front/ (images + prompts).

Usage:
  python run_dean_1p_focused.py [--dry-run]
  python run_dean_1p_focused.py --weights 0.8,0.7,0.65,0.6 --seed 92576021
"""
import argparse, datetime, json, os, random, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))                    # biglust dir (run_biglust_ab)
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))   # tests root (identity_refs)
import run_biglust_ab as R  # noqa: E402  (reuse proven builders/executor)
import identity_refs as REF  # noqa: E402  (versioned identity-pack source; refs/versions.json)

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

    ref = REF.resolve_ref("dean_front")
    print(f"using ref: {ref} ({os.path.getsize(ref)} bytes)  [dean pack {REF.version_for('dean')}]")

    stamp = datetime.datetime.now().strftime("%Y-%m-%d_%H%M%S")
    dean_ver = REF.version_for("dean")
    run_name = f"{stamp}-dean-{dean_ver}-front"
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
        "suite": f"dean-{dean_ver}-front-focused",
        "model": R.CHECKPOINT,
        "runDir": run_dir,
        "generatedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "endpointId": R.ENDPOINT_ID,
        "refSource": REF.source_report(),
        "note": f"Focused sfw-identity-1p-dean text-vs-ip against dean pack {dean_ver} "
                f"({REF.version_dir('dean')}). Matched seed.",
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
