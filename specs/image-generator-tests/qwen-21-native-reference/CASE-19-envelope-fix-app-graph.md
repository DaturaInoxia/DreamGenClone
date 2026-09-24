# Case 19 — the app's own graph with the QUALIFIED sampler envelope (defect fix, 2026-09-24)

## Why this case exists

Operator report: "image cb7711a6-… using 2.1 is bad, not sure what is wrong. clearly bad render."

The render (Composition stage, `qwen_image_2.1_int8_convrot.safetensors`) ran the **SDXL/Juggernaut
recipe** — `cfg 5, steps 30, dpmpp_2m_sde, karras` — because `SceneImageRenderingJobHandler` passed the
studio's `SettingsJson` into `ComfyUIImageClient.BuildQwenImage21Workflow`, which applied it
(`options?.Cfg ?? 1.0` → 5.0). At cfg 5 a cfg-1-distilled model is far out of distribution: the PNG
showed blown-out neon blobs, heavy grain and mottled anatomy. The submitted prompt was a correct
natural-language brief, so this was **not** a prompt problem.

## The fix under test

The 2.1 graph no longer accepts a sampler options bag at all. `BuildQwenImage21Workflow(unetName, refs, …)`
takes its `Steps` / `Cfg` / `SamplerName` / `Scheduler` from `QwenImage21Refs`, which is read from the
model's `NativeMultiReference` qualification (`QwenImage21ModelSettings.Resolve`, fail-fast when any value
is missing) — the same posture as FLUX, whose builder never took options either.

Recorded on both 2.1 rows:

```json
"Resolution":1024,"MaxReferences":16,"Steps":25,"Cfg":1.0,"SamplerName":"euler","Scheduler":"simple"
```

## Run

The graph was emitted by the **app's own builder** (test `BuildWorkflow_EmitGraphForHostProof_WhenRequested`,
`QWEN21_EMIT_GRAPH` / `QWEN21_EMIT_PROMPT`) and submitted to the local ComfyUI host **unchanged** by
`artifacts/tmp/qwen-2-1/envelope-fix/submit-app-graph.ps1`:

- prompt_id: `c19addbd-c9fb-49c8-a0ae-06a5020b3206`
- output: `dreamgen_app_00213_.png`
- graph sampler: **steps 25, cfg 1, euler, simple** (the qualified envelope)
- size: 1024x1024 (studio size control), prompt-only (no references), seed 20260922

Exact prompt submitted:

> Photorealistic wide shot in a secluded pine clearing at late afternoon: a lean, bare-chested man with short
> brown hair and light olive skin kneels upright on a large sun-warmed flat stone, one slick fist wrapped
> around his spent cock, white release gleaming on his knuckles and lower belly, a slow satisfied smile on
> his face, gaze down at her. A nude woman with brown hair in a bun, fair skin, soft belly and wide hips,
> lies open on her back on the same stone, knees fallen wide, flushed trembling thighs, one hand near her
> glistening sex, eyes locked up on his. A discarded blue swimsuit lies on the rock beside her. Low sun
> filters through the pines. 35mm, shallow depth of field, natural skin texture.

## Verdict: PASS (fix proven)

Same prompt, same seed, only the envelope changed — and the render is clean and correctly exposed: the pine
clearing, the low late-afternoon sun through the trunks, the sun-warmed grey stone, the man standing/kneeling
over the woman, the woman on her back, and the **discarded blue swimsuit on the rock**. No grain blow-out,
no neon artefacts, coherent anatomy.

Honest caveats (inherent to the official cfg-1 envelope, not defects):

- Prompt adherence is **looser** at cfg 1 (the negative prompt is inert by design). The man's arms are
  outstretched rather than wrapped around himself, and the woman's legs are not exactly as worded.
- This is a square 1024x1024 canvas. Earlier 2.1 proofs used 832x1216 / 1216x832. If adherence matters
  more than framing freedom, tune the **qualified** values (steps, and a 2.1-native aspect pair) with a
  proof — never by re-applying an SDXL recipe.
