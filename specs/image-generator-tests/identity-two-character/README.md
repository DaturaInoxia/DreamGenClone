# Identity — Two-Character Test Suite

Source-controlled test suite for the **two-character identity conditioning proof** in the B-032
Scene Image Generator epic. It records the exact prompts, settings, inputs, generated results,
scorecards, and research notes for testing that a single 1024×1024 render can hold **two distinct
character identities** (Dean + Becky) via regional IP-Adapter.

This suite exists because the original proof lived entirely under the git-ignored
`artifacts/tmp/two-character-proof/` — no prompts, results, or notes were tracked. This folder is
the durable, reusable record.

## Sub-suites

| Sub-suite | Folder | Cases | What it tests | Outcome (2026-08-26/27) |
|---|---|---|---|---|
| Matrix | `prompts/matrix` + `images/matrix` | 12 (6 cells × 2 seeds) | Regional IP-Adapter `PLUS FACE`, single frontal refs, weight 0.8 | **FAIL (strict gate)** — Dean loses identity in the angled cells (C2/C3); 10/12 pass; viable with near-frontal guardrail |
| FaceID probe | `prompts/faceid` + `images/faceid` | 6 (C1/C2/C3 × 2 seeds) | `IPAdapterFaceID` v2 alternative mechanism | **FAIL** — different face per angle, does not match PLUS FACE baseline |
| Multi-angle | `prompts/multiangle` + `images/multiangle` | 8 rendered | Angle-matched references (Option 1) + 4x-UltraSharp enhancement | **PENDING** — inconsistent across seeds on 2026-08-27; pod EXITED before completion |
| Positions | `positions/prompts` + `positions/index.json` | 18 (1M1F positions) | 2-person pack across explicit sexual positions (Dean+Becky) | **GENERATED** — workflows ready; images pending pod |

## 2-person pack position test

`positions/` is a **separate test case** for the two-character identity pack: it takes the position
prompts from the model-agnostic **baseline** (`../baseline/`, derived from `juggernaut/prompts/`),
names Dean + Becky, and applies per-character regional masks. See `positions/README.md`.

## Layout

```
identity-two-character/
├── README.md              # this overview
├── RUNBOOK.md             # how to run / re-run each sub-suite
├── manifest.json          # hashes, seeds, settings, scorecards
├── build_manifest.py      # regenerates manifest.json
├── dump_workflows.py      # regenerates prompts/*.json from the runners
├── runners/               # reusable, parameterized proof runners
│   ├── run_matrix.py          # 12-case regional matrix
│   ├── run_faceid_probe.py    # FaceID v2 probe
│   ├── run_multiangle.py      # multi-angle refs (Option 1)
│   └── run_upscale.py         # 4x-UltraSharp reference upscaler
├── prompts/
│   ├── matrix/            # exact workflow JSONs (12)
│   ├── faceid/            # exact workflow JSONs (6)
│   └── multiangle/        # exact workflow JSONs (16 declared)
├── images/
│   ├── matrix/            # generated results (12)
│   ├── faceid/            # generated results (6)
│   └── multiangle/        # generated results (8)
├── masks/                 # regional attn masks (c1..c6) + generator
├── refs/                  # canonical faces (dean_face, becky_face)
│   └── multiangle/        # 5-view angle-tagged refs per character
└── research/              # detailed notes & scorecards
```

## Rules

- Only committed assets here are source-controlled evidence. Transient re-runs belong under
  ignored `artifacts/tmp/`.
- Never point the runners at a pod with a different checkpoint/model stack than the recorded one.
- Update `manifest.json` (via `build_manifest.py`) whenever a prompt, image, or score changes.
- Mechanism decision records live in `specs/Planning/B-032-scene-image-generator/phase-2-character-identity/`
  (DECISIONS, scorecards); this folder is the reproducible evidence package.

## Related

- Spec: `specs/Planning/B-032-scene-image-generator/phase-2-character-identity/`
- Proof spec (multi-angle): `.../proofs/identity-conditioning/two-character-matrix/multi-angle/SPEC.md`
- Scorecard: `.../proofs/identity-conditioning/two-character-matrix/scorecard-2026-08-26.md`
- Backlog: `specs/Planning/backlog.md` (B-032)
