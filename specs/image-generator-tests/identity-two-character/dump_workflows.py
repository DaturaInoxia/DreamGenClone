#!/usr/bin/env python3
"""Generate the committed workflow JSONs for the identity-two-character suite.

Dumps the exact ComfyUI workflow (prompt + negative + sampler + seed + refs +
masks) for every test case in the matrix / faceid / multiangle sub-suites, using
the same runner modules as the live proof, so the committed prompts always match
the runner logic.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
RUNNERS = os.path.join(HERE, "runners")
PROMPTS = os.path.join(HERE, "prompts")
sys.path.insert(0, RUNNERS)

import run_matrix
import run_faceid_probe
import run_multiangle

SEEDS = [1001, 1002]
CELLS6 = ["c1", "c2", "c3", "c4", "c5", "c6"]
CELLS_MA = ["c1", "c2", "c3", "c4", "c5", "c6", "c2m", "c3m"]


def write(name, obj):
    path = os.path.join(PROMPTS, name)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=2, ensure_ascii=False)
    print("wrote", os.path.relpath(path, HERE))


def main():
    SUITE = HERE  # this file lives at the suite root (identity-two-character/)
    dean = run_matrix.find_ref(os.path.join(SUITE, "refs"), "dean_face")
    becky = run_matrix.find_ref(os.path.join(SUITE, "refs"), "becky_face")
    masks = os.path.join(SUITE, "masks")
    mdir = os.path.join(SUITE, "refs", "multiangle")

    # Matrix (12): single frontal refs, PLUS FACE regional, strength 0.8
    for cell in CELLS6:
        for seed in SEEDS:
            wf = run_matrix.build_workflow(cell, seed, dean, becky, masks, strength=0.8)
            write(f"matrix/{cell}_s{seed}.json",
                  {"prompt": wf, "client_id": f"two-char-{cell}-{seed}"})

    # FaceID probe (6): FACEID PLUS V2, lora 0.6 CPU, strength 0.8
    for cell in ["c1", "c2", "c3"]:
        for seed in SEEDS:
            wf = run_faceid_probe.build_workflow(cell, seed, dean, becky, masks,
                                                 strength=0.8, lora=0.6, provider="CPU")
            write(f"faceid/{cell}_s{seed}.json",
                  {"prompt": wf, "client_id": f"faceid-{cell}-{seed}"})

    # Multi-angle (8): angle-tagged refs, Dean 0.8 / Becky 0.6 (validated config)
    for cell in CELLS_MA:
        for seed in SEEDS:
            dean_ref, becky_ref, prompt = run_multiangle.CELLS[cell]
            base_cell = cell[:-1] if cell.endswith("m") else cell
            left_mask, right_mask = f"{base_cell}_left.png", f"{base_cell}_right.png"
            if base_cell == "c4":
                left_mask, right_mask = "c4_top.png", "c4_bottom.png"
            dl = run_multiangle.find_local(mdir, dean_ref)
            bl = run_multiangle.find_local(mdir, becky_ref)
            wf = run_multiangle.build_workflow(cell, seed, os.path.basename(dl), os.path.basename(bl),
                                               left_mask, right_mask, prompt,
                                               strength_a=0.8, strength_b=0.6)
            write(f"multiangle/{cell}_s{seed}.json",
                  {"prompt": wf, "client_id": f"multiangle-{cell}-{seed}"})


if __name__ == "__main__":
    main()
