# 056 — B-122 Phase 0 Section E-1: the body target and the BodyCard editor in Character Studio

**Status:** E-1 done (Web builds; 144 studio/identity/body tests + 22 body-service tests green; verified live on the
dev DB in the browser — Body tab renders, Faces tab unaffected)
**Date:** 2026-09-22
**Items:** B-122 B122-019 (and part of B122-022), following `debug/055` (Section D)

## Report

Sections A–D were code-complete and test-covered, but the Body tab in the Character Studio was still the
placeholder *"BodyCard and full-body references (clothed + unclothed) — B-122 Phase 0 … Not yet implemented."*
Nothing was drivable: no way to record the BodyCard, and no way to start a body build.

Two problems had to be solved before any body UI could exist:

1. **The studio loaded ONE build of any kind** — `_build = builds.FirstOrDefault()`, ordered newest-first. The
   moment a character had a body build, the Faces tab would render the *body* plan's steps (and Panel D's
   promotion gate, which reads `_build.CurrentStep`, would decide against the wrong build).
2. **The BodyCard had no writer reachable from the UI.** `CharacterBodyCardRepository.SaveAsync` is the only
   store, and the studio's rule is that the UI talks to services, never to a repository.

## Resolution

**The two builds are kept apart.** `OnInitializedAsync` now selects
`builds.FirstOrDefault(build => build.TargetKind == CharacterIdentityTargetKind.Face)` into `_build` (unchanged
meaning for every existing face path) and `…== Body` into a new `_bodyBuild` with its own `_bodyStepRows`. A
contract test asserts the any-kind form never comes back.

**The card is written through the service.** `ICharacterIdentityBodyService.SaveBodyCardAsync(card,
expectedVersion)` (new) refuses a card that names no character and otherwise delegates to the store, so the
optimistic-concurrency rule stays in one place; the editor saves under the version it loaded and reports the new
one.

**The Body tab** now holds:

- **Body card** — the seven canonical fields, labelled from `CharacterBodyCardFields.Require(field).Label` and
  marked `[DECIDE]` from the definition's own flag (body hair, tattoos, grooming), with the card's readiness
  shown in its own words (`Not recorded yet` / `Complete` / `Incomplete: …` listing every unanswered field).
  Nothing is inferred: an empty field stays an unanswered decision, and generation still refuses by name.
- **Body build** — the entry action, creating the character's *body* build (`CharacterIdentityTargetKind.Body`)
  on the seeded body plan, plus the plan's step rail so the next step is visible.

The card status, the field labels and the `[DECIDE]` marks all come from the domain definition — the UI holds no
second copy of the wording or of which fields need a decision.

## Tests

`CharacterStudioBodyContractTests` (new, 5): the card's inputs are bound to card properties and never to a
dictionary indexer (the bug that killed the Faces tab, B-121 note 009); the face and body builds are selected by
kind and the any-kind form is gone; labels/`[DECIDE]` come from the domain; the card is saved through the service
under the loaded version and its readiness is the card's own answer; the section's `@onclick` handlers contain no
batch/sweep/"generate all" affordance (asserted over the controls, not the prose — the copy *tells* the user there
is no batch action).

`CharacterIdentityBodyServiceTests` +2: `SaveBodyCard_CreatesV1_ThenRefusesAStaleVersionFromAnotherEditor`,
`SaveBodyCard_RefusesACardThatNamesNoCharacter`.

**Verification:** Web builds; studio/identity/body filter → 144 passed; body-service suite → 22 passed; app
restarted on `data/dreamgenclone.dev.db` (Development) and checked in the browser: the Body tab renders the card
editor with empty fields + `[DECIDE]` badges and the body-build entry action, and the Faces tab still renders the
face build, its panels and its prompt.

## Next in Section E

- **E-2** — the clothed and unclothed view grids: the ten canonical slots (and extended views) with one-view
  actions only — generate, edit from the accepted source, upload, record source as result, accept, findings,
  attributed override, re-run — plus quality evidence per view.
- **E-3** — the body promotion panel with its exact missing-item diagnostics (and the panel's
  `CurrentStep >= Promote` guard made target-aware), per-asset approval for the ten promoted references, and the
  settings surface `ReferenceWorkflowSettings` has never had: `BodyModelId`, `AngleYawMinAbsPercent`,
  `QualityGateMinSharpness` (B122-021).

**B122-019 is delivered; B122-020/021 are not.** The card editor and the body-target entry action are the two
things B122-019 names, and both are now reachable.
