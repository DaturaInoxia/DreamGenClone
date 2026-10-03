# CASE-25 — genital anatomy, erect state, and the framing rules that decide them (measured 2026-10-02)

**Question:** on Qwen-Image-2.1, what actually controls (a) whether a **vulva** and a **penis** appear
correctly, (b) the male **erect state** and **size**, and (c) whether a close-up looks like a person or a
disembodied torso?

**Answers:** (a) framing distance + person anchors, not LoRAs; (b) the **negative prompt** (requires cfg > 1);
(c) the wording — our own prompt style was the cause.

Envelope for every render in this case: 30 steps, cfg 3.0, `er_sde`/`beta`, 1024×1024, base weights
(no LoRA) unless stated.

---

## 1. Male erect state — positive wording cannot set it, a negative prompt can

Four probes share ONE photographic brief so only the state clause changes (same seed per probe, `malestate`
suite; the pair with a negative is the `maleneg` suite, same seeds).

| Probe | Seed | State clause (the only difference) |
|---|---|---|
| `male-flaccid` | 71001 | "His penis is completely soft and flaccid, hanging down naturally against his scrotum with no erection at all; the glans, foreskin and shaft are at rest." |
| `male-semi-erect` | 71002 | "His penis is half erect, thickening and beginning to lift away from his scrotum at a shallow upward angle but not fully hard; the glans is partly uncovered and the shaft is visibly swollen." |
| `male-erect-average` | 71003 | "His penis is fully erect and average in size, standing up and forward from his body at a natural angle with the glans exposed and the shaft straight." |
| `male-erect-large` | 71004 | "His penis is fully erect and noticeably large and thick, standing up and forward from his body with a heavy shaft, prominent veins and the glans fully exposed, the scrotum hanging low beneath it." |
| `male-erect-small` | 71005 | "His penis is fully erect but small and slender with modest, unobtrusive proportions; it lifts only slightly away from his full scrotum and the shaft is thin with the glans exposed." |

Shared frame (verbatim, all probes): *"A photorealistic photograph of one adult man standing nude in a
plainly lit room, facing the camera with his weight relaxed on one leg and his arms loose at his sides,
looking slightly away from the lens. [STATE CLAUSE] Natural realistic body proportions and unretouched skin
with chest hair and natural pubic hair. Medium shot from the front at chest height, 50mm lens, soft daylight
from a window on the left, shallow depth of field, sharp focus on the middle of his body."*

| Asked for | Positive only | With a negative |
|---|---|---|
| flaccid | **FAIL** — hangs (gravity honoured) but large and engorged-looking, no resting foreskin | **PASS** — genuine soft foreskin-covered hang, on base AND +coachbate |
| semi-erect | **FAIL** — full erection; base arm added a cleft/double-lobed glans | not re-tested |
| "average, standing up and forward" | **FAIL** — geometry ignored (shaft angled downward), size large | — |
| small erect | **FAIL** — oversized | **PASS** — visibly smaller organ; the negative also beat the coachbate LoRA's size bias |

Negatives used (live only at cfg > 1):
- flaccid: `erect penis, erection, hard shaft, engorged glans, swollen penis, large penis, thick shaft, tumescence`
- small: `large penis, thick shaft, huge penis, very big penis, oversized genitals, bulbous glans, long penis`

**Rule: on 2.1, erect state and size are NEGATIVE-prompt controlled.** Wording in the positive is not a lever.
The app's 2.1 graph hardcodes `negative_prompt = ''` and its qualified envelope is cfg 1, so today this channel
does not exist in the app.

## 2. Female vulva — present, but only legible when framed close

Six probes (`female` suite), every prompt **one adult woman alone — no male actor, no act** (that removes the
coitus trigger that produced a phantom organ, see CASE-26). Distance axis + state axis:

| Probe | Seed | Distinguishing clause |
|---|---|---|
| `female-vulva-full` | 72001 | "Full-length shot from the front with her whole figure inside the frame" — vulva "visible low in the frame between her thighs" |
| `female-vulva-medium` | 72002 | "The frame runs from her waist to her knees, so her vulva is centred" — "Medium shot from the front at hip height, 50mm" |
| `female-vulva-closeup` | 72003 | "Her labia are soft and closed, the outer lips resting together over the vaginal opening, the clitoral hood visible at the top" |
| `female-vulva-aroused` | 72004 | "Her labia are swollen and parted, the inner lips fuller and darker than the outer, the vaginal opening glistening and wet with natural lubrication, the clitoris visible beneath its hood" |
| `female-vulva-spread` | 72005 | "Both of her hands are between her legs, two fingers of each hand spreading her outer labia apart so that her inner labia, vaginal opening and clitoris are fully exposed" |
| `female-vulva-macro` | 72006 | "The frame is filled by her vulva: the outer lips, the inner labia, the clitoral hood and the vaginal opening all in sharp focus" — 100mm macro |

Verdict (base arms, judged on `images/sheet-1-vulva-distance.jpg`):

| Framing | Result |
|---|---|
| full-length | vulva is a **slit** with a hair patch — present, not legible |
| medium hip | slit only; labia/clitoris not readable |
| close-up (old wording) | recognizable vulva (labia + opening) but the frame is only a pelvis — the rejected toy crop |
| **macro (old wording)** | **the most detailed vulva in the whole programme** (minora, dark opening, individual hair strands, pores) — but framed as a detached crop |

So: **2.1 can render a detailed vulva; it needs a close frame.** The failure mode at distance is
under-modeling, and the failure mode we inflicted on ourselves was the framing instruction (below).

## 3. The "disembodied torso / toy" close-up is a PROMPT defect

The rejected style came from our own published wording:

- `cumshot-creampie`: *"Only the man's lower torso and penis are in frame… framed tightly on the point where
  the bodies join."*
- `missionary-penetration-closeup`: *"The frame is filled by their pelvises… The rest of their bodies fall
  out of the frame."*
- the first female probes: *"alone in the frame"*, *"the frame is filled by her vulva"*.

Those sentences **instruct the model to delete the person**, and it complies. The fix is to require person
anchors instead. Verified in the `personframing` suite (same subject, same seed, anchors added):

- `coitus-person` (74001): *"…framed as a medium close-up at hip level rather than a macro. Both people are
  clearly present: the man is on top with his chest, shoulder and the side of his face visible in the upper
  part of the frame, and the woman lies under him with her raised knee, her forearm and her hand gripping the
  sheet visible at the lower left, her head turned away at the edge of the frame. Between their bodies their
  genitals are joined and in sharp focus…"*
- `female-closeup-person` (74002): *"…framed as a close-up of her hips and thighs rather than a macro. She is
  fully present in the frame: her face and her loose hair are visible at the top edge, her breasts and stomach
  are in view above her pelvis, and both of her hands rest on her inner thighs, which are drawn up and apart…"*
- `female-macro-person` (74003): *"…framed so tight that her vulva fills the centre of the frame. She is still
  visibly a person, not a detached crop: her lower stomach and the tops of her thighs frame the subject on all
  sides, one of her hands rests on her thigh in the lower left corner with her fingers in view, and her pubic
  hair is continuous with the skin of her stomach."*

Result: person present AND vulva/join readable in both arms (`images/sheet-2-vulva-person-anchored.jpg`,
`sheet-3-coitus.jpg`). The toy look is gone.

## 4. Fused-organ defect in coitus close-ups, and its fix

At the `cumshot-creampie` framing the base model renders **one body carrying both a shaft and a vulva** — the
shaft grows out of the woman's own pubic mound and no male pelvis is in frame. Same at +vulva and
+coachbate+vulva. **Rejected frame** (this was originally mis-reported as the best anatomy of the session).

Fix, verified as a 2-render A/B at one seed (`phantomfix` suite): keep the tight framing but **name the male
body parts that are in frame**, so the model has to build two bodies:

> "…The man is clearly a separate person whose lower body enters the frame from above: his flat stomach and
> the tops of his hairy thighs are visible above the join, his scrotum rests against her perineum, and his
> erect penis runs from his own groin into her vagina. Her vulva is below his body, with her inner labia
> visible on either side of the base of his penis and her own thighs spreading away to the left and right…"

Both arms produced a clean two-body coitus read. The negative arm added a fusion negative
(`fused genitals, penis attached to the female body, one figure with both sets of organs, hermaphrodite
anatomy, merged bodies, penis growing from the vulva`) and was **no better than the positive-only arm** —
so the **anchor is the lever and the negative is redundant here**.

## 5. Rules for the 2.1 compiler (all zero-cost)

1. Never write "the frame is filled by X", "alone in the frame", or "only his lower torso and penis are in
   frame" — that deletes the person.
2. In any close-up, require person anchors (face, chest, stomach, hands, thighs).
3. For coitus, name the male body parts present above the join, and state that his penis runs from **his own**
   groin.
4. Vulva legibility requires **hip-and-thighs or tighter**; at distance expect a slit.
5. Erect state / size belong in the **negative**, and therefore require `cfg > 1`.

## Reproduce

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite malestate     -RunStamp s3-malestate
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite maleneg       -RunStamp s6-maleneg
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite female        -RunStamp s7-female
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite phantomfix    -RunStamp s8-phantomfix
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen21-nsfw-lora-proof.ps1 -Suite personframing -RunStamp s9-personframing
```

Verify every generated workflow before trusting a run: node 9 (if present) must be the LoRA loader and node 6
the KSampler carrying the intended `steps/cfg/sampler/scheduler`, with node 4's `negative_prompt` equal to the
intended negative. The runner also fails fast if a `class_type` is missing on the host.

## Not measured

- n = 1 seed per probe (defects may be seed luck — a second seed is required before these become claims).
- No clitoris/minora/urethral detail at any setting; no interior vaginal anatomy.
- Semi-erect with a negative was not re-tested (only flaccid and small-erect were).
- The `femaleneg` suite (female probes + male-organ negative) is defined in the runner but was **not run**.
