# Body Vocabulary & Body-Part Taxonomy (research deliverable)

**Status:** research — input to the body-authoring surface (canned vocabularies + variant history + review deck).
**Date:** 2026-09-22 · **Owner spec:** B-122 Phase 0 (BodyCard) · **Reconciles with:** `PhysicalAttributes` / `PhysicalAttributesCatalog`
**Not an implementation plan.** Nothing here is authorised code. It is the vocabulary the implementation consumes.

---

## 0. What already exists (the reference the operator pointed at)

Two vocabularies exist today and are the baseline this document extends — **not** a second system.

**A. The BodyCard contract** (`DreamGenClone.Domain/RolePlay/CharacterBodyCardModels.cs`) — the generation gate.
7 fields, all-or-nothing: `BodyShape`, `HeightBuild`, `Skin`, `BodyHair`, `Tattoos`, `ScarsMarks`, `PubicHair`.
`BodyShape` and `HeightBuild` are today **free text** — this is the field pair the operator is typing random words into.

**B. The template attributes** (`DreamGenClone.Domain/Templates/PhysicalAttributes.cs` + `PhysicalAttributesCatalog.cs`) — the *reference* vocabulary, already dropdown-backed in `PhysicalAttributesEditor.razor`:

| Already a dropdown | Already ordered range |
|---|---|
| BodyType | Slim, Petite, Athletic, Toned, Average, Curvy, Full-Figured, Muscular, Stocky, Plus-Size |
| BustSize | Flat-chested → Very small → Small → Average → Full → Large → Very large → Enormous → Overwhelming |
| WaistSize | Extremely slim → Very slim → Slim → Average → Soft → Full → Heavy |
| HipSize | Narrow → Slim → Average → Wide → Very wide → Voluptuous → Extremely wide |
| SkinTone / SkinTexture | 9 tones; 7 textures |
| BodyHairOptions / PubicHairOptions | 9 / 6 options incl. explicit "none" |
| HairColour/Style, EyeColour, Ethnicity | full lists |

**Verified gaps in A+B (checked in code and in the dev DB, 2026-09-22):**

1. **`ButtSize` is a dead field.** It exists on `PhysicalAttributes`, is *read* by `CharacterBodyCardPrefillService` (`("ButtSize", attributes?.ButtSize, "rear")`), but there is **no `ButtSizes` array in the catalog** and **no input in `PhysicalAttributesEditor.razor`** — the editor renders BodyType/BustSize/WaistSize/HipSize only. It can never be set through the UI.
2. **No `HeightOptions` / `WeightOptions` / `AgeOptions`.** Height and Weight are free text.
3. **`Templates` rows carry no `PhysicalAttributes` at all.** `SELECT TemplateType, COUNT(*)` → Location 10, ScenarioGuidance 5, Character 4, **with_phys = 0 for every type**. The vocabulary is modelled and has dropdowns, but not one character template has ever had it filled in — so it has never been exercised as a prefill source.
4. **`BodyTypes` is one flat list that mixes three independent axes** (frame, fat, muscle). This is the root cause of the operator's complaint, and §2 is the fix.

---

## 1. The design principle: axes, not a list

A single "body type" list cannot work, and the reason is structural:

- `BodyTypes` contains **Slim** (low fat), **Muscular** (high muscle), **Curvy** (fat *distribution*), **Petite** (frame), **Plus-Size** (fat *level*). These are not alternatives on one scale, so selecting two of them is not "more specific" — it is **contradictory**, and the model resolves the contradiction unpredictably. That is why typing more words produced no control.
- **"Overweight" never says where.** Fat distribution is a separate, independently visible axis, and it is the axis that separates a female gynoid figure from a male android one. With no distribution axis the model supplies its own default, which is the mechanism behind *"the body is coming out too thick"* — thick reads as **central/masculine mass**, not as the intended female shape.
- **Volume without shape is underdetermined.** "Full bust" / "full rear" set volume only; shape (round vs teardrop vs pendulous; round vs shelf vs flat) and position (perky vs settled) are separate visible facts the model otherwise invents per render. This is the instability that makes re-rolls look like different characters.
- **Weight in kg/lb is nearly information-free for an image model.** 140 lb is a different body at 5'2" and at 5'10". Build axes carry the signal; the number does not.

**Therefore: the body is a point in a space of ~8 independent axes.** Every canned value belongs to exactly one axis, the axes compose into one descriptor clause, and moving along one axis changes exactly one thing — which is precisely what makes "short and fat → tall and skinny" a *sequence of controlled edits* instead of a new prompt.

> **A note on the mechanism claims.** The conflation and missing-distribution arguments above are structural and hold regardless of model. The claim that the model's absent-axis default is *central/masculine mass* is a **hypothesis consistent with the operator's observation, not yet measured** — it is testable with the study in §7 and should be confirmed there rather than assumed.

---

## 2. The axes (both genders)

| # | Axis | Question it answers | Where it lives today |
|---|---|---|---|
| 1 | **Height** | how tall | `HeightBuild` (free text) |
| 2 | **Frame / skeleton** | bone width: shoulders, ribcage, pelvis | *missing* (buried in `BodyType`) |
| 3 | **Silhouette** | the front-view outline (hourglass vs rectangle vs V-taper) | *missing* (buried in `BodyType`) |
| 4 | **Adiposity level** | how much fat | *missing* (`Weight` is a poor proxy) |
| 5 | **Fat distribution** | *where* the fat sits | *missing* — the biggest single gap |
| 6 | **Muscle mass** | how much muscle | *missing* (buried in `BodyType`) |
| 7 | **Muscle definition** | how visible it is (leanness/conditioning) | *missing* — separates "ripped" from "dad bod" |
| 8 | **Regional volumes & shapes** | bust, rear, waist, hips, thighs, calves, arms, shoulders, neck, midsection | partially: Bust/Waist/Hip/Butt (volume only) |
| 9 | **Age-linked cues** | skin laxity, fullness, posture, grey | `Age` (number only) |
| 10 | **Surface invariants** | skin, body hair, pubic hair, tattoos, scars, piercings | **complete today** — no change needed |

Axes 6 + 7 + 5 together are what let one control express both **"Ripped"** and **"dad body"** for a man, and both **"model body"** and **"typical mom body"** for a woman, without a single hardcoded preset.

---

## 3. Female — ranges per axis

### 3.1 Height
| Band | Label |
|---|---|
| 4'10"–5'0" | Very petite |
| 5'1"–5'3" | Petite |
| 5'4"–5'6" | Average height |
| 5'7"–5'9" | Tall |
| 5'10"–6'0" | Very tall |
| 6'1"+ | Statuesque |

### 3.2 Frame / skeleton (bone width — independent of weight)
Petite and fine-boned · Small and narrow · Medium/average frame · Broad-framed (wide shoulders & ribcage) · Big-boned / heavy frame

### 3.3 Silhouette — the standard classification
Grounded in the apparel-industry classification (Hourglass / Bottom hourglass / Top hourglass / Spoon / Triangle / Inverted triangle / Rectangle, per Simmons–Istook FFIT; Connell's four-shape reduction is Hourglass / Pear / Rectangle / Inverted triangle). Reference: ~46% of women measure Rectangle, ~20% Spoon, ~14% Inverted triangle, ~8% Hourglass.

| Value | Meaning |
|---|---|
| **Hourglass** | bust ≈ hips, waist much narrower |
| **Bottom hourglass** | hips slightly > bust, waist clearly narrower |
| **Pear / Spoon / Triangle** | hips clearly > bust, wider rear + thighs |
| **Top hourglass** | bust slightly > hips, waist clearly narrower |
| **Inverted triangle** | shoulders/bust clearly > hips |
| **Rectangle / Ruler** | bust ≈ hips ≈ waist, little waist definition |
| **Apple / Round / Oval** | weight carried at waist and upper abdomen |
| **Diamond** | narrow shoulders and hips, mass at mid-torso |
| **Tubular** | straight, minimal waist or hip inflection |

### 3.4 Adiposity level
Very lean / athletic · Lean / slim · Average · Soft / fleshy · Full / plump (+size) · Heavy · Very heavy

*(Approximate body-fat bands for calibration only: athletic ~14–20%, lean ~20–24%, average ~25–31%, soft ~32–37%, full ~38–45%, heavy 45%+. These are context for the operator, not prompt text.)*

### 3.5 Fat distribution — **the missing axis**
| Value | Where it sits |
|---|---|
| **Gynoid / pear** | hips, thighs, rear — waist stays comparatively narrow |
| **Proportional / even** | distributed evenly |
| **Lower-body heavy** | thighs and calves |
| **Upper-body heavy** | bust, arms, upper back |
| **Central / midsection** | belly and love handles |
| **Android / abdominal** | waist and upper abdomen (typical post-menopausal pattern) |

### 3.6 Muscle mass
No visible muscle / soft · Slight tone · Toned · Fit · Athletic · Very athletic · Muscular · Bodybuilder

### 3.7 Muscle definition
Undefined (smooth) · Faint definition · Defined (visible abs) · Very defined (separation) · Ripped (striations, vascularity) · Over-dieted (lean but low mass)

### 3.8 Regional ranges
- **Bust** — 4 independent sub-parameters:
  - *Volume*: existing `BustSizes` list is correct and complete — **keep it unchanged**.
  - *Shape*: Round · Teardrop (fuller at the bottom) · Bell · Conical · Wide-set · Close-set · Pendulous · Asymmetric · Augmented (high, round, firm) · Natural (soft, settled)
  - *Position*: High and perky · Natural and full · Settled · Low
  - *Ribcage band relative to frame*: Narrow (30–32 band) · Average (34–36) · Broad (38–42) — **this is what makes a cup letter mean anything**
- **Waist** — narrowness: Tiny · Very narrow · Narrow · Slim · Average · Soft · Thick · Wide; definition: Cinched · Defined · Softened · Straight · Thick
- **Hips** — width: existing `HipSizes` is good (Narrow → Extremely wide); add *where*: High hip (hipbone prominence) · Saddle bags · Full across the rear
- **Rear / glutes** — 3 sub-parameters:
  - *Volume*: Flat · Small · Average · Full · Large · Very large
  - *Shape*: Round · Heart · Shelf · Square · Pear · Teardrop
  - *Lift*: High and lifted · Natural · Settled · Flat
- **Thighs** — Slim · Average · Full · Thick · Very thick; *gap*: Thigh gap · Touching · Rubbing
- **Calves** — Slim · Average · Full · Muscular
- **Arms** — Slender · Average · Soft · Full · Toned · Muscular
- **Shoulders** — Narrow and sloped · Average · Broad
- **Neck** — Slim · Average · Full · Short/thick
- **Midsection** — Flat and taut · Flat with faint abs · Soft · Rounded belly · Lower pouch · Prominent belly
- **Hands / feet** — Small and fine · Average · Broad/strong *(minor; include for completeness)*

### 3.9 Age-linked cues
Youthful (firm, taut, no lines) · Prime (fully developed) · Mature (slight softening) · Middle-aged (softening, the "mom bod" period) · Older · Elderly

---

## 4. Male — ranges per axis

### 4.1 Height
5'2"–5'4" Short · 5'5"–5'7" Below average · 5'8"–5'10" Average height · 5'11"–6'1" Tall · 6'2"–6'4" Very tall · 6'5"+ Towering

### 4.2 Frame / skeleton
Fine-boned · Narrow · Average · Broad-shouldered · Heavy / thick-set

### 4.3 Silhouette (menswear fit classification)
| Value | Meaning |
|---|---|
| **Trapezoid / V-taper** | shoulders > waist — the classic "athletic" |
| **Inverted triangle** | shoulders ≫ waist, slim hips — bodybuilder |
| **Rectangle / straight** | shoulders ≈ waist |
| **Triangle / pear** | waist and hips ≥ shoulders — softer |
| **Oval / round / apple** | mass carried at the midsection |
| **Stocky** | broad but thick through the middle |

### 4.4 Adiposity level
Shredded (competition-lean) · Athletic / lean · Fit / trim · Average · Soft (love handles) · Overweight · Heavy · Very heavy

### 4.5 Fat distribution
**Central / abdominal (belly forward)** — the "beer belly" pattern · **Love handles / flank** · **Even / proportional** · **Chest-heavy** · **Lower-body heavy**

### 4.6 Muscle mass
Untrained · Thin/soft · Average · Fit · Athletic · Muscular · Very muscular · Massive

### 4.7 Muscle definition
Undefined (smooth) · Faint · Defined (visible abs) · Very defined · Ripped (striations, vascularity) · Gaunt (lean, low mass)

### 4.8 Regional ranges
- **Shoulders / back** — Narrow · Average · Broad · Very broad with heavy lats (V-taper)
- **Chest / pecs** — Flat · Average · Defined · Full · Bulky · Heavy and sagging
- **Midsection** — Flat · Firm · Soft · Paunch · Beer belly · Prominent gut · Very large belly
- **Waist vs shoulders** — the taper statement: strong V-taper · moderate taper · straight · wider than shoulders
- **Arms** — Thin · Average · Muscular · Very muscular; *definition*: Soft · Defined · Vascular
- **Legs / thighs** — Thin · Average · Muscular · Heavy
- **Calves** — Slim · Average · Full · Muscular
- **Rear** — Flat · Average · Full · Prominent
- **Neck / traps** — Slim · Average · Thick · Very thick ("bull neck")
- **Hands / feet** — Average · Broad and strong *(minor)*

### 4.9 Age-linked cues
Youthful · Prime · Mature · Middle-aged (the dad-bod period) · Older · Elderly

### 4.10 Male colloquial presets worth exposing (each is a *point* in the axes above, never a replacement for them)
These are useful **named anchors** the operator can start from and then move one axis at a time:

| Preset | Frame | Adiposity | Distribution | Muscle mass | Definition |
|---|---|---|---|---|---|
| **Ripped / jock** | broad | shredded | even | high | ripped |
| **Athlete** | broad | athletic | even | high | defined |
| **Lean / runner** | narrow-medium | lean | even | medium | defined |
| **Dad bod** | average | soft | **central** | medium (retained limbs) | undefined |
| **Soft / cuddly** | average | soft-full | even | low-medium | undefined |
| **Stocky / burly** | heavy | average-full | even | medium-high | faint |
| **Heavy** | heavy | heavy | central | low-medium | undefined |
| **Mass monster** | very broad | avg-full | even | very high | faint |

The "dad bod" row is the important one: it is **central adiposity with retained limb and chest muscle** — a *distribution* pattern, not merely a fat level. It cannot be expressed by any single-value list.

---

## 5. Relationship to the existing fields (no parallel system)

| Axis | Existing home | Proposal |
|---|---|---|
| Height | `HeightBuild` (free text) | keep field; back it with the §3.1/§4.1 bands |
| Frame, Silhouette, Adiposity, Distribution, Muscle mass, Definition, Regional | `BodyShape` (free text) | **keep the one `BodyShape` field as the contract**, but compose it from these named picks |
| Bust / Waist / Hip | `PhysicalAttributes` dropdowns | keep; add **shape / position / band** sub-parameters |
| Rear | `ButtSize` (**dead** — no catalog, no input) | give it the catalog + the editor input it never had; add shape + lift |
| Weight | `Weight` (free text) | **demote** — not prompt-bearing; the axes carry the signal |
| Age | `Age` (number) | keep; add age-linked *body cues* |
| Skin / BodyHair / PubicHair / Tattoos / Scars / Piercings | complete | **no change** |

The BodyCard's 7-field completeness gate is **unchanged**. "None" handling is unchanged (already fixed — absence values are omitted from the prompt, not rendered).

---

## 6. Prompt-composition rules that fall out of this

1. **One axis → at most one clause.** Two values on the same axis is a contradiction, not emphasis — reject at authoring time.
2. **Absent axis → omit.** Never substitute a default; that is the no-fallback rule applied to vocabulary.
3. **Silhouette auto-derives from the measurements when they are present** (bust/waist/hip) so the operator does not state it twice — and is stated explicitly only when measurements are absent.
4. **A named preset expands into its axis picks** and is recorded as the picks, not as the preset name — so the preset stays movable.
5. **Decision-only values never enter the prompt** ("none", "n/a") — already implemented in `RendersInPrompt`.

---

## 7. How to verify the §1 hypothesis (recommended study)

Before committing to the distribution axis as *the* fix for "too thick":

1. Fix every other axis (one character, one seed, one pose, one framing, one sampler config).
2. Render a grid of **female, average adiposity, and vary only distribution**: gynoid → even → central.
3. Blind-score the renders for waist/hip/thigh mass vs central mass.
4. If the gynoid row reads as a female figure and the central row reproduces the "too thick" complaint, the hypothesis is confirmed and the distribution axis is validated as the control.

Same design for the male side to validate the "dad bod" distribution pattern.

---

## 8. Open questions for the operator

1. Should these axes extend the existing `PhysicalAttributes` dropdowns (reused by the template editor **and** the BodyCard), or be a new body-card-specific vocabulary? *(Recommendation: extend the existing one — B-127 made the Character template the identity owner.)*
2. Should `Weight` stay in the UI at all, given it is not prompt-bearing?
3. Are the §4.10 colloquial preset names wanted as visible starting anchors, or should presets be unnamed axis combinations?
