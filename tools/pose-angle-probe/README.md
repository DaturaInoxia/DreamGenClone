# pose-angle-probe

> **ComfyUI behind a proxy:** the app's provider URL (`https://comfy.kenacwood.net`) answers **403 to the
> default `Python-urllib/3.x` agent** on *every* call while accepting curl — verified 2026-09-25 with the
> same bytes to the same URL, only the agent differing. This tool now sends an explicit `User-Agent` on
> the upload, the `/prompt` submit and the `/history` poll. If you add another call, add the agent too:
> an unwrapped `urlopen` on `/history` aborted a whole measurement run with `HTTPError: 403`.

Measures how closely a **projected** pose agrees with what **DWPose reads back** from an image, and prints the
numbers. This is what has to run before an angle can be called `known-good`: the flag records a measurement, and
this is the measurement.

## Measured: what the skeleton carries on Qwen-Image-2.1, and what it does not (2026-09-24)

Runs through the **app's own graph** (`ComfyUIImageClient.BuildQwenImage21Workflow`, emitted via
`QWEN21_EMIT_GRAPH` and submitted unchanged by `helpers/local-comfyui-host/run-local-proof.ps1`), one skeleton
reference, 1024×1024, seed 20260922, cfg 1, 25 steps, euler/simple. **The prompt is the only thing that changed
between the two rows.**

| reference | prompt | mean | p95 | joints compared | verdict |
|---|---|---|---|---|---|
| `front.skeleton.png` | "…standing upright, arms at their sides…" | **17.30 %** | 30.27 % | 15 | fail — rendered **facing away** |
| same skeleton | "…**facing the camera, front view, we can see her face**…" | **2.45 %** | 4.88 % | 18 | pass |

Two conclusions, and they point in opposite directions:

- **The skeleton carries the pose.** In the passing run the render reproduced the rig's stance exactly, including
  the natural stance's fore/aft foot stagger. 2.45 % mean joint error is a genuinely good result.
- **The skeleton does NOT carry the facing.** Both runs used the same front-facing skeleton; one came back
  backwards. Facing is decided by the **prompt wording** on this route.

The reason is a size limit, not a plumbing fault: a COCO-18 stick figure encodes front-versus-back in **five small
face dots** (a nose and two eyes on a 1024 px canvas), and a diffusion model's encoder sees far less than that. The
pack's own DWPose skeletons have the same property. So on the native-reference route a pose preset cannot supply
the view direction by itself — the angle has to travel in the prompt as well as in the skeleton.

The 15-vs-18 joint count above is **not** a framing problem: a figure facing away genuinely has no visible nose or
eyes, so DWPose reports them hidden. That is the probe reading the render correctly.

## Measured baseline (2026-09-02), SDXL/ControlNet route

The five named body angles, projected at `PoseStudio:CameraDistance = 4.5`, rendered through `juggernautXL_ragnarok`
+ `OpenPoseXL2` at 0.85 strength on a 1024×1024 plate, then read back with DWPose:

| angle | mean | p95 | max | worst joint | candidate span | rendered span |
|---|---|---|---|---|---|---|
| front | 2.12 % | 4.08 % | 4.20 % | neck | 22.61 % | 20.34 % |
| 3/4 left | 2.68 % | 4.82 % | 5.79 % | l_shoulder | 16.89 % | 17.81 % |
| 3/4 right | 2.57 % | 5.90 % | 6.54 % | r_shoulder | 16.89 % | 17.32 % |
| profile left | 3.50 % | 6.62 % | 8.00 % | l_shoulder | 7.30 % | 1.76 % |
| profile right | 4.28 % | 8.07 % | 8.77 % | r_shoulder | 7.30 % | 2.05 % |

All five pass a 6 % mean bar. The **span column is the finding**: on the three front-facing angles the rig and the
render agree on shoulder width to within a couple of points, but in **profile the rig keeps 7.30 % against the
render's ~2 %**. That gap is perspective, not pose — at profile the shoulders are separated in *depth*, and a camera
at distance 4.5 spreads them on screen the way no real portrait lens does. `PoseProjectionTests`
`AProfileSpanShrinksTowardTheRealRenderAsTheCameraMovesBack` pins that a camera further back monotonically
reduces the span, so the lever is `PoseStudio:CameraDistance` and the way to resolve it is to raise it and re-run
this probe.

## What it compares

Two OpenPose person JSONs:

| Side | What it is | Where it comes from |
|---|---|---|
| **candidate** | the keypoints the app projected for an angle | the app (Projected poses exported from the Pose Library) |
| **reference** | keypoints DWPose extracted from an image in that view | the app's committed angle skeletons, or a plate run through ComfyUI with `--plate` |

## The shape DWPose actually returns

ComfyUI's `OpenposePreprocessor` emits `openpose_json` as a **canvas document**, not a bare list of people:

```json
[{ "canvas_height": 1024, "canvas_width": 1024,
   "people": [ { "pose_keypoints_2d": [54 floats], "face_keypoints_2d": [141],
                 "hand_left_keypoints_2d": [63], "hand_right_keypoints_2d": [63] } ] }]
```

Two traps live in that shape, and both were hit while building this tool:

- The **outer list holds the canvas, not the person.** Reading it as the person yields an empty joint list, which
  looks exactly like "DWPose could not find a full figure" and sends you off tuning prompts for a parsing bug.
  The tool unwraps `people` first and also accepts a bare person dict, so a saved payload can be passed to
  `--reference`.
- The coordinates are **fractions of the canvas (0–1)**, not pixels. The scoring is scale-invariant, so this does
  not affect the numbers — but it does mean a raw payload and a projection are directly comparable.

The node is `OpenposePreprocessor`. It is the DWPose-based estimator this server exposes; a `DWPreprocessor` node
does **not** exist on it. `POSE_KEYPOINT` is not a terminal output, so any graph reading it needs an image sink
(`SaveImage`) or ComfyUI rejects the prompt with `prompt_no_outputs`, and the tool re-uploads the plate under a
**unique name** on every run because a ComfyUI cache hit comes back through `/history` without the
`openpose_json` output.

## Why it scores joint geometry

Raster overlap can score a hollow outline as a match, and raster IoU was already proved invalid for this purpose
in B-123's scoping. Every number here is a **per-joint displacement as a percentage of figure height**.

The comparison is deliberately scale- and position-invariant — both sides are normalised by their own height and
centred on their own bounding box — because the candidate is the app's rig and the reference is whatever the image
model drew. What has to agree is the *shape* of the pose, not the figure's size.

It also reports the **shoulder span as a fraction of height** on both sides. That is the non-circular part: a round
trip through a plate the candidate itself conditioned will tend to reproduce the candidate, but the angle itself
still has to survive. A profile whose span did not collapse means the render ignored the request.

## Usage

```bash
# Against an existing reference (fast, no GPU work)
python tools/pose-angle-probe/probe_pose_angle.py \
    --candidate artifacts/tmp/pose-probes/profile-right.json \
    --reference pose-packs/openpose-nsfw/NSFW_standing/512768/NSFW_standing028.json \
    --max-mean-pct 6 --angle profile-right \
    --out artifacts/tmp/pose-probes/profile-right.measurement.json

# Producing the reference from an image, via the local ComfyUI. The plate must be a single, unobstructed
# full-body figure, and the app's export writes a matching .skeleton.png next to each .json to condition the
# render that produces it.
python tools/pose-angle-probe/probe_pose_angle.py \
    --candidate artifacts/tmp/pose-probes/profile-right.json \
    --plate artifacts/tmp/pose-probes/profile-right.plate.png \
    --comfy http://192.168.0.11:8188 \
    --max-mean-pct 6 --angle profile-right
```

`--max-mean-pct` is **required**. The bar is declared by the caller, not hidden in the script: a threshold buried
in a tool is a claim nobody agreed to.

Exit code is `0` when the measurement passes the declared bar and `1` when it does not, so it composes into a
gate without anyone parsing text.

## What it refuses

- A document holding **two people** — a two-person reference would score against the wrong figure and report a
  large error that looks like a bad projection.
- A partial extraction (fewer than 18 body joints) — it cannot be compared joint by joint.
- A pose with fewer than four visible joints, or zero height.
- Giving both `--reference` and `--plate`, or neither.

## Rendering the plate

Rendering the plate is not part of this tool: the declaration of *how* a person is rendered is a pipeline decision,
not a measurement decision. The harness used for the baseline above is `artifacts/tmp/pose-probes/render_plate.py`
(git-ignored, throwaway): `juggernautXL_ragnarok` + `thibaud-openpose-xl2/OpenPoseXL2` at strength 0.85, 1024×1024,
28 steps, `dpmpp_2m`/`karras`, seed 12345.

Keep the plate's aspect ratio equal to the skeleton's. ComfyUI's ControlNet apply resizes the conditioning image to
the latent non-uniformly, so rendering a 1024×1024 skeleton into a portrait latent squeezes the figure
horizontally and lengthens it vertically — the probe would then report a projection error that is really a stretch.

## Not a substitute for looking

Agreement between a projection and an extraction is evidence that the angle renders and reads back as asked. It
is not evidence that an angle *looks right* to a person, and it does not validate the model's taste. Eyeball the
plates too.
