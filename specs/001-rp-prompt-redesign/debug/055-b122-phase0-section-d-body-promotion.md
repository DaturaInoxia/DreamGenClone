# 055 — B-122 Phase 0 Section D: promotion to a `BodyComplete` pack

**Status:** Done (301 tests green on the identity/reference/scene-asset filter; app restarted on the dev DB, HTTP 200)
**Date:** 2026-09-22
**Items:** B-122 B122-015 … B122-018 (Section D), following `debug/054` (Section C)

## Report

`CharacterImageIdentityRepository.ValidateApprovalSet` has implemented the whole `BodyComplete` contract since
B-124 — the 5×2 body matrix, the canonical-pointer presence check, and "must be the approved unclothed `Front`".
It was **dead code** for three reasons, all of which had to be closed together:

1. `CreateDraftPackAsync(characterProfileId)` took no scope, so every pack was born `FaceOnly` from the *domain
   default* (the outstanding B121-031 note), and nothing could raise it.
2. Nothing could set `CanonicalFullBodyAssetId`; `ApproveAsync` writes only status/descriptor/canonical-face, so
   both scope and pointer have to be on the **draft** before approval.
3. The promotion path was face-only. A body build holds no face artifacts, yet a `BodyComplete` pack's face half
   is unconditional, so the five face slots can only come from the pack the face build produced.

## Resolution

**Explicit scope, one widening call.** `CreateDraftPackAsync(profileId, scope)` now takes a required scope — no
default — and never narrows a draft; asking for a wider scope than the existing draft has is refused, naming the
widening call. `SetDraftPackScopeAsync(packId, scope, canonicalFullBodyAssetId)` is that single widening path: it
accepts **drafts only** (an approved pack is refused, told to supersede instead), refuses to narrow, refuses a
canonical pointer that is not a `FullBody` unclothed `Front` asset **of that pack**, and refuses a `FaceOnly`
scope carrying a pointer at all. All five face-flow call sites (`CharacterIdentityPromotionService`,
`ReferenceBootstrapService` ×2, `SceneAssetProfilePackJobHandler`, `CharacterIdentity.razor`) now state `FaceOnly`
explicitly.

**Promotion, dispatched by target kind** (`CharacterIdentityTargetKind`), still the ONE promotion service
(B122-015). For a body build:

- **Readiness** = the ten canonical body slots from this build's accepted views (an extended view is never a
  pack slot) **plus the face half of the target draft pack**, reported as "no face reference for this slot" and
  "present but not approved" as two different reasons, **plus** a stored canonical pointer that does not resolve
  to this pack's unclothed `Front`. Every reason names the slot (`Clothed profile right: its status is Complete,
  not Accepted. Its findings are tattoos and marks (design and exact placement) (not reviewed).`).
- **Promotion** uploads the ten slots as `FullBody` assets with their own `BodyState` **and** `BodyView` tag,
  records provenance, then raises the draft to `BodyComplete` with the uploaded unclothed `Front` as the
  canonical pointer, and records the produced pack on the build.
- The body plan's remaining steps (`Validate`, `Angles`, `ValidateView`, `Promote`) are completed by the
  promotion, because it is what finishes them: every slot that reached it was accepted (the Section C gate is
  the per-view validation), and the records name the artifacts the pipeline actually produced (a validation step
  records its artifact in and out, exactly as the face pipeline already does).

**Deliberate asymmetries, both statement-confirmed:**

- The face half is checked *and* must be approved, the body half is not: the face slots are a **precondition** of
  the promotion (they must already exist and be curated), while the body slots are its **product** — their
  approval is the explicit per-asset step before the pack is approved, and the pack's own gate names all ten when
  they are missing.
- The sharpness floor is **not** re-measured for body views: the body pipeline's evidence is the accepted view,
  and re-measuring would be a second gate the body flow never promised.

## Two defects this section found and fixed

1. **The body plan's `Front` step could only ever complete once, but the body has two bases.** Accepting the
   *unclothed* front re-completed the (already complete) `Front` step → `Step Front is out of order; the current
   step is Validate`, which blocked every full 10-slot run and therefore every promotion. The seeded step carries
   the **clothed** acquisition template, so `AcceptAsync` now completes it for the clothed front only; the
   unclothed front is angle work like every other view (it stays a base for its own state's derivation).
2. **`SupersedeAsync` carried the canonical full-body pointer into the new draft unchanged** — pointing at an
   asset that belongs to the pack being superseded, so the superseding draft could *never* be approved
   (`the canonical full-body asset must belong to the pack being approved`). Supersede now repoints it at the
   copy (same immutable file, new id) and **refuses** to supersede a pack whose pointer names an asset it does
   not own, so a broken pointer cannot be carried forward silently. Audited the dev DB: no existing pack has a
   pointer, so nothing was affected.

## Validation

- `CharacterIdentityBodyPromotionTests` (new, 8 tests) runs against the **real** pack store, identity service and
  approval contract, so `ValidateApprovalSet` genuinely decides:
  - promote → 10 `FullBody` assets with the right state/view tags, scope `BodyComplete`, canonical pointer = the
    uploaded unclothed `Front`, then the pack **approves** and the body build reaches `Complete`/`Promote`;
  - the ten slots named individually when never started; the face half named as missing vs not-approved (the only
    two reasons once the body half is done); no draft → told to promote the face build or supersede; an
    unaccepted view's **findings** quoted in the reason; a refusal writing nothing (scope, pointer and asset
    count unchanged); a bad canonical pointer refused at approval; supersede preserving the prior approved pack
    (15 assets, status `Superseded`) while the new draft carries the body set and approves without re-promoting.
- `CharacterImageIdentityRepositoryTests` +2: the pointer follows the copy across supersede, and a pointer that
  is not in the pack is refused without retiring the pack.
- `CharacterImageIdentityServiceTests` +6: explicit scope persisted; a draft cannot be raised by creating it
  again (names `SetDraftPackScopeAsync`); raising records the canonical unclothed front; an approved pack is
  refused (names supersede); a non-unclothed-`Front` pointer is refused; narrowing is refused.
- `CharacterIdentityPromotionGateTests` +1 assertion: a face build creates its draft with `FaceOnly`.
- Runs: identity/reference/scene-asset filter → **301 passed / 0 failed**; the body filter → 83; app restarted on
  `data/dreamgenclone.dev.db`, HTTP 200.

## Next

**Section E** — the BodyCard editor and the clothed/unclothed view grids, which is what makes Sections B–D
drivable by a user: readiness/verdicts, the per-asset approval the pack gate requires, the promotion panel (whose
`CurrentStep >= Promote` guard must become target-aware), and exposing `BodyModelId`,
`AngleYawMinAbsPercent` and `QualityGateMinSharpness`. **Section F** then runs the live build on Becky (B122-024).
