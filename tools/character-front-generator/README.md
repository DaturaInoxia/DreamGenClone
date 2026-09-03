# tools/character-front-generator

Generate a batch of **near-frontal character face candidates** (a "clean front"
for an identity profile pack) via **gpt-image-2 (TogetherAI)**, using a prompt
hardened for **correct (level, even, same-size) eyes**.

Promoted from `artifacts/tmp/dean-new-front/generate_front_v7.py` (2026-09-02)
because the git-tracked `.github/skills/character-profile-pack/SKILL.md` depends
on this generator. Outputs go to git-ignored paths under `artifacts/tmp/`.

## Why it exists
The front image is the conditioning master for a character identity pack. A
JPEG hidden under a `.png` name produces a bad conditioning file (~120 KB vs
~1.3 MB), so this tool re-encodes gpt-image-2's JPEG bytes into **TRUE PNGs**
via Pillow.

## Run
Use the repo venv (requirements: `Pillow==12.3.0`, rest is stdlib):

```powershell
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/character-front-generator/generate_front.py --count 11 --outdir artifacts/tmp/<char>-new-front/v7
```

Dean's proven prompt is the default. For any other character:

```powershell
# template form (name + appearance clause)
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/character-front-generator/generate_front.py `
  --name "Jane Doe" --appearance "long black hair, brown eyes, olive skin" --count 8 --outdir artifacts/tmp/jane-new-front/v7

# or a full custom prompt
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/character-front-generator/generate_front.py `
  --prompt "<full prompt>" --count 4 --outdir artifacts/tmp/jane-new-front/v7
```

Options: `--count` (default 6) · `--outdir` (default `artifacts/tmp/character-front-generator/<ts>/`) ·
`--character dean` (built-in registry) · `--name`/`--appearance` (generic template) · `--prompt` (full override) ·
`--model`/`--api-url`/`--provider-id` (defaults: `openai/gpt-image-2`, TogetherAI, the DB's Together provider row).

## API key
The TogetherAI key is decrypted (DPAPI / CurrentUser) from the Model Manager
`Providers` row in the live dev DB
(`DreamGenClone.Web/data/dreamgenclone.dev.db`) — auto-located by walking up
from this file. Set `TOGETHER_API_KEY` to bypass the DB lookup.

## Interpreting results
- gpt-image-2 has a **systematic left-eye-lower bias** — expect most of a batch
  to fail. Generate more batches until a candidate is ~0 on every axis (Dean
  needed 11; front_7 was −0.01%).
- Validate every candidate with the canonical eye tool
  `tools/eye-validation/measure_iris.py` and **visually verify** the marker sits
  on the iris before trusting the number.
- Full end-to-end flow (front → pack → approve → refs → validate): see
  `.github/skills/character-profile-pack/SKILL.md`.
