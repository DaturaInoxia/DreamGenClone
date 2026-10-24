# 083 — Location reference ignored: nothing early in the prompt placed her in the scene

**Reported:** 2026-10-18
**Render:** `SceneImage 1cc55900-4d6e-4f5b-9cd6-586aadd543bf`
**Prompt record:** `6e31754e-35bb-48ba-a45b-4ca43c350bbb` (613 chars)
**Operator question:** *"location reference did not take on this one what did i do wrong."*

## Report

The render came back on a **plain grey studio backdrop** — the identity body plate's background — with the bound
location image ignored. Nothing the operator did wrong: the location resolved, was sent, and was named in the block.

## Analysis

From the submit event: `referenceCount: 3`, references `["approved identity build for p0 (Front · Unclothed)",
"wardrobe continuity (Jean Shorts)", "location continuity"]`, and the block said
`<image3> (location continuity) is the LOCATION reference: this image IS the room.` The binding was a valid AppImage
(`referenceImageId ae459ed2…`, checksum present) that survived planning.

The defect is one I introduced with the action-only contract (record 080). That contract **forbids the compiler from
describing the setting**, which is right — the location image owns it. But it left the finished prompt with **613
characters of pure body action and no environment at all**, and the identity reference is a full-figure studio shot,
which this model reproduces wholesale. With nothing early in the prompt placing her anywhere, the person plate's own
backdrop won.

The two renders that worked (`b91598c2`, `87454bef`) both happened to mention the surroundings in their prose — the very
leak record 082 removed. So the anchor was being carried by accident, by the thing we had to stop.

## Plan

Write the anchor deterministically instead of hoping the compiler writes one, at the single owner of the final prompt
text (`ReferenceRoleClauses.AppendToImagePrompt`):

- When the surface is a GENERATION and a Location role is present, PREPEND one sentence naming that role's own tag:
  *"The setting of this picture is the place shown in `<image3>`: that place supplies the background of the frame, and
  no person reference supplies the background."* No invented detail — just the assignment to the tag the block already
  numbers, using the same `TagFor` numbering the block uses.
- An EDIT gets no anchor: its source image already carries the setting, and the sentence would contradict the frame
  being edited.

## Resolution

- `DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceRoleClauses.cs` — `AppendToImagePrompt` prepends the
  anchor via new `LocationAnchorFor`; generation-only (`leadingImages == LeadingImageCount.Generate`), tag computed with
  the same `TagFor` the block uses.
- `DreamGenClone.Tests/RolePlay/ReferenceRoleClauseTests.cs` — three new tests: the anchor exists, is FIRST (before
  the prose and the block), and names `<image2>` for the second image; no anchor without a location role; no anchor on
  an edit. The existing `AppendToImagePrompt_AddsTheBlockWithoutDiscardingThePrompt` updated from `StartsWith(prompt)`
  to `Contains(prompt)` + `StartsWith(anchor)`.

Build: 0 errors. Suite: 4418 passed / 27 pre-existing `CharacterLoraCoverage*` failures / 0 skipped.

## Validated

- [ ] pending — renders from an ALREADY COMPILED prompt pick up the anchor at render time, so no new Generate Prompt is
  needed: just Generate Image again.
