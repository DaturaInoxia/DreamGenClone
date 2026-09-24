# 062 — B-122: the body card prefills from the character template

**Status:** Delivered and verified end-to-end on Becky's card; 69 affected tests green (85 in the wider class set)
**Date:** 2026-09-22
**Items:** B-122 Phase 0 (body card acquisition), following `debug/056` (body card + Body tab)

## Report

Once B-127 made the studio's identity key the character TEMPLATE, the body card could finally read the character it
belongs to. This step adds both prefill sources, and makes the template the single capture surface for the body
invariants the card asks for.

Operator decisions taken before coding: **both** sources (structured + model draft), **only empty fields** are ever
filled, and the two missing attributes (**BodyHair**, **Grooming**) move onto the template.

## What changed

**1. Template as the single capture surface**
- `PhysicalAttributes` gained **`BodyHair`** and **`Grooming`** (stored in the template's JSON payload — no migration).
- `PhysicalAttributesEditor.razor` gained both inputs.
- The **four hand-written `PhysicalAttributes` copies are gone**: `PhysicalAttributes.Clone()` is now the one
  implementation, and the four editors (`PhysicalAttributesEditor`, `Templates`, `TemplatesPanel`, `ScenarioEditor`)
  delegate to it. They had already been silently dropping `ButtSize` and `DefaultClothing`, which is exactly the bug
  class a reflection round-trip test now prevents (`Clone_CarriesEveryPhysicalAttribute`).
- `PhysicalAttributesFormatter` renders both fields in the roleplay block, the image-appearance block and the
  composition body block (body hair and grooming are renderable on an unclothed frame, so they belong to appearance,
  not to style).

**2. Deterministic prefill — `ICharacterBodyCardPrefillService.FromCharacterTemplateAsync`**
Composes each field from **named** attributes and reports which ones it used:
`BodyShape ← BodyType + Bust/Waist/Hip/Butt`, `HeightBuild ← Height + Weight`, `Skin ← SkinTone + SkinTexture`,
`BodyHair`, `Tattoos`, `ScarsMarks ← DistinguishingMarks`, `Grooming`. Tattoos carry the caveat that the template
records the design while the card also needs the placement — stated, never guessed.

**3. Model draft — `DraftFromDescriptionAsync`**
Reads the template's `Content` through the new **`RolePlayCharacterBodyCardDraft`** function, resolved by
`CharacterBodyCardDraftModelResolver` (own Model Manager entry; fails fast naming `/model-manager`). The prompt states
the contract: use only what the description says, return **null** for anything it does not, and never answer
"none"/"unknown"/"n/a" — and the parser refuses a non-answer anyway, so an invented "none" cannot reach the card.
Anything the description does not state comes back as a **gap**.

**4. Nothing writes but the operator**
- A new capability interface, `ISynchronousStructuredTextCompletionClient`, was added rather than widening the
  scene-beat one: that envelope is queue-shaped (lease, poll interval, retry schedule), and a synchronous,
  operator-triggered call must not invent those. The shared HTTP core in `OpenAiStructuredTextCompletionClient` is
  factored into one private method, so **no existing caller or test stub changed**.
- `CharacterBodyCard.TryApplyPrefill(field, value)` writes only when the field is unanswered — enforced in the Domain,
  not in the UI, and `CharacterBodyCardFields` now carries the field setter so the mapping lives in one place.
- The studio shows proposals with provenance, per-field Apply, "Fill the empty fields", and the gaps; **Save stays the
  only writer**, and a source test asserts the prefill handlers never call it.

## Verification (browser, Becky `de351eb3…`, app on `dreamgenclone.dev.db`, Development)

1. **Prefill from character** → `Proposed 4 field(s) from the character template's physical attributes`:
   `Curvy, bust Full, waist Soft, hips Wide` / `5'8", 170 lbs` / `Fair, Smooth` /
   `Few on arms and legs.` → with the placement caveat in the provenance; 3 gaps (body hair, scars/marks, grooming),
   and **the input fields stayed empty** until Apply.
2. **Fill the empty fields** → `Applied 4 proposed field(s). Save the card to keep them.` The four proposal buttons
   flipped to a disabled **Already set** — the never-overwrite rule visible in the UI.
3. **Draft from description** → the model (`z-ai/glm-4.7`) returned 4 drafted fields
   (`Curvy, full bust, soft waist, wide hips`, `5'8", 170 lbs`, `Fair, smooth, Caucasian`, `Few on arms and legs`)
   tagged `drafted from the template description by z-ai/glm-4.7`, all refused as **Already set**, plus 3 gaps.
4. Card saved; on reload the card reads back at **v2** with the prefilled values and the status line
   `Incomplete: Scars and marks, Grooming [DECIDE]` — i.e. the card still refuses to look complete while a decision
   is open.

Tests: **69/69** in `~CharacterBodyCard|~PhysicalAttributes|~FunctionDefault|~CharacterStudio`, **85/85** in the wider
set including `~SceneBeatAnalyzer|~StructuredText`. The wider `~Template|~ModelManager|~SceneImagePrompt` sweep was
**433/436** — the 3 failures are the pre-existing `SdxlSceneImagePromptBuilderTests` ones recorded on 2026-09-11
(another workstream), unrelated to this change.

## Findings the operator should know

- **The DeepSeek assignment failed on model identity.** Pointing the new function at the same registered model the
  scene-beat analyzer uses (`deepseek-v4-flash`) produced
  `The structured text provider returned an unexpected model identity.` — the client requires the provider's response
  `model` field to equal the registered `ModelIdentifier`, and DeepSeek returned something else. That is a **shared
  configuration issue on that model row, not a prefill bug**, and it would affect the beat analyzer too. The draft
  function is currently assigned to **`OP-glm-4.7`** (JsonObject mode), which works — change it in Model Manager
  whenever you prefer.
- A racing double save of a brand-new card surfaces the repository's create guard verbatim
  (`…already has a body card. Re-read it and save with its current version…`). The guard itself is correct; it is worth
  a look only if it can be triggered by a single click.
