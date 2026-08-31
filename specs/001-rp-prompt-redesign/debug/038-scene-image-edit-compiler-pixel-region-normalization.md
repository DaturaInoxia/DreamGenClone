# 038 — Scene image edit compiler: pixel-scale target regions (local LM Studio backend)

**Date:** 2026-08-31
**Area:** `QwenSceneImageEditPromptCompiler` / `SceneImageEditCompilationJobHandler`
**Related:** 036 (abliterated swap), 033/034/035 (compiler contract hardening)

## Report

While validating the local LM Studio Qwen2.5-VL compiler endpoint
(`http://192.168.0.16:1234`, model `qwen2.5-vl-7b-instruct-abliterated`), the compiler returned
`targets[].region` as **pixel coordinates** instead of normalized `0..1` fractions — e.g.
`{"x":700,"y":432,"width":805,"height":196}` on a 1024×1024 image (`x+width=1505 > 1`).

**Symptom in the app:** `QwenSceneImageEditPromptCompiler.ParseRegion` throws *"Compiler target
region must be normalized and contained within the image."* → the compilation attempt is marked
`Failed` and the edit cannot proceed. Reproduced with `helpers/qwen-vl-local-compiler-test.ps1`
(the app's exact request shape + strict parser).

## Analysis

**Root cause:** LM Studio serves the model through llama.cpp's JSON-schema→grammar conversion, which
**does not enforce numeric bounds** (`minimum`/`maximum` in the schema). The Qwen2.5-VL model's
intrinsic behavior is to emit pixel-style bounding boxes. On the previous RunPod/vLLM endpoint, the
structured-output layer enforced `maximum: 1`, so regions arrived normalized — this is a regression
specific to the local backend.

Verified facts:
- 4/4 live runs emitted pixel-scale regions, including with explicit prompt guidance
  ("region MUST be normalized 0..1") — prompt-level fixes do **not** work.
- The source image dimensions are already known at the call site
  (`SceneImageMultimodalInput.Width`/`Height` in `SceneImageEditCompilationJobHandler`).
- Everything else about the response (schema fields, status, targets, preserve, compiledPrompt) was
  well-formed; the only blocker was the region values.

## Plan

Pass the known source image dimensions into the compiler's `Parse` and, when a region is
pixel-scale (any value > 1), convert it to normalized by dividing by the matching dimension and
clamp into `[0,1]`. Already-normalized regions keep the existing strict containment check; a
pixel-scale region with no dimensions fails fast (no guessing).

**Files:**
- `DreamGenClone.Web/Application/RolePlay/ISceneImageEditPromptCompiler.cs` — `Parse(string, int imageWidth, int imageHeight)`
- `DreamGenClone.Web/Application/RolePlay/QwenSceneImageEditPromptCompiler.cs` — `Parse`/`ParseTargets`/`ParseRegion`
- `DreamGenClone.Web/Application/RolePlay/SceneImageEditCompilationJobHandler.cs` — pass `input.Width`/`input.Height`
- `DreamGenClone.Tests/RolePlay/QwenSceneImageEditPromptCompilerTests.cs` — regression tests
- `helpers/qwen-vl-local-compiler-test.ps1` — mirror the fix for live-endpoint retesting

**Blast radius:** compiler contract + its single call site + tests. No RP engine/continuation files
touched. Null and normalized regions are unaffected.

## Resolution

- `Parse` now accepts `imageWidth`/`imageHeight` (concrete defaults `0` preserve strict behavior for
  existing callers that lack dimensions).
- `ParseRegion` detects pixel-scale regions (`x>1 || y>1 || width>1 || height>1`), divides each by
  the matching image dimension, and clamps into the valid `[0,1]` range (width/height clamped to
  `1 - x` / `1 - y`). Containment check gets a `1e-9` float tolerance. Without dimensions, a
  pixel-scale region throws a specific "no source image dimensions" error (fail-fast).
- `SceneImageEditCompilationJobHandler` passes `input.Width`/`input.Height` into `Parse`.
- Added 3 regression tests: normalization with dimensions, fail-fast without dimensions, overflow
  clamp. Existing compiler/job tests unchanged and passing.

**Build + tests:** solution builds `0 errors`; full suite **1418 passed / 0 failed** (RolePlay
subset 1119 passed).

**Live retest (2026-08-31):** `helpers/qwen-vl-local-compiler-test.ps1` against the local endpoint
returned `status: ready` with pixel regions (`x:700, y:432, w:805, h:196`, 1024×1024) → normalized
to `x≈0.684, y≈0.422, width≈0.316 (clamped), height≈0.191` → **PARSE OK**.

## Validated

- [ ] pending — awaiting user confirmation of a real in-app "Prepare an edit" on the local endpoint
  (expected: compiles to Ready in ~8–20 s).
