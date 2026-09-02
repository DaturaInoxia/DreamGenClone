# Identity — Single-Character Test Suite

Source-controlled evidence for the **single-character identity render proof** (Dean) — the proof
that started the identity investigation. It records that a 1024×1024 render can hold **one
character's identity** from a single reference face via IP-Adapter `PLUS FACE (portraits)` (and
PuLID), before the two-character matrix was attempted.

## What's tested

- **IP-Adapter `PLUS FACE (portraits)`**, weight 0.8 — the selected mechanism.
- **PuLID** (`ip-adapter_pulid_sdxl_fp16.safetensors`, CPU InsightFace) — alternate mechanism,
  smoke-tested.
- Baseline Juggernaut NSFW render (no identity conditioning) for reference.
- Prompt: head-and-shoulders portrait, single person (single-reference semantics — one face per
  render).

## Outcome (2026-08-26)

- IP-Adapter `PLUS FACE` (0.8) **validated end-to-end** — Dean's face matches the uploaded
  reference. This became the selected mechanism for the two-character work.
- PuLID smoke render captured as evidence.
- NSFW confirmed on the proof pod (provider policy `AdultAllowed`, no SFW clamp).

## Layout

```
identity-single-character/
├── README.md
├── RUNBOOK.md
├── manifest.json
├── build_manifest.py
├── runners/
│   └── run_single.py        # reusable, parameterized single-char runner
├── prompts/
│   ├── ipadapter-single-dean.json   # frozen proof workflow
│   └── pulid-single-dean.json       # alternate mechanism
├── images/
│   └── smoke/               # ipadapter + pulid renders + juggernaut baseline
└── refs/                    # dean_face.png, dean_fullbody.png
```

## Related

- Two-character continuation: `specs/image-generator-tests/identity-two-character/`
- Decision record: `specs/Planning/B-032-scene-image-generator/phase-2-character-identity/DECISIONS-2026-08-26.md`
