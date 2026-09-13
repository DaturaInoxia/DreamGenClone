# B-124 — Reference Model Foundation & Asset Manager Shell

**State:** `planned` (design artifact — no code written). **Scope:** medium.
**Program:** `specs/Planning/identity-lora-program-map.md` — **stage 1, the first thing built.**
**Foundation it implements:** `specs/Planning/identity-and-reference-model.md` §2 (reference model)
and §4 (grouping).

## Purpose

Make the reference data model and the Asset Manager shell support the whole identity → body → LoRA
workstream **up front**, so every later item plugs into a stable schema and a working navigation,
instead of each defining its own slice and drifting. This item owns the model and the shell; it
builds **no features** (no studio steps, no body refs, no pose, no LoRA).

## Why it is first

Features must never define the schema as a side effect. If B-121 added `ViewDescriptorJson` while
building the face studio, and B-122 added the body descriptor while building body refs, the two would
drift and the Asset Manager would group the wrong shape twice. B-124 lands the schema and the shell
once, and B-121/B-122/B-123 consume them.

## Scope — three parts, all built here before any feature item

### A. Reference data model (additive)

- **`ReferenceViewDescriptor` DTO** — `Axis` (`Face`|`Body`), `YawDeg`, `PitchDeg` (face),
  `BodyRotationDeg` / `BodyPositionKey` (body), `Label`, `FaceCanonicalSlot`,
  `BodyCanonicalSlot`. Exactly one slot field may be set and it must match `Axis`.
- **Canonical contracts** — keep `SceneImageReferenceFaceView` unchanged and add the symmetric
  five-value `SceneImageReferenceBodyView` (`Front`, `ThreeQuarterLeft`, `ThreeQuarterRight`,
  `ProfileLeft`, `ProfileRight`) plus `SceneImageReferenceBodyState` (`Clothed`, `Unclothed`). Enum
  = contract; descriptor angles/positions = data. `BodyState` is required exactly for `FullBody`
  assets and is never inferred from prompts, filenames or pixels.
- **`ViewDescriptorJson`**, nullable **`BodyView`** and nullable **`BodyState`** columns added to
  `SceneImageReferenceAsset` **and** `SceneAsset`.
- **`CanonicalFullBodyAssetId`** added to `CharacterImageIdentityPack` (mirror of the existing
  `CanonicalFaceAssetId`).
- **Required `PackScope`** (`FaceOnly` | `BodyComplete`) added to the pack. Migration explicitly
  writes existing packs as `FaceOnly`; missing/unknown scope fails fast and is never inferred.
- **Required-set rule by declared scope:** `FaceOnly` approval requires all five canonical face
  slots. `BodyComplete` approval requires all five canonical face slots, matching clothed and
  unclothed five-slot body sets, and `CanonicalFullBodyAssetId` pointing at the approved unclothed
  `Front` body asset. B-121 creates `FaceOnly`; B-122 creates/promotes `BodyComplete`; B-123 accepts
  only `BodyComplete`.
- **Keep `SceneImageReferenceFaceView` unchanged** — it is the canonical-slot contract for the
  multi-angle compiler; the descriptor carries the extended set. Enum = contract, variety = data.

### B. Asset Manager shell (grouped UI)

> **Superseded 2026-09-11 by `studio-navigation-and-layout.md` §2–§4.** The shell is now an **owner
> index** (one entry per owner linking to `/characters/{id}` / `/locations/{id}`) plus a Cleanup
> bucket; the pack/view-set hierarchy lives **inside** the owner's studio, not in the list.

- **Grouped tree, root-agnostic** — every root kind follows the same pattern
  `Root → version/profile → view set / assets`:
  - `Character → Identity pack → view set (Face / Full-body / Wardrobe)`
  - `Location → Location profile → reference (plate) / derived (depth / canny / seg)`
  - `Wardrobe / Prop / Style → …`
  This is the **only** view — there is no flat list (unusable at the eventual image count). New root
  kinds plug in without changing the shell.
- **Surface all stores through one projection** — `ICharacterImageIdentityService.ListAssetsAsync(packId)`
  (identity), `ISceneAssetService.ListAssetsAsync()` (library), and location-profile references map
  to rows keyed by `(SourceStore, SourceAssetId)`. Every row supplies the group key, top-level owner
  id/name, version/status metadata and asset role; source ids from different stores never collide.
- **Group key** — `(RootKind, RootId, AssetKind, ViewKey?)` where `RootKind` ∈ `Character` /
  `Location` / `Wardrobe` / `Prop` / `Style`; `RootId` is the immediate versioned/profile aggregate
  (identity-pack id, location-profile id, wardrobe-look-version id, etc.), while separate projection
  metadata identifies the stable outer owner. `ViewKey` comes from the axis-appropriate canonical
  slot or normalized descriptor key; body keys include `BodyState`. It is null for non-viewable kinds.
- **Fixed hierarchy per root kind** — no flat mode, no group-by selector.

### C. Shared image create/edit primitive + pose foundation

The Asset Manager is not just a viewer — it is the single place image **creation** and **editing**
are defined, so Asset Studio, Production Studio and the roleplay image editor stop maintaining
parallel pipelines. A fix or new feature lands once and applies everywhere.

- **One create/generate primitive** — a single request shape (prompt, model, size, pose, region,
  references) and one enqueue path, replacing the duplicated asset-studio edit path
  (`SceneAssetImageEditCompilationService` + `SceneAssetImageEditJobPayloads`) and the production
  edit path (`SceneImageEditingJobHandler` + scene-image edit compiler) with one shared service.
- **One edit primitive** — the same request shape with a source image; prompt compilation, model
  resolution, pose/region application and durable job enqueue happen exactly once.
- **Pose as a first-class parameter** — the primitive accepts a pose (from the library or a fresh
  DW Pose extraction), so "apply pose X with model Y" is one shared action across every surface.
- **DW Pose extract client + `PosePreset` library store** — the pose foundation lives here:
  extract-from-image via the local DWPreprocessor, and a seeded, searchable pose library.
- **B-117/B-118 are re-scoped to consume this primitive** — the pose studio UI and the ControlNet
  render route build on the shared path rather than adding a second pose/edit path.
- **Delete the duplicate paths** — migrate `ImageEditWorkbench`/`PromptAssetCreator` (asset studio)
  and `SceneImageEditor`/`CompositionComposer` (production) onto the one primitive.

## Files

- Domain: `DreamGenClone.Domain/RolePlay/CharacterImageIdentityModels.cs`, `SceneAssetModels.cs`,
  and a new `ReferenceViewDescriptor` type.
- Infrastructure: additive SQLite columns + read/write mapping in
  `CharacterImageIdentityRepository.cs` and `SceneAssetRepository.cs`.
- Web: `AssetStudio.razor` refactor + a grouped-tree component.

## Tasks (ordered)

- [x] B124-001 Add `SceneImageReferenceBodyView`, `SceneImageReferenceBodyState` and
  `ReferenceViewDescriptor` (Axis, YawDeg, PitchDeg, BodyRotationDeg, BodyPositionKey, Label,
  FaceCanonicalSlot, BodyCanonicalSlot), including axis/slot/state validation and JSON round-trip.
- [x] B124-002 Add additive SQLite columns: `ViewDescriptorJson` + `BodyView` + `BodyState` on both
  asset tables, `CanonicalFullBodyAssetId` + required `PackScope` on the pack. The migration
  explicitly writes all existing packs as `FaceOnly`; no read-time fallback/default is permitted.
- [x] B124-003 Add repository read/write mapping for the new columns.
- [x] B124-004 Add the scope-specific required-set rules and explicit diagnostics naming every
  missing canonical slot or invalid canonical pointer.
- [x] B124-005 Build the grouped-tree component (group key, collapse, version/status badges, per-view
  thumbnails, empty-state "add …" hints).
- [x] B124-006 Build the cross-store tree projection and surface identity, library and
  location-profile references under the owning root without id collisions or version collapse.
- [x] B124-007 Apply search and the existing filters (type / approval / character) within the tree; no flat mode.
- [x] B124-008 [P] Tests: SQLite round-trip of the descriptor; required-set rejection; grouping of a
  pack with extended face views (pitch), canonical/extended body views (rotation/position), two pack
  versions under one character, and colliding ids from different stores; assert no flat-mode route,
  selector, or fallback exists.
- [x] B124-009 [P] Razor diagnostics + a source-contract test for the grouped view.
- [x] B124-010 Design the unified create/edit request contract (`MediaEditRequest`: prompt, model,
  source image + sha, pose, region, references, candidate batch, correlation id) as one DTO shared by
  both surfaces.
- [ ] B124-011 Add the DW Pose extract client + `PosePreset` library store (seed + search), as the
  pose foundation of the shared primitive. **Store done** (`PosePreset` model + `PosePresetRepository`
  + tests); seed importer + DW Pose extract client still pending.
- [ ] B124-012 Implement the single create/edit primitive + durable job (replacing the two parallel
  compilation paths) and route both surfaces through it.
- [ ] B124-013 Re-scope B-117/B-118 to consume the shared primitive (pose render route + pose studio
  UI build on it; no second edit path).
- [ ] B124-014 [P] Tests: one primitive serves asset-studio and production requests; a pose is
  applied through the same path in both; the old duplicate compilers are removed; no regression in
  either surface's contract tests.

## Non-goals

No studio steps (B-121), no body refs (B-122), no LoRA (B-123). B-117/B-118 remain separate items
for the pose *studio UI* and the ControlNet *render route* — this item lands only the shared
create/edit primitive and the DW Pose + pose-library foundation they build on. Feature code (studio
wizards, body refs, LoRA cells) must not leak into it.

## Acceptance (the wireframe test)

1. A pack containing extended face views (e.g. "looking down 30°") and body views (rotation +
   position) round-trips and groups correctly in the tree.
2. `FaceOnly` approval succeeds only with all canonical face slots. `BodyComplete` approval succeeds
  only with all canonical face slots, matching clothed/unclothed canonical body sets and a valid
  unclothed-front canonical body base. Missing/unknown scope, state or assets fail explicitly;
  existing migrated packs remain explicitly `FaceOnly`.
3. Asset Manager shows the grouped tree as the **only** view; search and filters narrow within it.
4. The identity view sets are visible in Asset Manager — a character's actual face/body views, not
  just library assets — and a location profile with its derived control assets groups under its own
  root with no shell changes. Multiple versions remain distinct, and equal source ids from different
  stores remain distinct rows.
5. Asset Studio, Production Studio and the roleplay image editor all produce edits through the one
   shared primitive; a pose is applied through the same path in each; the duplicated compilers are
   gone.
