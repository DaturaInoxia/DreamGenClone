# B-110 Plan — Unified Iterative Image-Edit Workbench

**Status:** Draft — awaiting go-ahead before implementation.
**Related:** B-106 (Production Studio staged workflow), B-108 (Reference Bootstrap Studio / Asset
Manager), B-032 Phase 1b (vision-aware image editing).

## Problem

The same conceptual action — "view an image, describe a change, get a grounded/compiled edit, run
it, compare before/after, and iterate on the result" — is implemented three separate times in this
codebase, at three different levels of completeness, and none of them share code:

| Surface | File | Loop today |
|---|---|---|
| Ad-hoc roleplay scene edit | [SceneImageEditor.razor](../../../DreamGenClone.Web/Components/Pages/SceneImageEditor.razor) (`/roleplay/image-editor/...`) | **Full loop.** View image → describe intent → vision-compiler grounds it (asks a clarifying question if needed) → editable compiled prompt → run → before/after → `Edit result` reopens the editor on the new image to iterate again. |
| Production Studio Identity stage | [SceneImageStudio.razor](../../../DreamGenClone.Web/Components/Pages/SceneImageStudio.razor#L3584) | One-shot. Auto-generated instruction string, single `Apply Identity` button, no preview/describe/iterate. |
| Production Studio Finish stage | [SceneImageStudio.razor](../../../DreamGenClone.Web/Components/Pages/SceneImageStudio.razor#L1127) | One-shot. Freeform textarea + change-class radio + `Enqueue Finish` button, no compiler grounding, no before/after, no iterate. |
| Asset Manager image edit | [AssetEdit.razor](../../../DreamGenClone.Web/Components/Pages/AssetEdit.razor) + [ImageEditWorkbench.razor](../../../DreamGenClone.Web/Components/Assets/ImageEditWorkbench.razor) | One-shot, bulk. Freeform textarea + model picker + output count (1–8) → queues N edits, shows only their ids. No compiler grounding, no before/after, no iterate. |

This plan unifies all three onto the one loop that already works well (`SceneImageEditor.razor`'s),
instead of leaving Identity/Finish/Asset editing permanently behind it.

## Evidence already gathered

- `SceneImageService.EnqueueEditAsync` ([SceneImageService.cs](../../../DreamGenClone.Web/Application/RolePlay/SceneImageService.cs#L648)) already propagates `ProductionGroupId` from the source image and stamps `ProductionStage = Finish` when present — the RP edit path is already half-wired into Production Studio's lineage, just never surfaced from the staged Finish panel.
- That same path never sets `FinishChangeClass` (stays `null`) or `IdentityStale` (stays `false`) — a real governance gap against [ui-contract.md](../B-106-production-studio-staged-workflow/ui-contract.md#L91), which requires a Geometry Finish edit to mark its child identity-stale and block approval. Any reuse for Finish must close this, not inherit it.
- `SceneImageService.EnqueueIdentityAsync` ([SceneImageService.cs](../../../DreamGenClone.Web/Application/RolePlay/SceneImageService.cs#L261)) takes a freeform instruction string; it resolves identity readiness and attaches ordered canonical-face references itself at execution time ([SceneImageEditingJobHandler.cs](../../../DreamGenClone.Web/Application/RolePlay/SceneImageEditingJobHandler.cs#L169)). It has no compiler session/attempt/revision and no iterate hook.
- `ISceneImageEditCompilationService.CreateSessionAsync` ([SceneImageEditCompilationService.cs](../../../DreamGenClone.Web/Application/RolePlay/SceneImageEditCompilationService.cs#L40)) is keyed by RolePlay `SessionId`/`InteractionId` and reads/writes `SceneImageRecord` via `ISceneImageRepository`. The Asset Manager's images (`SceneAssetImage` via `ISceneAssetRepository`, [SceneAssetService.cs](../../../DreamGenClone.Web/Application/RolePlay/SceneAssetService.cs#L140)) are a structurally different domain type with no RolePlay session/interaction at all — the compiler cannot be pointed at an asset image without a generalization step.

## Root cause

Three independent one-shot edit actions were each built against their own surface's immediate need
(Identity's reference attachment, Finish's change-class, Asset Manager's bulk-variant generation)
without reusing the vision-compiler describe/ground/iterate loop that already existed. The fix is
not three more one-shot bolt-ons; it is extracting the loop once and adapting each surface to it.

## Target design

### Shared core (extract, don't duplicate)

Extract the describe → prepare (compile) → clarification → editable prompt → run → before/after →
iterate block out of `SceneImageEditor.razor` into a shared component, parameterized by:

- `SourceImageBytesProvider` — how to read the current source image (today: `ISceneImageStorageService`; for Assets: the asset's own storage accessor).
- `CompilationContext` — an id pair the compiler service can key sessions/attempts/revisions against, generalized so it is not hard-coded to RolePlay `SessionId`/`InteractionId` (see Open Decision 1).
- `RunAction` — the exact enqueue call to make once a compiled prompt is accepted (differs per surface: generic RP edit, Identity edit with reference attachment, Finish edit with change-class, or Asset edit).
- Optional `ExtraControls` render fragment — for the fields that are NOT part of the generic loop but must still appear alongside it: Identity's read-only ordered-reference readiness list; Finish's change-class radio + adult-content toggle; Asset Manager's output-count (see Open Decision 2).

### Per-surface adapters

1. **Roleplay ad-hoc edit** (`SceneImageEditor.razor`) — becomes the reference host of the shared component with no `ExtraControls`; behavior unchanged from today.
2. **Production Studio Identity stage** — keeps its existing read-only ordered-reference readiness panel (unchanged, it is correct today) rendered as `ExtraControls` above the shared workbench, replacing the single `Apply Identity` button. `EnqueueIdentityAsync`/`SceneImageIdentityRequest` gain the same optional compiler-provenance fields `EnqueueEditAsync` already has (edit session/attempt/revision id + source/prompt checksums), so an Identity attempt is replayable exactly like a generic edit, while the existing readiness-resolution + reference-attachment logic in `SceneImageEditingJobHandler.ExecuteIdentityAsync` is untouched.
3. **Production Studio Finish stage** — keeps its change-class radio + adult-content toggle as `ExtraControls`, replacing the freeform textarea + `Enqueue Finish` button. `EnqueueFinishAsync`/`SceneImageFinishRequest` gain the same compiler-provenance fields; `FinishChangeClass` flows through to the created record so `IdentityStale` is set correctly on Geometry — this also fixes the governance gap noted above for the *existing* generic-edit-into-Finish path.
4. **Asset Manager** (`AssetEdit.razor` / `ImageEditWorkbench.razor`) — replaces the freeform textarea + bulk output-count with the shared workbench (single describe/compile/run/iterate loop) plus an `ExtraControls` output-count field for users who still want N variants of the same compiled prompt. Requires generalizing the compilation session key (Open Decision 1) so `ISceneImageEditCompilationService` can compile against a `SceneAssetImage` instead of only a `SceneImageRecord`.
5. **`Edit result` navigation stays in-panel** for Production Studio (reselects the new attempt as the workbench's new source without leaving the Studio) and stays as today's page navigation for the standalone editor and Asset Manager.

Composition (stage 1) stays out of scope — it is prompt-to-image generation, not source-image
editing, and does not fit this loop.

## Contract changes required

- `SceneImageIdentityRequest` / `EnqueueIdentityAsync`: add optional `EditSessionId`, `CompilationAttemptId`, `PromptRevisionId`, `SourceImageSha256`, `PromptSha256`.
- `SceneImageFinishRequest` / `EnqueueFinishAsync`: same optional compiler-provenance fields; ensure `FinishChangeClass` is persisted and `IdentityStale` is computed from it.
- `ISceneImageEditCompilationService` / `CreateSceneImageEditSessionRequest`: generalize the session key away from a hard `SessionId`+`InteractionId` pair (see Open Decision 1) so Asset Manager images can create compilation sessions.
- `SceneAssetService`: add an edit-compilation-aware enqueue path that still ends at `SceneAssetImage` records (today's `EnqueueImageEditAsync` signature stays for any caller that still wants a raw bulk instruction, but the Asset Manager UI stops calling it directly).
- New shared Razor component (name TBD, e.g. `ImageEditIterationWorkbench.razor`) replacing the compiler/run/iterate block currently inline in `SceneImageEditor.razor`.

## Task breakdown (phased — implement and test each phase before the next)

1. Extract the shared component from `SceneImageEditor.razor` with zero behavior change; re-verify the standalone editor still works identically (regression only, no new capability yet).
2. Extend `SceneImageIdentityRequest`/`EnqueueIdentityAsync` with optional compiler provenance; wire Identity stage panel onto the shared component; keep the existing readiness panel as `ExtraControls`.
3. Extend `SceneImageFinishRequest`/`EnqueueFinishAsync` with optional compiler provenance and confirm `FinishChangeClass`/`IdentityStale` propagate correctly (add the regression test this closes); wire Finish stage panel onto the shared component.
4. Generalize `ISceneImageEditCompilationService`'s session key (Open Decision 1) with a migration-safe additive change (no breaking change to existing RP edit sessions).
5. Wire Asset Manager's `AssetEdit.razor` onto the shared component via the generalized compilation session; decide and implement Open Decision 2 (bulk output count) as `ExtraControls`.

## Test impact

- `SceneImageStudioUiContractTests`, `SceneImageEditCompilationJobTests`, `SceneImageServiceJobTests` — updated for the new optional request fields and stage-scoped enqueue behavior.
- New regression: Identity attempts created via the compiler path still carry ordered reference bindings.
- New regression: Finish attempts created via the compiler path carry `FinishChangeClass` and set `IdentityStale` correctly on Geometry.
- New tests for the generalized compilation session key against `SceneAssetImage`.
- `AssetStudioUiContractTests` (exists today per repo test listing) — updated for the new Asset edit panel markup.

## Risks / blast radius

- Touches an already-shipped B-106 staged workflow (`ui-contract.md`) and a separate, independently
  evolving Asset Manager (B-108) — changes must not regress either's existing, tested behavior.
- The compilation-session generalization (Open Decision 1) is the highest-risk step: it changes a
  currently RP-only service's key shape. It must be additive (nullable/optional new fields), not a
  breaking rename, so existing RP edit sessions in the live dev DB keep working unmodified.
- `SceneAssetService.EnqueueImageEditAsync`'s existing bulk-instruction contract must keep working
  for any other caller until it is confirmed unused, per this repo's "no dangling fallback, but no
  silent breakage either" convention — confirm callers before removing it.

## Open decisions (need your confirmation before implementation starts)

1. **Compilation session key generalization approach for Asset Manager reuse** — either (a) keep `SceneImageEditSession`/`Attempt`/`Revision` rows as the single source of truth and make `SessionId`/`InteractionId` optional while adding an alternate `SceneAssetImageId` discriminator, or (b) keep the compiler engine itself decoupled from any specific repository (compile against raw bytes + intent only) and give each surface its own thin session-storage adapter. (b) is architecturally cleaner and lower-risk to the existing RP path; recommending (b).
2. **Asset Manager's bulk "N outputs" feature** — today's `ImageEditWorkbench` can queue up to 8 variants of one instruction in a single action. The shared loop is single-result-per-run-and-iterate. Recommend keeping bulk count as an `ExtraControls` field that, when >1, runs the same accepted compiled prompt N times instead of requiring N separate describe/compile passes — confirm this preserves the capability Asset Manager users rely on today.
3. Whether to update [B-106 ui-contract.md](../B-106-production-studio-staged-workflow/ui-contract.md) in place to reflect the new Identity/Finish panel design, or leave it as the historical record and let this plan supersede it for those two sections — no preference, just needs a decision before Studio markup changes land.

## Non-goals

- No change to Composition (stage 1) generation.
- No change to the underlying image-generation/editing provider clients or ComfyUI/RunPod workflows.
- No change to `CharacterAssetGenerationBatch`/LoRA dataset production (B-107) or the Reference Bootstrap batch mechanics (B-108) beyond the single edit-panel UI swap described above.
