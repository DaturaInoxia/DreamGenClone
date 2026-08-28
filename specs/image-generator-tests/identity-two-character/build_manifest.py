#!/usr/bin/env python3
"""Build manifest.json for the identity-two-character suite.

Hashes every committed prompt / image / mask / ref and records the per-case
scorecard (identity/composition/quality/verdict) captured from the 2026-08-26
human review, plus mechanism settings and gate outcome. Run any time the suite
changes:

  python build_manifest.py
"""
import hashlib
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def files_meta(rel_dir):
    base = os.path.join(HERE, rel_dir)
    out = {}
    if not os.path.isdir(base):
        return out
    for name in sorted(os.listdir(base)):
        p = os.path.join(base, name)
        if os.path.isfile(p):
            out[name] = {"bytes": os.path.getsize(p), "sha256": sha256(p)}
    return out


# Matrix scorecard (2026-08-26 human review). Keys: cell -> seed -> (identityA, identityB,
# crossContam, composition, quality, verdict, note)
MATRIX_SCORES = {
    "c1": {1001: (4, 4, 2, 4, 4, "PASS", ""),
           1002: (4, 4, 2, 5, 4, "PASS", "better of the two")},
    "c2": {1001: (2, 4, 2, 3, 4, "Dean identity FAIL", ""),
           1002: (2, 4, 2, 3, 4, "Dean identity FAIL", "")},
    "c3": {1001: (2, 4, 2, 4, 4, "Dean identity FAIL", ""),
           1002: (2, 4, 2, 4, 4, "Dean identity FAIL", "")},
    "c4": {1001: (4, 4, 2, 4, 4, "PASS", ""),
           1002: (4, 4, 2, 4, 4, "PASS", "")},
    "c5": {1001: (4, 4, 2, 4, 4, "PASS", "depth works"),
           1002: (4, 4, 2, 2, 4, "Composition FAIL", "split-screen, not depth")},
    "c6": {1001: (4, 4, 2, 4, 4, "PASS", ""),
           1002: (4, 4, 2, 4, 4, "PASS", "")},
}

# Multi-angle scorecard (2026-08-27 human review). User feedback captured verbatim-ish.
MULTIANGLE_NOTES = {
    "c2": {1001: "Dean does not look like picture, Becky meh",
           1002: "Dean ok, Becky meh"},
    "c3": {1001: "both bad", 1002: "both bad"},
    "c2m": {1001: "n/a - not part of recorded review"},
    "c3m": {1001: "ok"},
    "c1": {1001: "control - baseline", 1002: "control - baseline"},
}

manifest = {
    "suite": "identity-two-character",
    "purpose": "Two-character identity conditioning proof for the scene image generator. "
               "Tests whether IP-Adapter regional conditioning can hold TWO distinct character "
               "identities (Dean + Becky) in a single 1024x1024 render, across poses.",
    "mechanism": "IP-Adapter PLUS FACE (portraits), regional attn_mask per character",
    "checkpoint": "juggernautXL_ragnarok.safetensors",
    "sampler": {"steps": 30, "cfg": 5.0, "sampler": "dpmpp_2m_sde", "scheduler": "karras", "denoise": 1.0},
    "size": {"width": 1024, "height": 1024},
    "proofPod": "7i2mutjmry5tkt (EXITED as of 2026-08-27)",
    "characters": {
        "Dean": {"canonicalFace": "refs/dean_face.png"},
        "Becky": {"canonicalFace": "refs/becky_face.jpg"},
    },
    "subSuites": {
        "matrix": {
            "name": "Two-character identity matrix",
            "description": "6 cells x 2 seeds = 12 cases, single frontal refs per character, "
                           "regional IP-Adapter at weight 0.8.",
            "gate": {
                "criterion": "Median identity >= 4 for both actors, cross-contamination <= 2, "
                             "no case below identity 3",
                "result": "FAIL (strict gate) - Dean identity collapses 4/12 (C2/C3 angled cells). "
                          "10/12 pass. Mechanism viable WITH near-frontal composition guardrail.",
                "decision": "Adopt IP-Adapter regional conditioning for P2-023 multi-actor compiler "
                            "restricted to near-frontal arrangements; C2/C3-style excluded.",
            },
            "scorecard": {
                f"c{cell}": {
                    f"s{seed}": {
                        "identityA": sc[0], "identityB": sc[1], "crossContamination": sc[2],
                        "composition": sc[3], "quality": sc[4], "verdict": sc[5], "note": sc[6],
                    } for seed, sc in seeds.items()
                } for cell, seeds in MATRIX_SCORES.items()
            },
        },
        "faceid": {
            "name": "IPAdapterFaceID v2 probe",
            "description": "6 cases (C1/C2/C3 x 2 seeds). Alternative mechanism probed to rescue "
                           "the angled cells. Recorded FAIL: different face per angle, does not "
                           "match PLUS FACE baseline.",
            "gate": {"result": "FAIL - not selected", "reason": "Identity inconsistent across angles."},
        },
        "multiangle": {
            "name": "Multi-angle reference proof (Option 1)",
            "description": "Each cell conditions with the angle-matched reference (3/4 refs for "
                           "angled heads). Dean weight 0.8, Becky 0.6. 4x-UltraSharp upscale of "
                           "low-res refs tested as enhancement.",
            "gate": {"result": "PENDING - not yet passed", "reason": "Inconsistent across seeds on "
                     "2026-08-27; Becky identity drifts between renders. Pod EXITED before "
                     "completion."},
            "reviewNotes": MULTIANGLE_NOTES,
        },
    },
    "assets": {
        "prompts": {
            "matrix": files_meta(os.path.join("prompts", "matrix")),
            "faceid": files_meta(os.path.join("prompts", "faceid")),
            "multiangle": files_meta(os.path.join("prompts", "multiangle")),
        },
        "images": {
            "matrix": files_meta(os.path.join("images", "matrix")),
            "faceid": files_meta(os.path.join("images", "faceid")),
            "multiangle": files_meta(os.path.join("images", "multiangle")),
        },
        "masks": files_meta("masks"),
        "refs": files_meta("refs"),
        "refsMultiangle": files_meta(os.path.join("refs", "multiangle")),
    },
}

with open(os.path.join(HERE, "manifest.json"), "w", encoding="utf-8") as f:
    json.dump(manifest, f, indent=2, ensure_ascii=False)
    f.write("\n")
print("manifest.json written")
