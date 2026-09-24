# 060 — B-127 P0b: owner-based identity surfaces (Asset Manager, studio, packs page)

**Status:** Landed; solution builds; 25 targeted tests green; app restarted and browser-verified
**Date:** 2026-09-22
**Items:** B-127 P0b, following `debug/059` (re-key applied)

## Report

P0a made the *data* say "identity belongs to the character template". P0b makes the *surfaces* say it, so a person
can no longer land on a page that silently shows somebody else's (or nobody's) identity.

## What changed

| Surface | Before | After |
|---|---|---|
| Asset Manager (`AssetStudio.razor` / `SceneAssetTreeService`) | character roots grouped **by display name**; `Href` = the first instance id seen | roots grouped by **resolved identity owner**; `Href` = `/characters/{templateId}`; one root per template even when two scenarios carry the same character |
| Character Studio header | nothing said whose identity the page edits | `Identity owner: <template> <id>` and, when opened from an instance, `· opened from the <kind> <instance> <id>` |
| Character Studio route | `/characters/{any id}` rendered whatever it found | `/characters/{templateId}` is canonical; an instance/legacy id **redirects** (`replace: true`) to the template |
| Character Studio errors | the remedy was rendered inside the Faces section, so a Packs-tab visitor saw nothing | page-level `alert-danger` on every tab |
| Packs page (`CharacterIdentity.razor`) | read the raw route id | resolves first; sentence now says packs belong to the character **template** |

`SceneAssetTreeService` gained `ICharacterIdentityOwnerResolver`. A character whose owner cannot be resolved is
**not dropped**: it keeps its own root (named `<instance> (unlinked)`) so nothing disappears, and the studio it
opens states the remedy. The old `charactersByName` dictionary and `characterIds[0]` shortcut are gone — that
shortcut is what made "which Dean am I looking at" unanswerable.

## Verification

- `dotnet test --filter "~SceneAssetTree|~IdentityOwnershipSurface|~CharacterStudio"` → **25 passed, 0 failed**.
- `IdentityOwnershipSurfaceContractTests` (new, 4 tests) asserts the invariants that the UI depends on: grouping is
  by owner, same-name characters stay two roots, no identity read uses the raw route id, and the studio resolves
  before it displays.
- `SceneAssetTreeServiceTests` reworked: `TemplateId`-based scenarios, `StubOwners`, and a new
  `BuildTreeAsync_TwoCharactersWithTheSameName_StayTwoRoots` (the regression the user's question exposed).
- Browser, `/asset-studio`: exactly **three** character roots — `/characters/de351eb3…` **Becky**,
  `/characters/a4894571…` **Dean**, `/characters/a9137ebf…` **Sam**. Both Becky and Dean ids were confirmed to be
  `Templates` rows of type `Character`; previously the tree emitted one root per scenario instance and merged by
  name.
- Browser, `/characters/f58f959a…` (Becky's instance): redirected to `/characters/de351eb3…` and rendered
  `Identity owner: Becky de351eb3…`.

## Known gap (the rest of P0b)

The **link action is not built yet**. `CharacterIdentityLinks` is empty, so Sam `a9137ebf…` — the one character with
no `TemplateId` and no link row — still gets a root that opens a studio page which can only state the remedy.
Sam is the reason the link store exists. Remaining work, in order:

1. **Templates panel** (`/profiles?tab=templates`, `TemplatesPanel.razor`): for a `TemplateType.Character` template,
   show its instances (`ListInstancesAsync`) and the unlinked candidates, with a **Link character identity** action
   calling `ICharacterIdentityOwnerLinkService.LinkAsync(characterId, templateId, linkedBy, replaceExisting)`.
2. Re-run the tree afterwards and confirm Sam's root becomes the Sam template root.
