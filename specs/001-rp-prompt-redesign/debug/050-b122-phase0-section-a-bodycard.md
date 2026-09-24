# 050 — B-122 Phase 0 Section A: the BodyCard and the body target's seeded configuration

**Status:** Done (14 focused tests green; schema + seeds verified live in the dev DB)
**Date:** 2026-09-21
**Items:** B-122 B122-001, 002, 003, 004 — the first section of Phase 0, after B122-000a–d

## Report

B-122 needs a canonical body description before it can build anything: the capture list is explicit that
"vague 'a few tattoos' will not train consistently", and that the invariant body features bind to the trigger
token and are never captioned. Phase 0 previously had none of it — `BodyCard` existed **nowhere** in code
(only the placeholder line in `CharacterStudio.razor:542`), the `Body` target kind had no step plan, no handler
keys and no templates, and `ReferenceWorkflowSettings` had no body model.

## Decision (B122-000c, user-chosen)

**A new dedicated, character-owned typed BodyCard** — table `CharacterBodyCards`, one row per character, plus
`ICharacterBodyCardRepository`. Alternatives rejected: typing the pack's opaque `DescriptorSnapshotJson` (the
card would be per-pack, and could not exist before a pack), and reviving `CharacterBodyProfileVersion` (right
shape, but production-dead with a different purpose — a generic appearance-version snapshot). Neither of the
other two stores is written by Phase 0, so there is exactly one mutable body source.

## Resolution

- **Domain** (`CharacterBodyCardModels.cs`): `CharacterBodyCard` with the capture list's seven fields in its
  order — body shape, height/build, skin, body hair, tattoos (design + exact placement), scars/marks,
  grooming — plus `CharacterBodyCardFields` (label + `RequiresDecision` per field; body hair, tattoos and
  grooming are the `[DECIDE]` items), `UnresolvedFields` / `UnresolvedDecisions` / `IsReady`,
  `RequireReadyForGeneration()` (fails fast naming every unanswered field and marking the decisions) and
  `ToPromptLine()` (the canonical line pasted verbatim into body prompts, refusing to render while incomplete).
  `CharacterBodyWorkflowKeys` holds the eight body template keys.
- **Persistence** (`ICharacterBodyCardRepository`, `CharacterBodyCardRepository`): table
  `CharacterBodyCards` (PK `CharacterProfileId`), created at startup like the other reference stores.
  `SaveAsync(card, expectedVersion)` is **optimistic**: `0` creates version 1, otherwise the UPDATE carries
  `WHERE Version = $expectedVersion`, and zero rows affected throws naming both the stored and expected
  versions rather than overwriting another editor's work. A create against an existing card is refused
  (PK violation surfaced as a clear message); an update with no card is refused rather than silently creating
  one. The error path re-reads the card to say what the stored version actually is.
- **Templates + settings** (`ImageWorkflowRepository`, the ONE store): eight body keys seeded under the
  `identity.body.*` namespace — `clothed.acquire`, `unclothed.acquire`, four canonical angle keys,
  `extended.rotation`, `normalize` — with the card line as the `{BodyCard}` placeholder. New nullable
  `ReferenceWorkflowSettings.BodyModelId` (CREATE + ALTER-if-missing guard + SELECT + INSERT/UPDATE +
  `ReadSettings` ordinal 17), deliberately **unset by the seed** so a body action that resolves it fails fast
  naming the setting instead of reusing a face model.
- **Registration**: `ICharacterBodyCardRepository` in `Program.cs`, with `EnsureSchemaAsync()` in the startup
  block (the dev DB gains the table on boot — verified).

## Evidence

- Tests: `CharacterBodyCardTests` (14) — canonical field order; the three `[DECIDE]` items; the fail-fast
  message naming each unanswered field and marking the decisions; the exact canonical prompt line; the refusal
  to render while incomplete; create → version 1 → 2 with every field round-tripped; stale version refused with
  both versions named and the stored card untouched; duplicate create refused; update-without-card refused;
  unknown character returns null; all eight body templates seeded, resolvable, seed-equals-body; character
  override + reset behave like every other key; body keys coexist with the face keys.
  Run: `CharacterIdentity|CharacterStudio|ImageWorkflow|CharacterBodyCard` → **116 passed / 0 failed**.
- Live (dev DB, app restarted in Development on `data/dreamgenclone.dev.db`): `CharacterBodyCards` table = 1,
  `BodyModelId` column = 1, body template keys = 8, `GlobalBodyModelId` = NULL.

## Next (Section B of Phase 0)

Register the `Body` target kind: seed its step plan (`select/acquire base → validate base → create one
requested view → validate view → promote`), add its handler keys, and plumb the body view axis
(`SceneImageReferenceBodyView`) through the per-view acquisition path — the tasks require one request per view,
explicit `BodyState`, and no sweep. The face handlers' own key/asset-type parameterisation lands there too.
