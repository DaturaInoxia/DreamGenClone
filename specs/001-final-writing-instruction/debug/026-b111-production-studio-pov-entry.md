# B-111 Production Studio POV Entry

## Report

The completed Moment Enrichment action was disabled until a POV was selected on the upstream RolePlay Studio page. The requested workflow is to open Production Studio for the exact enriched Moment first, then select one of that Moment's multiple POVs within Production Studio.

Session and interaction identifiers were not supplied.

## Analysis

`SceneImageProductionGroup` is keyed by `MomentEnrichmentId` and `Pov`, so a group cannot truthfully be resolved before an explicit POV selection. `SceneImageStudio.razor` previously used only a group-keyed production route and therefore required `_productionPov` before creating the group and navigating.

The existing `SceneMomentEnrichment` record holds the exact Catalogue, Beat, Beat Production Plan/version, Moment Set/version, and Moment lineage required to restore a production entry without selecting a group or POV. The `SceneImagePovFramer` helper always offers the visible `Omniscient` option, but auto-selecting it would contradict the requested multi-POV choice.

Required spec artifacts were consulted. Their no-hidden-default and explicit-source principles apply: the entry route must restore persisted lineage and leave POV unset until the user selects it.

## Plan

1. Add an enrichment-keyed Production Studio entry route in `SceneImageStudio.razor`.
2. Change the completed-enrichment action to navigate to that route without requiring a POV.
3. Restore and validate the exact lineage from the enrichment ID; leave `_productionPov` empty.
4. Retain the existing Production Studio POV buttons as the sole POV selection control; choosing one loads that POV's existing group, while Compose creates it if necessary.
5. Carry the exact enrichment ID back to normal Studio when no group has been selected.
6. Update the focused source-contract test and validate Razor/build output.

## Resolution

Implemented in `SceneImageStudio.razor`:

- Added `/roleplay/studio/{sessionId}/{interactionId}/production/moment/{momentEnrichmentId}` as the dedicated Production Studio entry route.
- Replaced the Moment Enrichment POV selector and disabled Compose action with an enabled `Open Production Studio` action for a completed enrichment.
- Added exact enrichment lineage restoration for the entry route, validating Catalogue, Beat, Beat Production Plan/version, Moment Set/version, Moment, and completed enrichment state.
- Left POV unset after restoration. The existing Production Studio POV buttons are now the sole place where the user selects a POV. Selecting one loads its group; the existing Compose action creates that POV group when it does not yet exist.
- Added exact enrichment return navigation for entry routes without a production group.
- Removed the attempted automatic `Omniscient` POV selection because it contradicted the requested multi-POV workflow.

Updated `SceneImageStudioUiContractTests.cs` with a source-contract test for the enrichment-first entry route and no automatic POV selection.

## Validated

- [x] Razor and test-file editor diagnostics report no errors.
- [x] `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore --nologo` succeeded on 2026-09-06 (119 existing warnings, no errors) after stopping the locking Web host.
- [ ] Pending user workflow verification with the freshly started Development host.
