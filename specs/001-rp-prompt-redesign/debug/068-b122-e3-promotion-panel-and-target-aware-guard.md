# 068 — B-122 Section E-3: the body promotion panel and the target-aware promote guard

**Status:** Done (138 tests green on the identity/body/studio filter; web project builds with 0 errors)
**Date:** 2026-09-24
**Items:** B-122 B122-021 (E-3), following `debug/055` (Section D, the promotion service) and `debug/063` (Section E-2, the view grids)

## Report

The operator's body set was finished: all twelve canonical views for the character accepted, every findings
verdict passed, a current approved face pack (v8, `FaceOnly`, 5 assets) — and there was still **no way to promote
it**. Two independent reasons, both in the UI:

1. **The Body tab had no promotion panel at all.** The only promote panel in the studio lives in the Faces
   section (`Panel D · Promote identity pack`) and calls `PromotionService.GetReadinessAsync(_build.Id)` — the
   FACE build.
2. **Its guard was the trap.** The panel was wrapped in
   `@if (_build is not null && (int)_build.CurrentStep >= (int)CharacterIdentityBuildStep.Promote)`. That is a
   reasonable-looking gate that cannot open for a body build, because the body plan's remaining steps
   (`Validate`, `Angles`, `ValidateView`, `Promote`) are completed **by the promotion itself**
   (`CharacterIdentityPromotionService.CompleteBodyStepsAsync`, `debug/055`). The gate waited on the action it was
   hiding: readiness → nothing to press → steps never complete → readiness stays behind the gate.

Live state that proved it (dev DB, build `8333698fb13244d2ba40b3d6c2dc17c1`): 12/12 body views `Status=4`
(Accepted) with all four verdicts recorded, `CurrentStep=2`, steps `Front=Complete` and
`Validate`/`Angles`/`Promote`/`ValidateView` all `NotStarted`. The plan's own ordering is why nothing could
complete them: `CompleteStepAsync` enforces `EnsureCurrent` and requires an input **and** an output artifact for
every step after the first — a recorder that "just marks steps" could not have been correct.

## Resolution

**The panel is rendered from readiness, and it is the body build's own readiness.**

- `CharacterStudio` gained `_bodyPromotion` / `_bodyPromotionMessage` / `_bodyPromotionBusy` and
  `RefreshBodyPromotionAsync()` → `PromotionService.GetReadinessAsync(_bodyBuild.Id)` (the body build id, never
  `_build`).
- The Body section renders **Promote the body set to a BodyComplete pack**: one card per slot — the five FACE
  slots of the target draft pack, then the twelve body slots — each `Ready`/`Blocked` with its artifact id, the
  badge states how many requirements are unmet, and **every** blocking reason is listed verbatim, one per line
  ("Pack: this character has no identity pack draft to promote the body into…",
  "Clothed back: this request has not been started.", "its status is … , not Accepted. Its findings are …").
- The promote button is disabled on `!_bodyPromotion.Ready || _bodyPromotionBusy`, so the button's disabled state
  and the service's own refusal cannot disagree; `PromoteBodySetAsync` then re-reads the pack list and the build's
  step records, so the step rail shows the steps the promotion just completed.
- Readiness is re-read on both paths that can change it: when the body build is resolved during load, and when the
  Body tab is opened (`SelectSectionAsync`) — plus the panel's own **Re-read readiness** button, because accepting
  a view changes the answer while the panel is on screen.

**The step-index guard is face-only now, and pinned that way.** `CharacterStudioBodyContractTests` asserts the
gate is absent from the Body section *and* absent for the body build anywhere in the studio
(`_bodyBuild.CurrentStep >= … Promote`), so a later edit cannot quietly reintroduce it.

## What the operator still has to do (and why it is not a bug)

Readiness for a finished body set is still blocked when there is **no draft pack** to promote into:
"Promote the face build (or supersede the latest approved pack) first — a `BodyComplete` pack carries the five
face slots." That is deliberate (`debug/055`): an approved pack is immutable, and superseding it is the user's
explicit action, never a side effect of promoting. The path is Packs tab → **Supersede** on the approved pack
(creating a draft that carries the five face slots forward) → Body tab → Promote. Before this change the operator
could not even be told that, because there was no panel to say it in.

## Validation

- `CharacterStudioBodyContractTests` +2 (`TheBodyPromotionPanel_IsGatedByReadiness_NotByTheBuildsStepIndex`,
  `TheBodyPromotion_ReadsAndPromotesTheBodyBuild_AndReReadsWhenTheTabOpens`) — the readiness-driven panel, the
  named blocking reasons, the absence of the step-index guard, and the body build's own service calls.
- `dotnet build DreamGenClone.Web/DreamGenClone.csproj -p:OutDir=…\artifacts\build-check\studio\` → **0 errors**
  (the running app's `bin` was not disturbed).
- `dotnet test … --filter "CharacterStudioBodyContractTests|CharacterIdentityBodyViewsPanelTests|CharacterIdentityBodyPromotionTests"`
  → **37 passed / 0 failed**; the wider filter
  `CharacterIdentityBody|CharacterStudio|CharacterIdentityPromotion|CharacterIdentityBuild|CharacterIdentityStepPlan`
  → **138 passed / 0 failed**.
- The promotion contract itself (12 body assets tagged by state and view, scope `BodyComplete`, canonical pointer =
  the uploaded unclothed `Front`, the pack approving afterwards, and the body build reaching
  `Complete`/`Promote`) was already covered by `CharacterIdentityBodyPromotionTests`, and still passes.

## Next

**Section F** — B122-023 (full affected-project test runs), **B122-024** (the live Becky build through the UI:
supersede into a draft, then promote, then verify Asset Manager grouping), and B122-026 (expose
`AngleYawMinAbsPercent` / `QualityGateMinSharpness` on a UI-backed settings surface).
