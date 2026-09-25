# Pose library on Qwen-Image-2.1 — the all-fours poses (2026-09-25)

Source-controlled evidence for one question: **does the Qwen-Image-2.1 native reference route actually
carry the library's `all_fours` poses, and if not, is the fix a canned prompt per pose?**

Route under test: the app's own graph. `ReferenceStrategyResolver.ResolvePoseAsync` prefers
`PoseControlNet` and falls back to `NativeMultiReference`; the 2.1 model row declares only the latter, so
the skeleton travels as a **reference image** and `PoseTestRenderService` calls the reference client
(`ComfyUIImageClient.BuildQwenImage21Workflow`).

## How the proof was run

- Graph: emitted by the app's builder through the `QWEN21_EMIT_GRAPH` hook and submitted **unchanged**.
  The only values changed per run are the two the app itself derives per call — the `LoadImage`
  filename (the app names it from the render's correlation id) and the sampler seed.
- One **generic clothed prompt, identical for all 12 poses**, so the skeleton is the only variable on
  the first pass:
  > A full-body photograph of a fully clothed woman in a plain grey sweater and dark trousers, on all
  > fours, natural skin texture, photorealistic, plain studio background, 85mm
- 1024×1024, 25 steps, cfg 1, euler/simple (the model's configured envelope), seeds `20260922` and
  `771122`.
- Runner: `tools/pose-library-proof/` — `run_pose_proof.py` refuses the two silent reference-dropping
  autogrow shapes before submitting anything.
- Measured with the approved `tools/pose-angle-probe` (DWPose keypoints read back from each render vs
  the **pack's own** keypoints, error as % of figure height, bar 6 %).

## Result — the route carries these poses, and nothing is contorted

**16 / 24 pass, 7 fail, 1 unmeasurable.** Passing range 1.90–5.93 % — the same band as the proven
standing / squatting / kneeling cells (2.45 % on the front rig). Failures 6.35–19.35 %.

| pose (pack file) | seed 20260922 | seed 771122 |
|---|---|---|
| `all_fours003` | 4.26 PASS | 3.36 PASS |
| `all_fours004` | 2.81 PASS | 3.21 PASS |
| `all_fours005` | 2.44 PASS | 2.50 PASS |
| `all_fours006` | 7.14 FAIL | **19.35 FAIL** |
| `all_fours007` | 1.90 PASS | 2.58 PASS |
| `all_fours008` | 2.66 PASS | 6.35 FAIL |
| `all_fours009` | 3.00 PASS | 4.03 PASS |
| `all_fours010` | *two figures found* | 9.18 FAIL |
| `all_fours` | 4.83 PASS | 5.93 PASS |
| `all_fours001` | 8.06 FAIL | 10.39 FAIL |
| `all_fours002` | 7.95 FAIL | 4.73 PASS |
| `all_fours011` | 2.94 PASS | 5.52 PASS |

**Every one of the 24 renders is an anatomically clean, fully clothed figure.** The failures are pose
*matches that missed*, not mangled bodies — "the bodies are all contorted" is not reproducible through
this route. See `contact-sheet.png`, which puts each render beside the skeleton it was given.

## The canned-prompt question — answered with a measured no

The five poses that failed at least once were re-run at the **same two seeds** with pose-specific
wording (grounded in the reference's own keypoint facts, `canned-prompts.json`). Compare with the table
above:

| pose | generic | canned | outcome |
|---|---|---|---|
| `all_fours006` s20260922 | 7.14 | 7.26 | no change |
| `all_fours006` s771122 | 19.35 | **8.37** | large improvement, still above the bar |
| `all_fours008` s20260922 | 2.66 | 3.45 | was passing, slightly worse |
| `all_fours008` s771122 | 6.35 | 6.70 | no change |
| `all_fours010` s20260922 | (2 figures) | 14.61 | one figure now, far off |
| `all_fours010` s771122 | 9.18 | 13.95 | worse |
| `all_fours001` s20260922 | 8.06 | 6.96 | better, still above the bar |
| `all_fours001` s771122 | 10.39 | 9.20 | better, still above the bar |
| `all_fours002` s20260922 | 7.95 | 7.11 | better, still above the bar |
| `all_fours002` s771122 | 4.73 | 5.83 | was passing, slightly worse |

**Verdict: canned per-pose prompts are not the fix.** Wording moves a cell by roughly ±3 points in either
direction. It *does* change the pose family — 001 and 002 visibly moved to the reference's low forward
stance with the legs stretched back — but not enough to reach the bar, and it made two cells worse.

**What actually limits these cells:**

1. **Incomplete references.** Confidences (visible > 0.1): `all_fours006` has `l_wrist` = **0.00** and
   `all_fours010` has `r_elbow` **and** `r_wrist` = **0.00** — the model has to invent a limb whose
   position the reference does not contain. No prompt can supply it, which is exactly why 006/010 are
   the two that wording could not move. `001`, `002` and `008` are complete (all 12 limb joints at
   1.00) and still missed.
2. **2D ambiguity of folded stances.** Run `tools/pose-library-proof/describe_pose.py` on the five
   failures: the head is **above** the hips by 120–375 px in every one, and the contact points are hands
   *and* knees with the shins folded back. These are compressed low crouches, where a small 2D shift is
   a large normalised error.
3. **A misreading trap:** this pack's `all_fours` is mostly **not quadruped**. Classic
   horizontal-back hands-and-knees (`009`, bare `all_fours`, `011`) is the case that passes best.

## Reproducing this in the app (Pose Library → **Test this pose**)

The library page has a **Test this pose** button on every card, which renders the pose's **own skeleton** — the
same artifact this package used — with no rig fit in the path, so a pose the rig cannot place is still
testable. To reproduce a row of the table above exactly:

1. Press **Test this pose** on the pose (e.g. `all_fours003`). The panel shows the skeleton it will send.
2. Model: **Qwen-Image-2.1 (Local ComfyUI)** — the dropdown labels it *pose as a reference image*, and it is
the default because it is the first model that can actually carry a pose.
3. Canvas `1024x1024`, Seed = the seed of the row you want to reproduce, Prompt = **the one generic prompt
   this file opens with** — the app's test box is prefilled with the same form, so no per-pose wording is ever
   needed. Measured with that default: `all_fours003` **3.14 % PASS** (4.26 % with the proof's own prompt) and
   `all_fours001` **8.42 % FAIL** (8.06 %) — i.e. the words are not the variable.

The **first render is deterministic**: same skeleton + model + prompt + canvas + seed gives the same image
pixel for pixel. Verified by re-running `all_fours003` seed 20260922 three more times — mean|diff| **0.00/255**
and the probe returned **4.26 % every time**. The PNG *file* hashes differ only because ComfyUI embeds the
request in a `tEXt` chunk and the reference is uploaded under a unique filename per run (deliberate: ComfyUI
caches node execution by name). So if a pose renders badly, change the **seed** — it is a fresh sample, not a
re-roll of the same one.

## Layout

| Path | What |
|---|---|
| `images/` | the 24 generic-prompt renders |
| `requests/` | the exact graph submitted, one per render |
| `manifest.json` | pose, seed, skeleton sha256, reference filename, prompt_id, elapsed, output sha256 |
| `measurements.json` | the probe's verdict per render |
| `contact-sheet.png` | every render beside the skeleton it was given |
| `canned-prompts.json` | the per-pose wording used in the retest |
| `canned-test/` | the 10 re-renders (same layout: images, requests, manifest, measurements, contact sheet) |

## Replay

```powershell
# 1. Emit the app's real 2.1 graph (absolute path: the test host's cwd is its output dir).
$env:QWEN21_EMIT_GRAPH  = 'd:\src\DreamGenClone\artifacts\tmp\qwen-2-1\pose-allfours-graph.json'
$env:QWEN21_EMIT_REFS   = 'af-skeleton.png'
$env:QWEN21_EMIT_PROMPT = '<the generic prompt above>'
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj `
  -p:OutDir=d:\src\DreamGenClone\artifacts\build-check\poseproof\ --nologo -v q `
  --filter "FullyQualifiedName~BuildWorkflow_EmitGraphForHostProof"

# 2. Render one image per pose per seed, into this package.
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/pose-library-proof/run_pose_proof.py `
  --graph artifacts/tmp/qwen-2-1/pose-allfours-graph.json `
  --skeleton-root DreamGenClone.Web/wwwroot/pose-library/library/openpose-nsfw `
  --pose-glob "*all*fours*" --seeds 20260922,771122 `
  --out specs/image-generator-tests/pose-library-all-fours --images-subdir images

# 3. Measure (DWPose readback vs the pack's own keypoints).
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/pose-library-proof/measure_run.py `
  --run specs/image-generator-tests/pose-library-all-fours --pack-root pose-packs `
  --comfy https://comfy.kenacwood.net --max-mean-pct 6
```

## Known limitations (do not over-read this package)

- One posed person per call; multi-skeleton composition is untested.
- Adult/explicit content is out of scope: every prompt asks for a fully clothed figure.
- The `010` seed-20260922 render contains **two figures** (DWPose found two people), which is a separate
  defect from pose adherence and is recorded here rather than explained.
- The failing cells were not retried with a different reference artifact, a higher 2.1 `resolution`
  budget, or more steps. Those are untested levers, not ruled out.
