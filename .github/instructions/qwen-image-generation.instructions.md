---
description: "Qwen Image 2512 deterministic generation prompting and exact request settings. Read before implementing or changing Qwen generation compilers, profiles, or qualification proofs."
applyTo: DreamGenClone.Application/RolePlay/**/*ProductionMedia*.cs,DreamGenClone.Web/Application/RolePlay/**/*ProductionMedia*.cs,DreamGenClone.Tests/RolePlay/**/*ProductionMedia*.cs,specs/Planning/B-032-scene-image-generator/**
---

# Qwen Image 2512 Generation Rules

**Research refresh:** 2026-09-02. The official Qwen-Image repository is the primary source.
Generation is a separate family from Qwen Image Edit 2511.

## Family Contract

- Exact model identity: `Qwen/Qwen-Image-2512`; pipeline: `QwenImagePipeline`.
- Use descriptive natural-language visual instructions with explicit subject appearance, clothing,
  action/pose, environment, lighting, framing, and photographic/style treatment.
- Compile only validated structured production facts. Do not call Qwen-Plus prompt enhancement,
  DeepSeek, or another prompt-polishing service at runtime.
- Qwen generation supports a negative prompt. Its exact text is persisted in profile settings; the
  compiler must not invent or fall back to one.
- Seed, dimensions, steps, and true CFG are explicit and snapshotted.

## Exact Official Envelope

The official Qwen-Image-2512 example uses 50 inference steps and `true_cfg_scale = 4.0`.
The only accepted official aspect-ratio dimensions for the initial production compiler are:

- `1:1` = 1328 x 1328
- `16:9` = 1664 x 928
- `9:16` = 928 x 1664
- `4:3` = 1472 x 1104
- `3:4` = 1104 x 1472
- `3:2` = 1584 x 1056
- `2:3` = 1056 x 1584

A capability profile may expose only exact locally qualified tuples. Missing/unsupported dimensions,
steps, CFG, negative prompt, or model version fail before a request is persisted.

## Separation From Edit 2511

- Edit model identity: `Qwen/Qwen-Image-Edit-2511`; pipeline: `QwenImageEditPlusPipeline`.
- Official edit example: ordered image list, one output, seed 0, `true_cfg_scale = 4.0`, blank negative,
  40 inference steps, and `guidance_scale = 1.0`.
- The edit instruction names each ordered image role and states both requested changes and preserved
  properties. Input ordering is immutable request data.
- Generation cannot silently replace edit, and edit cannot silently replace generation.
- Official consistency claims are candidates only. Multi-person identity-after-composition remains
  blocked until the exact local qualification cell passes.
