# 025 - B-111 Dedicated Production Studio and lineage-preserving return

## Report

Production Studio and Production POV are currently embedded in the RolePlay Studio below Beat Catalogue, Beat Production, Production Moments, and Moment Enrichment. The user requires Production Studio and its POV workspace to open as a separate page after Moment Enrichment. Returning to RolePlay Studio must select the same Catalogue, Beat, Moment Set, Moment, completed enrichment revision, and POV that were active before navigation.

## Analysis

`SceneImageStudio.razor` owns both the upstream selection chain and the complete production workbench. The existing `/compose/{productionGroupId}` route is dedicated but only renders a partial composition form. The production group stores the exact frozen lineage identifiers and versions. The upstream page only requests current pipeline objects, so a bare return URL cannot prove it restores the originating selection.

The repository interfaces already expose exact-ID reads for Catalogue, Beat Production Plan, Moment Set, and Moment Enrichment. Pipeline service interfaces do not expose them yet. The correct solution is to expose exact read methods through those existing services, hydrate the selection from the exact production group, and reuse the existing production workspace instead of keeping a duplicate partial workflow.

## Plan

1. Expose exact-ID reads through the four pipeline services.
2. Add the production-group route to `SceneImageStudio` and render it as a production-only page when the group id is present.
3. Hydrate the exact stored lineage in that mode and when returning to the normal RolePlay Studio route with the group id as query state.
4. Hide the embedded Production Studio and legacy image-production controls from the upstream route; retain the Moment Enrichment handoff.
5. Make the existing focused compose route redirect to the canonical dedicated Production Studio route.
6. Build the Web project and inspect touched diagnostics. Tests remain disabled by user instruction.

## Resolution

- Added `/roleplay/studio/{sessionId}/{interactionId}/production/{productionGroupId}` to `SceneImageStudio.razor` as the canonical Production Studio route.
- The normal RolePlay Studio route now omits the embedded Production Studio workbench. Moment Enrichment creates or loads the exact group and navigates to the dedicated route.
- The dedicated route renders only the existing Production Studio / Production POV workbench, preserving its Compose, Identity, Finish, selected canvas, attempt strips, comparison, review, approval, branch, and edit behavior.
- The dedicated route's Back action carries `productionGroupId` to the regular studio route. The regular studio hydrates the selected catalogue, beat, production plan, Moment Set, Moment, enrichment revision, and POV from the exact persisted group.
- Added exact-ID read pass-through methods to the four existing pipeline service contracts and implementations. Each stored group lineage relationship and version is validated explicitly; no current/latest fallback is used for return restoration.
- Redirected the legacy `/compose/{productionGroupId}` route to the canonical dedicated Production Studio route.

## Validated

- Application service-contract build succeeded.
- Razor and C# editor diagnostics report no errors in all touched files.
- Web project build succeeded on 2026-09-06 after stopping the confirmed development Web host on port 5177 that had locked output DLLs.
- Tests remain disabled by user instruction.
- [ ] pending user workflow verification with an enriched Moment and the Production Studio Back action.
