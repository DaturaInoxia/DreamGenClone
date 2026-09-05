# B-108 Implementation Plan — Reference Bootstrap Studio

**Status:** Ready for implementation
**Spec:** [spec.md](spec.md)
**Tasks:** [tasks.md](tasks.md)

## Guiding constraint

Reuse everything that already exists for generation, storage, and target-specific promotion. The
only new persistence is: (1) a handful of additive `SceneAsset` fields, (2) a batch record with no
LoRA coupling, and (3) a deliberately minimal Location pair. No compiler, no image client, no
identity-pack mechanic, and no wardrobe mechanic is reimplemented.

## Reuse map (verified)

| Concern | Existing type/method | Location |
|---|---|---|
| Prompt compilation by asset type | `SceneAssetPromptCompiler.Compile(description, assetType, model)` | `DreamGenClone.Web/Application/RolePlay/SceneImagePromptCompilers.cs` |
| Text-to-image generation | `IImageGenerationClient` / dispatcher | `DreamGenClone.Infrastructure/Models/` |
| Same-image view/angle edit | `IImageEditingClient.EditAsync` (proven pattern: `SceneAssetProfilePackJobHandler.AngleEdits`) | `DreamGenClone.Web/Application/RolePlay/SceneAssetProfilePackJobHandler.cs` |
| General asset storage/status | `SceneAsset`, `SceneAssetType`, `SceneAssetStatus`, `SceneAssetKind` | `DreamGenClone.Domain/RolePlay/SceneAssetModels.cs` |
| Character face/body canonical promotion | `ICharacterImageIdentityService.UploadAssetAsync`, `ApprovePackAsync` | `DreamGenClone.Web/Application/RolePlay/CharacterImageIdentityService.cs` |
| Character wardrobe promotion | `CharacterWardrobeLookVersion`, `CharacterWardrobeAssetBinding` | `DreamGenClone.Domain/RolePlay/CharacterAppearanceVersionModels.cs`, `DreamGenClone.Infrastructure/RolePlay/CharacterAppearanceVersionRepository.cs` |
| Asset Manager shell | `/asset-studio` (P2-051/052 contract) | `DreamGenClone.Web/Components/Pages/AssetStudio.razor` |

## Design decisions

### D1 — A new batch concept, deliberately not `CharacterAssetGenerationBatch`

`CharacterAssetGenerationBatch` hard-requires `DatasetId` and is wired only to LoRA-dataset
candidate production. Rather than loosen that record's invariants (which exist for a reason —
LoRA training needs coverage-plan and dataset-freeze guarantees this package does not), this
package introduces `ReferenceBootstrapBatch`: same shape (target, description, count, capability
profile/cell, provider endpoint, dispatch policy, cost basis — reusing the exact durable
production graph types `ProductionIntentSnapshot`/`CompiledMediaRequest`/`ProductionWorkloadItem`/
`ProductionAttempt`), minus any dataset requirement.

*Justification:* keeps the LoRA pipeline's invariants intact for B-107, and keeps this package
genuinely standalone per the corrected understanding that no such standalone path exists today.

### D2 — Candidate curation is additive fields on `SceneAsset`, not a new join table

Mirrors B-106's own approach (three additive nullable fields on `SceneImageRecord`). Added to
`SceneAsset`:

| Field | Type | Purpose |
|---|---|---|
| `CandidateBatchId` | `string?` | Groups sibling candidates; null for non-bootstrap assets |
| `CandidateDecision` | `SceneAssetCandidateDecision?` (`Undecided`/`Accepted`/`Rejected`) | FR8-004 |
| `CandidateNotes` | `string?` | Optional reviewer note |
| `CandidateSourceAssetId` | `string?` | Set only on expansion candidates (FR8-006) |

No new table. Existing `SceneAsset` queries/filters extend naturally to include these.

### D3 — Promotion is always a copy, never a reference

Identity-pack and wardrobe storage are separate storage areas from the general asset catalog with
their own approval/versioning rules. Promotion reads the accepted candidate's bytes and calls the
existing upload/binding method for the target, exactly as `SceneAssetProfilePackJobHandler` already
does for faces. The original `SceneAsset` candidate is never mutated or deleted by promotion
(FR8-012) — this preserves audit history and lets a user re-promote a different accepted candidate
later without losing the first one.

### D4 — Location gets a minimal, distinctly-named pair, not a Phase 3 pull-forward

`ReferenceBootstrapLocationProfile` / `ReferenceBootstrapLocationReference` are new, small,
additive records — status lifecycle and ordered references only. They are **not** named
`LocationProfile`/`LocationReferenceAsset` (the names Phase 3's spec already reserves) specifically
so nothing later silently assumes compatibility. Phase 3, when it starts, makes an explicit
decision: reconcile/import these, or supersede them outright. Either way it is a recorded decision,
not an accident of shared naming.

*Justification:* the user explicitly asked to extend this to Locations now; Phase 3's full
continuity/blocking scope remains properly gated behind the Phase 2 exit gate. This is the
smallest possible slice that satisfies "generate/curate/promote a location reference image" without
touching anything Phase 3 actually gates on (coordinate frames, landmarks, blocking, shots).

### D5 — Expansion reuses the proven same-image edit pattern only

`SceneAssetProfilePackJobHandler.AngleEdits` proves same-image edits work today (rotate head angle,
preserve everything else). This package generalizes that pattern with additional instruction sets
for body pose and wardrobe-angle expansion — still one image in, one image out, no cross-image
merge. Cross-image face-graft (feeding two separate photos into one edit) explicitly waits on
B-106 Phase A and is called out as a non-blocking dependency, not a prerequisite for this package.

### D6 — Strict configuration, no fallback

Every generation/expansion call resolves its model through existing Model Manager configuration.
Missing or unqualified configuration fails fast naming the missing item — consistent with repo-wide
rules and B-106's precedent.

## Implementation phases

### Phase A — Batch and candidate persistence

Add `ReferenceBootstrapBatch`, the additive `SceneAsset` fields, and repository support for
querying/filtering candidates by batch, target, and decision.

**Files:** `DreamGenClone.Domain/RolePlay/SceneAssetModels.cs`,
`DreamGenClone.Domain/RolePlay/ReferenceBootstrapModels.cs` (new),
`DreamGenClone.Application/RolePlay/IReferenceBootstrapService.cs` (new),
`DreamGenClone.Infrastructure/RolePlay/` (additive schema)

**Exit:** a batch persists with N `Undecided` candidates queryable by batch id.

### Phase B — Candidate generation

Wire batch creation to existing compiler + generation client stack, generating N candidates from
one description.

**Files:** `DreamGenClone.Web/Application/RolePlay/ReferenceBootstrapService.cs` (new)

**Exit:** acceptance scenario 1.

### Phase C — Curation

Accept/reject/note actions; rejected candidates remain visible and intact.

**Exit:** acceptance scenario 2.

### Phase D — Expansion

Same-image edit from an Accepted candidate, recording exact source lineage.

**Exit:** acceptance scenario 3.

### Phase E — Promotion (Character Face/Body, Wardrobe)

Wire the three character-facing promotion actions to existing identity-pack and wardrobe-version
mechanics.

**Exit:** acceptance scenarios 4, 5, 7.

### Phase F — Minimal Location domain and promotion

Add `ReferenceBootstrapLocationProfile`/`ReferenceBootstrapLocationReference` and their promotion
action.

**Exit:** acceptance scenario 6.

### Phase G — Asset Manager UI

Implements [ui-contract.md](ui-contract.md): Bootstrap tab, target picker, candidate gallery,
compare mode, per-candidate actions, promotion actions, cross-target filtering.

**Exit:** acceptance scenarios 8, 9; full Playwright pass.

### Phase H — Validation

Full build, full suite, Razor diagnostics, live-session walkthrough of all nine acceptance
scenarios, and an explicit grep-based check that no new code references
`CharacterLoraDataset`/`CharacterLoraDatasetMember`.

## Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | Confusing this batch concept with the LoRA dataset pipeline during future maintenance | Distinct naming (`ReferenceBootstrapBatch` vs `CharacterAssetGenerationBatch`), no shared table, explicit non-goal in spec |
| R2 | Location naming collision with eventual Phase 3 aggregate | Distinct `ReferenceBootstrap*` prefix; FR8-015 requires an explicit reconciliation decision, no silent migration |
| R3 | Users expecting "expand view" to graft an existing face onto a new body | UI must state plainly that expansion is same-image only; cross-image graft is called out as pending B-106 Phase A |
| R4 | Additive `SceneAsset` fields drifting from B-106's own additive fields on `SceneImageRecord` (different table) | No overlap — different entities; verified no shared columns are needed |
| R5 | Razor regressions extending `AssetStudio.razor` | Follow `.github/instructions/razor-editing.instructions.md`: full-context read, micro-steps, diagnostics after each edit |

## Definition of done

All nine acceptance scenarios pass in the running application, the full test suite is green, Razor
diagnostics are clean, and a repository-wide search confirms zero new references to
`CharacterLoraDataset`/`CharacterLoraDatasetMember` from this package's code.
