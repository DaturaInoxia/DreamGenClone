# P0 — Baseline & Program Hygiene — Task List

**Phase goal:** deliver the ability to *measure* whether later phases work — the yardstick every
prior plan lacked. **No visual product capability ships in P0.**
**Gate:** `plan.md` → P0 exit gate. **Contracts:** C6 (scorer), C7 (component specs). **Governance:**
G1, G6.

**Legend:** `[ ]` not started · `[~]` in progress · `[x]` done · **Dispatch** = who runs it
(Luna = `GPT-5.6 Luna (copilot)` subagent; Coordinator = this chat; You = human).
Every dispatched task carries the standing constraints from `tasks/README.md §Standing constraints`.

> **Environment decision (2026-09-05).** The app venv is **Python 3.14.0** — too new for the ML
> scoring stack (no wheels; insightface/onnxruntime won't build). The scorer is a standalone CLI, so
> it gets its **own isolated `uv`-managed Python 3.12 venv** at `tools/consistency-scoring/.venv`
> (git-ignored). The app's 3.14 env is untouched. The identity metric uses **`facenet-pytorch`**
> (pure-PyTorch MTCNN + InceptionResnetV1/VGGFace2 embeddings) instead of insightface/onnxruntime —
> a valid face-recognition cosine metric with no Cython/onnx build risk. All scorer commands run
> through `tools/consistency-scoring/.venv/Scripts/python.exe`, **not** the app venv.

---

## Dependency order

```
T1 ── T1b ─┬─ T2 ─┬─ T6 ── T7 ── T8
         ├─ T3 ─┤
         └─ T4 ─┘
T9  (independent, docs)
T10 (independent, docs)
T11 (independent, docs)
T12 (independent, docs)
```

---

## T1 — Scaffold `tools/consistency-scoring/`
- **Dispatch:** Luna
- **Depends:** none
- **Objective:** create the tool skeleton per the repo `tools/` policy. No metric logic yet.
- **Files:**
  - `tools/consistency-scoring/README.md` — what/how/why/interpretation (follow `tools/eye-validation/README.md` shape)
  - `tools/consistency-scoring/requirements.txt` — pinned deps (see Steps)
  - `tools/consistency-scoring/score.py` — CLI entry with `--help`, subcommands stubbed to raise `NotImplementedError`
  - `tools/consistency-scoring/scoring/__init__.py` (empty package marker)
- **Steps:**
  1. `requirements.txt` pins (CPU-capable): `insightface==0.7.3`, `onnxruntime==1.19.2`, `torch==2.4.1`, `torchvision==0.19.1`, `open-clip-torch==2.26.1`, `pillow==10.4.0`, `numpy==1.26.4`. Add a one-line note that DINOv2 loads via torch.hub and CLIP via open-clip.
  2. `score.py` exposes argparse subcommands: `identity`, `subject`, `adherence`, `presence`, `sanitisation`, `scorecard` — each currently raising `NotImplementedError("P0-Tn")`.
  3. `README.md` documents the run command using the repo venv (`d:/src/DreamGenClone/.venv/Scripts/python.exe tools/consistency-scoring/score.py --help`) and states outputs go to `artifacts/tmp/consistency-scoring/**` (git-ignored).
  4. Register the tool in the root `tools/README.md` registry table (new row).
- **Constraints:** outputs must target `artifacts/tmp/**`, never write into `tools/`. Do not implement metrics in this task.
- **Acceptance:** `python tools/consistency-scoring/score.py --help` lists all six subcommands; `tools/README.md` has the new row; no metric implemented yet.
- **Verify (Coordinator):** run `--help`; confirm registry row.
- **Status:** `[x]` done (2026-09-05, verified).

## T1b — Bootstrap the isolated scorer venv (uv, Python 3.12) + robust stack
- **Dispatch:** Luna
- **Depends:** T1
- **Objective:** give the scorer its own working Python 3.12 environment with a build-risk-free stack, since the app venv is Python 3.14.
- **Steps:**
  1. `uv python install 3.12` then create the venv: `uv venv --python 3.12 tools/consistency-scoring/.venv`.
  2. Rewrite `tools/consistency-scoring/requirements.txt` to the robust stack (NO insightface, NO onnxruntime): `facenet-pytorch`, `torch`, `torchvision`, `open-clip-torch`, `pillow`, `numpy`, `pytest`. Resolve latest versions that install on 3.12, install them, then **freeze the exact resolved versions** back into `requirements.txt` (pinned). Keep the comment noting DINOv2 loads via torch.hub.
  3. Smoke-test imports in the new venv: `python -c "import torch, torchvision, open_clip; from facenet_pytorch import MTCNN, InceptionResnetV1; print('ok')"`. If anything fails to install/import, STOP and report the exact error — do not substitute silently.
  4. Add `tools/consistency-scoring/.venv/` to `.gitignore` (or confirm `**/.venv/` already covers it).
  5. Update `tools/consistency-scoring/README.md` run commands to use `tools/consistency-scoring/.venv/Scripts/python.exe`, and document the isolated-venv rationale (app venv is 3.14).
- **Constraints:** do not modify the app venv. Freeze real resolved versions — no guessed pins.
- **Acceptance:** the smoke-test prints `ok`; `requirements.txt` holds frozen installable pins; `.venv` is git-ignored.
- **Verify (Coordinator):** re-run the smoke-test; inspect frozen `requirements.txt`.

## T2 — Identity metric (face embedding cosine)
- **Dispatch:** Luna
- **Depends:** T1b
- **Objective:** implement `identity` — cosine similarity of aligned-face embeddings between a render and a reference, using **facenet-pytorch**.
- **Files:** `tools/consistency-scoring/scoring/identity.py`; wire into `score.py identity`; `tools/consistency-scoring/tests/test_identity.py`
- **Steps:**
  1. Use facenet-pytorch: `MTCNN` to detect + align the largest face in each image, `InceptionResnetV1(pretrained='vggface2').eval()` to embed; L2-normalise; cosine similarity.
  2. Return a JSON dict: `{ "similarity": float, "reference_face_found": bool, "render_face_found": bool }`. If either has **no** detectable face, `similarity: null` and the relevant flag `false` — never fabricate a score.
  3. `score.py identity --reference <img> --render <img>` prints the JSON.
  4. Tests (no real faces committed): (a) a solid-colour image as BOTH reference and render → both `*_face_found: false`, `similarity: null`; (b) a solid-colour render → `render_face_found: false`, `similarity: null`. Generate solid-colour fixtures in-test with PIL (`tmp_path`); do not commit face images.
- **Constraints:** first run downloads the VGGFace2 weights to the torch cache — document in the tool README. Fail fast (explicit message) if the model can't load; no silent fallback metric. Run via the tool's own venv interpreter.
- **Acceptance:** tests pass; no-face → explicit null; same-image-with-a-face path documented (not unit-tested to avoid committing faces).
- **Verify (Coordinator):** run `tools/consistency-scoring/.venv/Scripts/python.exe -m pytest tools/consistency-scoring/tests/test_identity.py`.

## T3 — Subject-fidelity (DINO + CLIP-I) and prompt-adherence (CLIP-T)
- **Dispatch:** Luna
- **Depends:** T1b
- **Objective:** implement `subject` (DINO + CLIP-I image similarity) and `adherence` (CLIP-T text–image).
- **Files:** `tools/consistency-scoring/scoring/subject.py`, `scoring/adherence.py`; wire into `score.py`; `tests/test_subject.py`, `tests/test_adherence.py`
- **Steps:**
  1. `subject`: DINOv2 (`dinov2_vitb14` via torch.hub) cosine + CLIP-I (open-clip ViT-B/32) cosine between two images → `{ "dino": float, "clip_i": float }`. Doc-note in README: **DINO is authoritative** where they disagree (FR-C6-03).
  2. `adherence`: CLIP-T cosine between a render and a text prompt → `{ "clip_t": float }`.
  3. Tests: identical image → dino ≈ 1.0, clip_i ≈ 1.0; a render vs a clearly-matching vs clearly-mismatching short prompt → matching clip_t > mismatching clip_t (use two solid-colour images + prompts like "a red square" / "a blue square").
- **Constraints:** models download on first run (document). Fail fast on load error. No metric substitution.
- **Acceptance:** tests pass; identical-image ≈ 1.0; matching prompt scores higher than mismatching.
- **Verify (Coordinator):** run both test files.

## T4 — Subject-presence + silent-sanitisation checks
- **Dispatch:** Luna
- **Depends:** T1b
- **Objective:** implement `presence` (expected face count actually rendered) and `sanitisation` (detect an image returned as success but content-altered).
- **Files:** `tools/consistency-scoring/scoring/presence.py`, `scoring/sanitisation.py`; wire into `score.py`; `tests/test_presence.py`, `tests/test_sanitisation.py`
- **Steps:**
  1. `presence --render <img> --expected N`: count detectable faces (facenet-pytorch MTCNN, `keep_all=True`) → `{ "faces_detected": int, "expected": N, "pass": bool }`. This is the check that would have caught the B-103 dropped-character failure (FR-C6-04).
  2. `sanitisation`: given a render and a **skin-exposure/coverage heuristic threshold**, flag an image that appears content-suppressed relative to an expected-explicit request. P0 implements a *conservative, documented* heuristic (e.g. detected-face present but body region coverage/clothing indicator above a threshold) and returns `{ "suspected_sanitised": bool, "signals": {...} }`. **Document clearly that this is a heuristic screen, not ground truth** — its job is to stop sanitised renders silently entering the baseline (FR-C6-08), and the human verdict overrides (P8).
  3. Tests: presence on a solid-colour image → `faces_detected: 0`; sanitisation returns a well-formed dict on any input and never throws.
- **Constraints:** the sanitisation heuristic must be conservative and documented; it flags for review, it does not delete or auto-fail silently. No hidden thresholds — the threshold is a named constant in the file with a comment. Run via the tool's own venv interpreter.
- **Acceptance:** tests pass; both subcommands emit well-formed JSON; heuristic + limitation documented in the tool README.
- **Verify (Coordinator):** run both test files; read the README note.

## T5 — Golden-set manifest (data, no code)
- **Dispatch:** Luna
- **Depends:** none (can run alongside T1)
- **Objective:** author the frozen golden set as version-controlled data the scorer consumes.
- **Files:**
  - `specs/Planning/B-111-consistent-visual-production/golden-set/manifest.json`
  - `specs/Planning/B-111-consistent-visual-production/golden-set/README.md`
- **Steps:**
  1. `manifest.json` encodes: **3 characters** (female/male/secondary — id, name, full appearance description text block), **2 locations** (interior/exterior — id, description), **2 outfits** (id, description), **1 two-character contact/occlusion scene** (ids + interaction description), and **12 prompts × 3 fixed seeds** (each prompt: id, text, expected subject ids, expected subject count, seeds `[...]`).
  2. **Every case is explicit** (spec P5) — no SFW variant, no `sfw`/`nsfw` field.
  3. Descriptions are fictional and self-authored (no real person). Keep each appearance block ≤ 240 chars (matches the builder cap).
  4. `README.md` states the set is **frozen**: changing it invalidates prior baselines and requires a recorded decision.
- **Constraints:** data only — no image files in P0 (reference images are produced by P2 bootstrap). No code.
- **Acceptance:** `manifest.json` parses; contains exactly the counts above; README states the freeze rule.
- **Verify (Coordinator):** parse JSON; count entries.

## T6 — Scorecard CLI (compose the triple)
- **Dispatch:** Luna
- **Depends:** T2, T3, T4, T5
- **Objective:** `score.py scorecard` reads the manifest + a folder of renders and emits the **triple** per case, plus a markdown summary.
- **Files:** `tools/consistency-scoring/scoring/scorecard.py`; wire into `score.py scorecard`; `tests/test_scorecard.py`
- **Steps:**
  1. Input: `--manifest <path> --renders <dir> --out <dir>`. For each render matched to a manifest case (by naming convention `caseId__seed.png`), compute identity (vs a named reference image if present), subject, adherence, presence, sanitisation.
  2. Output: `scorecard.json` (per-case triple + presence + sanitisation flags) and `scorecard.md` (human-readable table) in `--out` (default `artifacts/tmp/consistency-scoring/`).
  3. **Always emit the triple together** (identity + adherence + diversity/variance across seeds); never identity alone (FR-C6-01). Diversity = variance of the subject/embedding across the case's seeds.
  4. Missing reference or missing face → the value is `null` with a reason, never a fabricated number.
  5. Test: a tiny synthetic manifest + 2 solid-colour renders produce a well-formed scorecard with nulls where faces are absent.
- **Constraints:** output to git-ignored path. No silent defaults for missing inputs — report them.
- **Acceptance:** test passes; `scorecard.md` renders a table; triple always present.
- **Verify (Coordinator):** run the test; open the generated `scorecard.md`.

## T7 — ~~Record the current-pipeline baseline~~ DEFERRED to P2 (decision 2026-09-05)
- **Status:** **Not done in P0 — deliberately deferred.** A P0 baseline was dropped as premature and
  misframing. The golden set is a fixed measuring stick usable across every cell, **not** an endpoint
  choice; and its headline metric (identity) is **not measurable until P2**, because identity scoring
  needs the reference images that Reference Bootstrap produces. Rendering a mostly-`null` scorecard
  against one arbitrary endpoint now would waste cost and mislead. The **first real golden-set
  scoring** is a P2 exit-gate item (identity), and the full triple is P4's consistency gate. The
  scorer is the instrument; the UI/workflow is the product (P9/C7/G8).

## T8 — ~~Initial threshold calibration~~ DEFERRED to P2/P4 (decision 2026-09-05)
- **Status:** **Not done in P0.** Thresholds are calibrated on real golden-set outputs, which do not
  exist until P2. Calibration happens alongside the first real scorecards at P2 (identity) and P4
  (full triple), per FR-C6-06; the human verdict recalibrates on disagreement (P8).

## T9 — Clamp-removal map (docs, no code removal)
- **Dispatch:** Luna (research/read-only) → Coordinator review
- **Depends:** none
- **Objective:** produce the exact footprint of the SFW clamp so P1 can remove the *behaviour* while keeping the `ImageContentPolicy` capability signal.
- **Files:** `specs/Planning/B-111-consistent-visual-production/clamp-removal-map.md`
- **Steps:** enumerate every site touching `SfwClampSuffix`, `ImageContentPolicy.SfwFiltered`, `ResolveRatingTag(phase, policy)`, the SDXL `BuildSystemPrompt` SFW branch, and content-policy plumbing (`SceneImageRenderingJobHandler`, `SceneImagePromptCompilers`, `PonySceneImagePromptBuilder`, `SdxlSceneImagePromptBuilder`, `SceneImageService`, production workload/reconciliation/asset services, and all tests asserting clamp behaviour). For each: file, method, what it does, and **keep** (capability signal) vs **remove** (clamp behaviour).
- **Constraints:** **read-only** — this task changes no code. It is the map P1 executes.
- **Acceptance:** every clamp site classified keep/remove; the clamp still functions (unchanged).
- **Verify (Coordinator):** grep-cross-check the map against the code; confirm completeness.

## T10 — Qualification-matrix classification (docs/data)
- **Dispatch:** Luna (read-only) → Coordinator review
- **Depends:** none
- **Objective:** classify every `(strategy, model, endpoint)` cell as *qualified* / *unqualified* / *structurally-impossible* (FR-C3-04), grounded in the actual provider/model records and installed nodes.
- **Files:** `specs/Planning/B-111-consistent-visual-production/qualification-matrix.md`
- **Steps:** from the Model Manager records + endpoint capabilities + installed ComfyUI custom nodes, tabulate each cell. Mark hosted-API rows for graph-requiring strategies as **structurally-impossible** (F7.0). Mark PuLID on any serverless worker as **structurally-impossible until a worker image ships** (P3). Cite sources.
- **Constraints:** read-only. No claims without a cited record.
- **Acceptance:** matrix covers all strategies × configured endpoints; every cell has a class + citation.
- **Verify (Coordinator):** spot-check cited records.

## T11 — Corpus reconciliation verification (docs)
- **Dispatch:** Coordinator
- **Depends:** none
- **Objective:** confirm every superseded package is marked and every §2 conflict in `superseded-map.md` is resolved in writing; fix any stragglers.
- **Acceptance:** backlog banner, roadmap banner, and superseded-map are mutually consistent; no superseded package lacks a pointer to its B-111 phase.

## T12 — Shared-component specs (docs, no component code)
- **Dispatch:** Luna → Coordinator review
- **Depends:** none
- **Objective:** turn each C7 §2 shared component into a one-page buildable spec so P1+ builds from specs (FR-C7-02).
- **Files:** `specs/Planning/B-111-consistent-visual-production/ui/<Component>.md` for each of: `EditIterateWorkbench`, `CandidateGrid`, `RunTray`, `ResolvedRenderBadge`, `ReferencePicker`, `StrategySelector`, `ScorecardPanel`, `FrozenTextBlockEditor`, `LineageTree`.
- **Steps:** each spec states the single job (U1), the typed input contract, the states (empty/loading/error/populated), the data-volume behaviour (pagination/virtualization/on-demand, U4), and which screens reuse it.
- **Constraints:** docs only — no `.razor` in P0. Follow the repo's Razor conventions doc for later implementation references, but write no component code.
- **Acceptance:** nine component specs exist, each with a typed contract and data-volume behaviour.
- **Verify (Coordinator):** each spec passes the five-word job test.

---

## P0 completion (Coordinator + You)
When T1–T12 are `[x]`: run the full solution build + full test suite (green), run the scorer
self-tests, present the baseline scorecard, and record your P0 sign-off in `tasks/gate-log.md`.
Only then is P1 hardened from its plan.md outline into `tasks/P1-tasks.md`.
