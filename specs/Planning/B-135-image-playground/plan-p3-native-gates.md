# B-135 P3 — Native Gates (detailed plan)

**Status:** done — gates ported, wired, qualified · **Date:** 2026-10-04 · **Scope:** medium.
**Upstream:** B-135 spec D7 + D20 · plan §5 "Gates" · §6 Phases · tasks B135-017..020.
**What changed 2026-10-04:** P4 (the full `/playground` surface) is not needed and P5 (proof/comparison)
is done via the existing playground runner, so **P3 is the one remaining build phase** — the native
enforced gates that turn a rendered cell into evidence.

---

## 0. Locked constraints (do not re-litigate — spec D7, plan §5)

- **No gate ever blocks (operator 2026-10-04).** Every gate records a measured verdict and shows it;
  none refuses a render, retries anything, or changes behaviour. The "enforced vs advisory" split
  collapses — region, pose and sanitisation all produce a pass/fail that is shown, never acted on.
- **Native C# only — no Python at runtime, no ONNX.** The Python tools are used **offline, once, for
  qualification** of the port. Identity/subject similarity (DINOv2 + CLIP) is ONNX work → **out of scope**.
- **Two porting rules:**
  1. Each C# gate must reproduce the **same measurement** its canonical Python tool defines — never a
     re-derived heuristic (Haar boxes / dark-region centroids / Hough circles are proven wrong on
     photoreal faces; `tools/eye-validation` exists precisely because of that).
  2. Each gate is **qualified against its Python tool** on a fixed image set before it may gate
     anything; the agreement is committed as that port's own proof.
- **Phase gate to proceed:** each gate's qualification record committed.

---

## 1. Order (dependencies first)

| # | Task | Blocked on |
|---|---|---|
| 0 | Confirm `score_presence` mechanism (spike) | — |
| 1 | B135-017 Region containment | — (independent; measured numbers already exist) |
| 2 | B135-019 Sanitisation (presence dropped 2026-10-04) | task 0 (spike resolved) |
| 3 | B135-018 Pose agreement | app's own ComfyUI DWPose client (exists) |
| 4 | B135-020 Advisory eyes/head-angle | OUT OF SCOPE (MediaPipe = D7 wall) |

B135-017 can start immediately; B135-019 must wait for the presence-mechanism spike.

---

## 2. Task-by-task

### Task 0 — Confirm `score_presence`'s mechanism ✅ spike done 2026-10-04
`tools/consistency-scoring/scoring/presence.py` imports `torch` + `facenet_pytorch.MTCNN` and
constructs `MTCNN(keep_all=True, device="cpu")`, then counts detected **faces**. Verdict: the
presence metric is a **PyTorch face detector**, not a pixel heuristic — the same runtime-dependency
class (a neural model) that D7 forbids (no Python/ONNX at runtime), exactly like the identity/
subject similarity already dropped from scope. **Resolution: presence is DROPPED from the enforced
gates** (recorded deviation); sanitisation survives because it is a pure pixel heuristic.

### B135-017 — Region containment gate ✅ port done 2026-10-04
- **Measurement (canonical):** mean absolute diff **inside vs outside** the edited rectangle, blurred
  edge, against an **unmasked control**. `tools/qwen-region-proof/measure_region.py` measured:
  contained = **0.319** outside-diff vs **19.86** for the control (**62×**).
- **C# port:** `DreamGenClone.Web/Application/RolePlay/Evaluation/Gates/RegionContainmentGate.cs`
  — the tool's arithmetic exactly: same rect derivation, margin formula, per-channel mean + per-pixel
  max-channel stats, changed threshold (16/255), and the same verdict
  (`outMasked * 2 < outControl AND inMasked > outMasked`). The verdict IS the tool's own `contained`
  boolean — there is no separate pass/fail threshold to invent. Pinned by
  `RegionContainmentGateTests` on hand-computable synthetic images.
- **Qualification (remaining):** run `measure_region.py` and the C# gate on the SAME three renders of
  one source (the `p0Region*` triple, produced by the qwen-region proof) and commit the agreement
  record under `tools/qwen-region-proof/`.

### B135-019 — Sanitisation gate (presence dropped 2026-10-04)
- **Presence: DROPPED (2026-10-04, D7 deviation, operator-confirmed).** The canonical metric is an
  MTCNN face detector (`torch` + `facenet_pytorch`) — a neural model, which D7's "native C#, no
  Python/ONNX at runtime" forbids, and no C#-native detector reproduces it without re-deriving
  (porting rule 1). Same class as the already-dropped identity similarity.
- **Sanitisation: ✅ port done 2026-10-04.** Pure pixel heuristic — a skin-tone fraction over RGB +
  YCbCr bounds (`tools/consistency-scoring/scoring/sanitisation.py`), no detector.
  `Evaluation/Gates/SanitisationGate.cs` reproduces it (including PIL's YCbCr matrix + truncation),
  pinned by `SanitisationGateTests`.
- **Files:** `Evaluation/Gates/SanitisationGate.cs` (PresenceGate is not built).
- **Qualification:** against `tools/consistency-scoring` on a fixed set.

### B135-018 — Pose agreement gate ✅ threshold resolved 2026-10-04
- **Mechanism:** DWPose readback through the app's **own ComfyUI client** (not a new harness); scored
  as joint geometry as **% of figure height**, shoulder span reported.
- **Pass bar (`tools/pose-angle-probe`): 6 % mean joint error** — the probe's own line "all five pass
  a 6 % mean bar" (front 2.12 %, 3/4 2.57–2.68 %, profile 3.50–4.28 % on the SDXL/ControlNet route;
  2.45 % on the Qwen native-reference route with facing wording). The bar is the gate; the per-route
  spread is re-validated by the qualification, not a second threshold.
- **File:** `Evaluation/Gates/PoseAgreementGate.cs`.
- **Port: ✅ done 2026-10-04.** `PoseAgreementGate.cs` (18-joint COCO body, visibility floor 0.1,
  normalise by figure height + centre on the bbox, mean/p95/max/worst-joint error as % of height +
  shoulder span, caller-declared 6 % bar) — pinned by `PoseAgreementGateTests` (5/5 pass).
- **Qualification:** against `tools/pose-angle-probe`.

### B135-020 — Advisory eyes/head-angle ✅ OUT OF SCOPE 2026-10-04
- The canonical tool is `tools/eye-validation/measure_iris.py` (MediaPipe iris landmarks) — a
  **neural/ONNX model**, the same D7 wall as presence and identity similarity: native C# cannot run
  MediaPipe at runtime, and porting rule 1 forbids re-deriving a pixel heuristic for eyes (Haar /
  dark-region / Hough are proven wrong on photoreal faces).
- **Resolution: out of scope**, recorded as a D7 deviation. No `AdvisoryMeasurements.cs` is built.
  P3's delivered gates are therefore **region, pose and sanitisation** — each records a verdict,
  never blocks.

---

## 3. Wiring (where the gates run) — background, never on the user's path ✅ done 2026-10-04

The gates evaluate the **rendered image** — the `SceneAssetImage` the Playground render produces. The
free-layer `ImageRunCell` (the "Suites &amp; verdicts" run) is NOT the render surface: its image layer is
B135-023/B135-024, still unbuilt, so the verdicts land on the image row the render actually writes and
show in the Asset Studio where those images live.

- The render is **already a background job**: `IImageSuiteRenderDriver` → `AddGeneratedImageAsync` →
  `SceneAssetGenerationJobHandler`. Gate computation hooks **that same completion**, in the background
  worker — never a synchronous call on the request thread, never blocking the Playground UI. (The pose
  gate's DWPose readback is itself a ComfyUI call, i.e. already background work.)
- **Scoped to Playground renders only** (`asset.Type == SceneAssetType.Playground`), so identity packs,
  scene renders, LoRA cells and every other generation path is untouched.
- On completion: `ImageGateEvaluator` computes the verdicts (sanitisation always; pose agreement only
  when the render carried a pose-library preset, whose own keypoints are the candidate the DWPose
  readback is measured against), and the handler stores them as `SceneAssetImage.GateResultsJson` (a new
  column) through the existing image upsert.
- **Region containment is a proof measurement, not a runtime gate:** it needs the source/masked/control
  triple (only a region-edit proof renders all three), so it stays in the qwen-region proof and is not
  wired into the t2i completion.
- **No gate ever blocks:** the evaluator never throws — a gate that cannot run records its own failure on
  the image, and a gate error cannot fail a render that already completed.

## 3.1 UI — showing the verdicts (Asset Studio, additive)

- **Per image:** the Asset Studio image card's badge row gains one badge per gate — `sanitisation ✓` ·
  `pose-agreement ✗ 12.4%` — where a fail carries the measured number in the badge's tooltip. Rendered
  from `SceneAssetImage.GateResultsJson`; an image with no results shows no gate badges.
- No new tab, no new route; the Asset Studio's existing polling refreshes the card once the completion
  writes the verdicts.

## 4. Files & blast radius

- **New:** `Evaluation/Gates/RegionContainmentGate.cs` (✅ done), `PoseAgreementGate.cs` (✅ done),
  `SanitisationGate.cs` (✅ done), `Evaluation/ImageGateResult.cs` (✅ done),
  `Evaluation/Gates/ImageGateEvaluator.cs` (✅ done). `PresenceGate.cs` is **dropped** and
  `AdvisoryMeasurements.cs` is **out of scope** (both D7: MTCNN / MediaPipe are neural models; operator
  2026-10-04).
- **Touched:** `SceneAssetGenerationJobHandler` (completion → `ApplyGateResultsAsync`, Playground only),
  `SceneAssetRepository` (new `GateResultsJson` column on `SceneAssetImages`),
  `Domain/RolePlay/SceneAssetModels.cs` (`SceneAssetImage.GateResultsJson`), `AssetStudioView.razor`
  (gate badges), `Program.cs` (`IImageGateEvaluator` registration).
- **Tests:** per-gate unit tests on synthetic fixtures (`RegionContainmentGateTests`,
  `SanitisationGateTests`, `PoseAgreementGateTests`, `ImageGateEvaluatorTests`).
- **Not touched:** RP engine, beat/moment pipeline, identity/body pack flows, LoRA training dispatch.

## 5. Evidence plan (all three qualifications done 2026-10-04)

| Artefact | Where |
|---|---|
| Region gate qualification vs `measure_region.py` | ✅ `tools/qwen-region-proof/README.md` — C# reproduces 0.319 / 19.86 / 125.252 / CONTAINED on the same `p0*` renders |
| Pose gate qualification vs `pose-angle-probe` | ✅ `tools/pose-angle-probe/README.md` — C# reproduces 18 joints / mean 9.566 / p95 36.304 / max 45.102 / spans on the same two person JSONs |
| Sanitisation qualification vs `consistency-scoring` | ✅ `tools/consistency-scoring/README.md` — C# reproduces `skin_fraction` to full float precision on the same renders (the qualification caught and fixed a wrong YCbCr matrix in the first port) |
| Advisory eyes/head-angle | OUT OF SCOPE (MediaPipe, D7) — `tools/eye-validation` stays the offline canonical |

## 6. Open questions / decisions needed

None. P3 is complete: presence dropped (MTCNN) and eyes/head-angle out of scope (MediaPipe) — both the
D7 neural-model wall, operator-confirmed; region, pose and sanitisation ported, wired into the Playground
render completion (background, never blocking), shown in Asset Studio, and each qualified against its
canonical Python tool. The image layer for the free-layer run cells (B135-023/B135-024) is a separate,
still-unbuilt piece of work.
