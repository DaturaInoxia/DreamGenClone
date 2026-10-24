# 088 — "Load with reference image" bound TWO location references

**Session:** 2026-10-19 · **State:** implemented, build green, full suite 4446 passed / 27 pre-existing `CharacterLora*` failures
**Scope:** the image-card "use as reference" command and how it reaches the step's `Location` slot.
**Related:** 086 (prompt compiler refusal), 087 (the location plate that was a picture of her), B-157 (reference-role assembly).

---

## Report

Operator:

> "when the load with reference image command is performed there should only be 1 location reference loaded and that is
> the one selected for the command, the current process is loading 2."

---

## Analysis

The command is the image card's bookmark-plus action — *"reload what made this image **and** bind this image itself as the
composition's Location reference"*. It runs in two halves, in this order:

1. `UseCompositionImageAsReferenceAsync` → `RestoreCompositionInputsAsync` sets `_referenceBindings` to the source image's
   **recorded** bindings (`AppliedReferenceBindingsJson`), which include the place that image was rendered in.
2. It then sets `_requestedLocationReference`, and the composer applies it through `BindSourceAsync`.

`BindSourceAsync` removed the slot's existing assignments only when the slot does **not** declare `AllowsMultiple`, and
the `Location` slot declares exactly that (`ImageStepBlueprintFactory.ForPackIdentityComposition`, and the asset-creator
blueprint too — several views of one place are several references). So step 2 **added** beside step 1 and the step ended
up anchored to the old place *and* the picked image.

Two location bindings on one render are not a bug in themselves — `62a91af7` / `29a3190e` (2026-10-17) legitimately
carry two views of one location asset — which is why the fix is about the COMMAND's meaning, not about the slot's
capability.

---

## Resolution

One decision, in the planner, for every surface:

- `ReferenceSlotPlanner.AssignmentsFor(current, slot, source, asSoleReference)` — the single place that answers
  replace-vs-add: replace when the slot holds one reference **or** the caller names the slot's one reference; add only
  when the slot declares `AllowsMultiple` and the operator is adding another view.
- `ImageStepComposer.BindSourceAsync` takes `asSoleReference` and delegates the decision to the planner (it no longer
  carries its own `RemoveAll`).
- The `RequestedLocationReference` path passes `asSoleReference: true`. Every operator picker is unchanged.

Capability preserved: binding a second view of the place by hand still produces two references (pinned by test).

**Tests:** new `UseAsReferenceSoleLocationTests` — 4 tests: sole bind yields exactly one Location binding naming the
picked image (while the restored non-location references survive), the ordinary multi add still yields two, a
single-reference slot keeps replacing without the flag, and a source contract test that the composer applies the host
request as the sole reference and that nothing else binds that way.

---

## Verification

- Targeted: `UseAsReferenceSoleLocationTests` 4/4; the image-step/composition/reference-slot groups 271/271.
- Full suite: 4446 passed, 27 failed — the pre-existing `CharacterLora*` coverage-matrix failures only, unchanged by
  this work.

## Files

- `DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceSlotPlanner.cs` — `AssignmentsFor`.
- `DreamGenClone.Web/Components/Shared/ImageStepComposer.razor` — `BindSourceAsync(asSoleReference)`, request path.
- `DreamGenClone.Web/Application/RolePlay/ImageStep/SceneImageLocationReferenceRequest.cs` — the request means *the*
  reference of its slot.
- `DreamGenClone.Tests/RolePlay/UseAsReferenceSoleLocationTests.cs` — new.
