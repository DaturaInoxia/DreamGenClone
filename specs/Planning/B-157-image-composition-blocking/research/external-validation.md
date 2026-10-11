# B-157 — Research: external validation of the multi-reference prompt format

Independent, source-verified research on how **Qwen-Image-2.1** (the model behind the ComfyUI
`TextEncodeQwenImage21` node) binds multiple input images. Conducted 2026-10-09. No internal notes were
treated as authoritative; every claim below is quoted from an external source.

## Sources

| Source | URL |
|---|---|
| Official repo (dedicated) | `https://github.com/QwenLM/Qwen-Image-2.1` |
| Model card (HF) | `https://huggingface.co/Qwen/Qwen-Image-2.1` |
| Prompt rewriter system prompts | `QwenLM/Qwen-Image-2.1:prompt_rewrite/prompts/` |
| Prompt rewriter example data | `QwenLM/Qwen-Image-2.1:prompt_rewrite/data/edit_example.jsonl` |
| ComfyUI workflow template | `Comfy-Org/workflow_templates:templates/image_qwen_image_2_1_image_edit.json` |
| ComfyUI node source | `Comfy-Org/ComfyUI:comfy_extras/nodes_qwen.py` |
| Diffusers pipeline | `huggingface/diffusers:src/diffusers/pipelines/qwenimage21/pipeline_qwenimage21.py` |

Note: the older `QwenLM/Qwen-Image` repo has **no** `prompt_rewrite/` directory; the
`system_prompt_edit.txt` referenced by our code lives in `QwenLM/Qwen-Image-2.1`.

## Verified facts

### 1. Tags: mandatory for N≥2, forbidden for N=1 — CONFIRMED

`prompt_rewrite/prompts/system_prompt_edit.txt`:

> "For Multi-Image Input (N >= 2), the rewritten instruction MUST use `<image1>`, `<image2>`, ... to
> refer to each input image. Do not use natural language references like 'the first image' or 'image A'.
> This tagging format is mandatory and non-negotiable. For single-image input (N = 1), do NOT use tags —
> refer to the image naturally."

The ComfyUI template repeats it ("Mention them in the prompt as `<image1>`, `<image2>`, …").
The Diffusers pipeline auto-injects the vision placeholders, but the vendor's authored-prompt rule is to
write the tags explicitly.

### 2. Role = tag + "the person in imageX"; names are REFUTED — CONFIRMED

`system_prompt_edit.txt`:

> "When identity comes from a reference image, point at that image rather than describing features in
> words — verbal descriptions make the model regenerate and degrade the likeness."
> "State each image's role explicitly — which one is the canvas … and which supply material to transfer …
> For scene generation with no canvas (合影/合照 and the like), all images serve as identity sources.
> Describe every referenced image individually; never compress several into a range."

Character names are **not** the prescribed mechanism; the handle is `the person in <imageX>`.

### 3. Maximum references = 10 — CONFIRMED (16 is only the node's slot ceiling)

- Model card / README: "Support up to **10 reference images** …"
- ComfyUI template note: "Up to 10 reference images (image_1 to image_10)."
- `nodes_qwen.py`: `names=[f"image_{i}" for i in range(1, 17)]` — 16 autogrow *slots*, not a supported
  count. "32" appears nowhere.

### 4. Multi-person technique = tags + "person in imageX" + positional + keep-unchanged — CONFIRMED

Official `edit_example.jsonl` multi-portrait sample (verbatim):

> "将`<image1>`中的人物和`<image2>`中的人物置入一个现代抖音直播间的场景中，生成一张两人并排坐在直播桌后共同面向镜头介绍产品的合影照片。保持两位人物的面部特征、发型和服装外观完全不变。"

Translation: *"Place the person in `<image1>` and the person in `<image2>` into a modern live-stream room
scene; generate a group photo of the two sitting side-by-side behind the table, both facing camera. Keep
both persons' facial features, hairstyle, and clothing completely unchanged."*

The pattern is: `<imageX>` tag → "the person in `<imageX>`" → positional composition ("side-by-side,
both facing camera") → identity preserved by pointing at the image, never by describing it.

### 5. No `system_prompt_generate.txt` — the T2I prompt is `system_prompt_t2i.txt` and has no multi-image rules

The text-to-image rewriter (`system_prompt_t2i.txt`) describes only the finished image and the aspect
ratio; it contains no `<imageX>` tagging or reference rules. The multi-image rules live in the edit
prompt, which explicitly covers the no-canvas generation case ("all images serve as identity sources").
The HF model card showcases "Group photograph generated from six portrait references" as evidence that
multi-person generation works.

## Assumption table

| Assumption | Verdict |
|---|---|
| N≥2 requires `<imageN>` tags | CONFIRMED |
| N=1 forbids tags | CONFIRMED |
| Role via character names | REFUTED |
| Role via appearance re-description | REFUTED |
| Role via "the person in `<imageX>`" + positional | CONFIRMED (in substance) |
| Max 10 references | CONFIRMED |
| 16 = supported count | REFUTED (node slot ceiling only) |

## Design implication

The canonical role clause for our generation path (no canvas) is:

> "`<image1>` is the person on the LEFT; `<image2>` supplies their build; `<image4>` supplies their
> clothing; `<image3>` is the person on the RIGHT; `<image5>` supplies the background scene. Keep both
> persons' faces, builds and clothing unchanged."

Tags only, subject-by-position, no names, no appearance re-description, ≤10 images.
