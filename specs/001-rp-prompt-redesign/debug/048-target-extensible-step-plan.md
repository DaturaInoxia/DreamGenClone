# 048 — B121-011a: the pipeline becomes a persisted step plan per target kind

**Status:** Done (code + tests green, migration and seed verified in the dev DB, page verified live)
**Date:** 2026-09-21
**Item:** B-121 B121-011a (FR21-035) — the gating prerequisite for B-122 Phase 0

## Report

Requirement (from the program map §4.1 and the B-121 task list): the pipeline must be **target-extensible**
so B-122 Phase 0's body pack is *a new target kind with new seeded templates and new validation rules*, not a
second studio.

Verified before the change: the step set was the fixed `CharacterIdentityBuildSteps.Ordered` array keyed to
`CharacterIdentityBuildStep`; `CharacterIdentityBuildService` read it in four places (create rows, re-run
start index, `FirstIncomplete`, `AllDone`), `FirstIncomplete` assumed `Promote` was terminal, `CharacterStudio.razor`
rendered the status badges from it, and `CharacterIdentityBuild` had no notion of a target.

## Decision

The step plan is **persisted, seeded data**, the same shape as the prompt templates (D1: the seed *is* the
default; a missing row fails fast naming what is missing). Alternatives rejected: a code registry (keeps
structure in code and adds a second place to change), building a static switch (would force a fork).

## Resolution

- **Domain** (`CharacterIdentityStepPlanModels.cs`): `CharacterIdentityTargetKind` (`Face`, `Body`);
  `CharacterIdentityBuildHandlers` (the handler keys the app implements: `front`, `validate.eye`,
  `edit.garment`, `edit.crop`, `edit.enhance`, `angle.face`, `promote.facePack`) with `RequireKnown`;
  `CharacterIdentityStepDefinition` (Kind, Order, Step, HandlerKey, TemplateKey);
  `CharacterIdentityStepPlan` (`Ordered`, `TerminalStep`, `Contains`, `Require`, `RequireTemplateKey`).
  `CharacterIdentityBuild.TargetKind` added.
- **Persistence** (`CharacterIdentityBuildRepository`): new table
  `CharacterIdentityStepPlans (Kind, Step, OrderIndex, HandlerKey, TemplateKey)` with PK `(Kind, Step)`;
  the **Face plan is seeded** from `CharacterIdentityBuildSteps.Ordered` with the existing key names
  (`identity.front.generate`, `identity.garment.remove`) — no data rename, so existing rows and templates keep
  working. `ON CONFLICT DO UPDATE` because a plan is structure the app must be able to run (unlike a prompt
  body, which is the user's). `CharacterIdentityBuilds.TargetKind` added by additive migration
  (`INTEGER NOT NULL DEFAULT 1` = Face, written into existing rows). `ListStepPlanAsync(kind)` added.
- **Service** (`CharacterIdentityStepPlanService`): the single reader of a plan — fails fast when a kind has no
  plan, when two steps share an order (the PK already makes a step unique, so ambiguous order is the one hole),
  and when a row names a handler the app does not implement.
- **Machinery** (`CharacterIdentityBuildService`): resolves the plan from the build's target kind and walks
  *it* — create one row per planned step, first-incomplete resume, `AllDone` over the plan, terminal step = the
  plan's last step (no more hardcoded `Promote`), "the first step needs no input" instead of "Front does".
  `CreateBuildAsync` now takes the kind explicitly (no default).
- **UI** (`CharacterStudio.razor`): injects the plan service, loads the plan once per build and renders the
  status line from `plan.Ordered`.
- **Registration**: `ICharacterIdentityStepPlanService` in `Program.cs`.

### Tests

- New `CharacterIdentityStepPlanServiceTests` (5): seeded Face plan in order with its handlers and keys;
  unseeded kind fails fast naming the kind; unknown handler fails fast naming handler + step + kind; ambiguous
  order fails fast; a step outside the plan fails fast (and a step whose handler has no template says so).
- New `CharacterIdentityBuildServiceTests.SecondTargetKind_WalksItsOwnPlan_WithNoMachineryChange`: seeds a
  **four-step non-face plan** (Front → Validate → GarmentRemoval → Promote), then create → advance → skip →
  complete with no machinery change; asserts no Crop row exists, the terminal step is the plan's last, and a
  step the plan lacks fails fast naming the kind.
- All call sites updated for the explicit kind (build, front, garment, validation fixtures/tests).
- Run: identity + template-store + Faces-contract filter **74 passed / 0 failed**; whole RolePlay area
  **1719 passed / 3 failed** — the three are the known pre-existing `SdxlSceneImagePromptBuilderTests`
  failures documented on 2026-09-11, untouched by this change.

### Live verification (dev DB)

- Faces page load ran the migration + seed; `CharacterIdentityStepPlans` now holds the 7 Face rows
  (Kind 1, OrderIndex 1–7, handlers `front` … `promote.facePack`, keys preserved).
- The *Becky* build page renders its status line from the plan:
  `Front · Complete | Validate · Complete | De-clothe · Skipped | Crop · Skipped | Enhance · Skipped |
  Angles · Complete | Promote · NotStarted` — i.e. the build's `TargetKind` round-trips as Face and the plan
  drives the UI.

## Deliberately deferred (recorded in `tasks.md` + the map)

The *face* step handlers still hold their own template-key constants, and the front service still fixes
`SceneAssetType.CharacterFace` for its container. Parameterising key + asset type + view set belongs with the
first non-face handler that needs it (B-122 Phase 0's body front) — the plan rows already carry the keys.
Doing half of it now would touch ~20 call sites for a consumer that does not exist yet.

## Validated

- [x] Focused build 0 errors; targeted tests green; migration + seed verified in the dev DB and the page
      verified live.
- [ ] pending — the *body* kind will be the real proof; it arrives with B-122 Phase 0.
