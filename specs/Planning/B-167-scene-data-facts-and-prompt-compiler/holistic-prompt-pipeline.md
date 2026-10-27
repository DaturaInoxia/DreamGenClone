# Holistic still-image prompt pipeline (Krea 2 + Qwen-Image-2.1)

Status: proposed 2026-10-25. Supersedes the correction-by-correction approach recorded in
[plan.md](./plan.md).

## 1. Why we keep fixing the same class of bug

Nine of the last eleven fixes in this feature were validated against **one render** and each one
corrected a different wording symptom: a duplicate restatement, an inverted spatial relation, the
wrong body owning a pair of hands, a first-person caption, a fact deleted to fit a character cap, a
stale contact, a stale LoRA. They read as separate bugs. They are one bug:

> **Every layer is allowed to re-word the facts.** The brief holds facts, the projection anonymises
> them into prose, the fact sheet lists them as prose, a small LLM rewrites that prose into a
> caption, and a validator inspects the *resulting prose*. Nothing owns the meaning, so any layer
> can drop, invert, re-attribute or invent — and the next render finds a new way to.

The newest example is the clearest. SceneImage `8bb09d13` is missing Becky's head. The prompt said:

> "A close-up shot captured from above … **Her head is not visible due to being positioned below the
> frame line where the image cuts off at about waist height.**"

The image model did what it was told. **Nothing about that frame required a crop** — a close view
from above of someone bent over a bench frames her whole body, head included. The *caption* invented
an exclusion, and it had licence to: "close-up shot" plus "the subject fills the frame" says how full
the picture is while never saying what must be IN it. No amount of prompt-tweaking fixes that: the
pipeline has no rule that a caption may not describe what is outside the frame, and it never states
what the frame must include.

## 2. The design: one typed Prompt Plan, one realise step per family

```
moment (facts)  ──►  projection (per POV)  ──►  PROMPT PLAN (typed)  ──►  realise(family)  ──►  prompt
                          │                        │                        │
                   feasibility                 coverage                 structural
                    checks                      contract                 validation
```

### 2.1 The Prompt Plan (the missing owner of meaning)

A typed record, produced once per (moment, POV), holding every element the prompt must express —
and nothing twice:

| Field | Source | Notes |
|---|---|---|
| `Subjects[]` — identity/appearance, wardrobe, world pose, facing, frame placement | brief + projection | appearance suppressed when a face/body reference supplies it |
| `Contacts[]` — `(owner, part, owner, part, manner)` | brief, operator-editable | **ownership is a field**, so "his hands" cannot become "her hands" |
| `ActionLine` | `event.line` (single owner) | the only prose fact that survives |
| `Setting` — kind only | brief `location` | structure/surfaces belong to the location reference |
| `Lighting`, `TimeOfDay`, `Atmosphere` | brief | operator-owned, never suppressed by a location reference |
| `Frame` — shot size, angle, height, placement, **and the per-subject in-frame extent** | blocking + staging profile | §2.3; the extent is what a shot size never decides |
| `HolderBody` — parts + wardrobe | contacts | present only when the frame shows them |
| `MustAppear` — the coverage set | derived | §2.4 |

The plan is **serialisable and inspectable**: the composer can show it, a test can assert it, and a
render can store it, so "what did this prompt mean" stops being a question about prose.

### 2.2 Realisation is deterministic, per family

The LLM stops being the author of the caption. A renderer per family turns the plan into that
family's documented dialect:

- **Krea 2** — one photographic paragraph, no framing demands, no body-noun lists, no shot-size
  vocabulary (measured 2026-10-01, 59-cell matrix).
- **Qwen-Image-2.1** — its natural-language long-form dialect with the researched structure.
- **Pony / SDXL / Flux / API** — their existing documented shapes.

Every fact in the plan is realised exactly once, so a fact cannot be dropped to fit a character cap,
duplicated across restatements, or re-attributed by a paraphrase. The length budget becomes a
property the renderer must satisfy *without losing a slot* — a plan that cannot fit is a
**configuration error reported by name**, not a caption that quietly deletes the wardrobe.

The LLM may still be used for one thing: phrasing the `ActionLine` in the family's voice, gated on
the result parsing back into the same slot. If it cannot, the deterministic text is used.

### 2.3 Inclusion, not "feasibility" (fixes the missing-head class)

**Correction (operator, 2026-10-25):** an earlier draft of this section claimed a `close shot` at
`bird's eye` of a subject bent over a bench "cannot contain a head". That is false. A high, close
view of someone bent over a workbench frames her whole body — head, back, hips, feet — with the head
lower in the frame. **A shot size describes how much of the frame the subject occupies; it never
licenses removing a body part.**

What actually happened on `8bb09d13`: the prompt *stated an exclusion* —

> "Her head is not visible due to being positioned below the frame line where the image cuts off at
> about waist height."

— and the render obeyed it. The writer invented a crop, and our own wording gave it the licence: the
frame was realised as a "close-up shot" plus "the subject fills the frame", which describes how full
the picture is without ever saying what must be IN it.

So the rule is not a feasibility veto, it is an **inclusion contract**:

1. **The plan states inclusion explicitly**, per subject: which body extent is in frame ("her whole
   body from crown to feet", "head and shoulders", "hips to feet — the head is outside this frame
   *because the operator framed it that way*"). An extent is a fact the operator sets or the frame
   derives, never a side effect of a shot-size word.
2. **A caption never describes what is outside the frame.** Forbidden structurally: "not visible",
   "cut off", "below/above the frame line", "outside the frame", "hidden by the frame", "off-screen".
   Whatever is not in frame is simply not written.
3. **Real geometry still constrains, but only where it is a recorded fact**: a character whose
   `facing` points away cannot show a face; a body part that is physically occluded by the bench is
   not described. These come from the brief's facts, never from arithmetic on shot size × camera
   height.
4. **The frame's wording carries the extent, not the fullness**: "a close view from above in which
   her whole body lies across the frame" — never "close-up shot, subject fills the frame", which is
   what let the writer crop.


### 2.4 Two structural rules that end this family of failure

1. **A caption never describes what is outside the frame.** No "not visible", "cut off", "below the
   frame line", "hidden", "off-screen". The renderer writes what IS in the frame; anything else is
   either an inclusion statement §2.3 asks for, or it is not written at all.
2. **Coverage is checked against the plan, not against the prose.** Every `MustAppear` slot must be
   present in the realised prompt — subject count, each wardrobe, each contact with its owner, the
   setting kind, the recorded lighting, the holder's body when in frame, the frame itself. A missing
   slot refuses the prompt by name. This replaces the current "does some word from the note appear
   somewhere" check, which is why a caption saying "her hands on her thighs" passed.

## 3. What this deletes

- The free-prose fact sheet (`StillBriefFactSheet`) as the compiler's input — replaced by the plan.
- `SceneImageCompilerSystemPrompts` role as author for stills — reduced to dialect instructions for
  the `ActionLine` phrasing.
- `SceneImagePromptContractValidator`'s prose heuristics (recap detection, first-person, attribution
  by regex) — the plan makes them impossible instead of detectable.
- The `{{maxChars}}` "compress to fit" instruction — replaced by "fit without losing a slot".

## 4. Migration, in order (each phase is shippable and testable alone)

| # | Phase | Acceptance |
|---|---|---|
| P1 | **Prompt Plan** record + one builder from brief + projection; rendered *beside* the current path, asserted equal in tests | the plan names every fact the current fact sheet carries, and no fact twice |
| P2 | **Inclusion contract** — per-subject in-frame extent in the plan, and a structural refusal of any sentence that describes what is outside the frame (no render change yet) | the `8bb09d13` caption ("her head is not visible … cuts off at about waist height") is refused by name; a caption that states the whole body is accepted |
| P3 | **Realise(Krea2)** and switch that family to the plan path; keep the LLM path behind a switch | N renders per POV with zero prose defects; the same moment at Omniscient/Dean/Becky each produce a correct prompt |
| P4 | **Realise(Qwen21)**, then Pony/SDXL/Flux/API | same acceptance per family |
| P5 | **Coverage contract** replaces the prose validators; delete them | a prompt missing any slot refuses by name |
| P6 | Remove the LLM author path for stills (keep an explicit "refine wording" action) | no `SceneImagePromptProjected` input carries a fact sheet |

## 5. Decisions the operator must make

1. **Action phrasing** — deterministic template only, or LLM-with-round-trip-check? (Recommendation:
   LLM with round-trip, because action prose is the one place wording carries meaning.)
2. **Inclusion extent** — who sets the per-subject in-frame extent: the operator in the composer, or
   derived from the frame (recommendation: derived as a default, operator overridable, and always
   stated in the prompt rather than left to the shot-size word).
3. **POV scope** — build all family renderers first, or ship Krea 2 + Qwen-Image-2.1 (the two
   configured local models) and leave the API families on the current path until needed?
