# B-108 — Reference Bootstrap Studio (Character / Wardrobe / Location)

**State:** designed — ready for implementation
**Created:** 2026-09-05
**Owner surface:** Asset Manager (`/asset-studio`)

## Why this exists

The user asked for the standard current-technology workflow for creating a **wholly fictional
character** (no source photo): generate several candidates from a description → compare them
side by side → curate/accept → expand into more views → promote an accepted result to be the
canonical face/body/wardrobe/location reference. Extended to Wardrobe and Location as well.

Investigation found **no standalone implementation of this** exists today:

- `CharacterAssetGenerationService.CreateBatchAsync` hard-requires `batch.DatasetId`
  (`Require(batch.DatasetId, "Dataset id")`) and its only UI entry point,
  [LoraDatasetGeneration.razor](../../../DreamGenClone.Web/Components/Pages/LoraDatasetGeneration.razor),
  forces creating a `CharacterLoraDataset` before a single candidate can be generated. Its curation
  UI ([LoraDatasetCuration.razor](../../../DreamGenClone.Web/Components/Pages/LoraDatasetCuration.razor))
  operates on `CharacterLoraDatasetMember` rows, not general assets. This pipeline is a **LoRA
  training-data appendage**, not a standalone bootstrap tool — and LoRA activation is deferred to
  B-107, out of scope for Phase 2/3.
- `SceneAssetProfilePackJobHandler` ("Generate Profile Pack") generates **exactly one** image per
  view and commits it directly — no candidate comparison, no accept/reject step.
- `SceneAssetType` already has `Location`, `Wardrobe`, `CharacterFace`, `CharacterBody` as
  first-class values, and the asset-type-aware compiler (`SceneAssetPromptCompiler.Compile`)
  already branches per type — but there is **no aggregate** behind `Location` (no
  `LocationProfile`, no reference-asset binding) anywhere in the domain. Phase 3's location domain
  is entirely unbuilt.
- `CharacterWardrobeLookVersion` / `CharacterWardrobeAssetBinding` **do** already exist (B-032
  Phase 2 section G, complete) with draft/approve/supersede and asset-binding support — reusable
  as-is.

## Scope

Build a **standalone candidate generation → curation → canonical promotion** capability, hosted in
Asset Manager, decoupled from `CharacterLoraDataset` entirely, that targets three destinations:

1. **Character identity** (Face or FullBody) — promotes into the existing
   `CharacterImageIdentityPack` via the existing `ICharacterImageIdentityService`.
2. **Character wardrobe** — promotes into the existing `CharacterWardrobeLookVersion` via the
   existing `CharacterWardrobeAssetBinding`.
3. **Location** — a genuinely new, deliberately **minimal** slice (name, description, status,
   ordered reference bindings only — no coordinate frame, no landmarks-with-dimensions, no
   lighting/time/weather state, no blocking). Full location continuity remains B-032 Phase 3's
   scope, gated behind the Phase 2 exit gate as already sequenced. This package's location records
   are named distinctly (`ReferenceBootstrapLocationProfile`) so they are never confused with, or
   silently assumed compatible with, Phase 3's eventual `LocationProfile` aggregate.

## What is reused, unchanged

| Concern | Reused as-is |
|---|---|
| Prompt compilation for candidate generation | `SceneAssetPromptCompiler.Compile(description, assetType, model)` |
| Image generation | Existing `IImageGenerationClient` / dispatcher stack |
| Same-image view/pose expansion | `IImageEditingClient.EditAsync` (proven by `SceneAssetProfilePackJobHandler`'s angle-edit pattern) |
| General asset storage | `SceneAsset` / `SceneAssetType` / `SceneAssetStatus` |
| Character face/body canonical promotion | `ICharacterImageIdentityService` (`UploadAssetAsync`, `ApprovePackAsync`) |
| Character wardrobe promotion | `CharacterWardrobeLookVersion` + `CharacterWardrobeAssetBinding` repository |
| Asset Manager shell | `/asset-studio` (P2-051/P2-052 shared shell contract) |

## What is genuinely new

- A standalone `ReferenceBootstrapBatch` concept (candidate generation, no `DatasetId`).
- Additive candidate-curation fields on `SceneAsset` (batch id, decision, notes) — no new join
  table, mirroring B-106's additive-fields approach.
- A minimal `ReferenceBootstrapLocationProfile` / `ReferenceBootstrapLocationReference` pair.
- Asset Manager UI: a Bootstrap tab with target picker, candidate gallery, compare, accept/reject,
  expand-view, and target-specific promote-to-canonical actions.

## Dependency on B-106

Generating a **new, independent** full-body/wardrobe/location candidate needs nothing from B-106 —
it's plain text-to-image generation, available today. **Grafting an already-accepted face onto a
separately generated body** (rather than accepting whatever face that body's own generation
produced) requires B-106 Phase A's ordered-reference Qwen edit operation (B106-001→007), which does
not exist yet. This package's "expand a canonical view" step is scoped to same-image edits (proven,
available today); the cross-image face-graft step is explicitly marked as depending on B-106 Phase A
and is not blocked from proceeding without it — same-image expansion and independent-candidate
generation both work standalone.

## Documents

| File | Purpose |
|---|---|
| [spec.md](spec.md) | Goal, non-goals, user stories, functional requirements, acceptance scenarios, exit gate |
| [plan.md](plan.md) | Design decisions, reuse map, new additive schema, phased implementation, risks |
| [ui-contract.md](ui-contract.md) | Asset Manager Bootstrap tab: layout, candidate gallery, compare, promotion actions, states |
| [tasks.md](tasks.md) | Ordered, checkable implementation tasks |

## Controlling upstream documents

- `specs/Planning/B-032-scene-image-generator/phase-2-character-identity/production-ui-contract.md` — Asset Manager shared shell (extended, not replaced)
- `specs/Planning/B-032-scene-image-generator/phase-3-location-and-multi-pov/spec.md` — full location-continuity scope (this package's location slice is explicitly smaller)
- `specs/Planning/B-106-production-studio-staged-workflow/plan.md` — the ordered-reference edit mechanism this package's face-graft step depends on
- `specs/Planning/backlog.md` — B-107 (LoRA deferral, this package does not depend on or feed it)
