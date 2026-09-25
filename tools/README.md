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
| **consistency-scoring** | `tools/consistency-scoring/` | B-111 consistency scorer: identity/subject/adherence/presence/sanitisation/scorecard | `tools\consistency-scoring\.venv\Scripts\python.exe tools\consistency-scoring\score.py --help` |
| **pose-library-proof** | `tools/pose-library-proof/` | Renders ONE library pose per image through the app's OWN emitted graph (Qwen-Image-2.1 native reference route) and records images + request graphs + a manifest. Refuses the two silent reference-dropping autogrow shapes. `measure_run.py` wraps `pose-angle-probe` over a whole run; `describe_pose.py` prints a pose's verifiable keypoint facts (contacts, head-vs-hips, missing limbs) so prompt wording is grounded rather than guessed; `make_contact_sheet.py` pairs each render with its reference. | `python tools/pose-library-proof/run_pose_proof.py --graph <emitted.json> --skeleton-root <app pose-library folder> --pose-glob "*all*fours*" --seeds 20260922,771122 --out <package dir> --images-subdir images` then `python tools/pose-library-proof/measure_run.py --run <package dir> --pack-root pose-packs` |
| **pose-angle-probe** | `tools/pose-angle-probe/` | B-128 measuring tool: how closely a projected pose agrees with what DWPose reads back from an image. Scores **joint geometry** (never raster IoU) as % of figure height, and reports the shoulder span on both sides — the non-circular check that the angle itself survived. This is the measurement that has to exist before an angle is called `known-good`. **Baseline measured 2026-09-02**: front 2.12 %, 3⁄4 2.57–2.68 %, profile 3.50–4.28 % mean error; profile span disagrees with real renders (7.30 % vs ~2 %) — see its README for the table and the camera-distance lever. | `python tools/pose-angle-probe/probe_pose_angle.py --candidate <projected.json> --plate <plate.png> --comfy http://192.168.0.11:8188 --max-mean-pct 6 --angle profile-right` |

## Notes
- The first approved tool (`eye-validation/measure_iris.py`) was promoted out of
  `artifacts/tmp/eyemeasure/` (2026-09-02). The exploratory dead-end checkers
  (Haar/Hough) intentionally stayed in tmp and were not promoted.
- Also promoted 2026-09-02 (to git-tracked homes outside `tools/`): the
  `runpod-billing-query.ps1` RunPod cost CLI went to `helpers/runpod/` (see
  `helpers/runpod/README.md`), and the e2e override-restore verification query
  went to `DreamGenClone.DbQuery/queries/e2e-override-restore.sql`.
