# 024 - B-111 Production POV canvas and attempt-deck layout

## Report

The Production POV workspace displays the selected image in the central canvas, but its Composition, Identity, and Finish attempt strips render below the left context rail and right inspector. The attempt-level Edit action does not navigate to the established image editor; it loads prompt and settings into legacy lower-page controls. The attempt deck, selection, comparison, review, approval, branching, and image editing must belong to the central production workspace.

## Analysis

`SceneImageStudio.razor` declares `.scene-production-grid` with left context, center main canvas, and right inspector. Its `.scene-production-attempts` block is rendered after that grid, which makes it a full-width sibling instead of a center-column child. `ContinueFromImageAsync` restores an old prompt/settings state in the page rather than using the existing `OpenImageEditor` route.

The production attempt operations already use exact `SceneImageProductionGroup` lineage and existing `ProductionService` methods; this is a layout and navigation ownership issue, not a data or rendering issue. The B-111 UI contract requires the workflow to flow rather than trap users and prohibits duplicate edit loops.

## Plan

1. Move the existing production attempt deck inside the central workspace section after the selected canvas and stage controls.
2. Replace the attempt Edit action with `OpenImageEditor`, which navigates to the established editor for the exact selected image.
3. Add selected-image actions beside the central canvas so editing and branching do not require scrolling the full attempt deck.
4. Build the Web project. Tests remain disabled by user instruction.

## Resolution

- Added `scene-production-workspace` as the three-column grid owner.
- Kept the POV selector, context rail, center canvas, inspector, and existing attempt deck in that one workspace grid.
- Made the Composition, Identity, and Finish attempt deck a center-column row beneath the canvas and stage controls; both rails span beside it on desktop.
- Added exact selected-image Edit and Composition branch controls in the central workspace.
- Routed all attempt-card Edit actions to the established exact-image editor route rather than restoring legacy lower-page prompt state.

## Validated

- Razor and CSS diagnostics report no errors.
- `dotnet build DreamGenClone.Web\\DreamGenClone.csproj --no-restore --nologo` compiled the projects but failed during final Web output copying because `.NET Host` process `32232` holds the existing Web DLLs. The failure is `MSB3021/MSB3027` file locking, not a source compilation error.
- Tests remain disabled by user instruction.
- Pending user visual verification of the central canvas and attempt deck layout.
