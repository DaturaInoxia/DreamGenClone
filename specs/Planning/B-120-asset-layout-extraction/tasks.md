# B-120 — Asset Manager: structure/layout extraction from source images: tasks

Contract: `plan.md` in this folder. Coordinate boundaries: do NOT build B-117 (OpenPose render), B-118 (pose editor), or B-119 (depth/canny render) work here — this item is the shared derived-asset store + extraction jobs + bindings they consume.
Repo rules: no `git restore`; no fallback branches (capability-gated, fail-fast); suite green; no skipped tests; `.razor` edits follow razor-editing rules; additive only.

Legend: `[ ]` not started, `[X]` done.

---

## Phase 1 — Domain + repository (derived structure asset)
- [ ] **T01** Add the `DerivedStructureAsset` model + `StructureKind` enum (`Depth|Canny|OpenPose|Segmentation|LocationPlate`) beside the identity/wardrobe asset models (B-032 pattern): `{ Id, SourceImageId, SourceSha256, StructureKind, ExtractorName, ExtractorParamsJson, FileRelativePath, ThumbnailPath, Status (Draft/Approved/Superseded), ApprovedUtc, DescriptorSnapshot }`. Acceptance: enum round-trips (non-zero values); model compiles.
- [ ] **T02** Repository `DerivedStructureAssetRepository` (self-contained schema like `CharacterImageIdentityRepository`): CRUD + approval/supersede + delete-in-use guard (an Approved/Superseded asset referenced by a render cannot be deleted). Acceptance: round-trip + guard tests (incl. delete-draft-restores-parent analogue).
- [ ] **T03** Storage + ingest: derive sha256/byteLength/mediaType + PNG dims at save; store under the scene-image root (e.g. `layout/{sourceId}/{assetId}.png`); reject non-image. Acceptance: ingest test + remove-file-on-reject.

## Phase 2 — Extraction job (local ComfyUI preprocessors)
- [ ] **T04** ComfyUI client: preprocessor workflow builders — `LoadImage(source) → <preprocessor per StructureKind> → SaveImage` for Depth (DepthAnything), Canny (PyraCanny), OpenPose/DWPose (DWPreprocessor), Segmentation (OneFormer). Acceptance: workflow-builder unit tests (node per kind).
- [ ] **T05** New `BackgroundJobTypes.SceneAssetStructureExtraction = "scene-asset-structure-extraction"` + `DerivedStructureExtractionJobHandler` (+ payload `{ SourceAssetId, StructureKind, ExtractorParams }`): resolves source (Complete/Approved, fail-fast), runs extraction on a ComfyUI provider (`ImageProtocol.ComfyUi` only — explicit diagnostic otherwise), stores the derived PNG, sets Status Approved (or Draft for review). Register in `Program.cs`. Acceptance: gating tests (non-ComfyUi → error; missing source → error); handler stub test.
- [ ] **T06** Service entry (`ISceneAssetService.EnqueueStructureExtractionAsync(...)` mirroring existing enqueue patterns) + dedupe key `scene-asset-structure-extraction:{assetId}:{kind}`. Acceptance: service validation + dedupe tests.

## Phase 3 — Consumption contract + gating
- [ ] **T07** Render/consumption: a request field set `{ LayoutReferenceImageId, StructureType, ControlStrength }` resolved from an Approved derived asset (bindings: per location/pose). Persist on the consuming image record for provenance. Acceptance: round-trip; ValidateImage requires a structure source when StructureType set.
- [ ] **T08** Capability gate: extraction/consumption gated on the model declaring the matching graph strategy (`SupportedVisualStrategiesJson`) on a ComfyUI provider; resolver fail-fast (no fallback to a plain render). Acceptance: ≥5 no-fallback tests.

## Phase 4 — UI
- [ ] **T09** Asset Manager / Studio action: "Derive structure from this image" on an approved/render image → pick StructureKind (+ params) → enqueue extraction → show derived asset with thumbnail + provenance + Approve/Supersede. Acceptance: UI contract test; razor rules followed.

## Phase 5 — Coordination + validation
- [ ] **T10** Confirm boundaries with B-117/B-118/B-119 (shared store consumed by B-117/119 renders; pose assets align with the B-118 pose-library store — no duplicate tables). Update those items' plans to reference B-120 as the store. Acceptance: no overlapping schema; cross-refs present.
- [ ] **T11** Full suite green (fix forward). Live validation: derive a Depth asset from a real beat image on the local host → visual review (honest pass/fail); then one depth-ControlNet render through B-119 consuming it. Update this file + plan.md + backlog B-120 status when validated.
