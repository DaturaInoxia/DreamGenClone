# Identity Two-Character — 2-Person Pack Position Test

A **separate test case** for the two-character identity pack, derived from the position prompts in
the `juggernaut/prompts/` folder (via the model-agnostic baseline at
`../baseline/`). The Juggernaut prompts are the **starting point**; they are adapted here to
support the two-person identity pack (Dean + Becky).

## What this tests

Whether the two-character identity conditioning (regional IP-Adapter `PLUS FACE`) can hold **both
Dean and Becky's identities across explicit sexual positions** — not just the near-frontal
composition cells in the matrix suite. Each workflow:

- Names both characters in the prompt ("Dean and Becky ...").
- Applies per-character regional `attn_mask` (Dean weight 0.8 / Becky weight 0.6).
- Uses the **original Juggernaut position prompt** (from the baseline SDXL variant) so a plain
  Juggernaut run stays comparable.
- Reuses the original seed from the juggernaut test for reproducibility.

## Scope

18 positions included — all **1M1F** positions from the baseline (69, cowgirl, missionary, doggy,
fellatio, reverse cowgirl, spooning, standing, cumshot facial/in-mouth/on-body/creampie, plus
close-up variants).

**Excluded** (cannot be tested with a 2-character pack): 3-person MFF/MMF, 4-person orgy, and the
double-facial cumshot — these need 3+ identity refs and are out of scope for the two-character
mechanism.

## Files

```
positions/
├── index.json            # position -> seed / masks / path
├── prompts/              # 18 generated Dean+Becky workflows
└── images/               # (pending — pod EXITED as of 2026-08-27)
```

## Regenerate

```powershell
$py = 'd:\src\DreamGenClone\.venv\Scripts\python.exe'
& $py 'd:\src\DreamGenClone\specs\image-generator-tests\identity-two-character\dump_positions.py'
```

This re-reads the baseline and rebuilds all 18 workflows. Re-run `dump_positions.py` whenever the
baseline changes.

## Status

- [x] Baseline created (`specs/image-generator-tests/baseline/`) — 32 positions, model-agnostic
- [x] 18 two-person workflows generated (Dean + Becky, regional masks)
- [ ] Images rendered — **pending** (proof pod `7i2mutjmry5tkt` EXITED)
- [ ] Human review + scorecard — pending render

## Caveats

- The per-position regional masks are **starting defaults** chosen to match each geometry
  (top/bottom for man-below positions, left/right for behind positions). They are documented in
  `dump_positions.py` (`POSITION_MASKS`) and must be validated/adjusted on review.
- Cumshot positions often frame the man's face out of frame — the Dean mask may be redundant
  there; Becky's identity is the meaningful check.
