# Identity Single-Character — Runbook

How to re-run the single-character identity render proof against a ComfyUI pod.

## Prerequisites

- A running ComfyUI pod with `juggernautXL_ragnarok.safetensors` + IP-Adapter custom nodes
  (`PLUS FACE (portraits)` preset; PuLID nodes for the `--mechanism pulid` variant).
- Python 3 (runners use only the stdlib).

## Run

```powershell
$py   = 'd:\src\DreamGenClone\.venv\Scripts\python.exe'
$suite = 'd:\src\DreamGenClone\specs\image-generator-tests\identity-single-character'

# Selected mechanism (validated): IP-Adapter PLUS FACE, seed 20256
& $py "$suite\runners\run_single.py" --mechanism ipadapter --seed 20256

# Alternate mechanism: PuLID
& $py "$suite\runners\run_single.py" --mechanism pulid --seed 20256

# Print the exact workflow JSON (matches prompts/*.json)
& $py "$suite\runners\run_single.py" --mechanism ipadapter --seed 20256 --dump-json
```

Outputs land in `artifacts/tmp/images/identity-single-character/` (overridable with `--out`).

## Regenerate committed artifacts

```powershell
& $py "$suite\build_manifest.py"   # rewrites manifest.json (hashes)
```

## Review protocol

Compare the render's face against `refs/dean_face.png`. Identity must match the reference to pass.
Do not declare a pass from structure alone.
