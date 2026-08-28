#!/usr/bin/env python3
"""Generate the 2-person pack position workflows from the generic baseline.

Reads the model-agnostic baseline positions (specs/image-generator-tests/
baseline/positions/*.json) and, for every 2-person (1M1F) position, builds a
Dean+Becky identity-conditioned workflow: regional IP-Adapter "PLUS FACE"
(Dean weight 0.8, Becky weight 0.6), the baseline SDXL variant adapted to name
both characters, and a per-position regional mask default.

This is the "new separate test case" for the 2-person pack — the baseline
prompts are the starting point and are modified here to support the two-
character identity pack. The exact SDXL variant is preserved so a plain
Juggernaut run of the same prompt remains comparable.

Regenerate:
  python dump_positions.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))       # .../identity-two-character
SUITE = os.path.dirname(HERE)                            # .../image-generator-tests
BASELINE_DIR = os.path.join(SUITE, "baseline", "positions")
OUT_PROMPTS = os.path.join(HERE, "positions", "prompts")
RUNNERS = os.path.join(HERE, "runners")

sys.path.insert(0, RUNNERS)
import run_matrix  # reuse the regional workflow graph

NEGATIVE = run_matrix.NEGATIVE

# Per-position regional mask default. We reuse the existing matrix masks where
# the geometry matches; each entry is (Dean_mask, Becky_mask). These are
# STARTING defaults and must be validated/adjusted per position on review.
# c4_top/c4_bottom = upper/lower band; c1_* = left/right halves.
POSITION_MASKS = {
    "juggernaut-nsfw-69-test": ("c4_bottom.png", "c4_top.png"),                 # man below, woman above
    "juggernaut-nsfw-cowgirl-test": ("c4_bottom.png", "c4_top.png"),             # man below, woman riding
    "juggernaut-nsfw-cowgirl-penetration-closeup-test": ("c4_bottom.png", "c4_top.png"),
    "juggernaut-nsfw-missionary-test": ("c4_top.png", "c4_bottom.png"),          # man on top
    "juggernaut-nsfw-missionary-penetration-closeup-test": ("c4_top.png", "c4_bottom.png"),
    "juggernaut-nsfw-doggy-test": ("c1_right.png", "c1_left.png"),               # man behind (right)
    "juggernaut-nsfw-doggy-penetration-closeup-test": ("c1_right.png", "c1_left.png"),
    "juggernaut-nsfw-fellatio-test": ("c4_top.png", "c4_bottom.png"),            # man standing, woman kneeling
    "juggernaut-nsfw-reverse-cowgirl-test": ("c4_bottom.png", "c4_top.png"),
    "juggernaut-nsfw-reverse-cowgirl-penetration-closeup-test": ("c4_bottom.png", "c4_top.png"),
    "juggernaut-nsfw-spooning-test": ("c1_right.png", "c1_left.png"),            # man behind
    "juggernaut-nsfw-spooning-penetration-closeup-test": ("c1_right.png", "c1_left.png"),
    "juggernaut-nsfw-standing-test": ("c1_right.png", "c1_left.png"),            # man behind hip contact
    "juggernaut-nsfw-standing-penetration-closeup-test": ("c1_right.png", "c1_left.png"),
    "juggernaut-nsfw-cumshot-facial-test": ("c1_right.png", "c1_left.png"),      # man out-of-frame; Becky face focus
    "juggernaut-nsfw-cumshot-in-mouth-test": ("c1_right.png", "c1_left.png"),
    "juggernaut-nsfw-cumshot-on-body-test": ("c1_right.png", "c1_left.png"),
    "juggernaut-nsfw-cumshot-creampie-test": ("c1_right.png", "c1_left.png"),
}

# Per-position ANGLE-MATCHED reference face (pack view), the same pattern as
# run_multiangle.py. Each position uses the pack view that matches the target
# head orientation, instead of always the front face. This fixes the "odd
# pictures" from forcing a frontal ref onto turned-head poses.
# Views: front / 34l / 34r / profl / profr (per character in refs/multiangle/).
POSITION_REFS = {
    "juggernaut-nsfw-69-test": ("dean_34l", "becky_34l"),          # heads turned toward each other's groin (side)
    "juggernaut-nsfw-cowgirl-test": ("dean_front", "becky_front"),  # face to face, both near-frontal
    "juggernaut-nsfw-cowgirl-penetration-closeup-test": ("dean_front", "becky_front"),
    "juggernaut-nsfw-missionary-test": ("dean_front", "becky_front"),   # face to face
    "juggernaut-nsfw-missionary-penetration-closeup-test": ("dean_front", "becky_front"),
    "juggernaut-nsfw-doggy-test": ("dean_front", "becky_34r"),      # woman's head turned away from camera
    "juggernaut-nsfw-doggy-penetration-closeup-test": ("dean_front", "becky_34r"),
    "juggernaut-nsfw-fellatio-test": ("dean_front", "becky_34l"),    # woman's head tilted up toward the penis
    "juggernaut-nsfw-reverse-cowgirl-test": ("dean_front", "becky_34r"),  # woman facing away
    "juggernaut-nsfw-reverse-cowgirl-penetration-closeup-test": ("dean_front", "becky_34r"),
    "juggernaut-nsfw-spooning-test": ("dean_34l", "becky_34l"),      # both heads turned to the side (same direction)
    "juggernaut-nsfw-spooning-penetration-closeup-test": ("dean_34l", "becky_34l"),
    "juggernaut-nsfw-standing-test": ("dean_front", "becky_front"),   # face to face
    "juggernaut-nsfw-standing-penetration-closeup-test": ("dean_front", "becky_front"),
    "juggernaut-nsfw-cumshot-facial-test": ("dean_front", "becky_front"),   # Becky's face is the focus, facing camera
    "juggernaut-nsfw-cumshot-in-mouth-test": ("dean_front", "becky_front"),
    "juggernaut-nsfw-cumshot-on-body-test": ("dean_front", "becky_front"),
    "juggernaut-nsfw-cumshot-creampie-test": ("dean_front", "becky_front"),
}

# 2-person (1M1F) baseline positions only. Non-2-person positions (MFF/MMF/orgy/
# double-facial) cannot be tested with the two-character identity pack.
def load_two_person():
    out = []
    for fn in sorted(os.listdir(BASELINE_DIR)):
        if not fn.endswith(".json"):
            continue
        with open(os.path.join(BASELINE_DIR, fn), encoding="utf-8") as f:
            p = json.load(f)
        if p["actors"] == "1M1F":
            out.append(p)
    return out


def adapt_prompt(sdxl_variant):
    """Name both characters in the SDXL prompt (Dean + Becky) while keeping the
    original body of the position description and the identity pack in mind."""
    text = sdxl_variant
    for phrase in ("one adult man and one adult woman", "an adult man and an adult woman",
                   "an adult man finishing", "one adult man", "an adult man"):
        if phrase in text:
            text = text.replace(phrase, "Dean and Becky", 1)
            break
    # Keep "exactly two adult men" style phrases (double facial) untouched — not 1M1F anyway.
    return text


def build_one(p, dean_ref, becky_ref, masks_dir, seed):
    left_mask, right_mask = POSITION_MASKS[p["id"]]
    prompt = adapt_prompt(p["variants"]["sdxl-juggernaut"])
    wf = run_matrix.build_workflow(
        "c1", seed, dean_ref, becky_ref, masks_dir,
        checkpoint="juggernautXL_ragnarok.safetensors", strength=0.8,
    )
    # Overwrite the composition prompt + masks with the position-specific ones.
    wf["6"]["inputs"]["text"] = prompt
    wf["13"]["inputs"]["image"] = left_mask
    wf["14"]["inputs"]["image"] = right_mask
    wf["9"]["inputs"]["filename_prefix"] = f"two_char_position_{p['id'].replace('juggernaut-nsfw-','').replace('-test','')}"
    return {"prompt": wf, "client_id": f"2pack-position-{p['id']}-{seed}"}


def main():
    positions = load_two_person()
    # Angle-matched refs per position (from the FULL FACE PACKS). Each position
    # selects the pack view matching its head orientation — the same pattern as
    # run_multiangle.py — instead of forcing the front face everywhere.
    pack_refs = os.path.join(SUITE, "identity-two-character", "refs", "multiangle")
    masks_dir = os.path.join(SUITE, "identity-two-character", "masks")
    os.makedirs(OUT_PROMPTS, exist_ok=True)

    index = []
    for p in positions:
        if p["id"] not in POSITION_MASKS or p["id"] not in POSITION_REFS:
            print(f"SKIP (no mask/ref mapping): {p['id']}")
            continue
        dean_stem, becky_stem = POSITION_REFS[p["id"]]
        dean_ref = run_matrix.find_ref(pack_refs, dean_stem)
        becky_ref = run_matrix.find_ref(pack_refs, becky_stem)
        if dean_ref is None or becky_ref is None:
            print(f"MISSING ref {dean_stem}/{becky_stem} in {pack_refs}")
            sys.exit(1)
        seed = p["settings"]["seed"] or 20256
        obj = build_one(p, dean_ref, becky_ref, masks_dir, seed)
        path = os.path.join(OUT_PROMPTS, p["id"] + ".json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(obj, f, indent=2, ensure_ascii=False)
        index.append({"id": p["id"], "actors": p["actors"], "closeup": p["closeup"],
                      "path": f"positions/prompts/{p['id']}.json",
                      "seed": seed, "masks": POSITION_MASKS[p["id"]],
                      "refs": {"dean": os.path.basename(dean_ref), "becky": os.path.basename(becky_ref)}})
        print("wrote", os.path.relpath(path, HERE))

    with open(os.path.join(HERE, "positions", "index.json"), "w", encoding="utf-8") as f:
        json.dump({"suite": "identity-two-character-positions",
                   "count": len(index),
                   "note": "2-person pack position test using the FULL FACE PACKS with "
                           "ANGLE-MATCHED refs per position (POSITION_REFS) — same pattern as "
                           "run_multiangle.py. Dean+Becky regional IP-Adapter (PLUS FACE), Dean "
                           "0.8 / Becky 0.6. Masks and refs are starting defaults; validate per "
                           "position on review.",
                   "positions": index}, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"index written: {len(index)} two-person positions")


if __name__ == "__main__":
    main()
