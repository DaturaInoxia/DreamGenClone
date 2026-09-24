# 061 — B-127 P0b (rest): the link action, and Sam linked by hand

**Status:** Delivered; 235 identity/scene-asset tests green; Sam linked through the UI and his rows re-keyed; app restarted
**Date:** 2026-09-22
**Items:** B-127 P0b, closing `debug/060`'s known gap

## Report

`debug/060` left one gap: nothing in the app could *create* an identity link, and the link store existed for exactly one
character — Sam `a9137ebf…`, an asset character with no template. This step builds the action, links Sam through it, and
finishes the re-key the link unblocked.

## What changed

**The work list came from the resolver, not from the UI.** `ICharacterIdentityOwnerResolver` gained
`ListUnlinkedAsync()`: every scenario character and character asset that resolves to nothing, each carrying

- `Reason` — the resolver's **own** refusal text (the list never re-words why a character is unlinked), and
- `CanLink` — true only when writing a link row can change the outcome.

The second point is not cosmetic. A scenario character that already carries a `TemplateId` is resolved from the
**scenario**, and the resolver reads that reference before it ever consults a link row — so offering "Link character
identity" for such a character would be a fix that silently does nothing. `CanLink` is false there, and the reason says
to clear the reference in the Scenario Editor first. Two tests pin exactly that (`ListUnlinked_RefusesToOfferALinkForAScenarioCharacterThatCarriesATemplateReference`).

**`TemplatesPanel` (Profiles → Templates) is where the link is made.** For a saved `TemplateType.Character` template the
editor now shows a *Character identity* panel:

- **Characters using this template (N)** — `OwnerResolver.ListInstancesAsync(templateId)`, with each instance's kind
  badge and id, and a *Remove link* button for instances that resolve through a link row (`IdentityLinks.ListAsync`).
- **Characters with no template (N)** — `OwnerResolver.ListUnlinkedAsync()`, each row showing the resolver's refusal and
  a **Link character identity** button (`IdentityLinks.LinkAsync(instanceId, templateId, "Templates panel")`), disabled
  when `CanLink` is false.
- Every read and write goes through the resolver or the validating link service; the panel writes no ownership itself
  (`IdentityOwnershipSurfaceContractTests.TemplatesPanel_IsWhereACharacterIsLinkedToItsTemplate` asserts this, including
  that it never contains `CharacterProfileId =`).

**The Asset Manager stopped letting an unlinked character pose as an identity root.** The library-asset branch of
`SceneAssetTreeService` (the older path that bypassed the resolver entirely) now labels a character asset by the owner it
resolves to, and marks one that resolves to nothing `<name> (unlinked)`. That branch is how Sam's root was mislabelled in
`debug/060`.

## Sam, linked by hand (dev DB — backup `artifacts/tmp/db-backups/dreamgenclone.dev.db.b127-postlink`)

A Sam **character template** already existed (`4684be8f-73d4-4599-bd9b-3369eddba7d3`), so no template was created.

1. Profiles → Templates → **Sam (Character)**. The panel read *Characters using this template (0)* / *Characters with no
   template (1)*: `Sam — asset character — a9137ebfa4df43d08c3347242aaa2261`, with the refusal and a link action.
2. Clicked **Link character identity**. The panel reported
   `Linked Sam (asset character) to this template (4684be8f…).`, then listed Sam under *Characters using this template
   (1)* with a *Remove link* action, and *Characters with no template (0)* — "Every scenario character and character
   asset resolves to a character template."
3. `CharacterIdentityLinks` verified in the DB:
   `a9137ebf… | 4684be8f-73d4-4599-bd9b-3369eddba7d3 | Templates panel | 2026-09-22T16:15:20Z`.
4. Re-ran `b127-identity-rekey apply` (the documented follow-up for a new link):
   **`20 row(s) re-keyed, 28 version set(s), 0 shadowed draft(s) demoted`**. Sam's identity rows moved off the asset id
   onto the Sam template — verified: `builds(samTemplate) = 2`, `builds(samAssetLegacy) = 0`, `packs = 0` (Sam has no
   packs yet), `links = 1`. The preview had said "No unresolved owners: the apply can run."
5. `/characters/4684be8f…` now renders `Identity owner: Sam 4684be8f…` instead of a page that could only state the
   remedy. `/asset-studio` still shows three character roots — Becky `de351eb3…`, Dean `a4894571…`, Sam `a9137ebf…`.

## Verification

- **235 passed, 0 failed** (`~CharacterIdentity|~CharacterStudio|~SceneAssetTree|~IdentityOwnership|~CharacterImageIdentity|~CharacterLora|~SceneAsset`).
- New tests: four in `CharacterIdentityOwnerResolverTests` (the work list, the refusal as the reason, the
  link-cannot-help case, and end-to-end *link → resolves → leaves the list → appears in the template's instances*), one
  contract test for the panel, and `StubOwners` extended for the new resolver member.
- Browser-verified the whole loop (select Sam's template → see Sam unlinked → link → Sam becomes an instance).

## Not done here (deliberate)

- **A character *asset* still gets its own Asset Manager root** rather than being nested inside its owner's root. Its
  images hang off the asset id, and the group content check only sees packs and scenario-mapped assets, so folding it in
  would make the character vanish from the tree when the owner has no packs. Its label and its studio redirect make the
  ownership visible; restructuring the asset branch is **P2** work.
- The **physical column rename** (`CharacterProfileId` → `CharacterTemplateId`) remains **P3**, as recorded in
  `debug/059`.
