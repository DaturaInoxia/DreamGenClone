# Baseline — Model-Agnostic Position/Act Prompt Catalog

The **generic prompt baseline** for sexual position/act image generation, derived from the
Juggernaut NSFW test suite and genericized so it can be reused across **other image models** (Pony,
Qwen, future model families).

This is the "starting prompts" list: each entry is the neutral, model-agnostic scene, plus
per-model variants that each model family actually consumes.

## How this is meant to be used

1. Point the Playground at this directory (`Playground:ManifestRoot`, default
   `specs/image-generator-tests`).
2. Import it — the catalog becomes an **Image Suite** (`ImageSuiteKind.Catalog`), one cell per
   position, `VariantsJson` holding one prompt per model key.
3. Pick a model (e.g. `biglust`), tick **one**, **many**, or **all** cells, and Run.
4. Every render lands in the Asset Manager as a **run container**, so you can look at the images and
   judge them yourself.
5. Repeat with a different model key, or with a character LoRA applied, and compare by eye.

The catalog exists to answer "what can this model actually do, on the things I care about" — not to
produce a pass/fail system verdict. Verdicts about *consistency across models* live in the
evaluators, and they are only ever spoken about evidence that was actually collected.

## Position file format

Each position file under `positions/` is:

```json
{
  "id": "missionary",
  "title": "missionary",
  "actors": "1M1F",
  "closeup": false,
  "userInput": "Put them in missionary on the bed",
  "expected": "A photorealistic explicit scene of two nude adults on a bed, ...",
  "neutralScene": "Two naked adults on a bed in missionary position ...",
  "variants": {
    "biglust": "...",              // natural-language photoreal prose
    "juggernaut": "...",           // natural-language photoreal prose
    "pony": "score_9, ... rating_explicit, 1girl and 1boy, ...",
    "qwen-image-2.1": "...",       // long descriptive prose
    "flux": "...",                 // ordered subject -> action -> style -> context
    "qwen-edit-2511": "...",       // instruction: what changes AND what is preserved
    "qwen-image-2.1-edit": "...",
    "krea2": "..."                 // photographic BRIEF: format + subject + action + lens + DoF
  },
  "settings": { "seed": 73190, "steps": 30, "cfg": 5.0, "sampler": "dpmpp_2m_sde", ... }
}
```

**There is no `negative` field.** The catalog authors specify none, and the render path sources
negatives from the checkpoint profile, so a negative stored here would be dead data. A short guard
negative for Pony lives on the Pony checkpoint profile.

**There is no `source` field either, and no other undocumented property.**
`PromptSuiteManifestValidation` reports an unknown property as a problem on purpose - a field named
slightly differently would otherwise be dropped silently and the cell would run with an empty prompt -
and its accepted set is exactly `id`, `title`, `actors`, `closeup`, `userInput`, `expected`,
`neutralScene`, `negative`, `variants`, `settings`, `bindings`.

> **Pre-existing noise:** the 32 positions authored before 2026-09-30 still carry a `source` block
> ("refactored from <file>.json"), so an import of this catalog reports those 32 as unknown-property
> problems. They are recorded provenance and harmless - the cells run - but new content must not copy
> the pattern. The 13 single-female cells added on 2026-09-30 carry no `source`.

## Actor legend

| Code | Meaning |
|---|---|
| `1M1F` | 1 man + 1 woman (2-person — testable with the 2-char identity pack) |
| `2F1M` | 2 women + 1 man (MFF threesome) |
| `1F2M` | 1 woman + 2 men (MMF threesome) |
| `2F2M` | 2 women + 2 men (orgy) |
| `2M1F` | 2 men + 1 woman (double facial) |

## Model legend

Defined once in `manifest.json` under `models`, each with a `dialect` and the rule its prompt must
follow. `dialectNotes` records the cross-cutting rules (multi-person separation, no negatives).

| Key | Dialect | Prompt rule |
|---|---|---|
| `biglust` | natural-language | 2–4 sentences of photoreal prose, each person stated explicitly, concrete anatomy |
| `juggernaut` | natural-language | same as BigLust; each person a self-contained clause |
| `pony` | danbooru-tags | V6 quality string first, then `rating_explicit`, then a count tag, then short tags |
| `qwen-image-2.1` | descriptive-prose | long prose: appearance, clothing, action, environment, lighting, framing |
| `flux` | ordered-fields | subject → action → critical style → context → secondary detail; no negative field |
| `qwen-edit-2511` | edit-instruction | names what changes **and** what is preserved; instructs the action, not the result |
| `qwen-image-2.1-edit` | edit-instruction | as above |
| `krea2` | natural-language brief | **photographic brief**: format + subject + the subject's **action** + lens + depth of field. Never a body-part noun list, never a framing demand, and for acts never a face-facing clause. No negative exists for this family. |

**Multi-person separation** is mandatory for the prose dialects: `2F1M` → Pony `2girls and 1boy,
3people`, prose says "three separate nude bodies" and states each person as a self-contained clause.
Without this, three-person prompts merge bodies.

> **Known consolidation:** `qwen-edit-2511` and `qwen-image-2.1-edit` currently resolve to the same
> validated editor runtime (`Qwen-Rapid-AIO-NSFW-v23` + `QwenEdit2511` LoRA). They are kept as
> separate keys so they can diverge when a distinct Qwen-Image-2.1 edit runtime is registered.

## Files

- `positions/*.json` — 49 position entries (36 two-person incl. the four added 2026-10-01 + 13 single-female)
- `manifest.json` — the model legend, dialect notes, and the position index

> **Note:** `positions/` also still holds the 32 pre-refactor `juggernaut-nsfw-*-test.json` provenance
> files. They share ids with the refactored files and are **not** part of the catalog —
> `ImageSuiteImporter` enumerates `manifest.Positions`, not the directory — so leave them alone.

## Reading the manifest

`PromptSuiteManifestValidation.ParseManifest` is deliberately **permissive**: only invalid JSON and a
position with no `id` are fatal. Everything else (unknown model key, missing variant, out-of-budget
prompt) is collected into `Problems` and reported, because a catalog you cannot import is a catalog
you cannot run.

## Writing new content

`build_baseline.py` is the **pre-refactor** generator: it emits the old `juggernaut-*` ids, an
`sdxl-juggernaut` variant key, and a `negative` field, none of which exist here any more. It is kept
only as provenance for the original prompts. **Do not run it to regenerate this catalog** — it would
overwrite the refactored files with the old shape. New positions are authored directly as
`positions/<model-agnostic-id>.json` plus a `manifest.json` entry.

## Consuming suites
- `identity-two-character/positions/` — 2-person (1M1F) subset adapted for the Dean+Becky identity pack.
