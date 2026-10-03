# CASE-27 — the sampler envelope decides person count in multi-person cells (measured 2026-10-02)

**Question:** the operator observed that "the settings will depend on the position being achieved". Is that
true for Qwen-Image-2.1, and where?

**Answer: yes, and most sharply on person count.** At the app's currently-qualified envelope
(`25 steps / cfg 1.0 / euler / simple`) a 3-person cell renders **two people**; at
`30 steps / cfg 3.0 / er_sde / beta` it renders three with both contacts visible. The same envelope also
removes the **negative-prompt channel**, because the negative branch is inert at cfg 1.

Suite `multiperson`: 3 catalog cells × 4 arms = 12 renders, 1024×1024, catalog prompt text and catalog seed
(`mmf-double-penetration` 20301, `mmf-spitroast-closeup` 20305, `orgy-four-way` 20401).
Sheet: `images/sheet-5-lora-arms.jpg` (DP cell, 4 arms); the spitroast and orgy arms are in
`artifacts/tmp/qwen21-nsfw-lora/s4-multiperson/`.

## `mmf-double-penetration` — 3 people requested (one man behind + one beneath)

| Arm | Envelope | LoRA | Result |
|---|---|---|---|
| `base-envelope` | 25 / **1.0** / euler / simple | none | **FAIL — only 2 people.** The second man is simply absent; the genital join is a fused mass. DP not achieved. |
| `base-recipe` | 30 / 3.0 / er_sde / beta | none | **PASS** — 3 people, both contacts present, vulva readable, penis entering, second man's penis at the other orifice; minor limb crowding, no merging. |
| `alpacas-recipe` | 30 / 3.0 / er_sde / beta | `alpacas` | PASS — 3 people, explicit, both contacts; glossier skin, larger/veinier organs. |
| `coachbate-recipe` | 30 / 3.0 / er_sde / beta | `coachbate` | PASS with defects — 3 people, the least glossy skin of the recipe arms, but her arm merges into the man's thigh and the hand at the vulva is malformed. |

## `mmf-spitroast-closeup` — macro, two contacts in one frame

| Arm | Result |
|---|---|
| `base-envelope` | **one penis + fingers — no second partner at all.** The requested two contacts are absent (genital detail itself is strong). |
| `base-recipe` | two penises flanking the vulva/anus, vulva and anal opening both detailed. Anatomy PASS; the literal action is only partial (the front contact lands at the anus/vulva rather than in her mouth). |
| `alpacas-recipe` | two contacts still present, but the LoRA brings back the oiled/silicone gloss and kills the skin texture; organs more inflated. PARTIAL. |
| `coachbate-recipe` | two contacts, best skin fidelity of the recipe arms. PASS. |

## `orgy-four-way` — exactly four adults requested

| Arm | Result |
|---|---|
| `base-envelope` | crowded frame, one clear contact; person count not legible |
| `base-recipe` | **two couples on one bed** — a legitimate 4-person orgy read (bodies overlap/crop partially) |
| `alpacas-recipe` | most crowded/most bodies, but glossy with limb ambiguity |
| `coachbate-recipe` | **FAIL on count — only 3 people** (1 man + 2 women), although it had the best anatomy and skin of the four and a very well formed penis (veins, glans, scrotum) |

**No arm reliably held "exactly four".** 4-person composition remains an open problem (it also failed for
BigLust v16 in the baseline catalog run of 2026-09-30, which produced a 2-panel composite).

## Consequences for the app

1. **The 2.1 `envelope` (25/cfg1/euler/simple) must not be used for 3+ person cells** — it silently drops a
   participant. This is a behaviour difference no prompt wording recovered.
2. `cfg 1` also disables the negative branch, so state control (CASE-25 §1) is unavailable there. Two
   independent reasons to move the explicit-shot envelope to `cfg 3.0 / er_sde / beta`.
3. Whether to keep a single 2.1 envelope or split it by position class is an app design decision; this case
   says the split is real for **count**, not just for polish.

## Reproduce

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite multiperson -DryRun
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite multiperson -RunStamp s4-multiperson
```

## Not measured

- n = 1 seed per arm; "2 people vs 3 people" is a single-seed observation per arm and should be repeated on a
  second seed before it is treated as a rate.
- 4-person counts across seeds; whether a higher resolution or a different camera clause recovers 4.
- Interaction with reference images (identity refs are capped at ≤6 for a pose-carrying composition — see
  `../qwen-21-native-reference/CASE-22-reference-count-ceiling.md`); no references were used here.
