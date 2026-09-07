# Debug 021: B-111 Studio Chain Handoff and BigLust Identity on Create

## Report

On 2026-09-06, the user reported that the RolePlay Studio still looks almost unchanged, cannot make a new image for the selected Moment, and does not visibly provide the requested new workflow. The user clarified that the enriched Moment must remain part of the existing Beat Catalogue -> Beat Production -> Production Moments -> Moment Enrichment chain; it is the exact source context for image work. The user also expects identity conditioning to be selectable at creation time when BigLust is selected.

No session id, interaction id, or provider error was supplied with the report.

## Analysis

- `SceneImageStudio.razor` renders a legacy `ProductionWorkspace` plus an additional staged production workbench on the same large page. The actual Composition controls are buried after the pipeline panels and are easy to miss.
- The selected Moment enrichment and POV are already persisted as exact lineage in `SceneImageProductionGroup`, so they are the correct route handoff contract.
- Composition passes `ExecutableStrategies="TextOnlyReferenceStrategy"`; this hides all reference conditioning even for a selected graph-capable model.
- `SceneRenderRequest` and `SceneImageRenderingJobHandler` already implement an actual identity-on-create path: `RenderMode.IdentityControlled` persists exact approved pack selections, and `RunPodServerlessIdentityClient` submits the configured IP-Adapter workflow. It is not valid to expose this path until the selected model's `ReferenceConditioning` qualification is confirmed from live Model Manager data.
- B-111 `FR-C7-01`, `FR-C7-06`, and `FR-C7-07` require a focused decision screen, inspectable submitted inputs, and state preservation. They prohibit the current combined page approach.

## Plan

1. Query the live BigLust and hosted Qwen model rows, including protocol, identity mechanism, declared visual strategies, and qualification proofs.
2. Retain the existing pipeline as the authoritative selection chain. Replace its handoff after a completed Moment enrichment with dedicated Compose, Identity, and Finish production routes that receive the exact existing production group.
3. Make Compose select `IdentityControlled` only when the user opts in, the selected model has real configured identity dispatch, and `ReferenceConditioning` resolves as qualified. Persist the exact identity packs selected from the completed Moment. Do not substitute a different model or strategy.
4. Keep hosted Qwen text-only for create and show its structural graph-strategy limitation explicitly.
5. Build the web project after each source edit. Tests remain disabled by the user.

## Resolution

- Added `ISceneImageProductionService.GetGroupAsync` so routed production screens load and validate the exact existing group rather than reconstructing Moment context.
- Added dedicated Studio routes for Compose, Identity, and Finish. Each route validates its group belongs to the requested session and interaction. Compose creates prompt-only work by default and presents an explicit, inspectable identity-on-create decision.
- Moved the POV chooser and `Compose image for this Moment` action into the completed Moment Enrichment panel. The original pipeline remains the only way to choose the frozen Moment; selecting the action creates/loads its exact production group and opens Compose.
- Removed the embedded legacy `ProductionWorkspace` from the active Studio handoff and removed its durable bootstrap requirement, which rejected hosted image models before a Composition request could be created.
- Queried live Model Manager data. BigLust has an actual configured `IpAdapter` mechanism and strength, but `SupportedVisualStrategiesJson` and `CapabilityQualificationsJson` are both empty. Compose therefore reports `ReferenceConditioning` as `Unqualified` and does not enable identity-on-create until a real proof is registered. Together Qwen reports the expected `Impossible` hosted-API state.
- Builds after the implementation succeeded: `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore --nologo` (119 warnings, 0 errors). Tests were not run because the user disabled them.

## Validated

- [ ] Pending user validation in a fresh RolePlay Studio session.