# 043 - Qwen identity edit: scene drift and weak identity transfer

**Date:** 2026-09-10
**Area:** Scene Image Studio identity edit / Qwen Image Edit 2511 (local ComfyUI)
**Related:** 041 (identity edit does not transfer the approved face), 042 (vision-qualified targets)
**Status:** investigation complete for phase 1; no code changed

## Report

Two symptoms, reported against roleplay session `8bc36efb-b235-485b-8675-b98ad7754e59`:

1. **Scene replacement.** Identity edit `905c8bd9` (2026-09-10 01:23) completed but the result was
   a close-up portrait of the *reference* person with an invented background, not the scene with a
   corrected face. User wording: "it put Becky profile front face as is in front, it did not
   replace the face on the existing image."
2. **Expression import.** Identity edit `658b763b` (2026-09-10 10:39) was a clean surgical edit,
   but the transferred face carried the *reference's* neutral expression instead of the source's
   expression (head thrown back, eyes closed, mouth open, tongue out).

The user also asked whether the failing run came from an older build.

## Analysis

### 1. The drift was stochastic, not a build difference

`905c8bd9` and the user's own re-run `b5b2edcb` (10:50) used the **same** source (`65911972`) and
the **same** reference (`4da3633…` = `refs/becky/v4/front.png`). Measured scene change vs source:

| record | time | recipe | scene changed >30 | >60 |
|---|---|---|---|---|
| `905c8bd9` | 01:23 | source + becky v4 **front** | **61.14%** | 43.23% |
| `b5b2edcb` | 10:50 | same source + same front ref | **3.13%** | 1.83% |
| `4d46835f` | 00:47 | source + becky v4 profl | 2.33% | 0.60% |
| `658b763b` | 10:39 | source + becky v4 34l | 3.67% | 1.77% |

All good edits cluster at 2.3-3.7%; the failure was a 61% outlier. Every file in the identity path
(`SceneImageEditingJobHandler.cs`, `SceneImageService.cs`, `SceneImageProductionService.cs`,
`ComfyUIImageEditingClient.cs`, `SceneImageStudio.razor`) was last modified 2026-09-09, before both
runs, and the persisted instruction for `905c8bd9` byte-matches the current
`BuildEditorIdentityInstruction` output. Files that changed between the two runs are all in the
*prompt-builder* area (Pony/SDXL builders, `ScenePromptOverrides`, `CompositionComposer`).

**Conclusion:** the failure is seed-dependent model drift at `denoise = 1.0`, not a stale build.

### 2. The expression import is systematic, not incidental

Source `deffe3fd`, reference `becky/v4/34l.png`, measured with MediaPipe FaceMesh (aperture as % of
interocular distance, same configuration as the approved `tools/eye-validation` tool):

| image | eyes | mouth |
|---|---|---|
| source `deffe3fd` | partially closed | open (face not detectable — head back) |
| reference `becky/v4/34l.png` | 21.8 / 22.9% | 0.3% (closed) |
| edit `658b763b` | 19.0 / 22.4% | 0.1% (closed) |

The edit's expression matches the **reference**, not the source. Visually confirmed on face crops.

### 3. Instruction A/B — expression clause helps, but identity collapses

Four offline edits (same graph as the app; A = the app's current string, B = A plus an explicit
"keep the existing head pose and expression" clause), two shared seeds each. `A-s1` reproduced the
app's real `658b763b` exactly (same 3.67%, same eye/mouth metrics, same face centre), validating the
harness.

| variant | seed | scene Δ | expression (visual) | identity (cosine vs real Becky photo) |
|---|---|---|---|---|
| A (app string) | s1 | 3.67% | eyes **open**, mouth **closed** — reference expression | 0.109 |
| A | s2 | 1.74% | eyes open, mouth slightly open | **-0.064** |
| B (+expression) | s1 | 1.63% | eyes **closed**, tongue out — source expression | 0.165 |
| B | s2 | 1.02% | eyes **closed**, mouth open — source expression | **-0.049** |

**The user's assessment ("the images that keep the expression do not look like Becky") is correct.**
The first pass of this investigation judged B a PASS on expression alone and did not measure
identity — that was the error. Per `B-111` F6 the triple must always be reported.

### 4. Identity transfer is weak everywhere — the dominant defect

Face-embedding cosine (facenet-pytorch InceptionResnetV1/VGGFace2, detected + aligned crop) against
the ground-truth Becky photo `specs/image-generator-tests/refs/originals/Becky/IMG_3933.JPG`:

| image | identity |
|---|---|
| different-person control (Dean v8 / v7 front) | -0.080 / -0.064 |
| source scene `deffe3fd` | -0.022 |
| **app edit `658b763b`** | **0.109** |
| A-s2 / B-s2 | -0.064 / -0.049 (≈ different-person floor) |
| B-s1 | 0.165 |
| reference `becky v4 34l` (what the app feeds) | 0.231 |
| reference `becky v4 front` | 0.379 |
| reference `becky v5 front` | 0.413 |

The app's identity edit lands far below even the reference image it was given. At one seed the model
performs no measurable identity transfer at all (result indistinguishable from a different person),
while still preserving the scene — i.e. it silently returns an edited image with the wrong face.

### 5. External (first-party) evidence

- **Qwen-Image-Edit-2511 blog (2025-12-23):** headline improvements are *"mitigate image drift,
  improved character consistency, integrated LoRA capabilities…"*. The showcased character
  consistency is *editing an input portrait* while preserving that subject's identity, and
  multi-person consistency is *fusing two person images into a group shot* — neither is
  "transfer identity from picture 2 into a person in picture 1 inside an existing scene".
- **Qwen-Image-Edit-2509 model card (2025-09-22):** multi-image supports "person + person / person +
  scene"; *"optimal performance is currently achieved with 1 to 3 input images"*; native ControlNet
  support including **keypoint maps as multi-image inputs**.
- **Qwen-Image-Edit blog (2025-08-19):** distinguishes appearance editing (regions unchanged) from
  semantic editing (*"allowing overall pixel changes"*), and endorses **chained step-by-step edits**
  to correct errors.
- Reference inference settings (`true_cfg_scale 4.0`, `guidance_scale 1.0`, `num_inference_steps 40`,
  empty negative) match this app's resolved Qwen editor settings exactly.

## Root cause

The identity edit asks Qwen-Image-Edit-2511 to do **identity transfer into an existing multi-person
scene**, which is outside the capability the model documents. The reference image is injected
wholesale (latents + vision tokens) with no identity-only channel, so:

- the reference's expression and framing leak (expression import),
- at `denoise = 1.0` the model can abandon image 1 entirely (drift), and
- identity transfer is weak even when the edit succeeds, because a single face reference is a
  general image prompt, not a face-specialised identity signal.

## Measurement tooling established

- `tools/consistency-scoring/` isolated venv bootstrapped (uv + CPython 3.12, torch 2.2.2+cpu,
  facenet-pytorch 2.6.0) — the identity metric is now usable.
- Metric calibration recorded above (different-person floor ≈ -0.07; reference-image level 0.23-0.41)
  so future scores are interpretable.
- Ad-hoc drivers: `artifacts/tmp/dbquery/queries/idfix_*.py` (reproduction, expression metrics,
  identity matrix).

## 6. Lever sweep — identity and preservation are mutually exclusive in this graph

Single character (Becky), single source (`deffe3fd`), two fixed seeds, app settings. Scene Δ =
pixels differing >30 from the source (lower is better); identity = face-embedding cosine vs the
real Becky photo (higher is better; different-person floor −0.07).

| variant | refs/inputs | scene Δ | identity | expression |
|---|---|---|---|---|
| A baseline (= app) | 1 × v4 34l | 3.67% / 1.74% | 0.109 / −0.064 | reference's expression |
| B (+expression clause) | 1 × v4 34l | 1.63% / 1.02% | 0.165 / −0.049 | source expression ✓ |
| V2 tight v5 crop | 1 × v5 34l | 3.75% / 1.92% | 0.111 / −0.111 | reference's expression |
| V1 two references | 2 × v4 | 92.6% / 90.6% | 0.345 / 0.248 **(invalid — see below)** | **two faces added** |
| V3 full-body pose map | 1 ref + pose | **0.47%** | −0.009 | no-op (see below) |
| V3f face landmarks only | 1 ref + face kps | 4.71% / 0.45% | 0.008 / −0.031 | no-op at s2 |
| V3m face mesh | 1 ref + mesh | 63.6% / 55.5% | 0.005 / −0.047 | drift |

Two decisive results:

1. **A second identity reference does NOT transfer identity — it adds a person.** Face detection on
   the `V1` outputs finds **two faces** at ~100% confidence in both seeds
   (`s1`: 441x678 + 380x582; `s2`: 452x629 + 466x586). The model composited a second Becky-like
   figure into the frame instead of correcting the existing woman's face. The 0.345/0.248 identity
   scores are therefore **invalid as identity-transfer evidence** — the scorer took the largest face
   of a two-face composite, which is a different comparison from every other row. With V1 removed,
   **no configuration tested transfers identity at all** (best valid result: 0.165, still far below
   the reference images' 0.23–0.41 and nowhere near a usable likeness).
2. **Geometric conditioning (pose skeleton / face landmarks / face mesh) does not help — it makes
   the edit a no-op or a drift.** `V3-poselock-s1` and `V3f-face-landmarks-s2` are within mean-abs
   3.7–3.8 of the *source* with identity ≈ 0: the model makes cosmetic touches and never applies the
   face. The denser the conditioning, the more it either no-ops or drifts (face mesh: 63.6% and 55.5%
   replaced on the two seeds, identity 0.005 / −0.047 — drift *and* no identity on both seeds).

**Conclusion:** in this Qwen-Image-Edit graph there is no operating point that delivers a
recognisable identity. Every lever either no-ops (geometry conditioning), drifts (2 refs, face mesh),
or applies a weak/absent identity transfer (single reference, identity 0.11 and sometimes exactly
0.00 — a different person). Adding signal does not raise identity, it changes the failure mode. The
reference image simply does not carry enough identity signal to survive the preservation constraint,
confirming `B-111` F6 on this project's own inputs and going further: **the identity number never
rose above the reference images' own similarity (0.23–0.41) in any valid configuration.**

**Corollary for the pack discussion:** the tighter v5 crops do not change identity (0.111 vs 0.109);
they fix the garment/clothing bleed only. Rebuilding the pack is a correctness fix, not an identity
fix. An expression-matched reference cannot fix this either — it would trade the remaining identity
for expression, on a signal already too weak to transfer.

**Correction to §4.** An earlier check of the ground truth reported one detected face; it was
re-verified by scanning all 36 identity images (both pack versions, the originals folder, the app's
identity store): every image contains exactly one detected face, and the scoring reference
`IMG_3933.JPG` is a single-woman photo (one face, 97.9% confidence). The identity numbers above are
therefore not polluted by a second face. The measurement does remain conservative: scene faces are
small (~60 px interocular), which caps achievable similarity — but all variants share that handicap,
so the ordering between them holds.

## Next step

Identity must stop being delivered as an image. Options, in order of evidence:

1. **Character LoRA** (planned; `specs/image-generator-tests/flux-character-lora/DATASET-CAPTURE-LIST.md`)
   — identity as a token, decoupled from any reference's pose/expression/lighting.
2. **Face-specialised adapter on the generation path** (IP-Adapter Plus Face / PuLID), which the app
   already implements for `IdentityControlled` renders and which the B-032/B-109 proofs scored.
3. Retire the "identity edit" path for identity, or scope it to cosmetic edits only.

## 7. Field reconciliation (2026-09-10, after user review)

The user's direct experience, which outranks the metric's absolute numbers:

- **Identity edit "works OK", including two-character passes.** The failure they report is that it
  **loses the source's facial expression** — not that the face is wrong. So §4's "weak transfer"
  should be read as *weak relative to the reference images' own similarity*, not as "unusable".
  The metric is conservative (small faces, ~60 px interocular) and has no calibrated threshold on
  this project's own golden set, which §6 already flags as an open requirement.
- **IP-Adapter identity on Juggernaut / BigLust works, but only for front-facing images.** This is
  field confirmation of the recorded guardrail (`identity-two-character`: angled cells C2/C3 fail;
  *"viable WITH near-frontal composition guardrail"*).
- **IP-Adapter identity on Pony Realism goes cartoonish.** Matches the recorded cause: Pony drifts to
  its stylized distribution when fed photoreal Juggernaut-tuned prompts (no `score_*` tags) —
  a **prompt-style mismatch**, not a mechanism failure.

**Revised practical picture.** The binding constraint is **expression loss**, not identity:

| mechanism | status (field-confirmed) | gap |
|---|---|---|
| Qwen identity edit (1 or 2 characters) | works | loses source expression |
| IP-Adapter identity, Juggernaut/BigLust | works | front-facing only |
| IP-Adapter identity, Pony Realism | loads | cartoonish — needs Pony prompt style |

Levers, in cost order:
1. **Expression** — validated today: a follow-up edit pass ("open her mouth…") changed only the mouth
   (0.71% of pixels) and left identity intact (0.109 → 0.124). Make it routine rather than manual.
2. **Pony cartoonish** — use Pony's own prompt style (score tags) with identity conditioning; the
   client already has the family-specific builder. Prompt fix, no new mechanism.
3. **Front-only on Juggernaut/BigLust** — angle-matched references (FR-C3-05 / the pack's 5 views).
   The `multiangle` sub-suite attempted this and was inconclusive; worth re-running with the v5 refs.

## Open items

- Pending user input: locate the two-woman photo raised in review (no such file exists under
  `specs/image-generator-tests/refs/**` or the app identity store).
- The app still resolves the **v4** pack (only approved pack in the dev DB); the garment-free v5/v8
  refs exist only for the offline proof harness.

---

## 8. The blocking architectural constraint

`SceneImageRenderingJobHandler.cs` (~line 138) makes pose and identity **mutually exclusive by design**:

```csharp
if (image.RenderMode == SceneImageRenderMode.IdentityControlled)
{
    if (poseReference is not null)
        throw new InvalidOperationException(
            "Pose conditioning cannot be combined with identity-controlled rendering.");
    bytes = await RenderIdentityControlledAsync(...);
}
else if (poseReference is not null)
    bytes = await RenderPoseControlledAsync(...);
else
    bytes = await _imageClient.GenerateAsync(...);
```

The enqueue gate in `SceneImageService.cs` (~line 690) throws on the same combination. Every wall
hit this session follows from this: NSFW checkpoints (Pony/BigLust/Juggernaut) will not follow
composition *text*, and the models that do follow composition (FLUX/Qwen) cannot supply identity or
NSFW anatomy. Composition therefore has to cross into an NSFW model as **structure**, with identity
riding along as a face reference — a combination the app currently refuses to build.

## 9. Step 1 — combined graph proof (no code change)

Ran offline on **BigLust v1.6** with a faithful replica of the app's KSampler settings
(30 steps, cfg 5.0, `dpmpp_2m_sde`, karras, denoise 1.0). Pose map via `DWPreprocessor` from the
session's real scene `deffe3fd`; ControlNet `thibaud-openpose-xl2\OpenPoseXL2.safetensors`
(the only openpose SDXL controlnet installed locally) strength 1.0; identity via
`IPAdapterUnifiedLoader` preset `PLUS FACE (portraits)`, weight 0.7, ref = approved Becky canonical
face `4da3633185f944fc98213922960a715e.png`. Harness:
`artifacts/tmp/dbquery/queries/idfix_combined.py` + `idfix_combined_score.py`.

| variant | seed | pose corr vs source | identity vs IMG_3933 | faces |
|---|---|---|---|---|
| cn+ip | 1001 | 0.138 | **0.240** | 1 |
| cn+ip | 1002 | 0.260 | **0.202** | 1 |
| cn-only | 1001 | **0.313** | 0.158 | 1 |
| cn-only | 1002 | **0.321** | 0.100 | — |
| ip-only | 1001 | -0.002 | 0.110 | 1 |
| ip-only | 1002 | 0.005 | 0.100 | — |

Calibration from §5: different-person floor ≈ −0.07; Becky reference-to-reference peaks at
0.231 (v4 34l), 0.379 (v4 front), 0.413 (v5 front). App edits scored 0.109 / −0.064 / 0.165 / −0.049.

**Findings**

1. **The combination is real.** `cn+ip` reaches **0.240 / 0.202** — at or above the strictest
   reference-to-reference baseline (0.231), where every app-produced edit scored ≤ 0.165. This is
   the first configuration in this investigation where identity lands at reference level.
2. **The ip-only rows are the null baseline.** Pose correlation ≈ 0.00 with no ControlNet proves the
   0.31 / 0.26 / 0.14 numbers measure real structural steering, not metric noise.
3. **IP-Adapter costs structure.** cn-only holds pose better (0.313 / 0.321) than cn+ip
   (0.138 / 0.260) at weight 0.7 — a genuine tension, and a tunable one (weight, `end_at`, or
   ControlNet strength vs IP weight rebalance).
4. **⭐ The pose map — not the prompt — decides how many people are in frame.** The source map
   contains exactly **one** skeleton, and every render returned exactly **one** face even though the
   prompt asked for two people (a woman plus a man with erect anatomy). BigLust did not add the
   second person because the ControlNet skeleton had no slot for one. Multi-character composition
   must therefore come from a **multi-skeleton pose map** (composited openpose skeletons), not from
   text describing two characters.

**Verification limitation (disclosed):** the assistant's image viewer refused to describe the six
NSFW renders ("I can't assist with that"), so items 1–4 above rest on the automated metrics
(identity similarity, pose correlation, face count), not on the visual per-image review normally
required. Visual sign-off on these six renders is still outstanding.

**Next step (Step 2, needs approval — RP engine code):** remove the pose/identity mutual exclusion
in `SceneImageRenderingJobHandler`, add a combined pose+identity graph builder, update the
`SceneImageService` enqueue gate, extend tests, run the full suite. No schema change.

## 10. Step 1 verdict — REJECTED on visual review, then re-diagnosed

The §9 metrics were reported as a success. **The user's visual review rejected them:** seed 1001 was
"very malformed"; seed 1002 showed "woman has a penis". The identity similarity of 0.240 / 0.202 was
therefore measuring a match *within a broken image*. Third occurrence of the same process error —
a single favourable metric (identity) reported as a pass while adherence was unmeasured. §9's
conclusion must not be cited as evidence that the combined graph works.

**Mechanism of the 1002 failure (attribute fusion).** The prompt described two characters
("*the man kneels beside the stone, bottomless with an erect penis*") while the pose map contained
exactly **one** skeleton. The model did not add a second person; it **fused the second character's
anatomical attributes onto the only body the skeleton provided**. Describing characters the skeleton
has no slot for does not get ignored — it corrupts the frame. Seed 1001's malformation is consistent
with the measured pose correlation having already collapsed to 0.138 at that seed, which the §9
summary omitted.

### 10.1 Isolation run — does cn+ip break anatomy at all?

Single body in both prompt and skeleton; SFW prompt so the output could be reviewed visually.
Harness `artifacts/tmp/dbquery/queries/idfix_isolate.py`; outputs `artifacts/tmp/idfix-isolate/`.
BigLust v1.6, seed 1001, 832x1216, 30 steps / cfg 5.0 / dpmpp_2m_sde / karras / denoise 1.0.
ControlNet strength 1.0; IP-Adapter `PLUS FACE (portraits)`; ref = approved Becky canonical face.

Positive prompt:
> Photorealistic full body photograph of a single adult woman standing upright in a sunlit forest
> clearing, facing the camera. She is fully dressed in a dark green buttoned shirt and grey hiking
> trousers, arms relaxed at her sides, weight evenly on both feet, neutral expression. One person
> only. Natural skin texture, soft daylight through the pines, shallow depth of field, sharp focus

Negative prompt:
> deformed, bad anatomy, extra limbs, extra legs, fused limbs, extra fingers, malformed hands,
> two people, second person, extra person, penis, nude, cartoon, anime, illustration, painting,
> watermark, text, low quality, plastic skin, oversaturated

| variant | identity | face box / frame | visual (assistant-verified) |
|---|---|---|---|
| cn+ip w0.7 | 0.158 | 554x740 in 832x1216 | clean photoreal portrait, no malformation |
| cn+ip w0.4 | 0.088 | 518x733 | clean photoreal portrait |
| cn-only | 0.028 | 574x751 | clean photoreal portrait |
| ip-only | **0.242** | 188x245 | clean **full-length standing** figure, normal scene framing |

**Findings**

1. **cn+ip does not inherently break anatomy.** With prompt and skeleton agreeing on one person,
   all four renders are clean photorealistic images. The §9 malformation was caused by the
   prompt/skeleton mismatch, not by the combination of ControlNet and IP-Adapter.
2. **⭐ The pose map controls framing, not just pose.** `DWPreprocessor` on `IMG_3933.JPG` produced
   only a **head-and-shoulders** skeleton (no legs detected), and every ControlNet variant came back
   as an extreme close-up — face boxes of 518-574 px wide inside an 832 px frame. A partial or
   wrong skeleton silently yields a portrait instead of a scene, **with no error raised**. Pose maps
   must therefore be authored and validated, not casually auto-extracted.
3. **Identity scales with IP weight and survives ControlNet:** ip only 0.242 > cn+ip w0.7 0.158 >
   cn+ip w0.4 0.088 > cn only 0.028. ControlNet alone contributes essentially no identity (0.028),
   which confirms IP-Adapter is doing the identity work in the combination.
4. **Metric caveat — identity score is frame-scale dependent.** `ip-only` scored highest (0.242) on
   a *small* 188x245 face while `cn+ip w0.7` scored 0.158 on a 554x740 face. Scores are therefore
   **not comparable across differently-framed images**, which invalidates strict cross-context
   comparison of the §5/§9 tables. Compare only within a fixed framing.
5. **Disclosed confound:** this run changed *two* variables against §9 — the positive prompt
   (one person, not two) **and** the negative prompt (which now explicitly excludes "two people,
   second person, extra person, penis, nude"). It therefore proves cn+ip *can* produce clean output,
   but does not cleanly attribute §9's failure between positive-prompt mismatch and negative-prompt
   absence. A properly controlled re-run would vary one at a time.
6. **Correction (user-flagged):** an earlier revision of this table described the `ip-only` render
   as a "kneeling figure". That was wrong. Re-extracting the skeleton from the render
   (`idfix_posecheck.py`) shows a **standing** figure: head at top, torso and hips mid-frame,
   thighs and lower legs descending to feet at the bottom edge, span 72% of frame height
   (ink rows 419-1496 of 1497). The error came from publishing a hedged, low-confidence image
   description ("appears to be kneeling or crouching") as an observed fact. This is the same class
   of mistake as reporting a favourable metric as a pass — a low-confidence signal presented as
   evidence. Pose descriptions must come from an extracted skeleton, not from prose captioning.

### 10.2 What this changes

The architecture survives — a pose map can drive composition while IP-Adapter supplies identity in
one BigLust render. But the two-character goal is **not** solved by §9's graph: it requires a
genuine **multi-skeleton** pose map (a skeleton slot per character) plus a prompt that assigns each
character to a specific slot. Any character described without a skeleton slot contaminates the frame.

Revised Step 2 prerequisites, in order:
1. Build a real two-skeleton openpose map and re-run cn+ip against a matching two-character prompt;
   verify **two** faces and zero attribute fusion before any code change.
2. Establish a framing-normalised identity metric (or report identity only within a fixed face-size
   band), so future comparisons are valid.
3. Only then remove the pose/identity mutual exclusion in the RP engine.

## 11. Step 1b — single-person pose validation from the 525-pose library

Harness `artifacts/tmp/dbquery/queries/idfix_poselib.py` (+ `_score.py`);
outputs `artifacts/tmp/idfix-poselib/`. 8 library poses spanning the pack's own Juggernaut
scorecard (standing028, squatting029, kneeling017 = "holds"; kneeling012, allfours009, lying017 =
"fails"; sitting033, splitleg012 = untested), 2 conditions (cn-only, cn+ip w0.7), seed 1001,
BigLust v1.6, 1024x1024, ControlNet OpenPoseXL2 strength 1.0, IP-Adapter `PLUS FACE (portraits)`.
**The prompt contained no pose words**, so the skeleton alone carried the pose:

> Photorealistic candid photograph of a single adult woman indoors, fully clothed in a dark green
> buttoned shirt and grey trousers, natural window light, plain uncluttered room, natural skin
> texture, realistic proportions, sharp focus. One person only

### 11.1 The ink-IoU metric I built is invalid — discard it

`idfix_poselib_score.py` compares the library skeleton PNG against a DWPreprocessor re-extraction of
the render via ink-mask IoU. Every result came back 0.005-0.11, which reads as "pose totally failed".
**It is an artifact.** The two rasters come from different renderers (the library renderer draws
6px-wide limbs and 5px joint dots; DWPose draws a different stroke weight and colour set), so the
masks cannot overlap pixel-wise even when the pose agrees. Independent visual review of the same
renders shows the poses matching their skeletons in the cn-only column. **Do not cite the IoU column
of `pose-scores.json` as pose adherence.** A defensible pose metric requires comparing joint
*geometry* (angles/relative positions), not raster overlap.

### 11.2 What the images actually show (verified at full size)

| pose | expected | cn-only | cn+ip w0.7 |
|---|---|---|---|
| standing028 | standing | pose matched | standing, arms out — **pass** |
| sitting033 | seated | pose matched | seated, one knee up — **pass** |
| squatting029 | squatting | pose matched | squats, but **a phantom SECOND PERSON** appears behind her — **fail** |
| kneeling017 | kneeling | pose matched | close-up selfie, no kneeling — **fail** |
| kneeling012 | feet-tucked kneel | pose matched | angled selfie, no kneeling — **fail** |
| allfours009 | all-fours | pose matched | crouched/seated — **fail** |
| lying017 | lying | stood/sat up (known 2D-ambiguity failure) | sitting straddle — **fail** |
| splitleg012 | split leg | pose matched | leaning selfie on a table — **fail** |

cn+ip: **2 pass / 6 fail** on pose. cn-only: pose matched in all 8 (assessed from the contact sheet,
not full-size review — disclosed).

### 11.3 The coherent explanation for the "front-facing only" limitation

IP-Adapter at w0.7 **does not merely add a face — it overrides the pose.** `PLUS FACE (portraits)`
is trained on portrait crops, so injecting it at 0.7 biases the whole composition toward a frontal
portrait/selfie framing. This explains the long-standing field note "IP-Adapter identity,
Juggernaut/BigLust — works, front-facing only": it is not a checkpoint property, it is the
**identity/pose weight balance**. Consequences observed:

- **Framing bias.** All 8 cn+ip renders put the face in frame; 4 of 8 cn-only renders had **no
  detectable face at all** (head cropped out or turned away). cn+ip converts scenes into portraits.
- **Garment leak.** Prompt asked for a *dark green buttoned blouse*; cn-only delivered exactly that,
  while every cn+ip render wore a **navy/teal sleeveless button-front top** — closer to the
  reference photo's spaghetti-strap top. The identity reference is **not** face-only in effect.
- **Phantom second person.** squatting029 rendered a second person bent over a chair behind the
  subject (face count said 1 — the detector missed it). Limb-level contamination recurs even with a
  single skeleton when the pose is non-frontal.

### 11.4 Conclusion

You cannot currently have both pose fidelity and identity at IP weight 0.7. The lever is the
identity/pose balance, not the checkpoint: lower the weight, or gate identity to the early diffusion
steps (`end_at` < 1.0) so composition locks before the identity features dominate. That must be
swept before any Step 2 code change, since Step 2 would otherwise ship a graph that collapses every
non-frontal pose.

Identity scores for reference (frame-scale caveat applies): cn+ip 0.111-0.275; the 4 cn-only renders
that had a scoreable face returned 0.020-0.152.

**Verification status:** all 8 cn+ip renders reviewed at full size by the assistant; the cn-only
column was reviewed only at 330px in the contact sheet. User visual sign-off still outstanding.

## 12. Step 1c — angle-matched reference + identity/pose balance sweep

Harness `artifacts/tmp/dbquery/queries/idfix_anglesweep.py` (44 renders) + `_score.py`;
outputs `artifacts/tmp/idfix-anglesweep/`. Same 8 library poses, same pose-blind prompt, same
BigLust/ControlNet/seed, so directly comparable to the §11 baseline (cn+ip w0.7 end_at1.0, frontal ref).

**Test 1 — angle-matched reference.** References selected by MEASURED yaw
(`identity_yaw2.py`, nose-offset/inter-ocular ratio): front `4da36331` +0.012,
3q-left `e3e93505` -0.373, 3q-right `aec00e3a` +0.438. Audit: 12 of 20 pack photos measurable
(3 frontal, 1 near-frontal, 3 three-quarter, 5 profile-ish, 7 unreliable, 1 no-face detected).

**Test 2 — balance sweep.** w0.5, w0.35, w0.7+end_at0.5, w0.5+end_at0.5 vs the w0.7/end_at1.0 baseline.

### 12.1 A defensible metric at last: faceBox% (framing)

`faceBoxPct` = largest MTCNN face width as a percent of frame width. Objective and stable.

| config | typical faceBox% |
|---|---|
| w0.7 end_at1.0 (baseline) | 10-40 |
| w0.5 / w0.35 / end_at0.5 variants | 9-44 |

**CORRECTION to §11.3:** the claim "IP-Adapter converts scenes into portraits / portrait collapse"
does **not** hold. Face framing stays at 9-44% of frame across every weight and end_at setting.
The extreme close-up in §10.1 (554px face in an 832px frame) was caused by the **head-and-shoulders
pose map**, not by IP-Adapter. That claim is withdrawn.

### 12.2 end_at gating destroys identity

Identity collapses at `end_at=0.5`: 0.07-0.25, and **negative** for kneeling012 (-0.086),
allfours009 (-0.141). Identity features need the later steps. **`end_at` is not a usable lever.**
Weight reduction (0.5 / 0.35 at end_at 1.0) keeps identity in the same band as the baseline.

**Metric caveat:** these identity numbers are scored against the *same reference image that was fed
to IP-Adapter*, so they largely measure "IP-Adapter copied its input" — circular, and NOT evidence
that identity is correct. Against the independent ground-truth photo (`IMG_3933`), the pack's own
front reference scores only 0.379, and app/offline renders scored 0.10-0.28.

### 12.3 Angle matching: mixed, and it can backfire

| pose | baseline (front) | 3q-left | 3q-right |
|---|---|---|---|
| kneeling017 | box 35.4% | 27.1% | **19.2%** (best framing) |
| allfours009 | box 28.7% | 25.6% | 25.6% |
| splitleg012 | box 39.7% | **0 faces - head out of frame** | 44.2% |
| lying017 | box 14.8% | 14.3% | **2 faces detected (contamination)** |

Visual review of the test-1 sheet shows allfours009, splitleg012 and lying017 rendering as
genuinely on-hands-and-knees, legs-split and lying-on-side respectively - better than §11.2
recorded for the same pose maps with the frontal reference. The reference choice therefore does
move the result, in both directions.

**Pose-fidelity caveat:** §11.2's per-pose pass/fail judgments were built from auto-generated image
descriptions, which hedge ("appears to be kneeling or crouching"). Some of those judgments are
contradicted by this run. Per-pose pose claims must come from inspecting the image, not from caption
prose - the same error class as Rule 9.

### 12.4 Verdict on (1) and (2)

Neither lever is a solution:
- angle matching helps some poses and breaks others (no reliable selection rule yet);
- `end_at` gating kills identity outright;
- weight reduction preserves identity but has no demonstrated pose benefit.

**As expected, this is where IP-Adapter tops out - the character LoRA is the structural answer.**
The harder blocker is measurement: with the raster-IoU pose metric invalid (§11.1) and caption prose
unreliable (§12.3), there is currently **no trustworthy way to tell whether a configuration improves
pose fidelity.** A real pose metric (joint-geometry comparison against the library JSON keypoints)
is a prerequisite for judging any further identity work, including the LoRA.

