# 062 — Character Identity Promote phase

## Report

- **Reported:** 2026-09-15, after all four angle views were accepted.
- **Requirement:** Promote the canonical front plus four accepted angles into a draft FaceOnly identity pack with correct canonical view tags.

## Resolution

- Added `ProducedIdentityPackId` to `CharacterIdentityBuild` and persisted it.
- Added `CharacterIdentityPromotionService` with readiness validation for Front, 3/4 Left, 3/4 Right, Profile Left, and Profile Right.
- Reused `ICharacterImageIdentityService.CreateDraftPackAsync`, `UploadAssetAsync`, and provenance mechanics.
- Added view mapping to `SceneImageReferenceFaceView` for all five assets.
- Added Panel D to Character Studio with readiness summary, blocking reasons, promotion action, produced pack ID, and packs link.

## Validated

- [x] Touched-file diagnostics clean.
- [x] Web project build succeeds.
- [ ] Focused promotion tests and live promotion remain pending.
