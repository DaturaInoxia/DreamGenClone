# 072 — "Promoted the five accepted views…" but the pack kept the old images

**Status:** Done (376 tests green on the identity/asset/edit filter; solution builds with 0 errors)
**Date:** 2026-09-24
**Report:** "it says this 'Promoted the five accepted views to draft identity pack 2d13c667-…' but it did not replace
the images in the pack, it is still the older images."
**Follows:** `debug/069` (front chain visibility), `debug/070` (angle chain acceptance), `debug/071` (description job).

## What the records said

Pack `2d13c667-a690-4b5a-992a-93359591aa41` = **v9 · Draft · BodyComplete**, created 01:02:54, **22 assets, 5
approved**, `PromotedByBuild = 8333698f…` (the BODY build). Its face slots held **two assets each**:

| slot | carried forward, approved @01:02 | promoted @02:33 (`…build c05b2f56…`) |
|---|---|---|
| Front | `24cfe798…` 518 KB ✔ | `e1234ad0…` 872 KB |
| ThreeQuarterLeft | `a7cb66a1…` ✔ | `f4eff5b9…` 1.59 MB |
| ThreeQuarterRight | `433c8edf…` ✔ | `dabd3871…` 1.71 MB |
| ProfileLeft | `0ab91545…` ✔ | `79db04b9…` 1.75 MB |
| ProfileRight | `86b06c0b…` ✔ | `bf872fe1…` 1.75 MB |

`CanonicalFaceAssetId` was empty and the five old rows were `IsApproved = 1`, so every reader that resolves "the
approved face for this view" (pack screen, identity-conditioning resolvers) kept using the **pre-existing** image.

## The three defects

1. **The promotion appended instead of replacing.** `PromoteFaceAsync` called `UploadAssetAsync`, which always
   inserts a new row, and nothing removed the pack's existing asset for that slot. The promotion wrote into the
   character's *current draft* (v9 — `CreateDraftPackAsync` returns the existing draft), which the earlier supersede
   and body promotion had already filled with five approved face assets, so each slot doubled. The upload really
   happened; the pack simply preferred the old, approved asset thereafter.
2. **A second Promote press claimed success while writing nothing.** `PromoteAsync` returned early when the build had
   recorded `ProducedIdentityPackId`, so "Promoted the five accepted views to draft identity pack X" could be printed
   for a call that uploaded nothing at all.
3. **The descriptor snapshot nested one level deeper per approval.** The pack screen seeded its descriptor editor
   from the draft's **raw snapshot** (`{"descriptor":"…"}`) and approval wrapped the editor value in exactly that
   object, so v9's snapshot reads `{"descriptor":"{\"descriptor\":\"{…` . Supersede copies the snapshot verbatim, so
   the nesting accumulates across versions.

## Resolution

| Change | Where |
|---|---|
| `ReplaceSlotAssetAsync(packId, kind, fileName, content, faceView / bodyState+bodyView)` — writes ONE slot, deleting whatever occupied it (assets + unreferenced files), refusing a non-draft pack and a slot the caller cannot name (only `Face` and `FullBody` occupy slots). Skips the write when the slot already holds exactly these bytes, so re-promoting is not file churn. Moves a canonical pointer that named a replaced asset onto the replacement, and **seeds** `CanonicalFaceAssetId` with the promoted Front when the pack has none — the same shape the body path already used for its canonical full-body pointer | `ICharacterImageIdentityService` (+ `SceneImageReferenceSlotWrite`), `CharacterImageIdentityService` |
| Both promotion paths use it per slot and sum what they replaced; the result record carries `UploadedSlots` / `ReplacedSlots`, and the studio message states them ("…replacing 10 asset(s) that occupied those slots" / "…no slot was replaced — the pack already held these images") | `CharacterIdentityPromotionService`, `ICharacterIdentityPromotionService`, `CharacterStudio.razor` |
| The `ProducedIdentityPackId` early return is gone: promoting again is the same operation, so the slots are replaced from the build's CURRENT accepted views. A produced pack that is frozen needs no special case — creating the draft refuses with "supersede the latest approved pack…", and the body path names the missing draft | `CharacterIdentityPromotionService.PromoteAsync` |
| The descriptor editor is seeded with the descriptor TEXT, and `DescriptorText` unwraps every nesting level (bounded), so a legacy snapshot converges to plain text the next time it is approved | `CharacterIdentity.razor` |

**Consequence for the operator's v9:** pressing **Promote** again on the face build replaces each face slot — the old
approved row *and* the 02:33 row go, the accepted image takes the slot, and the canonical face is seeded to the
promoted Front — leaving 5 face + 12 body = 17 assets. The superseded v8 keeps its own copies (and its files, which
are shared), so nothing historical is lost.

## Validation

- `CharacterImageIdentityServiceTests` +3: the slot writer replaces and seeds/moves the canonical pointer; identical
  bytes are a no-op; an approved pack and an unnamed slot are refused with named reasons.
- Existing suites re-run: `CharacterImageIdentity*`, `CharacterIdentity*`, `CharacterImageIdentityRepository`,
  `SceneAssetTreeService`, `ImageEdit*` → **376 passed / 0 failed**; solution build **0 errors**.
- Test doubles updated for the new interface member (`CharacterIdentityPromotionGateTests.StubIdentity`,
  `SceneAssetTreeServiceTests.StubIdentityService`, `MediaEditHeadMeasurementServiceTests.StubWorkspaceService`).
