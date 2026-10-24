# 080 — The long-form compile contract forces the compiler to invent what the references supply

**Reported:** 2026-10-18
**Surface:** Composition Composer → User Intent prompt source, four references bound
**Render:** `SceneImage f74058d3-8d41-408a-99aa-8b0b6bd00ce3`
**Prompt record:** `ec7ac081-f411-4a41-9003-f52a37893f6d`
**Reference render (same bindings, 3 min earlier, came out right):** `b91598c2-59da-46b4-8670-9a49f6de3209` / `27b5adee-1b95-4ad8-b5eb-ac6042265ca8`

## Report

Operator: *"it was working, not sure what happened f74058d3 wrong everything in this case."*

Same production group, same code, **byte-identical bindings** (verified by diffing `AppliedReferenceBindingsJson`
between `b91598c2` and `f74058d3` — Body `Profile Left · Unclothed`, Wardrobe `Jean Shorts`, Location `Clothesline`,
Prop `Laundry Basket`, ordinals 1–4 in both). Outcome:

| Element | `b91598c2` (03:55) | `f74058d3` (03:58) |
|---|---|---|
| Clothing | white tank top + shorts ✅ | topless ❌ |
| Prop | white lattice basket ✅ | woven rattan basket ❌ |
| Location | porch, flowerbed, rotary clothesline ✅ | invented wooden fence + two posts + lawn ❌ |

This was NOT a regression of the prop feature: the prop slot worked — the correct basket appeared in `b91598c2`.

## Analysis

The previous fix (records 078/079) was confirmed **present** in both compiler inputs: the `SceneImagePromptProjected`
debug events for both prompts carry `REFERENCE-SUPPLIED ELEMENTS`, `These are WORN` and `OMIT CONTRADICTIONS` at
byte-identical offsets. So the notice was not missing and the app was current.

The compiled prose itself invented all three elements:

- `ec7ac081`: *"a clothesline stretches across the yard, its posts and taut lines … with a fence and open ground behind
  it"*, *"dressed in blue denim jean shorts and a top"*, *"a low woven plastic basket with a rounded rim"*.
- `27b5adee`: *"the clothesline crosses the upper band of the frame above her"*, *"blue denim cut-off jean shorts with
  frayed hems"*, *"a rigid plastic basket with a lattice of oval holes"*.

**Root cause — two instructions that cannot both be obeyed.** `SceneImageCompilerSystemPrompts.Qwen21Canonical` (the
contract the intent path compiles under) requires:

> "Convert … into **ONE long, descriptive** natural-language image prompt … a **full, concrete, present-tense account of
> the finished picture**" and "14. Keep the whole prompt **between 300 and 1600 characters**".

That contractually obliges the compiler to write the room, the outfit and the objects — exactly the elements the bound
images supply — while the ownership notice tells it not to. The dominant contract wins, and the model invents. Which
invention it produces varies run to run, which is why the same inputs gave a good render and then a broken one.

## Plan

Give a reference-bound User Intent draft its own compile contract: action only, everything a reference owns removed.

1. `SceneImageCompilerSystemPrompts.Qwen21CanonicalActionOnly` — 40–120 words; action, pose, expression, interaction
   and camera moment only; explicit `DO NOT DESCRIBE THE SETTING / CLOTHING OR NUDITY / ANY OBJECT / APPEARANCE`;
   no long-form length floor, no reference tags in the prose.
2. `NaturalLanguageSceneImagePromptBuilder.BuildIntentSystemPrompt(bool referencesBound)` — virtual, defaulting to
   `BuildCanonicalSystemPrompt()` so every other family is byte-identical; Qwen-Image-2.1 overrides it.
3. `BuildIntentMessages` derives the supplied-element set ONCE and uses it for both the ownership notice and the
   contract choice, so the two cannot disagree. "Bound" means an image actually arrives — a text-only binding
   supplies nothing and keeps the long-form contract, since no image could replace the description it would lose.

Blast radius: `PromptSource == UserIntent` **and** at least one image-supplying binding. Scene drafts, text-only
intents and all other surfaces are unchanged.

## Resolution

- `DreamGenClone.Domain/RolePlay/SceneImageCompilerSystemPrompts.cs` — added `Qwen21CanonicalActionOnly`.
- `DreamGenClone.Web/Application/RolePlay/NaturalLanguageSceneImagePromptBuilder.cs` — `BuildIntentSystemPrompt`,
  single derivation of `suppliedElements`, notice signature now takes the derived list.
- `DreamGenClone.Web/Application/RolePlay/QwenSceneImagePromptBuilder.cs` — override selecting the action-only
  contract when references are bound.

Tests (`IntentPromptReferenceOwnershipTests`):
- `SystemPrompt_WithReferencesBound_UsesTheActionOnlyContract` — asserts the action-only markers AND that the
  long-form floor ("between 300 and 1600 characters", "400-500 words") is GONE, since its length floor alone would
  re-require the description.
- `SystemPrompt_WithNoReferences_KeepsTheLongFormContract`.
- `SystemPrompt_WithOnlyATextOnlyBinding_KeepsTheLongFormContract`.

Build: 0 errors. Suite: 4414 passed / 27 pre-existing `CharacterLoraCoverage*` failures / 0 skipped.

## Validated

- [ ] pending — requires a republish, **Generate Prompt** (to recompile under the new contract), then **Generate
  Image**. Watch for: no sentence describing the yard, no garment named, no object described — and all three
  references landing.
