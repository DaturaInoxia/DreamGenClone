# 087 — "She was meant to face away; only her mid torso reversed": the preservation clause beat the instruction, and the location plate was a picture of her

**Session:** 2026-10-18 · **State:** implemented, build green, targeted suite 158 passed
**Scope:** the reference-role block appended to every reference-conditioned image prompt — its authority relative to the instruction.
**Related:** B-157 (reference-role prompt assembly), CASE-25 (reference naming), 086 (the prompt compiler this ran through).

---

## Report

Operator, on SceneImage `f6bab9b3` (Edit, 18:57), with a **Back · Unclothed** body plate bound:

> "i wanted her fully facing the other way, her back naked image was provided, **only her mid torso of her body was reversed**"

---

## Analysis

### The compiled instruction was correct

`SceneImageEditCompilationAttempts.6c4b6267` (Ready), raw intent *"she is facing away from the camera her back and ass
facing the camera her face can not be seen…"*, compiled prompt:

> "Turn the woman in the center so she faces away from the camera. Her shorts should be pulled down to mid thigh,
> revealing her bare ass. Hold the setting and subject's identity at input fidelity. Change nothing else."

`requestedChanges` even carried *"Her face should not be visible."* The edit was a whole-frame edit — no mask, no
outpaint (`RegionJson`/`OutpaintJson` null) — so nothing confined it to the torso.

### What actually fought it

1. **The last clause the model read was absolute.** The reference block is appended *after* the instruction, and it
   ended with *"Keep each person's face, hair, skin tone, build and clothing **exactly** as the reference image that
   carries them shows it."* The earlier preservation sentence in the instruction has an escape ("…unless the instruction
   explicitly requests that specific change"); this one did not. "Keep her face exactly" and "her face can not be seen"
   are direct contradictions, and the later clause wins — measured as a front-facing head and chest on rotated hips.

2. **The "location" reference was a picture of her.** `<image3>` was `7178f32d` — a **full scene render of the same
   woman in a white tank top, facing the camera**. The location clause says *"this image IS the room… reproduce its
   structure, surfaces, objects and its own lighting"*, but the model also reproduces the person and her clothes from
   it. That is where the **white tank top came back** (the source is topless) and why the upper body stayed front-on.
   A location plate has to be a plate of the place, not a render of the character in it.

3. **A 180° turn is at the edge of Qwen-Image-2.1's ability.** With a front-facing source and a back-facing body
   reference, the editor composites rather than rotates. The `headView` vocabulary (`front`, `three_quarter_left/right`,
   `profile_left/right`) has no `back` token either, so the compiler could not even record a rear view for the target.

---

## Resolution

The blanket preservation clause now **defers to the instruction**, in both places it is emitted (numbered multi-reference
block and the untagged single-reference block) via one shared constant:

> "Where the instruction explicitly changes that person's view, pose, stance or clothing, follow the instruction for
> that change; everything it does not name stays exactly as the reference shows it."

Identity preservation is unchanged wherever the instruction is silent — the clause still names the categories and
supplies no values (Qwen's own multi-reference example; B-157).

**Tests:** `ReferenceRoleClauseTests` now pins the precedence sentence in the plain-person and wardrobe variants.

---

## What the operator can change (no code)

- **Generate instead of rotating.** Bind the **Back · Unclothed** plate in a *User Intent* generation: the model draws a
  back view natively instead of being asked to turn a front view around.
- **Bind a clean location plate** (no person in it) when editing; a scene render of the subject as the "location"
  re-supplies her clothing and pose.
- **One change per pass:** turn her first, then pull the shorts down.

## Left open

- `headView` has no `back` token. Recording a rear-facing subject is currently impossible; adding one means auditing the
  identity-angle matching that consumes it, so it is deliberately not bundled here.
- The app cannot tell that a location reference contains the subject. A validator for that is possible (person/subject
  detection on the location slot) but is a separate change.

## Files

- `DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceRoleClauses.cs` — `InstructionPrecedence`, applied to both
  preservation clauses.
- `DreamGenClone.Tests/RolePlay/ReferenceRoleClauseTests.cs` — the precedence assertions.
