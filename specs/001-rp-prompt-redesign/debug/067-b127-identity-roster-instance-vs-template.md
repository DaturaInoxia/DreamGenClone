# 067 — B-127 consumer gap: identity rosters read packs with the scenario instance id

**Reported 2026-09-24** (scene-image editor, `/roleplay/image-editor/8bc36efb…/85da7c1b…/3e9ab48a…`):

> "No scenario characters currently have an approved identity pack with approved face references. but there are
> approved identity packs"

## Root cause — the ownership rule was applied at the WRITE surfaces but not at these READ surfaces

`SceneImageProductionService.ResolveIdentityReadinessAsync` was fixed for B-127, but four identity readers still
handed the pack store a **scenario instance id**, while packs belong to the character **template**:

| Site | Call | Effect |
| --- | --- | --- |
| `SceneImageEditWorkspaceService.LoadRosterAsync` | `ImageIdentityRosterBuilder.TryBuildChoiceAsync(_identity, character.Id, …)` | every scenario character dropped → the reported message |
| `SceneAssetImageEditWorkspaceService.LoadRosterAsync` | same builder, template ids | worked by accident (a template resolves to itself) |
| `SceneImageProductionService.ResolveIdentityReadinessAsync` | `ListPacksAsync(character.CharacterId)` with the frozen Moment id | would throw "Character 'Becky' has no approved identity pack." |
| `SceneImageProductionService.ResolveCharacterIdentitySelectionsAsync` | `ListPacksAsync(group.Key)` | same, for explicit identity selections |
| `CompositionComposer.razor` (`LoadIdentityPackOptionsAsync`) | `ListPacksAsync(character.Id)` | approved packs never appeared in the composer's pack list |

Evidence from the dev DB for the reported session (scenario `135a9237…`, "Campground Intimacy"):

```
Name  | scenario instance id                    | template id (owner)                     | packs: before | after | roster faces
Becky | f58f959a-8050-4388-a219-99d2df3446a1    | de351eb3-69d3-421a-a762-79ae8ee183ed    |      0        |   1   |      5
Dean  | faee1ec0-1cf3-459e-97d2-ad59717c41ba    | a4894571-2513-4063-9e16-ef8c4f8134ed    |      0        |   1   |      5
Ken   | 55ed2a0a-e77e-4d5c-aed1-e5aea5d75345    | 51923303-fe6a-4e65-a657-bfa1b5995f71    |      0        |   0   |      0
```

The frozen Moment contracts for every production group in that session carry the **instance** ids
(`f58f959a…`, `faee1ec0…`) — so the readiness path was broken for this session too, not just the editor.

## Fix — resolve the owner at the boundary, key everything on the template

- `ImageIdentityRosterBuilder.TryBuildChoiceAsync` now takes `ICharacterIdentityOwnerResolver`, resolves the id,
  lists packs for `owner.TemplateId`, and reports the **template** id in the choice. It returns
  `(Choice, Reason)` instead of a bare null, so a listing can say *why* an owner is not bindable.
- Both rosters collect those reasons and use them as `UnavailableReason` when nothing is eligible — the resolver's
  own "link this character to a template" refusal is repeated verbatim, and the pack rules name themselves
  ("has no approved identity pack", "has multiple approved identity packs", "no approved face reference with an
  angle tag, a stored file and a checksum"). The old generic message remains only as the last resort.
- `SceneImageProductionService` gained `ICharacterIdentityOwnerResolver` (long ctor; the short test ctor passes
  null and the reads fail fast if it is ever missing). Readiness resolves each frozen id once, joins selections by
  the resolved owner, and reports the **owner id** — so the value the stage shows can be handed straight back as a
  selection and find the same pack.
- `ResolveCharacterIdentitySelectionsAsync` resolves each selection id before reading packs.
- `CompositionComposer.razor` resolves through the injected `ICharacterIdentityOwnerResolver`.

## Guards

- `SceneImageIdentityReadinessTests.ResolveIdentityReadiness_ResolvesAScenarioCharacterToTheTemplateThatOwnsItsPack`
  — the production shape (instance in the frozen cast, pack under the template) plus the selection round trip.
- `IdentityOwnershipSurfaceContractTests.IdentityRosters_ResolveTheOwnerBeforeReadingPacks` — source contract over
  all five sites, asserting the resolver is used and the raw-id reads are gone.

**Verified:** 724/727 targeted (3 pre-existing `SdxlSceneImagePromptBuilderTests`), web build 0 errors.
