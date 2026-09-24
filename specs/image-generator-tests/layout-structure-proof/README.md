# Layout-structure proof (B-119 route C1)

Does a **layout reference image** contracted by **Depth** or **Canny** ControlNet hold a two-body
arrangement on an SDXL-family local checkpoint, when the prompt supplies a different location and
clothing? This folder is the executable proof for B-119 route C1, and it also carries the two
comparison arms that make the answer meaningful: **no structure** (text only) and **OpenPose**
(the existing app route, B-117).

**Result so far: mechanism PROVEN, reclining discriminator NOT yet proven.**
Read [`FINDINGS.md`](FINDINGS.md) first — it records the measured IoU table, the honest per-image
verdicts, the canny over-coupling caveat, and exactly what still needs a reclining reference.

## Run it

```powershell
# structure maps only (fast, no sampler jobs)
powershell -ExecutionPolicy RemoteSigned -File specs/image-generator-tests/layout-structure-proof/run-c1-structure-proof.ps1 -OnlyMaps

# full matrix: 2 checkpoints x {text, openpose, depth, canny} at one seed
powershell -ExecutionPolicy RemoteSigned -File specs/image-generator-tests/layout-structure-proof/run-c1-structure-proof.ps1 `
  -Reference <layout-reference.png> `
  -OutDir specs/image-generator-tests/layout-structure-proof/runs/<yyyyMMdd>-<label>

# measure how faithfully each render inherited the arrangement
python specs/image-generator-tests/layout-structure-proof/measure-arrangement-match.py `
  <layout-reference.png> specs/image-generator-tests/layout-structure-proof/runs/<run>
```

Prerequisites:

- The SDXL depth + canny weights must be installed on the ComfyUI host:
  `helpers/local-comfyui-host/install-sdxl-controlnets.ps1` (idempotent, hash-pinned).
  It is a **host** change, not a RunPod pod — documented in `docs/local-comfyui-model-manager-setup.md`.
- Renders are submitted through `helpers/local-comfyui-host/run-local-proof.ps1` (upload → `/prompt`
  → `/history` → `/view`, with a node-class preflight that fails fast on host drift).
- The measurement script needs `rembg` + `onnxruntime` in `.venv` (`isnet-general-use`).

## Layout

| Path | What |
|---|---|
| `run-c1-structure-proof.ps1` | Harness: extracts structures, builds every variant graph, delegates submission, writes `run-manifest.json` |
| `measure-arrangement-match.py` | Silhouette IoU + vertical/horizontal mass-profile correlation vs the reference |
| `runs/<run>/<variant>/workflow.json` | The exact submitted graph, per variant |
| `runs/<run>/00-maps/` | The extracted depth / canny / DWPose structures actually fed to ControlNet |
| `runs/<run>/arrangement-match.json` | Measured arrangement match for every render |

## Rules this harness follows

- One variable at a time: same seed, canvas, sampler, steps, CFG and prompt across all variants.
- No fallbacks: a variant that fails is reported as `failed` in the manifest and is never retried
  with different settings to make it look better.
- Every render is visually reviewed and reported honestly per image — a metric alone is not a verdict.
- Clothed, non-explicit prompts: the structure is the variable being tested, not anatomy.
