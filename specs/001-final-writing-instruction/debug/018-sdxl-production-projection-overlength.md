# SDXL Production Projection Overlength

## Report

Session `4f2eec18-b190-4beb-ad35-8d520ae5c800`, interaction `dad139a5-8e98-469b-ab74-8e55404f1732` failed in the fresh Web runtime while creating the durable Scene Production revision. The exact error was `SDXL prompt exceeds the qualified 800-character limit.`

## Analysis

The durable bootstrap had stopped passing the complete provider snapshot as composition, but the first projection still duplicated the same frozen-state facts across actor description/action/clothing and included an unbounded composition object array. `ProductionMediaCompilerBase.VisualTerms(...)` recursively emits every string in all four intent JSON fields, so this projection exceeded the qualified SDXL limit before persistence. The strict 800-character validation is correct and remains required by the SDXL/Juggernaut production contract.

## Plan

Update `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` so each renderable fact has one owning projection field, each field is explicitly bounded, and non-renderable metadata is excluded. Preserve the complete semantic snapshot separately for audit and lineage. Rebuild, run focused production tests, and retry the Studio action against a fresh Web binary.

## Resolution

The projection now emits one compact actor description per frozen character, bounded location/visual/environment/object composition, bounded POV camera intent, and bounded lighting/mood/time style intent. Duplicated actor properties and unbounded object text were removed. Missing required source fields still fail explicitly.

## Validated

- [x] Razor diagnostics report no errors after the projection change.
- [x] Web build completed successfully with no errors.
- [x] Focused production tests passed: 22 passed, 0 failed.
- [ ] Fresh revision-creation Studio action and persisted DB evidence pending because the current selected beat has no completed Moment enrichment in the live session; the UI correctly disables that production path until enrichment exists.
