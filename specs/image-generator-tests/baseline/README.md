# Baseline — Model-Agnostic Position/Act Prompt Catalog

The **generic prompt baseline** for sexual position/act image generation, derived from the
Juggernaut NSFW test suite and genericized so it can be reused across **other image models** (Pony,
Qwen, future model families).

This is the "starting prompts" list: each entry is the neutral, model-agnostic scene, plus
per-model variants that different image models actually consume. Model-specific test suites
consume this catalog (e.g. `identity-two-character/positions/` adapts the 2-person subset for the
Dean+Becky identity pack).

## Format

Each position file under `positions/` is:

```json
{
  "id": "juggernaut-nsfw-missionary-test",
  "title": "missionary",
  "actors": "1M1F",
  "closeup": false,
  "neutralScene": "Two naked adults on a bed in missionary position ...",   // model-agnostic
  "negative": "deformed, bad anatomy, ...",                                  // shared guard set
  "variants": {
    "sdxl-juggernaut": "Photorealistic explicit sex scene, ...",             // natural language
    "pony": "score_9, score_8_up, ..., rating_explicit, 1girl and 1boy, ...",// tag form
    "qwen-edit": "Reposition the couple into missionary position ..."        // edit instruction
  },
  "settings": { "seed": 73190, "steps": 30, "cfg": 5.0, "sampler": "dpmpp_2m_sde", ... },
  "source": { "suite": "juggernaut", "workflow": "juggernaut/prompts/juggernaut-nsfw-missionary-test.json" }
}
```

## Actor legend

| Code | Meaning |
|---|---|
| `1M1F` | 1 man + 1 woman (2-person — testable with the 2-char identity pack) |
| `2F1M` | 2 women + 1 man (MFF threesome) |
| `1F2M` | 1 woman + 2 men (MMF threesome) |
| `2F2M` | 2 women + 2 men (orgy) |
| `2M1F` | 2 men + 1 woman (double facial) |

## Variant notes

- **`sdxl-juggernaut`**: the exact original Juggernaut natural-language prompt (unchanged from the
  frozen `juggernaut/prompts/`). The canonical photorealistic expression.
- **`pony`**: Pony V6 tag form following `.github/instructions/pony-v6-prompting.instructions.md`
  — full quality string first, `rating_*` chosen by content policy, danbooru count tags, short
  tag-like phrasing, camera view tag.
- **`qwen-edit`**: source-image edit instruction form (Qwen is an editor, not text-to-image). Each
  assumes a neutral base image of the same two characters and re-describes the pose.

## Files

- `positions/*.json` — 32 position entries (18 two-person + 14 multi-person)
- `manifest.json` — hashes + actor/closeup metadata

## Regenerate

```powershell
$py = 'd:\src\DreamGenClone\.venv\Scripts\python.exe'
& $py 'd:\src\DreamGenClone\specs\image-generator-tests\build_baseline.py'
```

`build_baseline.py` reads the frozen `juggernaut/manifest.json` for the SDXL/Juggernaut variant
and settings, and authors the neutral/Pony/Qwen variants. **Adding a new position** = add an entry
to `POSITIONS` in `build_baseline.py` and re-run.

## Consuming suites

- `identity-two-character/positions/` — 2-person (1M1F) subset adapted for the Dean+Becky identity pack.
- Future: Pony-specific position suite, Qwen edit suite, etc. can each consume `variants.pony` /
  `variants.qwen-edit` / `neutralScene` directly.
