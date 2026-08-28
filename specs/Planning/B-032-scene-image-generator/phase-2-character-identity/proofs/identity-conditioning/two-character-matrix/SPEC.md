# Two-Character Identity Matrix — Spec (2026-08-26)

## Goal

Prove that IP-Adapter can keep **two different characters recognizable in a single render** (Dean +
Becky), using **regional conditioning** so each reference face is confined to its own region. This is
the Phase 2 gate (`P2-014`..`P2-016`): if a mechanism passes, it becomes the selected two-actor
approach; if not, record the closest failure and decide LoRA (`P2-030`).

## Mechanism under test

**IP-Adapter + regional conditioning** (nodes verified present on proof pod `7i2mutjmry5tkt`):
- `IPAdapterUnifiedLoader` / `IPAdapterFaceID` for the two reference passes
- `IPAdapterRegionalConditioning` per character (reference image + region mask)
- `RegionalPrompt` + `CombineRegionalPrompts` (regional prompt merge)
- Pinned sampler: 30 steps / cfg 5.0 / dpmpp_2m_sde / karras (matches single-actor proof)

PuLID is single-identity and is **not** a two-actor candidate (noted in `research.md`).

## Frozen packs (canonical faces)

| Character | Scenario | Pack Id | Canonical face | Notes |
|---|---|---|---|---|
| Dean | Campground Intimacy | `3341c088-...` | `identity/faee1ec0-.../c0732107...png` (1000x1332) | portrait |
| Becky | Campground Intimacy | `8a7dc2ae-...` | `identity/f58f959a-.../08f9cee6...jpg` (2576x1932) | landscape |

## Composition cells (6) × seeds (2) = 12 cases

| Cell | Region masks (left/right) | Prompt (both named) |
|---|---|---|
| C1 side-by-side | left/right halves | "Dean on the left, Becky on the right, standing side by side, full body" |
| C2 facing | two vertical bands | "Dean and Becky facing each other, close, full body" |
| C3 embrace | overlapping bands | "Dean and Becky embracing, Dean on the left, Becky on the right" |
| C4 seated/standing | upper/lower bands | "Dean standing behind, Becky seated in front, full body" |
| C5 one behind | depth bands | "Dean behind, Becky in front, two-shot" |
| C6 two-shot from side | horizontal split | "Dean and Becky side by side in profile, two-shot" |

Seeds: fixed per case (e.g., 1001, 1002) so results are reproducible. Each mask is a
black/white PNG at 1024x1024 (white = region the reference conditions).

## Scorecard

Per case, score 1–5 on:
- **Identity A** (Dean looks like Dean)
- **Identity B** (Becky looks like Becky)
- **Cross-contamination** (Becky's face on Dean, or vice-versa — lower is better)
- **Composition** (matches the cell arrangement)
- **Quality** (artifacts, anatomy, general render quality)

Gate: a mechanism passes if median Identity ≥ 4 for both actors across cases, cross-contamination
median ≤ 2, and no case below Identity 3. Otherwise record the closest failed constraints.

## Artifacts

- `workflows/two-character-ipadapter-c1.json` (representative regional workflow)
- `masks/` — generated region masks
- `outputs/` — the 12 rendered cases
- `scorecard-2026-08-26.md` — per-case scores + gate decision

## Status

- [x] Two approved packs frozen (Dean + Becky, same scenario)
- [x] Regional workflow validated on pod (1 cell)
- [x] 12 cases rendered
- [x] Scorecard completed + mechanism decision (→ `scorecard-2026-08-26.md`; strict gate FAIL, guarded viable)
