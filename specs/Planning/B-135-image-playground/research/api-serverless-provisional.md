# API / serverless checkpoints — PROVISIONAL (B135-037)

These six API checkpoints have **no published prompting guide captured yet**, so every value on
their `ImageCompilerProfile` row is **PROVISIONAL** until B135-037 research lands. The shape below
follows the API natural-language family row (`ApiSceneImagePromptCompiler`); nothing here is
author-documented.

| Checkpoint | Note |
|---|---|
| `ByteDance-Seed/Seedream-4.0` | No published prompting guide captured yet. |
| `black-forest-labs/FLUX.1.1-pro` | FLUX family natural language; no negatives (BFL). |
| `google/flash-image-3.1` | No published prompting guide captured yet. |
| `Qwen/Qwen-Image-2.0-Pro` | Qwen image family reads long descriptive prompts (operator statement 2026-09-29). |
| `google/imagen-4.0-preview` | No published prompting guide captured yet. |
| `openai/gpt-image-2` | No checkpoint-prompt dialect, no deterministic negative (repo compiler). |

## What "provisional" means here
- The `SystemPrompt`/budget/shape for these rows is inherited from the API family defaults, **not**
  from an official guide, and must not be mistaken for researched guidance.
- To close any of these out, fetch the model's official prompt guide + settings envelope (§4 of the
  governance doc), record the source URL + fetch date here, and move the row's values off the
  family defaults onto the cited values — or leave them labelled provisional deliberately.
