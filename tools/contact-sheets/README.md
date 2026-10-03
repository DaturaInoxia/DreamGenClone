# tools/contact-sheets — labeled comparison sheets for render programmes

## What it is

Builds **labeled contact sheets** from a JSON spec: each tile is a render at a fixed size, each tile is
captioned with its cell + arm, so a verdict can be pinned to a pixel region instead of to someone's
description of a downscaled preview.

It exists because of a concrete failure: in the Qwen-Image-2.1 explicit-anatomy programme (2026-10-02) three
verdicts were wrong when called from downscaled chat previews and had to be retracted. Anatomy/detail verdicts
must be called from a sheet at native resolution. The evidence package that rule protects lives at
`specs/image-generator-tests/qwen-21-explicit-anatomy/`.

## Run

```bash
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/contact-sheets/build_contact_sheet.py \
    --spec specs/image-generator-tests/qwen-21-explicit-anatomy/contact-sheets.json \
    --out-dir artifacts/tmp/qwen21-nsfw-lora/sheets            # PNG, for reading
```

For images meant to be committed (smaller), use JPEG:

```bash
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/contact-sheets/build_contact_sheet.py \
    --spec specs/image-generator-tests/qwen-21-explicit-anatomy/contact-sheets.json \
    --out-dir specs/image-generator-tests/qwen-21-explicit-anatomy/images --format jpeg --quality 88
```

Options: `--tile` (default 700 px), `--label-height`, `--format png|jpeg`, `--quality`.

## Spec format

```json
{
  "sheets": [
    {
      "name": "sheet-1-example",
      "title": "TITLE DRAWN AT THE TOP",
      "rows": [
        [ {"path": "a.png", "label": "arm A"}, {"path": "b.png", "label": "arm B"} ],
        [ ["c.png", "arm C"], ["d.png", "arm D"] ]
      ]
    }
  ]
}
```

- Rows are laid out left to right; entries are `[path, label]` or `{"path":…, "label":…}`.
- An empty entry (`["", ""]`) leaves a blank slot.
- A **missing file renders as a red placeholder** naming the path rather than aborting the sheet — so a
  partially re-run programme still produces a usable sheet.
- A bare object (no `sheets` key) is treated as a single sheet.

## Interpretation rules

- Put the arms of ONE comparison on one row, and prefer a left/right pair where the only difference is the
  variable under test (LoRA on/off, negative prompt on/off, envelope A/B).
- Captions must carry the arm, not a verdict — the sheet is the evidence, the CASE file holds the verdict.
- Review at native resolution / full zoom on the tile. A 700 px tile viewed scaled down is what caused the
  three retractions.

## Outputs

Write generated sheets to a **git-ignored** path (`artifacts/tmp/**`) for working review. Commit sheets into
an evidence package only when they are the artifact a spec references (`--format jpeg --quality 88` keeps
each sheet well under 1 MB).
