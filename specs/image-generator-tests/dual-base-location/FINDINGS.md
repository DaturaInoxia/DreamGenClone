# Dual-base location and identity findings

**Date:** 2026-09-14  
**Status:** Planning evidence; no production application change made.

## Objective

Generate two base images for the same arbitrary location and the same two characters:

- **Base A:** Dean and Becky facing one another in side profile.
- **Base B:** Dean directly behind Becky, Becky's back against Dean's chest; both face the same direction and the camera sees their side profiles.
- Later slideshow edits use Base A for the first sequence and Base B after the orientation transition.

The location must remain the same, while Dean and Becky must retain their approved identities.

## Verified findings

### 1. SDXL IP-Adapter identity works, but it is not location conditioning

The repository's working BigLust/Juggernaut identity workflows use:

- `CheckpointLoaderSimple`
- `EmptyLatentImage`
- Regional `IPAdapter` nodes with `PLUS FACE (portraits)`
- Per-character masks and angle-matched face references

This proves the current IP-Adapter path for face identity. It does **not** prove arbitrary location preservation. Feeding a bedroom image into the face IP-Adapter as a location reference is the wrong conditioning type.

### 2. BigLust and Juggernaut were not verified as arbitrary-location img2img solutions

The existing Juggernaut identity proof files are text-to-image workflows, not location-reference img2img workflows. They do not contain `VAEEncode` or ControlNet location conditioning. Therefore, there is no evidence in this repository that changing BigLust to Juggernaut alone solves location preservation.

Do not describe Juggernaut as proven for this use case without a new A/B proof.

### 3. Qwen AIO location-only proof works for location composition but not identity

`Qwen-Rapid-AIO-NSFW-v23.safetensors` can edit a location source image and produce a usable bedroom composition. However, the location-only proof relied on text names/descriptions for Dean and Becky; the generated faces were not the approved Dean/Becky identities.

The Qwen proof scripts and runs include several prompt iterations for Base B. The correct intended Base B description is:

> Dean directly behind Becky; Becky's back against Dean's chest; both facing the same direction to the right of the frame; side-view camera from the right; both right profiles visible.

Avoid phrases such as **"away from the camera"**. That describes a rear-facing view and caused the wrong composition.

### 4. Qwen native multi-reference is a separate mechanism

Qwen Image Edit's native reference path is not SDXL IP-Adapter. The working split-model proof graph uses:

- `UNETLoader` with `qwen_image_edit_2511_fp8mixed.safetensors`
- `CLIPLoader` with `qwen_2.5_vl_7b_fp8_scaled.safetensors`, type `qwen_image`
- `VAELoader` with `qwen_image_vae.safetensors`
- `TextEncodeQwenImageEditPlus`
- `FluxKontextMultiReferenceLatentMethod` with `reference_latents_method = index_timestep_zero`

The source is `image1`; ordered extra references are passed as `image2`, `image3`, etc. For this use case, `image2` should be Dean and `image3` Becky.

The portable reference graph is:

- `helpers/runpod/workflows/qwen-image-edit-2511-proof.json`
- `helpers/runpod/serverless/proofs/qwen-edit-aio.workflow.json` for the exact AIO graph shape used by the serverless proof

The repository's debug record is:

- `specs/001-rp-prompt-redesign/debug/032-qwen-image-edit-native-reference-conditioning.md`

### 5. Current AIO checkpoint limitation

The current `Qwen-Rapid-AIO-NSFW-v23.safetensors` is a baked AIO checkpoint used through `CheckpointLoaderSimple` and the merged AIO graph. The attempted custom native multi-reference graph was rejected by the host; it did not return a usable prompt ID.

The split Qwen 2511 model set is the path to test later for native multi-reference identity:

- `qwen_image_edit_2511_fp8mixed.safetensors`
- `qwen_2.5_vl_7b_fp8_scaled.safetensors`
- `qwen_image_vae.safetensors`

The split models are not yet installed/configured for this proof on the current host. Do not claim the AIO checkpoint supports this native multi-reference graph.

### 6. Character LoRAs are a future alternative, not currently implemented

Dedicated Dean and Becky LoRAs trained from their approved multiangle packs could provide identity through model conditioning rather than reference images. This may avoid the current AIO native-reference limitation, but it does **not** automatically guarantee exact location geometry. The location still requires an appropriate text/img2img/ControlNet strategy.

Potential advantages:

- Stronger identity prior than text-only names.
- No dependency on Qwen `image2`/`image3` references.
- Potentially simpler dual-base prompts.

Required future validation:

- Train/evaluate Dean and Becky separately.
- Test the LoRAs on the intended base checkpoint.
- Test identity strength and profile direction independently.
- Test interaction with the existing Qwen edit LoRA; do not assume arbitrary LoRA stacking is safe.
- Run a location A/B proof before adopting the approach.

## Planning recommendation

Do not run the full slideshow until the following focused proof succeeds:

1. Same location source.
2. Base A and Base B generated independently.
3. Correct spatial relationship to a fixed location feature, such as the bed.
4. Dean identity verified.
5. Becky identity verified.
6. Correct profile view for each base.
7. Manifest records exact checkpoint, model files, reference files, prompts, seed, and graph.

The next technically grounded experiment is to provision the **split Qwen 2511 model set** on the local host and replay the exact working split-loader graph with location source + Dean/Becky `image2`/`image3` references. If that cannot fit the available GPU memory, evaluate SDXL ControlNet with actual installed Canny/Depth model files. Do not infer support merely from a checkpoint filename.

## Important correction to earlier notes

The earlier claim that “Qwen AIO supports native multi-reference” was too broad. The accurate statement is:

> The Qwen Image Edit family has a native multi-reference mechanism, and the repository has a verified split-loader graph. The current AIO NSFW checkpoint/graph has not been proven to accept the same multi-reference graph.
