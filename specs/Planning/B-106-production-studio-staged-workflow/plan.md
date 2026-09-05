# B-106 Implementation Plan — Production Studio Staged Workflow

**Status:** Ready for implementation
**Spec:** [spec.md](spec.md)
**Tasks:** [tasks.md](tasks.md)

## Guiding constraint

Persistence is already built. This plan adds **execution paths and UI**, and touches the domain only
where the spec names a specific additive field. Nothing here re-models the production graph.

Verified reusable state:

| Concern | Existing type | Location |
|---|---|---|
| Group, stages, identity policy, skip reason, current approval | `SceneImageProductionGroup`, `SceneImageProductionStage`, `SceneImageIdentityPolicy` | `DreamGenClone.Domain/RolePlay/SceneImageProductionGroup.cs` |
| Attempt with parent, stage, disposition, checksum, purge | `SceneImageRecord` (`SourceImageId`, `ProductionGroupId`, `ProductionStage`, `Disposition`, `RegenerateOfId`, `Sha256`) | `DreamGenClone.Domain/RolePlay/SceneImageRecord.cs` |
| Approval decisions | `ApprovedSceneFrameDecision` | same file as group |
| Group orchestration, disposition, approval, retention, promotion | `SceneImageProductionService` | `DreamGenClone.Web/Application/RolePlay/SceneImageProductionService.cs` |
| Identity packs and approved canonical faces | `ICharacterImageIdentityService` / repository | `DreamGenClone.Web/Application/RolePlay/` |
| Source-image edit transport | `IImageEditingClient` + ComfyUI / RunPod Serverless clients + dispatcher | `DreamGenClone.Application/Abstractions/`, `DreamGenClone.Infrastructure/Models/` |
| Edit execution job | `SceneImageEditingJobHandler` | `DreamGenClone.Web/Application/RolePlay/` |

## Design decisions

### D1 — Identity is a reference-conditioned source-image edit

Stage 2 consumes the stored composition bytes and applies ordered approved face references through
the Qwen edit path. Identity-conditioned text-to-image (`+ Identity one-pass`) is **not** the
Identity stage and is not accepted as a substitute.

*Justification:* B-100 FR-054 ("identity and finishing edits shall consume its exact stored output"),
`production-studio-image-workflow.md` Stage 2, Stage 2/3 handoff §4.1.

*Feasibility:* the app already performs in-app Qwen source edits — `SceneAssetProfilePackJobHandler`
generates a profile pack as "front + 4 canned Qwen edits". The only missing capability is passing
**ordered reference images** alongside the source.

### D2 — Extend the editing contract with ordered references; do not fork the interface

`IImageEditingClient` gains one additional operation that accepts ordered references. The existing
`EditAsync` remains for reference-free Finish edits. Both transports and the dispatcher implement it.

`ComfyUIImageEditingClient.BuildWorkflow` already encodes the source through
`LoadImage → FluxKontextImageScale (node 2) → TextEncodeQwenImageEditPlus (nodes 6 and 7, image1)`.
`TextEncodeQwenImageEditPlus` accepts additional image slots, so ordered references bind to
`image2…imageN` on both the positive and negative encode nodes.

**Both** workflow builders in that file must be updated — the split loader graph
(`UNETLoader` + `CLIPLoader` + `VAELoader`) and the merged AIO graph (`CheckpointLoaderSimple`),
because the AIO serverless worker validates only its own graph shape.

Reference bytes are uploaded through the same `/upload/image` path already used for the source.

### D3 — Ordering hazard is made explicit, not assumed away

Applying faces and then changing geometry degrades the applied identity. Rather than forbidding
Finish edits or silently accepting drift, each Finish attempt declares a change class
(`Cosmetic` | `Geometry`). A `Geometry` child of an identity-carrying parent is marked
identity-stale and blocks approval while `IdentityPolicy` is `Required` (FR6-016, FR6-017).

This is a user-visible correctness rule, not a hidden heuristic.

### D4 — Skip is persisted policy, never inferred

`IdentityPolicy` and `IdentitySkipReason` already exist on the group and are currently written once
as `Required`. B-106 adds the explicit user action that sets `SkippedByUser` with a mandatory
non-empty reason, and the action that clears it. No default, no inference (FR6-011).

### D5 — Lineage needs no new graph

The attempt tree is derived from existing columns: `ProductionGroupId` scopes it,
`ProductionStage` groups it, `SourceImageId` gives the parent edge, and `RegenerateOfId` marks
siblings. No new lineage table or column is required.

### D6 — Minimal additive domain fields

Only three additive fields, all explicit and nullable so existing rows remain valid:

| Field | Type | Purpose |
|---|---|---|
| `SceneImageRecord.FinishChangeClass` | `SceneImageFinishChangeClass?` | FR6-016 |
| `SceneImageRecord.IdentityStale` | `bool` | FR6-017 |
| `SceneImageRecord.IdentityReferenceBindingsJson` | `string?` | FR6-005, FR6-024 |

`TypedReferenceSnapshotJson` is left alone — it carries B-100 typed brief references and must not be
overloaded with edit-time identity bindings.

### D7 — One Studio, staged controls

The staged flow lives in `SceneImageStudio.razor` where the stepper already exists. Stage rows 2 and
3 become live. `ProductionWorkspace.razor` remains the durable-operations surface and is not the
place where users perform creative steps.

### D8 — Strict configuration, consistent with repo rules

Every resolved model, editor, policy, and threshold comes from persisted configuration. Missing
values fail fast naming the missing item. No fallback branch, no hardcoded sampler/steps/CFG —
those come from the resolved editor model, as the existing Qwen workflow builders already do.

### D9 — Scope boundary

No changes to RP engine files, prompt slots, continuation, or Phase 3 concerns.

## Implementation phases

### Phase A — Reference-capable edit mechanism

Add the ordered-reference edit operation end to end: contract, both ComfyUI workflow builders,
serverless client, dispatcher, and workflow-structure tests. No studio changes yet.

**Files:** `DreamGenClone.Application/Abstractions/IImageEditingClient.cs`,
`DreamGenClone.Infrastructure/Models/ComfyUIImageEditingClient.cs`,
`DreamGenClone.Infrastructure/Models/RunPodServerlessEditingClient.cs`,
`DreamGenClone.Infrastructure/Models/ImageEditingClientDispatcher.cs`,
`DreamGenClone.Tests/RolePlay/ComfyUIImageEditingClientTests.cs`

**Exit:** workflow JSON tests prove ordered `image2…imageN` binding on both graph shapes and on both
positive and negative encode nodes.

### Phase B — Identity stage

Additive domain fields and schema, identity readiness resolution, an identity-stage enqueue path,
and job execution that dispatches the reference edit and writes the child attempt with ordered
bindings and snapshots.

**Files:** `DreamGenClone.Domain/RolePlay/SceneImageRecord.cs`,
`DreamGenClone.Infrastructure/RolePlay/` (scene image repository schema),
`DreamGenClone.Web/Application/RolePlay/SceneImageProductionService.cs`,
`DreamGenClone.Web/Application/RolePlay/SceneImageService.cs`,
`DreamGenClone.Web/Application/RolePlay/SceneImageEditingJobHandler.cs`

**Exit:** an Identity child attempt exists with exact parent, ordered bindings, and a dispatch debug
event; a missing pack fails naming the character.

### Phase C — Identity skip

Skip and clear-skip operations on the production service plus the Studio control and display.

**Exit:** acceptance scenarios 3 and 4.

### Phase D — Finish stage

Finish enqueue scoped to the production group, change-class declaration, identity-stale marking,
content-policy gating of adult edits, and branch-from-parent.

**Exit:** acceptance scenarios 5, 6, 7.

### Phase E — Image management

Implements [ui-contract.md](ui-contract.md) in full: workbench layout regions, stage stepper with
explicit per-step states and inline blocking reasons, cast/identity readiness panel, stage-specific
canvas controls, branch-aware lineage tree grouped by stage, side-by-side compare with a snapshot-
derived differences list, identity-gated approval surface, empty/loading/failure states,
accessibility and focus rules, and six new state keys.

**Exit:** acceptance scenario 8, Playwright desktop/mobile acceptance; closes P2-053, P2-054, and
the UI half of P2-056.

### Phase F — Legacy retirement

Remove the legacy one-off generation action from new-session production navigation. Keep the
standalone editor route for non-production assets only.

**Exit:** closes P2-055.

### Phase G — Validation and ledger

Full build, full suite, Razor diagnostics, live-session walkthrough of all ten acceptance scenarios,
captured debug events, then update the Phase 2 ledger and open the Phase 2 release gate work.

## Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | A geometry-changing Finish edit degrades applied identity | FR6-016/FR6-017 make it explicit and block approval; never silently accepted |
| R2 | Identity conditioning is only qualified near-frontal (P2-016 scored 10/12; four angled cases failed) | Treat angled identity as unqualified; surface the limitation rather than claiming coverage. Do not re-tune to force a pass |
| R3 | Hardcoding sampler/steps/CFG into the new edit path | All values come from the resolved editor model, matching the existing builders |
| R4 | The AIO serverless worker accepts only its own graph shape | Update both workflow builders and cover both with structure tests |
| R5 | Upstream Phase 1B acceptance is still open | Record it as a known open dependency; do not silently mark Phase 2 accepted on B-106 completion alone |
| R6 | Razor regressions in a large component | Follow `.github/instructions/razor-editing.instructions.md`: full-context read, micro-steps, diagnostics after each edit |

## Definition of done

All ten acceptance scenarios pass in the running application, the full test suite is green, Razor
diagnostics are clean, and P2-053/P2-054/P2-055 are marked complete with recorded evidence.
