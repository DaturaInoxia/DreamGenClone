# pose-library-proof

Proves (or disproves) that a family of library poses actually survives the **Qwen-Image-2.1 native
reference route**, one pose per render, and leaves the images and their request graphs behind as
evidence.

## Why it exists

The 2.1 pose route is *"the skeleton travels as a reference image"* — `ReferenceStrategyResolver`
prefers `PoseControlNet` and falls back to `NativeMultiReference`, and the 2.1 model row
(`3f1c9a52-…`) declares only the latter, so `PoseTestRenderService` calls the reference client
(`ComfyUIImageClient.BuildQwenImage21Workflow`). The recorded proof for that route
(`specs/image-generator-tests/qwen-21-native-reference/`) covers standing / squatting / kneeling, and
its own limitations section lists **all-fours and lying as unmeasured**. "All-fours works too" is
therefore not a fact on record — it is a question, and this runner answers it with one render per pose.

## It does not invent a graph

The runner takes the graph **the app emitted** and changes only the two values the app itself derives
per call:

- the `LoadImage` filename (the app names it from the render's correlation id), and
- the sampler seed.

Before submitting anything it refuses the two **silent reference-dropping shapes**
(a bare `image_1` input; a hand-built `images` dict) and requires the flat dotted
`images.image_N` autogrow wiring. ComfyUI returns HTTP 200 for a bogus input name, so a wrong wiring
renders a plausible **unposed** image — a "pass" that tested nothing.

A unique reference filename is uploaded per run: ComfyUI caches node execution by name, and a cache
hit returns history without the keypoints the probe needs.

## Run it

```powershell
# 1. Emit the app's real graph (hermetic unless QWEN21_EMIT_GRAPH is set).
#    NOTE: the test host's working directory is its output dir, so pass an ABSOLUTE path.
$env:QWEN21_EMIT_GRAPH  = 'd:\src\DreamGenClone\artifacts\tmp\qwen-2-1\pose-allfours-graph.json'
$env:QWEN21_EMIT_REFS   = 'af-skeleton.png'
$env:QWEN21_EMIT_PROMPT = '<the prompt>'
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj `
  -p:OutDir=d:\src\DreamGenClone\artifacts\build-check\poseproof\ --nologo -v q `
  --filter "FullyQualifiedName~BuildWorkflow_EmitGraphForHostProof"

# 2. Render one image per pose per seed.
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/pose-library-proof/run_pose_proof.py `
  --graph artifacts/tmp/qwen-2-1/pose-allfours-graph.json `
  --skeleton-root DreamGenClone.Web/wwwroot/pose-library/library/openpose-nsfw `
  --pose-glob "*all*fours*" `
  --seeds 20260922,771122 `
  --out specs/image-generator-tests/pose-library-all-fours --images-subdir images
```

Resumable: a run already in `manifest.json` is skipped unless `--force`.

Useful flags:

| Flag | Why |
|---|---|
| `--images-subdir images` | writes the renders into `<out>/images` — the proof-package shape |
| `--prompt-map <json>` | per-pose prompt overrides (`{poseStem: prompt}`), for a canned-wording retest |
| `--prompt-suffix "<text>"` | appends a clause to every prompt without rewriting the graph |
| `--pose-glob` | which poses to test (matched against the skeleton folder) |

### Grounding the wording

`describe_pose.py` prints a pose's verifiable keypoint facts — which joints are lowest (the contacts),
whether the head is above or below the hips, and where each limb chain ends:

```powershell
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/pose-library-proof/describe_pose.py `
  pose-packs/openpose-nsfw/NSFW_all_fours/512768/NSFW_all_fours006.json
```

Use it before writing canned wording. Reading an arm-versus-leg off a stick figure is guesswork — the two
renderers use different limb palettes — and it also reveals **incomplete references** (a joint with
confidence 0.00 is one the model has to invent, which no prompt can fix).

## Measure adherence (do not eyeball it)

`measure_run.py` wraps the approved probe over a whole run: it maps each rendered pose back to the
pack's own keypoint JSON (using the importer's id rule) and writes `measurements.json` plus one probe
result per render. It adds no scoring of its own.

```powershell
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/pose-library-proof/measure_run.py `
  --run specs/image-generator-tests/pose-library-all-fours `
  --pack-root pose-packs `
  --comfy https://comfy.kenacwood.net --max-mean-pct 6
```

`--pack-root` takes either the packs ROOT (one folder per pack — the app's `PoseLibrary:PacksRoot`, i.e.
`pose-packs`) or a single pack folder (`pose-packs/openpose-nsfw`). Passing a pack's *sub*folder silently
matches nothing, so every row reports "no pack JSON for this preset id" — the tool now says which of the
two shapes it found.

The probe extracts DWPose keypoints from the render itself, so no separate extraction step is needed.
To measure a single render by hand, `--candidate` is the **pack's own** JSON for that pose:

```powershell
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/pose-angle-probe/probe_pose_angle.py `
  --candidate pose-packs/openpose-nsfw/NSFW_all_fours/512768/NSFW_all_fours003.json `
  --plate specs/image-generator-tests/pose-library-all-fours/runs/<run>/<pose>__s20260922.png `
  --comfy https://comfy.kenacwood.net --max-mean-pct 6
```

It exits 1 on a fail. Joint geometry is scored as a percentage of figure height and is
scale/position invariant, so a render at a different size than the reference is still comparable.

## Outputs

| Path | What |
|---|---|
| `<out>/images/<pose>__s<seed>.png` | the render (with `--images-subdir images`, the proof-package shape) |
| `<out>/requests/<pose>__s<seed>.json` | the exact graph submitted, per run |
| `<out>/manifest.json` | pose, seed, skeleton sha256, reference filename, prompt_id, elapsed, output sha256 |
| `<out>/measurements.json` | the probe's verdict per render (`measure_run.py`) |

Renders go in the proof PACKAGE (`specs/image-generator-tests/<package>/`), never a `runs/<timestamp>`
path or `artifacts/tmp`: the operator reads that folder, and a timestamped path reads as scratch.

## Reading the result

- **Joint error, not vibes.** A pose that "looks off" but measures under a few percent of figure
  height is following the skeleton; a pose that measures 15 %+ is not.
- The route carries **geometry, not facing** (measured 2026-09-24: an identical front-facing
  skeleton came back facing away until the prompt stated the view). For an all-fours pose the
  equivalent ambiguity is which way the figure faces along its long axis, so a failure to face the
  intended way is a *prompt* finding, not a skeleton finding.
- One posed person per call. Multi-skeleton composition is out of scope for this runner.
