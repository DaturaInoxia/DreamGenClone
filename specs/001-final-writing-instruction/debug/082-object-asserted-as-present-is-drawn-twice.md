# 082 — Direction prose naming an existing fixture made the renderer draw a second one

**Reported:** 2026-10-18
**Surface:** Composition Composer → User Intent, four references (app-image location)
**Render:** `SceneImage 87454bef-1d6e-4ecc-ab5d-2397650051a5`
**Prompt record:** `9438aec3-8cb2-4758-b0ed-401f95ef7fa7` (548 chars — the action-only contract was live)
**Operator question:** *"it added another clothesline, it is like it does not know there is a clothesline there already, should user intent be more specific?"*

## Report

The reference fix worked: the Studio render bound as an **AppImage** location reference
(`referenceImageId f107a8cd-776-4121-b06e-3fa6d274a175`) survived planning and reached the render, and the picture
matched every reference — white tank top, denim shorts, white lattice basket, porch with flowerbed and rotary
clothesline.

One defect: a **second, taut clothesline** was strung across the top-right of the frame, separate from the rotary
dryer already present in the location plate.

## Analysis

The final positive carried these sentences (from the action-only draft):

- *"both arms reach up and to the right to pin a shirt **along the line**"*
- *"The frame catches the instant both hands are up **at the line**"*
- *"**A laundry basket sits low beside her right calf**, within easy reach."*

The third sentence breaks the contract's own rule 3 ("DO NOT DESCRIBE ANY OBJECT") outright: it is a **placement
claim** about an object the object reference already supplies.

All three share the mechanism. A noun asserted as present is an instruction to DRAW that noun; the renderer does not
infer "the line the reference already shows". So it built a line for her hands to reach — beside the rotary line that
came from the plate. The same mechanism explains the basket sentence: it happened to match the prop reference, but it
was redundant and could have produced a second basket.

**Not a user-intent specificity problem.** The intent was accurate. The leak is in the compile contract: it forbids
*describing* objects but said nothing about *asserting their presence*, and nothing about sentences whose subject is
not the person.

## Plan

Two rules appended to `Qwen21CanonicalActionOnly`:

- **EVERY SENTENCE IS ABOUT THE PERSON** — no sentence whose subject is an object or part of the setting ("a basket
  sits…", "the line runs…", "a fence stands…"), because a thing stated as present is a thing drawn.
- **A FIXTURE THE ACTION USES IS ALREADY IN THE PICTURE** — when her hands touch something a reference shows, name it
  only inside the verb phrase that says what the hands do ("pegging a shirt onto the line"), never as a new thing,
  never with a position, material, colour or size, and never "another / a second / a new" one.

## Resolution

- `DreamGenClone.Domain/RolePlay/SceneImageCompilerSystemPrompts.cs` — `Qwen21CanonicalActionOnly` rules renumbered
  5–13 with the two new rules; both cite the measured case.
- `DreamGenClone.Tests/RolePlay/IntentPromptReferenceOwnershipTests.cs` — the action-only contract test now asserts
  both new rules are present, so a future edit cannot quietly drop them.

Build: 0 errors. Suite: 4415 passed / 27 pre-existing `CharacterLoraCoverage*` failures / 0 skipped.

## Validated

- [ ] pending — republish, **Generate Prompt**, **Generate Image**. The direction should contain no sentence whose
  subject is an object, and only one clothesline should appear.
