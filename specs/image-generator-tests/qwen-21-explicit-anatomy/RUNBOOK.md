# Qwen-Image-2.1 — explicit anatomy, erect state, and NSFW LoRA selection (2026-10-02)

## OPERATOR REVIEW — read before any verdict below (2026-10-02)

After reviewing the gallery the operator's verdict was: **"going through most of the image, most of them are not good."**

Everything below is therefore **structural evidence only**, not a quality acceptance:

- The `PASS` / `PARTIAL` / `FAIL` grades in this package were assigned by the agent against a **structure-present**
  bar: *is the intended thing in the frame, and is it not grossly broken?* That bar is much weaker than
  *usable production still*. The two bars disagreeing is what produced this review, and the operator's bar wins.
- **Working conclusion: this pipeline is NOT production-usable for explicit anatomy as configured today.**
  Structural absence ("no vulva", "no penis", "third person missing") the agent caught; overall image quality
  it did not.
- The genuinely load-bearing results are the ones that do not depend on a quality judgement:
  the **negative prompt controlling erect state**, the **envelope controlling person count**, the
  **fused-organ defect and its prompt-shape fix**, and the **LoRA regression on the vulva axis** (visible as
  hair/texture removal, which is measurable rather than taste).
- Treat the settings table and the prompt rules as *necessary, not sufficient*: they remove known defects, they
  do not add up to a good image.

## Purpose

Source-controlled evidence package for the operator question: **"are there NSFW LoRAs for Qwen-Image-2.1,
should we use them, and what are the ideal settings?"** It covers genital anatomy (male + female), male
erect state, multi-person counts, and the sampler envelope — measured on the local ComfyUI host, not asserted.

Plain-language answer:

1. **No LoRA is needed to unlock explicit content on 2.1**, and none of the three tested LoRAs is needed for
   correct *genital anatomy*. The base model at the right envelope does better.
2. **The envelope is the biggest lever**, and it is per position class: `cfg 3 / er_sde / beta` for anything
   explicit. The app's currently-qualified 2.1 envelope (`25 / cfg 1.0 / euler / simple`) both weakens anatomy
   and **silently drops a participant** on 3-person cells.
3. **Male erect state and size are negative-prompt controlled, never positive-prompt controlled.** That
   requires `cfg > 1` (the negative branch is inert at cfg 1), so today the app cannot reach them.
4. **The "missing vulva" symptom is a framing problem**: 2.1 renders a detailed vulva only at hip-and-thighs
   or tighter, and the old prompt style ("the frame is filled by her vulva" / "alone in the frame")
   *instructs* the model to delete the person, producing the rejected toy-like crops.
5. One general NSFW LoRA (`TheseAlpacas v2`) does earn a place, but for **act engagement**, not anatomy.

## Package layout

| Path | Contents |
|---|---|
| `RUNBOOK.md` | this file — method, LoRAs, verdict tables, the settings decision, reproduction |
| `CASE-25-genital-anatomy-and-erect-state.md` | vulva/penis presence + correctness, the negative-prompt lever, framing rules |
| `CASE-26-nsfw-lora-arms.md` | the three LoRAs, arm-by-arm evidence, keep/drop recommendation |
| `CASE-27-multiperson-envelope-and-count.md` | envelope decides person count; DP/organ-count failures |
| `images/` | the five contact sheets the verdicts were called from (JPEG; full-res PNGs regenerate) |
| `manifest.json` | machine-readable model/envelope/LoRA/suite/verdict metadata + replay commands |

## Model + envelopes

Base stack (unchanged, no ControlNet): `qwen_image_2.1_int8_convrot` + `qwen3vl_8b_int8_convrot` +
`qwen_image_2.1_vae_bf16` on the local ComfyUI host (WOOD-GAME-MAIN, RTX 5080 16 GB), 1024×1024.

| Envelope | Settings | Notes |
|---|---|---|
| **`envelope`** | 25 steps, cfg **1.0**, euler/simple | what the app's 2.1 rows are currently qualified with. Negative prompt is **inert**. |
| **`recipe`** | 30 steps, cfg **3.0**, `er_sde`/`beta` | what every LoRA author recommends, and what won the quality comparison. Negative is **live**. |

## LoRAs under test (all pinned by SHA-256, fetched with `helpers/local-comfyui-host/fetch-qwen21-nsfw-lora.ps1`)

| Key | File | Civitai | Bytes | SHA-256 |
|---|---|---|---|---|
| `alpacas` | `NSFW Qwen by TheseAlpacas V2.safetensors` | 2958918 / v3357315 | 79,744,352 | `83ABA822AD8EE10A1E836828F5088259D93BCE7B5F3419B46DBC0258C2BE46A9` |
| `coachbate` | `qwen-image-2.1_penis_coachbate_preview1.safetensors` | 2952865 / v3344536 | 159,436,160 | `0F79FAE880BA2B89960676E036DFEE1FEA6020F7ACBF6D1A333DC14B477CDEEB` |
| `vulva` | `qwen21_v2_000002750.safetensors` | 2960871 / v3354330 | 79,744,256 | `71D8EDBFBB693DA3B1ACDFE3A48C1C61942974CB1DFA864E5F0862630A0A3C4C` |

## Suites measured (66 renders, every one viewed)

| Suite | Cells | Arms | Renders | Question |
|---|---|---|---|---|
| `core` | 4 catalog cells | base/lora × envelope/recipe | 16 | does the LoRA or the envelope matter more? |
| `multiperson` | 3 catalog cells | envelope, base-recipe, +alpacas, +coachbate | 12 | do 3-4 person cells hold their count? |
| `genital` | 2 catalog close-ups | base, +vulva, +coachbate+vulva | 6 | close-up genital anatomy |
| `malestate` | 4 new probes | base, +coachbate | 8 | can wording set erect state/size? |
| `maleneg` | 2 new probes | base, +coachbate | 4 | does a negative prompt set the state? |
| `female` | 6 new probes | base, +vulva | 12 | vulva anatomy vs distance + state |
| `phantomfix` | 2 new probes | base (A/B on the negative) | 2 | fix for the fused-organ defect |
| `personframing` | 3 new probes | base, +vulva | 6 | does anchoring the person kill the toy-crop look? |

Also defined but **NOT run** (no results in this package): `femaleneg` (female probes + a male-organ
negative). Listed so nobody reads it as evidence.

## Headline verdicts

| # | Finding | Evidence |
|---|---|---|
| 1 | Base 2.1 needs **no unlock**; it renders explicit nudity, acts and genitals at `cfg 1` already | `core` (7 PASS / 1 PARTIAL across the 8 base arms) |
| 2 | The `recipe` envelope beat `envelope` for anatomy/skin quality in 3 of 4 `core` cells | `core`, sheet-5 |
| 3 | At `envelope` (cfg 1) a 3-person DP cell renders **2 people**; at `recipe` it renders 3 | sheet-5, CASE-27 |
| 4 | **Erect state/size: positive wording fails, a negative prompt works** (flaccid and small-erect both) | set `malestate` vs `maleneg`, sheet-4, CASE-25 |
| 5 | The **vulva LoRA is a regression** at strength 1.0 — strips pubic hair/body texture, softens the vulva; base ≥ LoRA in all four distance rows | sheet-1, CASE-26 |
| 6 | **No genitals LoRA needed** for correct penis or vulva; the base macro is the most detailed vulva in the programme | sheet-1, CASE-26 |
| 7 | The toy-like "disembodied torso" close-up was caused by **our own prompt wording**, not the model; person anchors fix it | sheet-2, CASE-25 |
| 8 | "Point of penetration" macro framing fails outright (shaft beside rather than inside the vulva; the vulva LoRA fuses both organs into one) | CASE-25/26 |
| 9 | Naming the male body parts in frame (with the man in the same frame) fixed the fused-organ coitus defect; the fusion negative added nothing | sheet-3, CASE-25 |

## Settings decision

| Shot class | Envelope | LoRA | Notes |
|---|---|---|---|
| Nude / explicit portrait, 1 person | 30 / **3.0** / er_sde / beta | none | 25/cfg1 also works but is weaker; keep only for speed |
| 2-person act | 30 / 3.0 / er_sde / beta | none | acts render without help |
| **3-4 person / DP / orgy** | 30 / **3.0** / er_sde / beta | none | **cfg 1 loses a participant**; 4-person counts are still unreliable (§or CASE-27) |
| Act engagement where the base won't engage (oral) | 30 / 3.0 / er_sde / beta | `alpacas` 0.8-1.0 | the one real capability delta; costs gloss |
| Vulva close-up | 30 / 3.0 / er_sde / beta | **none** | base wins; LoRA regresses at 1.0; 0.4-0.5 untested |
| Flaccid / soft male | 30 / 3.0 / er_sde / beta | none | **requires the negative prompt** |
| Small / large erect male | 30 / 3.0 / er_sde / beta | none | size is steered negatively; wording alone does not work |
| Large/veiny male look | 30 / 3.0 / er_sde / beta | `coachbate` (optional) | **recurring notched/cleft glans**, reduced person count in orgy |
| Genital macro where detail must be pristine | — | — | recommend a mature 2511/SDXL stack instead; 2.1 tops out at "recognizable but simplified" |

**Prompt rules that came out of this (all zero-cost):**

1. Never write "the frame is filled by X" / "alone in the frame" / "only his lower torso and penis are in
   frame" — those delete the person and produce the toy crop.
2. In close-ups, **require person anchors**: face, chest, stomach, hands on thighs.
3. For coitus, **name the male body parts that are in frame** above the join (his stomach, the tops of his
   hairy thighs, his scrotum at her perineum, "his penis runs from his own groin").
4. Vulva legibility needs framing at **hip-and-thighs or tighter**; at full-length and medium-hip it is a slit.
5. Erect state and size go in the **negative**, not the positive.

## Reproduce

```powershell
# LoRAs onto the host (idempotent, verifies SHA-256 + safetensors header, then confirms ComfyUI lists them)
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/fetch-qwen21-nsfw-lora.ps1 -ModelVersionId 3357315
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/fetch-qwen21-nsfw-lora.ps1 -ModelVersionId 3344536
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/fetch-qwen21-nsfw-lora.ps1 -ModelVersionId 3354330

# Renders, one suite at a time (dry-run first to print prompts and write workflows)
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite core -DryRun
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite multiperson -RunStamp s4-multiperson
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite maleneg -RunStamp s6-maleneg
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite female -RunStamp s7-female
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite personframing -RunStamp s9-personframing

# Contact sheets (the artifacts the verdicts are called from). Committed spec:
#   specs/image-generator-tests/qwen-21-explicit-anatomy/contact-sheets.json
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/contact-sheets/build_contact_sheet.py --spec specs/image-generator-tests/qwen-21-explicit-anatomy/contact-sheets.json --out-dir artifacts/tmp/qwen21-nsfw-lora/sheets
# committed (smaller) copies:
d:/src/DreamGenClone/.venv/Scripts/python.exe tools/contact-sheets/build_contact_sheet.py --spec specs/image-generator-tests/qwen-21-explicit-anatomy/contact-sheets.json --out-dir specs/image-generator-tests/qwen-21-explicit-anatomy/images --format jpeg --quality 88
```

Generated outputs live in git-ignored `artifacts/tmp/qwen21-nsfw-lora/<run stamp>/` (per-cell PNG +
`workflows/<label>.workflow.json` + `results.json`).

## Corrections log (read this before trusting older notes)

Three verdicts were originally called from downscaled chat previews and were **wrong**; they are corrected
here and superseded:

| Original claim | Correction |
|---|---|
| "the creampie close-up is the best male+female anatomy of the session" | It is a **rejected** frame: the shaft grows out of the woman's own pubic mound with no male pelvis in frame. |
| "the vulva LoRA is a genuine win for female legibility" | It is a **regression** at 1.0 — hair removed, doll-smooth body, softer vulva. Base ≥ LoRA in all four distance rows. |
| "the base macro's vaginal opening is an unlit black void" | The **base macro is the most detailed vulva in the programme**; the softer one is the LoRA arm. |

**Standing method rule for this area: judge anatomy on `tools/contact-sheets` output, never on a downscaled
chat preview.** All three errors above happened at preview resolution.

## Not measured (do not over-read)

- **n = 1 seed per arm.** Defect patterns (blobs, notched glans) need a second seed before becoming claims.
- **LoRA strength sweep:** everything ran at 1.0. 0.4-0.5/0.7 is untested — the obvious next test, since
  every LoRA failure looks like over-driving.
- 4-person counts: the orgy cell produced 2 couples, a 3-person scene, and a crowded frame depending on arm;
  no arm reliably held "exactly four".
- Clitoris/minora/urethra detail, interior vaginal anatomy, fourchette: not rendered at any setting tested.
- The vulva LoRA at reduced strength; the `femaleneg` suite; the second uncircumcised-penis LoRA (Civitai);
  the other Civitai 2.1 anatomy LoRAs (vagina-visibility, PornMaster sliders, paid erect-penis LoRA).
- Nothing in this package has been wired into the app, and no app code was changed.
