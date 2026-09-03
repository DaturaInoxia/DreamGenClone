# tools/ — Approved Agent Tools (git-tracked)

The single, **git-tracked** home for approved developer/validation tools so they
survive and are reproducible. `artifacts/` is git-ignored — anything kept only in
`artifacts/tmp/**` is **not** approved/persisted and can be lost.

Agents: prefer these tools over ad-hoc `artifacts/tmp` scripts. See
`.github/instructions/agent-tools.instructions.md` for the agent-facing rules.

## Policy (adding a new approved tool)
1. Create `tools/<name>/` with:
   - runnable script(s) (Python → run with the repo venv
     `d:/src/DreamGenClone/.venv/Scripts/python.exe`),
   - `README.md` (what / how to run / why it exists / interpretation),
   - `requirements.txt` (pinned deps, with a note if a pin matters).
2. Register it in the table below.
3. Scripts must write generated outputs to **git-ignored** paths
   (`artifacts/tmp/**`) — never write artifacts into `tools/`.
4. Never leave the only copy of an approved tool in `artifacts/tmp/`.

## Registry

| Tool | Location | Purpose | Run |
|---|---|---|---|
| **eye-validation** | `tools/eye-validation/` | Measure face eye level / symmetry with real iris landmarks (MediaPipe FaceMesh). Canonical eye check — Haar/centroid/Hough are known-bad for this. | `d:/src/DreamGenClone/.venv/Scripts/python.exe tools/eye-validation/measure_iris.py <img...>` |
| **e2e** | `tools/e2e/` | Playwright E2E harness for the live Blazor Server UI (LLM-free, non-destructive; captures the SignalR circuit-attach probe-retry + suite snapshot/restore patterns). | `cd tools/e2e; npm test` (Node ≥18 + chromium; webapp on `http://localhost:5177`) |
| **character-front-generator** | `tools/character-front-generator/` | Generate near-frontal character face candidates (gpt-image-2/TogetherAI) with the eye-symmetry-hardened prompt; TRUE-PNG output. | `d:/src/DreamGenClone/.venv/Scripts/python.exe tools/character-front-generator/generate_front.py --count 6` |

## Notes
- The first approved tool (`eye-validation/measure_iris.py`) was promoted out of
  `artifacts/tmp/eyemeasure/` (2026-09-02). The exploratory dead-end checkers
  (Haar/Hough) intentionally stayed in tmp and were not promoted.
- Also promoted 2026-09-02 (to git-tracked homes outside `tools/`): the
  `runpod-billing-query.ps1` RunPod cost CLI went to `helpers/runpod/` (see
  `helpers/runpod/README.md`), and the e2e override-restore verification query
  went to `DreamGenClone.DbQuery/queries/e2e-override-restore.sql`.
