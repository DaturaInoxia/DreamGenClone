# CASE-26 — NSFW LoRA arms for Qwen-Image-2.1: which to use (measured 2026-10-02)

**Question:** there are now a dozen community NSFW LoRAs for `Qwen/ Qwen-Image-2.1`. Do we need any, and which?

**Answer: none of them is needed for correct genital anatomy; one (`alpacas`) earns a place for act
engagement; `coachbate` is a keep-for-looks-only; `vulva` is a regression at strength 1.0.**

> **Operator review (2026-10-02):** "going through most of the image, most of them are not good." The per-cell
> PASS grades below are **structural presence checks by the agent**, not quality acceptance. Read the LoRA
> keep/drop recommendations as defect-avoidance only — none of these arms was judged a good image overall.

All arms ran at strength **1.0** (no strength sweep yet) on the `recipe` envelope (30 steps, cfg 3.0,
`er_sde`/`beta`), 1024×1024, same seed within a comparison.

## LoRAs under test

| Key | File | Civitai | Bytes | SHA-256 | Claimed purpose |
|---|---|---|---|---|---|
| `alpacas` | `NSFW Qwen by TheseAlpacas V2.safetensors` | 2958918 / v3357315 | 79,744,352 | `83ABA822AD8EE10A1E836828F5088259D93BCE7B5F3419B46DBC0258C2BE46A9` | general acts + anatomy |
| `coachbate` | `qwen-image-2.1_penis_coachbate_preview1.safetensors` | 2952865 / v3344536 | 159,436,160 | `0F79FAE880BA2B89960676E036DFEE1FEA6020F7ACBF6D1A333DC14B477CDEEB` | male anatomy |
| `vulva` | `qwen21_v2_000002750.safetensors` | 2960871 / v3354330 | 79,744,256 | `71D8EDBFBB693DA3B1ACDFE3A48C1C61942974CB1DFA864E5F0862630A0A3C4C` | female vulva |

Both files are bf16, 384-tensor safetensors, loaded with the built-in `LoraLoaderModelOnly` (no custom node).
For a `coachbate` + `vulva` chain, two loaders are stacked (each takes the previous loader's model output).

## Arm-by-arm evidence

### `core` suite (4 catalog cells × 4 arms) — see `images/sheet-5-lora-arms.jpg`

| Cell | base-envelope | +alpacas-envelope | base-recipe | +alpacas-recipe |
|---|---|---|---|---|
| fellatio | PASS | PASS — shaft in mouth (more engaged) | **PASS (best)** | PASS — glossier |
| missionary | PARTIAL (genital region incoherent) | PARTIAL (limb blob) | **PASS (best)** | PARTIAL (contact less clear) |
| erotic-legs-spread | PASS | PARTIAL (melded limb) | PASS | **PASS (best)** |
| cumshot-facial | PASS | PARTIAL (featureless torso blob) | **PASS (best)** | PARTIAL (odd brows, glossy skin) |

Base arms 7 PASS / 1 PARTIAL; LoRA arms 3 PASS / 5 PARTIAL. The LoRA's genuine win is **act engagement**
(at the same seed it turned a lip-contact fellatio into shaft-in-mouth) — everything else it touched got
glossier, and it introduced limb/torso blobs at 1.0.

### Male anatomy — `coachbate`

- Positive: adds size, upward curve and vein/shaft detail; on the male-state probes it gave well-formed
  organs and natural skin (the least glossy of the recipe arms in `multiperson`).
- Negative: a **recurring notched / cleft glans** (seen in `male-semi-erect` base, `male-erect-large`, and the
  chained `both-recipe` creampie arm), and it **reduced person count** in the orgy cell (3 people instead of 4).
- It cannot unlock a state the base will not render — the flaccid probe still needed the negative prompt
  (CASE-25 §1).

### Female anatomy — `vulva`

Judged on `images/sheet-1-vulva-distance.jpg` (base vs +vulva, same seed, four distances):

| Framing | base | +vulva |
|---|---|---|
| full-length | natural mottled skin + pubic hair, vulva a slit | **shaved, doll-smooth**, vulva a thin line |
| medium hip | natural skin + hair triangle, slit with slight labia | smooth plastic skin, **no pubic hair**, thinner slit |
| close-up | labia + dark opening, pubic hair | smoother/flatter, no hair, less texture |
| macro | **most detailed vulva of the programme** (minora, opening, hair strands, pores) | softer, less defined, less hair |

The LoRA also changes body type (larger breasts, smoother skin) and removes pubic hair **even though the
prompts ask for "natural pubic hair" and "unretouched skin"** — so the LoRA and our prompt style disagree.

**Verdict: regression at 1.0 on the vulva axis. Base ≥ LoRA in all four distance rows.**

### Chained (`coachbate` + `vulva`), `genital` suite

On `missionary-penetration-closeup` the chained arm had the best *male* organ of the three but still no
readable insertion; on `cumshot-creampie` it produced the notched glans plus an inflated look. No cell was
better than base-recipe overall.

## Recommendation

| LoRA | Decision | Rationale |
|---|---|---|
| `alpacas` | **keep, conditional** — 0.8-1.0 only when the base will not perform the act (oral contact, some acts) | the only measured capability delta; costs gloss and limb fidelity at 1.0 |
| `coachbate` | **optional, looks-only** — accept the notched-glans risk; do not use on cells whose count matters | no capability delta, has defects |
| `vulva` | **do not ship at 1.0** — test 0.4-0.5 before any use | regression vs base on the vulva axis |

Nothing here is a substitute for the envelope and framing rules in the RUNBOOK: **the envelope changed the
output more than any LoRA did, and the framing rules fixed a defect no LoRA touched.**

## What the operator's own verdict said (recorded for balance)

The operator's read after the first round was that the LoRAs are "much better than not having them". The
evidence supports that for **explicitness / act engagement** (the `alpacas` arm) and does **not** support it
for **genital fidelity**, where base is better at the same seed. Both can be true: they are different axes.

## Corrections applied to earlier claims (this package supersedes prior chat notes)

| Superseded claim | Correct position |
|---|---|
| "the vulva LoRA is a genuine win for female legibility" | regression at 1.0 — hair stripped, body smoothed, vulva softer |
| "the base macro's opening is an unlit black void" | the base macro is the most detailed vulva in the programme |
| "the LoRA is never the best arm" (round 1) | true for anatomy at 1.0, **false** for act engagement |

## Not measured

- **Strength sweep** (0.4 / 0.5 / 0.7) — the single most important open test; every LoRA defect seen here is
  consistent with over-driving at 1.0.
- The other published 2.1 anatomy LoRAs: `Improve vaginal visibility`, `qwen 2.1 vagina` variants not fetched,
  the PornMaster sliders, the paid `Perfect erect penis`, the separate uncircumcised-penis LoRA.
- LoRA + identity/character LoRA interaction (both are model-only loaders, so they can chain, but the
  combination is untested).
- fp8/GGUF quantisation effects on LoRA loading.

## Reproduce

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/fetch-qwen21-nsfw-lora.ps1 -ModelVersionId 3357315
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/fetch-qwen21-nsfw-lora.ps1 -ModelVersionId 3344536
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/fetch-qwen21-nsfw-lora.ps1 -ModelVersionId 3354330
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite core    -RunStamp r1
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite genital -RunStamp s5-genital
```

Arm → LoRA mapping lives in the runner's `$suiteArms` table (`core`, `multiperson`, `genital`, `malestate`,
`maleneg`, `female`, `femaleneg`, `phantomfix`, `personframing`); `-DryRun` prints every prompt and the LoRA
list per arm before anything is submitted.
