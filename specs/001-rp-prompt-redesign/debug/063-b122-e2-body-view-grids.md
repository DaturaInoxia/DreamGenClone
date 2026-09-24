# 063 — B-122 E-2: the clothed and unclothed view grids

**Status:** Delivered; 60 tests green in the body/studio/promotion set; browser-verified on Becky's body build
**Date:** 2026-09-22
**Items:** B-122 Phase 0 Section E-2, following `debug/056` (E-1: the body card + the body-build entry action)

## Report

E-1 left the body target reachable but blind: a body build existed, its step rail rendered, and there was no way to
acquire a single view. E-2 adds the grid that does it — and nothing else. Ten rows, one request per row.

## What changed

**1. The ten slots are defined once, in the domain.**
`CharacterIdentityBodySlots.All` (new, `Domain/RolePlay/CharacterIdentityBodyModels.cs`) lists the ten canonical
slots — five views in each state, state-major, with their labels and their base flags — plus `For(state)`,
`Require(state, view)` (refusing a pair that is not a slot, and naming the extended-view alternative) and
`LabelFor(view)` for canonical *and* extended views.

`CharacterIdentityPromotionService` had its own private copy of that table. It now reads
`CharacterIdentityBodySlots.All`, so the grid, the promotion gate and the readiness diagnostics cannot disagree
about which slots exist; a contract test asserts the duplicate is gone.

**2. `BodyViewsPanel.razor` (new, `Components/RolePlay/`).**
Two grids — **Clothed** and **Unclothed** — each with its five slots and an `n / 5 accepted` badge, plus an
extended-view row (state + rotation + position key) that is explicitly *not* promotable. Per row:

| Element | Source |
|---|---|
| label, base badge | `CharacterIdentityBodySlots` |
| status | the persisted view's status (NotStarted when the request has not been made) |
| thumbnail | the view's `OutputArtifactId` resolved through the build's container images |
| quality evidence | `view.QualityRating` (+ notes as the tooltip) |
| override | `ManualOverrideApplied` / reason / author |
| failure | `view.FailureReason` when the status is Blocked |
| findings state | `view.Findings.NotPassed` — what acceptance refuses on |

Opening a row resolves its prompt through `ICharacterIdentityBodyService.ResolvePromptAsync` (the panel composes no
prompt of its own) and offers exactly the one-view actions the plan names: **Generate**, **Re-run**, **Edit from
accepted source**, **Record source as result**, **Upload**, **Accept**, **Analyze quality**, **Record findings**,
**Record override**. Each action reports into *that row's* message, so one view's refusal can never be read as
another's outcome.

**No values are assumed.** The panel has one explicit model selector (`Select model…` empty by default) and a size
field; Generate refuses with a named reason until both are chosen — `Choose a model and a size above before
generating — neither is assumed.` A second contract test asserts no `@onclick` handler name in the panel contains
"all", "every", "sweep" or "batch".

**3. The studio's Body tab renders it** under the build card, with
`BuildId="@_bodyBuild.Id"` and `CharacterName="@BodyPanelCharacterName"` — the latter being the resolved owner
template's name, so the prompts address the same identity the rest of the page reads and writes.

## Verification

- Tests: **60/60** (`~BodyViewsPanel|~CharacterIdentityBody|~CharacterIdentityPromotion|~CharacterStudio`), including
  the 7 new grid/slot contracts.
- Browser, Becky `de351eb3…` (body build `85441803e157429f9707dcde79bacb38`, InProgress at step Front):
  the Body tab renders **10 rows** — `Clothed Front (base)`, the four clothed angles, `Unclothed Front (base)` and
  the four unclothed angles, all `NotStarted` with their own **Open** action — plus the extended-view row and the
  image-model selector.
- Opening **Clothed Front** resolved its prompt through the service (373 chars):
  `Full-body photograph of Becky, head to feet, standing straight and facing the camera: Curvy, bust Full, waist
  Soft, hips Wide, 5'8", 170 lbs, Fair, Smooth, small triangle pubic hair, tattoo of a tree on left calve,, none,
  clean. Wearing plain everyday clothing. Neutral background, even lighting, sharp focus, natural skin texture, the
  whole body in frame and unobstructed.` — the card line is embedded, and all nine action buttons, the upload input,
  the four findings selects and the override fields render.

## Note for the operator

The tree changed under this session: the body card's grooming field and the matching template attribute are now named
**`PubicHair`** (the dev DB column is `PubicHair` too), not `Grooming`. The prefill service, `PhysicalAttributes.Clone`,
the formatter and the tests all follow the current names and are green — worth knowing only because a record from
earlier today (`debug/062`) still says `Grooming`.

## Next

- **E-3** — the body promotion panel with its exact missing-item diagnostics (the panel's `CurrentStep >= Promote`
  guard made target-aware), per-asset approval for the ten promoted references, and the settings surface
  `ReferenceWorkflowSettings` has never had: `BodyModelId`, `AngleYawMinAbsPercent`, `QualityGateMinSharpness`
  (B122-021).
- Note: the grid exposes the same per-view actions the promotion panel will read its readiness from, so a slot the
  gate refuses now names a request the operator can actually see in this grid.
