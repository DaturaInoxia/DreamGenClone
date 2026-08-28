#!/usr/bin/env python3
"""Build manifest.json for the identity-single-character suite."""
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


manifest = {
    "suite": "identity-single-character",
    "purpose": "Single-character identity conditioning proof (Dean). Validated that a 1024x1024 "
               "render holds ONE character's identity from a single reference face via IP-Adapter "
               "PLUS FACE, before the two-character matrix was attempted.",
    "mechanism": "IP-Adapter PLUS FACE (portraits), weight 0.8 (selected); PuLID smoke-tested",
    "checkpoint": "juggernautXL_ragnarok.safetensors",
    "sampler": {"steps": 30, "cfg": 5.0, "sampler": "dpmpp_2m_sde", "scheduler": "karras", "denoise": 1.0},
    "size": {"width": 1024, "height": 1024},
    "proofPod": "7i2mutjmry5tkt (EXITED as of 2026-08-27)",
    "character": "Dean",
    "prompt": "a young man, head and shoulders portrait, looking at the camera, neutral "
              "expression, studio lighting, photorealistic, sharp focus",
    "outcome": {
        "date": "2026-08-26",
        "result": "IP-Adapter PLUS FACE (0.8) validated end-to-end - face matches reference. "
                  "PuLID smoke render captured. NSFW confirmed on proof pod.",
        "decision": "IP-Adapter PLUS FACE selected as the mechanism; single-reference semantics "
                    "(one face per render) established - multi-character requires regional "
                    "conditioning (see identity-two-character suite).",
    },
    "seeds": {
        "ipadapter": 20256,
        "pulid": 20256,
    },
    "assets": {
        "prompts": files_meta("prompts"),
        "imagesSmoke": files_meta(os.path.join("images", "smoke")),
        "refs": files_meta("refs"),
    },
}

with open(os.path.join(HERE, "manifest.json"), "w", encoding="utf-8") as f:
    json.dump(manifest, f, indent=2, ensure_ascii=False)
    f.write("\n")
print("manifest.json written")
