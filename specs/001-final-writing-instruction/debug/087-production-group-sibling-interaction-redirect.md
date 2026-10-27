# 087 — Composition refused a production group paired with a sibling interaction

**Report**

- Session: `8cdc3a8f-4966-469e-9523-33185ed74466`
- Studio interaction in the URL: `5b114330-ccca-4e1e-89e7-ab4f8ecaf843`
- Production group's interaction: `569c3531-6d08-40c6-9fa0-bfd76053df1e`
- Production group: `58b521c4-8181-4096-9d8d-aac0f33772bf` (Omniscient, the Composition from `085`/`086`)
- Error: `InvalidOperationException: The requested production group does not belong to this Studio context.` —
  `CompositionComposer.LoadAsync()`, HTTP 500.
- Log evidence (release `20261027-104124`):
  - `10:43:35` GET `/roleplay/studio/…/5b114330…/production/moment/0be05d3d…` logged
    `Production group '58b521c4…' does not match the exact selected lineage and POV.`
  - `10:43:38` GET `/roleplay/studio/…/5b114330…/production/58b521c4…/composition` threw the error above.

**Analysis**

There is one production group per Moment + POV, and it is anchored to the interaction that CREATED it. The Studio can
be opened on any interaction of the same turn, so `GetCurrentGroupAsync(momentEnrichmentId, pov)` legitimately returns
a group whose `InteractionId` is a sibling interaction.

`SceneImageStudio.LoadProductionGroupAsync` adopted that group without checking its interaction, then
`EnsureProductionGroupMatchesSelection` threw — but it had already assigned `_productionGroup`, so the page kept
rendering the group's production stage. Its Composition link is built from the ROUTE's interaction plus the group id,
producing the mismatched pair. `CompositionComposer.OnInitializedAsync` then threw an unhandled exception.

**Resolution**

- `SceneImageStudio.LoadProductionGroupAsync` no longer binds a group from another interaction: it clears production
  state and navigates to the group's own Studio URL (`ProductionGroupUrl`, built from the group's session and
  interaction). A group refused by the exact-lineage/POV guard is cleared before the error propagates, so no stage link
  can point at a group the page just refused.
- `RestoreProductionGroupSelectionAsync` (the pinned `/production/{groupId}` route) does the same redirect when only the
  interaction differs; a different session is still an error.
- `CompositionComposer` keeps the session check, but a sibling interaction is recoverable: it records the canonical
  Composition URL and navigates there from `OnAfterRender` (navigation is unavailable during prerendering), instead of
  throwing a 500.

**Validated**

- [x] Web project builds (Razor included).
- [x] All test classes that read either page pass: 111 passed, 0 failed on 2026-10-27.
- [x] New source-contract test `ProductionStudio_RedirectsAProductionGroupToItsOwnInteraction`.
- [ ] Wrong-interaction URLs return 200 + client redirect after republish (was 500).
