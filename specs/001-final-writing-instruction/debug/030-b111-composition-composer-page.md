# B-111 Composition Composer Page

## Report

On 2026-09-06, the user requested that Production Studio remain unchanged after POV selection except for a new `Composition Composer` button. The button must open a separate page that owns Composition prompt generation, model selection, identity/reference selection, manual prompt editing, repeated image generation, and the bounded list of generated Composition images.

## Analysis

`SceneImageStudio.razor` currently combines Composition, Identity, and Finish inside one stage workbench. Its Composition section already owns the relevant persisted production-group state, prompt generation, model/reference controls, `EnqueueProductionCompositionAsync`, and Composition attempt history. Moving those controls to a dedicated route can reuse the same services and records without altering the production group, durable job, model-resolution, or downstream Identity/Finish workflows.

The authoritative B-111 UI contract names `Studio: Composition` as a single-purpose screen and requires separate stage screens sharing one production group. The current unified-model document describes a future broad replacement; this change is limited to the existing `SceneImageRecord`/production-group flow and does not introduce a second image pipeline.

## Plan

1. Add a dedicated `CompositionComposer.razor` route scoped to the existing session, interaction, and POV production-group identifiers. Reuse the current Composition prompt generation, manual prompt edit, selected model, identity/reference selection, Composition generation/regeneration, and bounded Composition attempt list.
2. In `SceneImageStudio.razor`, retain the post-POV Studio layout and downstream Identity/Finish/Jobs behavior. Replace the embedded Composition controls with a `Composition Composer` navigation button that preserves the exact production group context.
3. Add a route return affordance from the composer to the same Studio production route. Do not modify image-job dispatch, model fallback policy, production group creation, Identity, Finish, or Jobs scope.
4. Add focused UI source-contract coverage for the dedicated route and preserved production-group handoff, then run diagnostics and the Release Web build. Automated tests remain disabled unless the user re-enables them.

## Resolution

Added `CompositionComposer.razor` at `/roleplay/studio/{sessionId}/{interactionId}/production/{productionGroupId}/composition`. It validates the exact production-group ownership, reuses the existing Still brief, prompt queue, selected-model resolution, `ReferenceApplyPanel`, `ResolvedRenderBadge`, `SceneRenderRequest`, and persisted production attempts. It supports prompt regeneration, direct prompt edits, Composition generation, sibling generation, and a bounded list of the group's Composition attempts.

`SceneImageStudio.razor` now navigates to Composer when it creates/loads the selected POV's production group and exposes a `Composition Composer` button after POV/group selection. The embedded Composition controls and Composition attempt strip are removed. Identity, Finish, Jobs, production-group selection, and downstream approval behavior remain in Production Studio.

`SceneImageStudioUiContractTests.cs` now asserts the Studio-to-Composer handoff and Composer's exact group/brief request contract, model/reference controls, and bounded Composition attempt list.

## Validated

- [x] Existing Composition ownership and navigation boundary traced in `SceneImageStudio.razor`.
- [x] B-111 UI contract and unified production model reviewed.
- [x] Release Web build completed successfully with zero errors on 2026-09-06.
- [ ] Pending user usability validation.