# 066 — B-122 body card: decision records must not reach the prompt (+ P1/P2 agreed plan)

**Status:** Fix shipped and verified live (92 tests green); P1/P2 vocabularies awaiting the operator's sign-off
**Date:** 2026-09-22
**Items:** B-122 Phase 0 body card, operator report *"I am just typing random words and not having the effect I want"* / *"none for scars should not put none in the prompt"*

## Operator's request (recorded in their words)

- Better ways to modify the body card and generate images; **compare different body-card changes**, "not just start
  over and all those images are gone".
- **Canned dropdowns** for the typical body-part changes; "separate out things more"; the **prompt builder should be
  smarter** about pulling it together.
- A **full review deck and compare**.
- Controllable spectrum: short/fat → tall/skinny; model body → typical mom body → larger; male: ripped vs dad body.
- Decisions taken: **P1+P2 first** (card vocabularies + smart prompt), then P3+P4; **no negative prompts should be
  needed** (absences are simply not sent); **named card variants (snapshots) that own their images**.

## Shipped now — the "none" bug

`CharacterBodyCard.ToPromptLine()` comma-joined all seven values, so a *decision record* went into the positive prompt.
Verified in the live DB before the fix: `BodyHair=[none]`, `ScarsMarks=[none]`, and the resolved prompt contained
`… tattoo of a tree on left calve,, none, clean.` — the literal word went to the model twice.

- `ToPromptLine()` now renders only **renderable descriptors**; `CharacterBodyCard.IsDecisionOnly(value)` names the
  decision vocabulary (`none`, `n/a`, `unknown`, `not specified`, `unspecified`, `nil`, blank).
- The completeness gate is **unchanged**: an unanswered field still refuses to render, so a decision must still be
  made — it just is not drawn.
- One existing test changed because the *requirement* changed: `ToPromptLine_IsTheCanonicalCardLine_…` now expects
  `ScarsMarks="none"` to be absent from the line, and a new
  `ToPromptLine_OmitsDecisionRecords_ButStillRequiresTheDecision` pins both halves.
- **Verified live** on Becky's `Clothed Front`: `containsNone=false`; the line now reads
  `… Fair, Smooth, tattoo of a tree on left calve,, small triangle pubic hair.` (The remaining double comma is a
  trailing comma inside the operator's own Tattoos value — the P1 pickers remove that class of typo.)
- Tests **92/92** (`~CharacterBodyCard|~CharacterIdentityBody|~BodyViewsPanel|~CharacterStudio`).

## Verified facts behind the P3/P4 work (not yet built)

- Becky has **six body builds**; her images sit in **two different containers** (`338c1bd9…` 2 images, `a38a56d2…`
  1 image) and four builds have no container at all. The grid only ever shows the **newest** build's container, so
  "Start over" **orphans** earlier renders instead of deleting them: the files are on disk, invisible in the UI.
- Nothing links an image to the **card (version) that produced it**, so comparing card changes is impossible today.

## Proposed P1 vocabularies (awaiting sign-off before code)

Each field becomes a picker of option ids whose **prompt phrase** the builder renders; free text stays possible, and a
value that is not a known option id still renders literally (minus decision vocabulary).

| Field | Options (id → prompt phrase) |
|---|---|
| Height | `petite` → "petite, 5'1"", `short` → "short, 5'3"", `average` → "average height, 5'6"", `tall` → "tall, 5'9"", `very tall` → "very tall, 6'0"" |
| Build | `very slim` → "very slender, low body fat", `slim` → "slim build", `average` → "average build", `soft` → "soft, slightly rounded build", `heavy` → "heavy-set build", `very heavy` → "very heavy-set build" |
| Shape archetype | `model` → "fashion-model proportions", `athletic` → "athletic, toned figure", `average` → "ordinary figure", `mom` → "typical mother's figure, soft post-pregnancy belly", `curvy` → "curvy hourglass figure", `pear` → "pear-shaped figure", `plus` → "plus-size figure" |
| Bust | `flat`, `small`, `medium`, `full`, `very full`, `huge` → "flat chest" … "very large bust" |
| Waist | `very narrow`, `narrow`, `average`, `soft`, `thick`, `wide` |
| Hips | `narrow`, `average`, `wide`, `very wide` |
| Rear | `flat`, `small`, `average`, `rounded`, `full`, `very full` |
| Muscle (male/neutral) | `soft`, `average`, `toned`, `athletic`, `ripped` → "rifffed, clearly defined musculature" |
| Shoulders (male) | `narrow`, `average`, `broad`, `very broad` |
| Skin | `very fair`, `fair`, `olive`, `tan`, `brown`, `deep` + free-text texture |
| Body hair | `hairless` → "hairless, smooth body" (the positive form of "none"), `sparse`, `light`, `moderate`, `heavy`, + free text for pattern |
| Pubic hair | `shaved` → "cleanly shaved", `triangle` → "small trimmed triangle", `strip`, `natural`, `bushy` |
| Marks/scars | `unmarked` → "unmarked, flawless skin" (positive form of none), otherwise free text |
| Tattoos | `unmarked` → "unmarked skin", otherwise design + placement free text |

Whole-card **presets**: `Slim model`, `Average`, `Mom body`, `Curvy`, `Plus`, `Athletic`, `Ripped`, `Dad body` — each
sets height/build/archetype/bust/waist/hips/rear/muscle/shoulders in one click. **This is the "canned" lever that
actually moves "too thick".**
