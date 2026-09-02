---
applyTo: 'tools/**,specs/image-generator-tests/**'
description: 'Approved agent-tools infrastructure: the git-tracked tools/ folder, the promote-a-tool policy, and the canonical eye/face validation tool (tools/eye-validation/measure_iris.py). Read before validating faces/eyes or adding an approved tool.'
---

# Agent Tools Infrastructure (DreamGenClone)

## Approved tools live in `tools/` (git-tracked) — NOT in `artifacts/tmp/`
`artifacts/` is git-ignored. Anything under `artifacts/tmp/**` is ephemeral and
**not** reusable/committed. When a script proves useful, **promote** it into
`tools/<name>/` (runnable script + README.md + pinned requirements.txt), register
it in `tools/README.md`, and have it write outputs to git-ignored paths.

## Canonical face/eye validation tool
For measuring whether a face's eyes are level/even/symmetric, use
**`tools/eye-validation/measure_iris.py`** — the only trustworthy method in this
repo (MediaPipe FaceMesh iris landmarks). Do NOT re-derive an eye checker from
Haar box centers, dark-region centroids, or Hough circles — those fail on
photoreal portraits (brow/hair darker than the pupil; mis-centered boxes).

Run:
```powershell
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/eye-validation/measure_iris.py <img1.png> [...]
```
Interpretation and the mandatory "verify the marker is on the iris at high zoom
before trusting dy" rule are in `tools/eye-validation/README.md`.
Useful for the BigLust image-generator test suite (IP-Adapter identity renders)
and any character identity/ref image work.

## Promoting a tool (rule of thumb)
A change is not "done" until the tool used is reproducible: if you (or the user)
rely on a script for validation/generation, it belongs in `tools/` with a README
and requirements — not only in `artifacts/tmp/`.
