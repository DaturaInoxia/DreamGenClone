# 075 — The pose-library "Test this pose" tests the wrong thing, and defaults to the wrong model

**Session:** 2026-09-25 · **State:** implemented, targeted tests green
**Scope:** pose-library test render (UI) + per-element reference strategy source + Qwen-Image-2.1
reference route as the focus for pose renders

---

## Report

Operator, in sequence:

1. "can you run the proof that from the library all the all_fours poses can be done and render
   properly… it seems like we may need to have canned prompts for each of these poses… i am trying out
   the pose-library test pose and NONE are coming out properly, the bodies are all contorted, not
   following the pose reference."
2. "i thought we can proved the pose references worked with qwen 2.1" → "focus only on 2.1 model".
3. "put the generated images in the same folder not some random temp folder".
4. "so then what was done in the pose-library test pose it does not work at all, lets focus the UI and
   app on qwen 2.1 model, this model accepts reference images… (not biglust and controlnet etc)".

---

## Analysis

### 1. The 2.1 pose route was never the problem — measured, not argued

12 all-fours pack poses × 2 seeds, **one** generic clothed prompt, 1024×1024, through the app's **own**
emitted graph (`ComfyUIImageClient.BuildQwenImage21Workflow` via the `QWEN21_EMIT_GRAPH` hook; only the
`LoadImage` filename and the seed changed per run). Measured with the approved `pose-angle-probe`
(DWPose readback vs the pack's own keypoints, % of figure height, bar 6 %):

**16/24 PASS, 7 FAIL, 1 unmeasurable.** Range on passes 1.90–5.93 % (the same band as the proven
standing/kneeling cells); failures 6.35–19.35 %. **No render was anatomically contorted** — every image
is a clean, fully clothed figure. The failures are pose *mismatches*, which is a different defect from
the "bodies all contorted" the operator saw.

Package: `specs/image-generator-tests/pose-library-all-fours/` (images, request graphs, manifest,
measurements, contact sheet).

### 2. Why the UI looked like it failed — two defects, both verified in code

**(a) The test sent the wrong object.** `PoseAuthorPanel.RunCurrentPoseTestAsync` built its bytes with
`LibraryService.RenderAuthoredPreview(_view, _head, canvas, _rotations)`. For a library preset
`_rotations` is `PoseRigFit.Fit(...)` — the app fits its **3D rig** onto the pack's 2D keypoints and
then submits the **projection of the rig**, never the pose's own skeleton. The panel previews the pack
skeleton and sends something else. A quadruped stance is exactly where a body-scale 3D rig fit is worst,
and the fit residual is only a number in a caption.

**(b) The default model was the wrong mechanism.** `_testModelId = _testModels.FirstOrDefault()` over
`ListSceneImageModelsAsync`, which orders by `DisplayName` ordinal-ignore-case:

```
BigLust v1.6 (Local ComfyUI)     <- default: PoseControlNet @ 0.80 (SDXL)
FLUX.1-dev fp8 (Local ComfyUI)
Juggernaut XL Ragnarok
Qwen-Image-2.1 (Local ComfyUI)   <- LAST
```

So "Test this pose" tested **BigLust + ControlNet at 0.8** — the route already on record as re-posing
awkward stances — while the model that carries a pose as a reference image sat at the bottom. Plus the
default prompt said "arms at their sides", a stance the skeleton denies for every non-standing pose.

### 3. The failing references are partly incomplete (a fact about the pack)

Confidences (visible > 0.1) for the five failing poses:

| pose | missing limb keypoints | consequence |
|---|---|---|
| `NSFW_all_fours006` | `l_wrist` = 0.00 | the left arm's endpoint is absent; the model must invent it |
| `NSFW_all_fours010` | `r_elbow`, `r_wrist` = 0.00 | the whole right forearm/hand is absent |
| `001`, `002`, `008` | none — all 12 limb joints at 1.00 | complete references that still mismatched |

And a misreading trap: this pack's "all_fours" is **not** quadruped. `describe_pose.py` shows the head
**above** the hips by 120–375 px in all five failures — they are low kneeling/crouching stances with the
hands reaching the floor, seen from various angles. Classic horizontal-back hands-and-knees (009, bare
`all_fours`, 011) passes.

### 4. Canned prompts were tested and are NOT the fix — measured, same seeds

Five failing poses re-run with pose-specific wording (grounded in the references' own keypoint facts):

| pose | generic | canned | outcome |
|---|---|---|---|
| `006` s20260922 | 7.14 | 7.26 | no change |
| `006` s771122 | **19.35** | **8.37** | large improvement, still above the bar |
| `008` s20260922 | 2.66 (pass) | 3.45 (pass) | was passing, slightly worse |
| `008` s771122 | 6.35 | 6.70 | no change |
| `010` s20260922 | (two figures) | 14.61 | one figure now, far off |
| `010` s771122 | 9.18 | 13.95 | worse |
| `001` s20260922 | 8.06 | 6.96 | better, still above the bar |
| `001` s771122 | 10.39 | 9.20 | better, still above the bar |
| `002` s20260922 | 7.95 | 7.11 | better, still above the bar |
| `002` s771122 | 4.73 (pass) | 5.83 (pass) | was passing, slightly worse |

Wording changes the pose FAMILY — 001 and 002 visibly moved to the reference's low forward stance with
the legs stretched back — but moves the score only ~±3 points, crossing the bar in neither direction.
The binding constraints are the incomplete references (006/010) and the 2D ambiguity of compressed
folded stances (001/008/010), not the words. Recorded in
`specs/image-generator-tests/pose-library-all-fours/README.md`.

### 4. Roadmap position (do not invent new work)

The per-element strategy gap is the open half of **B-128 §5.3 U1**, already recorded in
`specs/001-rp-prompt-redesign/debug/074-production-studio-native-reference-and-framing.md` §4: the
page-level half was done 2026-09-24, and `ReferenceApplyPanel`'s `Location => {TextOnly, ControlNet}`
arm (with its `throw` on unknown keys) was the remaining blocker.

---

## Plan

Approved by the operator (A + B + C in one pass; pose test sends the preset's own skeleton):

| # | Change | Files |
|---|---|---|
| A | Test sends the library pose's OWN skeleton when unedited; the rig projection only when edited, and the result states which | `PoseAuthorPanel.razor` |
| B | Model list carries the resolved mechanism + label, is ordered capable-first then reference-route-first, defaults to a capable model, disables incapable ones, and the button is gated on capability | `PoseTestRenderService.cs`, `PoseAuthorPanel.razor`, `PoseTestRenderServiceTests.cs` |
| C | Per-element strategy source becomes a capability-filtered catalogue: `Location` gains `NativeMultiReference`, the `throw` on an unknown element key is gone (degrades to text, logged), strength is not offered where nothing reads it, and `PromptAssetCreator` stops passing a text-only list | `ReferenceApplyPanel.razor`, `PromptAssetCreator.razor` |
| — | New approved tool for the proof: `tools/pose-library-proof/` (runner, measurement wrapper, contact sheet, pose describer) + registry row | `tools/**` |

---

## Resolution

- **A** — `PoseEdited()` compares the current view/head against an adoption point (plus a nested-save
  flag from the drag editor); `ReadSkeletonAsync(_loadedFrom.Id)` is used when nothing was edited. The
  section help and the result note now name the bytes that were sent.
- **A+ (the library's own test, so it works for ALL poses)** — `PoseLibraryPage` gained a **Test this pose**
  button on every card with a page-level test panel. It renders the preset's **own skeleton** read from the
  library, with **no rig fit anywhere in the path**, so a pose the rig cannot place is still testable and the
  bytes tested are the pose rather than a projection of it. The panel shows the skeleton that will be sent,
  names the mechanism, and its note states the byte count and the mechanism that actually carried the pose.
- **Prompt (one copy, one prompt for every pose)** — `PoseTestPrompts.Default` is the proof's own formulation
  with the hand-fitted parts removed, because the proof rendered 12 different poses with ONE unchanged prompt:
  a generic subject ("a fully clothed person"), no stance clause and **no view clause**. An earlier attempt at
  this default carried "Front view, facing the camera" — inherited from a measurement on the app's *front rig*
  skeleton (17.30 % → 2.45 % when the view was stated) — but generalising that to library poses asserts a facing
  the reference may contradict, which is the same defect class as the "arms at their sides" wording it replaced.
  Measured with the final default: `all_fours003` **3.14 % PASS** (proof prompt: 4.26 %), `all_fours001`
  **8.42 % FAIL** (proof prompt: 8.06 %) — the wording is not the variable, so the operator never needs per-pose
  text. A specific view or clothing belongs in the editable box when a render needs one.
- **Determinism measured** — re-running `all_fours003` seed 20260922 three times: the images are **pixel
  identical** (mean|diff| 0.00/255) and the probe returned **4.26 % every time**. File hashes differ only in
  ComfyUI's embedded `tEXt` chunk, which carries the per-run unique reference filename. So the app can
  reproduce a proof row exactly, and the seed is the lever when a pose renders badly (a different sample, not
  a re-roll of the same one).
- **B** — `ListModelsAsync` returns `IReadOnlyList<PoseTestModelChoice>` (`Mechanism`, `MechanismLabel`,
  `CanCarryPose`, `Reason`) resolved per model through `IReferenceStrategyResolver.ResolvePoseAsync` —
  the ONE decision the render also takes, so the label cannot promise a different mechanism. The render
  result's `Mechanism` uses the same label vocabulary. `CanTestPose()` gates the button in both places; the
  hint line names the mechanism, or the resolver's reason when the model cannot carry a pose.
- **C** — `ElementStrategies` replaces the switch; unknown keys degrade to text and are logged; the
  strength input is disabled with "Not used on the reference-image route." for `NativeMultiReference`.
- **Evidence fixes on the way:** the ComfyUI tunnel (`https://comfy.kenacwood.net`) answers **403 to the
  default `Python-urllib/3.x` agent** on *every* call while accepting curl (verified: same bytes, same
  URL, only the agent differs). Both proof tools now send an explicit agent — including the probe's
  `/history` poll, whose unwrapped `urlopen` aborted an entire measurement run.

### Build / test

- `dotnet build DreamGenClone.sln` → **0 errors** (git-ignored OutDir, running webapp untouched).
- Targeted tests (`Pose*`, `ReferenceStrategy*`, `SceneImageResolution*`) → **232/232 pass**.

---

## Validated

- [ ] pending — needs the operator to restart the webapp and press **Test this pose** on a library pose,
      confirming it sends the pose's own skeleton through the reference route and reproduces a proof row.
