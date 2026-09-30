# Qwen-Image-2.1 Prompt Enhancer (official) — verified findings + integration

**Source:** `QwenLM/Qwen-Image-2.1` @ commit `7307809` ("Add system prompt files for t2i and edit PE"), path `prompt_rewrite/`.
**Read:** 2026-09-29, via GitHub file listing + README + the two prompt files.
**Why this matters:** B135-008 needs a Qwen long-form compiler grounded in *externally sourced* material (compiler-standards governance rules 4 and 9). This **is** that material, and it is the vendor's own reference implementation.

---

## 1. What ships

| Artifact | What it is |
|---|---|
| `prompts/system_prompt_t2i.txt` | System prompt for **text-to-image prompt expansion** (text in, no image) |
| `prompts/system_prompt_edit.txt` | System prompt for **image-editing instruction rewrite** (text + 1..N images in) |
| `pe_core.py` | Task profiles (sampling), message construction, answer parsing, output records |
| `run_vllm.py` / `run_transformers.py` / `client.py` | vLLM batch / local transformers / OpenAI-compatible client |
| `Qwen/Qwen-Image-2.1-PE-T2I`, `Qwen/Qwen-Image-2.1-PE-I2I` | The two fine-tuned **Qwen3.5-VL 9B** checkpoints (HuggingFace) |

**The two prompts are NOT interchangeable, and there is no merged prompt.** The vendor states the answer contract is part of what each model was trained on, so the prompt must travel with the weights (they even look for `system_prompt.txt` inside the checkpoint dir, precisely so "swapping checkpoints and forgetting to swap the prompt" cannot happen silently).

**CRITICAL CAVEAT (vendor-stated):** pointing these prompts at the **stock** open-source Qwen3.5-VL 9B "will load and generate, but it was never trained against either system prompt, so it does not reliably emit the answer JSON — expect `parse_ok: false` on most rows." So this is **not** a drop-in system-prompt swap for our existing Qwen VL compiler; it requires the PE weights (or accepting that the contract must be repaired by hand).

## 2. Contract (what the model returns)

```
t2i   {"rewritten_prompt": "<long English description>", "wh_ratio": "16:9"}
edit  {"rewritten_prompt": "<rewritten instruction>", "wh_ratio": "", "ratio_follow": "<image1>"}
```

- `wh_ratio` and `ratio_follow` are **mutually exclusive** — exactly one carries a value, the other is `""`.
- `rewritten_prompt` must be **one continuous paragraph, no newlines**.
- **No resolution or aspect ratio may appear in `rewritten_prompt`** — those travel only in `wh_ratio` / `ratio_follow`.
- Thinking is **on and required**; the harness splits it out and parses only the answer.
- The README's record shape also carries **`negative_prompt` — always `""`**. Worth noting: the vendor's own enhancer emits an empty negative. Independent corroboration for B-135 D10.

## 3. Sampling defaults (per task, from `pe_core.py`)

| | `t2i` | `edit` |
|---|---|---|
| temperature | 1.0 | 1.0 |
| top_p | 0.95 | 0.95 |
| top_k | 20 | 20 |
| min_p | 0 | 0 |
| **presence_penalty** | **1.5** | **0** |
| max_new_tokens | 16256 | 24000 |
| thinking | on (required) | on (required) |

The vendor is explicit that **`presence_penalty` is the load-bearing value**: the two are not interchangeable, and a wrong penalty "does not fail loudly — it quietly changes the distribution you sample from". That is why there is no global default and the effective value is logged. This maps directly onto our own no-fallback rule: **the penalty must be profile data, per task, never a default.**

## 4. `t2i` prompt shape (the long-form Qwen prompt we lack)

An **eight-step procedure** producing ~20 sentences / 400–500 words, written as *an observer describing the finished picture* (present tense, third person, declarative).

1. Read the brief; split into what the user **fixed** (must survive verbatim: text strings, named objects, counts, colours, positions, ratio) and what is **open** — a short brief means inventing most of the frame, not writing less.
2. Fix the frame: `3:2` horizontal / `2:3` vertical defaults; square, cinematic, phone, banner variants; **ratio lives only in `wh_ratio`**.
3. Opening sentence (~20 words): medium + style + subject + background/palette; the medium noun is never omitted.
4. Inventory: 8–14 positional phrases reaching corners/edges/centre; every legible text string in reading order.
5. Walk the frame — regions (background first, then top band, then left→centre→right, then bottom band) or the subject (background, pose/placement, head/face, body/garments, held objects, edges). ~A third of sentences open on the positional phrase. One paragraph unless genuinely region-stacked.
6. Every piece of text: where it sits, what it looks like, what it says — **exact characters, in its own script**, quotes for rendered text only.
7. Lighting gets its own sentence: source, direction, quality, resulting shadows/highlights.
8. Close with exactly one whole-frame sentence (composition, palette, style, mood).

**Throughout:** ~20 sentences is the size whatever the brief length; **no quality boosters** (explicitly: no "masterpiece", "8K", "highly detailed", "award-winning"); hedge what is uncertain ("appears to be", "likely", offer a pair); colours named with a modifier; give the material not just the noun; enumerate, never summarise; **people get observable surface only — age as a life stage or decade, NEVER a number of years**; objects by class not brand; physical consistency; **description always in English** (only in-image text keeps its own script).

## 5. `edit` prompt shape

- **Governing principle — attribute disentanglement at full strength:** edit exactly the attributes named, push each to an unmistakable degree, hold everything else at input fidelity. Both failure modes are named (leakage / under-editing), and *preservation locks content, never edit strength*.
- **Anchor on the image.** Nothing invented; an uncertain detail is omitted rather than specified.
- **Say what stays, without repainting it** — a concrete description of something you meant to keep reads as a generation instruction and drifts.
- **Identity is the hardest invariant**; when identity comes from a reference image, **point at the image rather than describing features in words** (verbal descriptions make the model regenerate and degrade the likeness). ← directly relevant to our identity/reference work.
- **Resolve ambiguity, then commit**; keep the user's own verb and spatial relations; preserve impossible intent rather than correcting it.
- **Only what was asked** — do not clean up unmentioned defects.
- **Write it as an instruction**, leading with the operation.
- **Two separate language decisions** — (A) the descriptive prose, (B) the text rendered *into* the image; (B) has a strict 3-step priority (explicit text/language → the image's dominant language if it has text → the instruction's language). Rendered text must be monolingual.
- **Image reference rules:** for N ≥ 2, `<image1>`, `<image2>` tagging is **mandatory and non-negotiable** (no "the first image"); for N = 1, do **not** tag. Each image's role must be stated; images are sent **first and in order**.
- **Preservation stated affirmatively** ("keep X unchanged") rather than as prohibitions ("do not change X").
- **Output size:** a full decision tree — explicit size/ratio; single-image follow (`ratio_follow` = `<image1>`); the single-image *scene generation* exception; the multi-image **canvas table** (compositing → target scene; face swap → body image; clothing swap → person image; style transfer → content image; background replacement → foreground subject; local object → original); no-canvas scene generation by semantics; **outpainting** (30–50 % extra space, direction-dependent ratio); panorama (2:1 / 3:1); multi-grid **adaptive** ratio (never a fixed default).

## 6. Integration plan for B-135

**D135-PE1 — Vendor the prompts byte-exactly, at a pinned commit.** Do NOT hand-copy. Add a git-tracked download script (`helpers/…`) that fetches `system_prompt_t2i.txt` + `system_prompt_edit.txt` from **commit `7307809`** and records a **sha256 per file**. A reflowed or re-typed prompt is a paraphrase and silently changes behaviour. *(This document deliberately does not contain the prompt text: the fetch that produced it was line-reflowed, so copying it would not have been byte-faithful.)*

**D135-PE2 — Two task profiles, never one.** `ImageCompilerProfile` gains the PE task dimension and the sampling data from §3, including the **per-task `presence_penalty`** (1.5 vs 0) as a configured value with no default.

**D135-PE3 — An answer-contract parser, fail-fast.** `QwenPromptEnhancerAnswer` with `RewritePrompt` / `WhRatio` / `RatioFollow`, enforcing: exactly one of `wh_ratio`/`ratio_follow` set, `rewritten_prompt` non-empty, **no ratio or resolution string inside the prompt**, no newlines, and refusal when the thinking block is absent (thinking is required, so its absence means the wrong server config).

**D135-PE4 — `wh_ratio`/`ratio_follow` must drive our canvas.** Today our step carries an `ImageSize` like `1024x1024`. The PE decides shape semantically; so a PE-backed cell must map `wh_ratio` to a size inside the checkpoint's qualified dimension set, and `ratio_follow` to "inherit the bound reference's canvas". An unresolvable pair (a ratio outside the qualified set) is **refused with the ratio named**, never silently clamped.

**D135-PE5 — The `<imageN>` contract must match our reference ordering.** Our render already sends ordered references with semantic roles; the PE's `<image1>`… must be the **same order**, or every reference in the rewrite silently re-points (the vendor calls this out explicitly). So the PE call and the render must share one ordered reference list, and the canvas image must be identifiable.

**Feasibility / licensing gates to settle before building:**
1. **Weights required.** Stock Qwen3.5-VL does not reliably emit the contract (vendor-stated). Do we register the PE checkpoints? PE is **Qwen3.5-VL 9B** — a VRAM and hosting question for both the local 5080 and RunPod.
2. **Licence.** Determine the PE checkpoints' licence before use.
3. **What it replaces.** Our current Qwen-2.1 path injects the **SDXL-branded** `SdxlSceneImagePromptBuilder`. The PE t2i prompt is the correct long-form replacement — and it **conflicts** with our current SDXL-shaped output in specific ways we must not paper over: PE forbids quality boosters and style-cue tails that the SDXL builder writes ("35mm, natural skin texture"), and PE forbids the ratio appearing in the prompt at all.

## 7. Tasks to add

| Task | Contents |
|---|---|
| **B135-039** | Vendor both prompts at commit `7307809` via a scripted, hash-recorded fetch + a test asserting the hashes |
| **B135-040** | PE task profiles (sampling incl. per-task `presence_penalty`) as `ImageCompilerProfile` data |
| **B135-041** | `QwenPromptEnhancerAnswer` parser with the full refusal set (mutual exclusivity, no ratio in prompt, thinking required) |
| **B135-042** | `wh_ratio` / `ratio_follow` → canvas resolution, mapping into the checkpoint's qualified dimension set, refusing an unmappable ratio |
| **B135-043** | PE call wired to the **same ordered reference list** the render uses, with the canvas image identified |
| **B135-044** | Replace the SDXL-branded builder on the Qwen-2.1 path with the PE t2i contract, and record the deliberately-dropped SDXL style-tail/quality-booster behaviour as an expected, tested difference |

---

## 8. Decision (2026-09-30) — route 1 now, route 2 parked pending analysis

Two routes exist for grounding the Qwen-2.1 compiler, and they are not interchangeable.

### Route 1 — ADOPT THE VENDOR'S *RULES* INTO OUR OWN QWEN BUILDER PROSE ← **the plan of record, now**

We write the Qwen long-form builder's system prompt ourselves, following the vendor's documented **shape and
prohibitions** (§4 t2i, §5 edit) and citing this document as the external source (compiler-standards governance
rules 4 and 9).

Adopted: the eight-step observer-description procedure; ~20 sentences / 400–500 words **whatever the brief length**;
no quality boosters (`masterpiece`, `8K`, `highly detailed`, `award-winning`); age as a life stage or decade and
**never** a number of years; colours named with a modifier; enumerate-never-summarise; the ratio lives only in
`wh_ratio` and never in the prose. On the edit side: attribute disentanglement at full strength, anchor-on-the-image,
preservation stated affirmatively rather than as prohibitions, and identity **points at the reference image** instead
of describing features in words.

Why this route first: it needs **no new weights**, it closes the defect the code itself documents (*"the shared
natural-language system prompt is SDXL-branded"*), and it is testable — a Qwen compile must never emit Pony tags or
an SDXL-shaped brief, and the SDXL style tail (e.g. "35mm, natural skin texture") and quality boosters must never
appear. The vendor's **contract** is what the PE weights were trained to emit; the **guidance** is ours to write.

### Route 2 — USE THE VENDOR'S PROMPTS BYTE-EXACTLY ← **PARKED: needs its own analysis before any task starts**

This is what §6 gate 1 and the §1 caveat are about, restated here with its gates so the dependency cannot get lost:

> **Use the vendor's prompts byte-exactly.** That requires the **PE checkpoints** (`Qwen-Image-2.1-PE-T2I` /
> `-PE-I2I`, fine-tuned Qwen3.5-VL 9B). The vendor is explicit that the stock model "will load and generate" but
> wasn't trained against either prompt, so expect `parse_ok: false` on most rows — and this document flags exactly
> this as the feasibility gate before the PE tasks.

Analysis this needs before it is buildable (none of these are answered yet):

1. **Do we have, or can we register, the PE weights?** `Qwen-Image-2.1-PE-T2I` / `-PE-I2I` are Qwen3.5-VL **9B** —
   a VRAM and hosting question for the local 5080 and for RunPod, plus a Model Manager registration question.
2. **Licence** of those checkpoints.
3. **Is the stock-model + repaired-contract path viable at all?** If `parse_ok: false` dominates, the contract must be
   repaired by hand — an unbounded repair burden, not a compile step. Quantify on a small sample before deciding.
4. **Sampling is load-bearing and per task:** `presence_penalty` **1.5** (t2i) vs **0** (edit), thinking on and
   required, `max_new_tokens` 16256 / 24000. Per our no-fallback rule these must be profile data, never defaults.
5. **Byte-exactness is a build requirement, not a nicety** — pinned commit `7307809` plus a sha256 per file from the
   fetch script. A reflowed copy is a paraphrase and silently changes behaviour (which is also why this document does
   not carry the prompt text).
6. **Relationship to route 1.** B135-008 must therefore put the Qwen long-form text behind the profile's
   `SystemPrompt` — one replaceable source — so route 2 can later swap the *text* without rewriting the builder.
   Route 1 is a step toward route 2, not a dead end; the seam is the deliverable.

### Task status under this decision

- **B135-039** (vendor both prompts + record hashes + asset test) — DONE.
- **B135-040 … B135-044** — **BLOCKED** on the route-2 analysis above; do not start them by swapping the prompt text
  onto a model that was not trained against it.
- **B135-008** — proceeds under **route 1**, and is the next piece of work.
