# B-120 — Asset Manager: structure/layout extraction from source images

**Status:** designed. **State:** `designed`. **Priority:** medium · **Scope:** medium. Additive.

> **Program context:** structural-capability block of `specs/Planning/identity-lora-program-map.md`.
> This item **owns** the derived-asset store + extraction jobs (map §3) and is **consumed by**
> B-117 (OpenPose), B-119 (depth/canny), B-116 (location plate) and B-123 (control images for
> layout-controlled training cells). It **shares the pose store with B-118** — B-118 owns pose
> authoring/library, this item owns the derived record. Not on the identity → body → LoRA critical
> path; scheduled in parallel.
**Purpose:** the Asset-Manager slice that CREATES layout/control images FROM source images — the production half that B-117 (OpenPose) and B-119 (depth/canny) ControlNet renders, and B-116 (location plate re-skin), consume.
**Coordinate boundaries (do NOT duplicate):**
- **B-117** = OpenPose ControlNet composition render path.
- **B-118** = 2D pose editor UX + DWPose extract-from-image + pose library.
- **B-119** = depth/canny ControlNet render path (layout-reference input) + decision workflow.
- **B-120 (this item)** = the **shared derived-asset store + extraction jobs + semantic bindings** that produce and hold the control images all of the above consume.
- **B-116** = img2img re-skin (consumes the raw *location plate*, which B-120 also catalogs).
Related: B-108 (reference-bootstrap), B-032 (SceneAsset / identity/wardrobe aggregates).

---

## 1. Problem
The user must be able to **create a layout/control image from an arbitrary source image** (a real photo, an approved render, a location plate) and have that derived image drive a ControlNet render or a re-skin. Today the Asset Manager has Location/Wardrobe/identity *semantic* assets and a `ControlNet` graph-strategy token, but **nothing produces a derived structure image** (depth/canny/skeleton/segmentation) from a source, and nothing stores it as an approved, provenance-tracked asset the render path can bind to.

## 2. Extraction menu (all runnable on local ComfyUI preprocessors — verified 2026-09-09)
| Source image shows… | Extract (extractor) | Consumed by |
|---|---|---|
| Blocking incl. reclining/multi-body | **Depth** (DepthAnything / MiDaS / Zoe / Metric3D) | B-119 depth-ControlNet (Route C1) |
| Hard geometry to lock | **Canny** (Canny / PyraCanny) | B-119 canny-ControlNet |
| Person(s) in a pose | **Multi-person OpenPose / DWPose** skeleton | B-117 OpenPoseXL2-ControlNet (pose store shared w/ B-118) |
| Scene to split into regions | **Segmentation** (OneFormer / UniFormer) | semantic/regional layout control (future) |
| The scene/location itself | keep the **raw image as the location plate** | B-116 img2img re-skin |

## 3. Design (mirror the identity/wardrobe asset model — B-032 phase-2 aggregates)
1. **Derived asset concept** (a new asset kind under the SceneAsset catalog / alongside `SceneAssetType.Location` and identity packs):
   - `DerivedStructureAsset { Id, SourceImageId, SourceSha256, StructureKind (Depth|Canny|OpenPose|Segmentation|LocationPlate), ExtractorName, ExtractorParamsJson, FileRelativePath, ThumbnailPath, Status (Draft/Approved/Superseded), ApprovedUtc, DescriptorSnapshot }`.
   - Approval/version/supersede/delete-in-use guards copied from `CharacterImageIdentityPack` + `SceneAsset` patterns (approved/superseded assets and their files cannot be deleted while referenced).
2. **Extraction job** (new `IBackgroundJobHandler` on the local ComfyUI provider):
   - Payload `{ AssetId (source), StructureKind, ExtractorParams }`.
   - Runs a ComfyUI graph that `LoadImage(source) → <preprocessor> → SaveImage`, writes the derived PNG via scene-image storage, records provenance + sha, marks the derived asset Approved (or Draft for review).
   - Gated: requires a source image that is Complete/Approved; fail-fast otherwise.
3. **Semantic bindings**: bind a derived asset to a meaning (per location / per pose / per beat-set) so a composer picks "canonical blocking for the pine-clearing rock" rather than a raw file. Reuse/extend the location-profile bootstrap (`ReferenceBootstrapLocationProfile`/`LocationReference`) as the anchor for Location-derived assets.
4. **Consumption contract**: a composition render request carries `{ LayoutReferenceImageId, StructureType, ControlStrength }`; the resolver runs a ControlNet render **only** when the resolved model declares the corresponding graph strategy on a ComfyUI provider — no fallback to a plain render (mirror identity resolver rules). Raw plates additionally feed B-116 re-skin.

## 5. Gating / no-fallback rules
- StructureKind extraction only executes on local ComfyUI (`ImageProtocol.ComfyUi`) with the preprocessor present; explicit diagnostic otherwise.
- Render with a StructureType requires the model row to declare the strategy (`SupportedVisualStrategiesJson`) — no silent plain render.
- Missing/invalid derived-asset reference fails fast with the asset id.

## 6. Non-goals / out of scope
- No ControlNet weight installs (that is B-117/B-119 host work: depth/canny weights).
- No pose *editor* (B-118) and no OpenPose *render* wiring (B-117) / depth-canny render wiring (B-119).
- No training, no 3D staging, no compositing.

## 7. Test plan (suite green; no skipped tests)
- Repository: derived-asset round-trip + approval/supersede/delete-in-use + sha provenance.
- Extraction job: gating (non-ComfyUi source → error), storage writes + provenance, handler stub test.
- Resolver/consumption: missing strategy / wrong provider / bad asset → explicit errors (no-fallback tests ≥5).
- UI contract test for the "derive structure from this image" action visibility.
