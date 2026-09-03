# Identity Two-Character Proof — Runbook

How to run each sub-suite of `specs/image-generator-tests/identity-two-character/` against a
ComfyUI pod. All runners are parameterized — they default to the proof pod
`https://7i2mutjmry5tkt-3000.proxy.runpod.net` and the suite's own inputs, and every path can be
overridden with `--base`, `--out`, `--masks`, `--ref-dir`.

## Prerequisites

- A running ComfyUI pod (the proof pod `7i2mutjmry5tkt` is currently **EXITED** — start it via
  `helpers/runpod/pod.ps1 -Action start -PodId 7i2mutjmry5tkt` or a migrated successor).
- The pod must carry:
  - `juggernautXL_ragnarok.safetensors` (checkpoint)
  - IP-Adapter custom nodes with `PLUS FACE (portraits)` preset (and `FACEID PLUS V2` for the probe)
  - `4x-UltraSharp.pth` (for the upscale step)
- Python 3 with `PIL` for the mask generator. Runners use only the stdlib otherwise.
- A `User-Agent: Mozilla/5.0 (compatible; DreamGenClone/1.0)` header is required on every request
  to the pod (403 without it) — the runners already send it.

## 1. Matrix (12 cases)

Single frontal reference per character, regional `attn_mask`, weight 0.8.

```powershell
$py = 'd:\src\DreamGenClone\.venv\Scripts\python.exe'
$suite = 'd:\src\DreamGenClone\specs\image-generator-tests\identity-two-character'

# Full 12-case run
& $py "$suite\runners\run_matrix.py" --all --seeds 1001 1002

# Single case
& $py "$suite\runners\run_matrix.py" --cell c2 --seed 1001 --strength 0.8

# Print the exact workflow JSON (matches prompts/matrix/c2_s1001.json)
& $py "$suite\runners\run_matrix.py" --cell c2 --seed 1001 --dump-json
```

Outputs land in `artifacts/tmp/two-character-proof/outputs/` (overridable with `--out`).

## 2. FaceID probe (6 cases)

```powershell
& $py "$suite\runners\run_faceid_probe.py" --cell c2 --seed 1001 --strength 0.8 --lora 0.6 --provider CPU
& $py "$suite\runners\run_faceid_probe.py" --all --seeds 1001 1002
```

Outputs → `artifacts/tmp/two-character-proof/outputs-faceid/`.

## 3. Multi-angle (Option 1)

Angle-matched references. List what the suite expects first, then run:

```powershell
& $py "$suite\runners\run_multiangle.py" --list

# Focused pass (the previously-failing cells + control)
& $py "$suite\runners\run_multiangle.py" --cell c2 --seed 1001 --strength 0.8 --strength-b 0.6
& $py "$suite\runners\run_multiangle.py" --all --seeds 1001 1002 --strength 0.8 --strength-b 0.6
```

Outputs → `artifacts/tmp/two-character-proof/outputs-multiangle/`.

### Upscale low-res refs first (recommended)

Identity conditioning degrades below ~700px. If a character's angle refs are small (~400px),
4x-UltraSharp them before re-running:

```powershell
# Stage the refs you want to upgrade, then:
& $py "$suite\runners\run_upscale.py" --images <ref1> <ref2> ... --out <outdir>
# Drop the <stem>_4x.png results back into refs/multiangle/ (or --ref-dir to a copy).
```

## 3b. 2-person pack positions (18 cases)

The 2-person pack position test (`positions/`) reuses the matrix runner graph with the baseline
position prompts adapted to name Dean + Becky. Workflows are pre-generated in
`positions/prompts/`. To run them against a live pod, submit each workflow JSON via the standard
ComfyUI `/prompt` API (or re-run through the matrix runner with the position's masks + prompt).

The position prompts and their per-position masks are generated from the baseline:

```powershell
& $py "$suite\dump_positions.py"   # rewrites positions/prompts/* + positions/index.json
```

Outputs should land under `positions/images/` (pending pod availability).

## 4. Composition-first Qwen identity matrix (6 cases)

The C1 control and failed angled C2/C3 cases use each committed matrix image as the source
composition, followed by Dean and Becky identity references in fixed order. This suite uses the
existing `img-qwen-edit-serverless` endpoint (`79wkn5jz5d5txx`) and the RunPod Serverless
`input.workflow + input.images` contract. It does not use or require a Qwen pod.

```powershell
# Offline structure/input validation
python specs/image-generator-tests/identity-two-character/runners/run_qwen_composition_identity.py --validate-only

# Live execution requires RUNPOD_API_KEY in the environment
python specs/image-generator-tests/identity-two-character/runners/run_qwen_composition_identity.py `
  --out artifacts/tmp/qwen-composition-identity/<run-id>
```

The runner submits authenticated `/run` jobs, polls `/status/{jobId}`, and records job IDs plus
request, workflow, input, reference, and output hashes in `run-manifest.json`. See
`composition-first/SPEC.md` for the frozen Rapid-AIO v23 settings, scoring gate, reference roles,
and the separate unqualified FLUX.2 candidate.

## 5. Regenerate committed artifacts

After any change to runner logic, regenerate the committed workflow JSONs and the manifest so the
records stay truthful:

```powershell
& $py "$suite\dump_workflows.py"    # rewrites prompts/*
& $py "$suite\build_manifest.py"    # rewrites manifest.json (hashes + scorecards)
```

## Review protocol

- Each generated image must be visually reviewed (identity per actor 1–5, cross-contamination,
  composition, quality) and the result recorded in `manifest.json` scorecards and/or
  `research/` notes before any mechanism decision is made.
- Do not declare a gate pass from structure alone — face identity requires human review against
  the reference photos in `refs/`.
