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

## Scope — two halves, both built here

### A. Reference data model (additive)

- **`ReferenceViewDescriptor` DTO** — `Axis` (`Face`|`Body`), `YawDeg`, `PitchDeg` (face),
  `BodyRotationDeg` / `BodyPositionKey` (body), `Label`, `CanonicalSlot`.
- **`ViewDescriptorJson`** column added to `SceneImageReferenceAsset` **and** `SceneAsset`.
- **`CanonicalFullBodyAssetId`** added to `CharacterImageIdentityPack` (mirror of the existing
  `CanonicalFaceAssetId`).
- **Required-set rule:** approval requires the canonical face slots **and** the canonical body base.
- **Keep `SceneImageReferenceFaceView` unchanged** — it is the canonical-slot contract for the
  multi-angle compiler; the descriptor carries the extended set. Enum = contract, variety = data.

### B. Asset Manager shell (grouped UI)

- **Grouped tree, root-agnostic** — every root kind follows the same pattern
  `Root → version/profile → view set / assets`:
  - `Character → Identity pack → view set (Face / Full-body / Wardrobe)`
  - `Location → Location profile → reference (plate) / derived (depth / canny / seg)`
  - `Wardrobe / Prop / Style → …`
  This is the **only** view — there is no flat list (unusable at the eventual image count). New root
  kinds plug in without changing the shell.
- **Surface all stores** — `ICharacterImageIdentityService.ListAssetsAsync(packId)` (identity),
  `ISceneAssetService.ListAssetsAsync()` (library), and the location-profile references under the
  same owning root.
- **Group key** — `(RootKind, RootId, AssetKind, ViewKey?)` where `RootKind` ∈ `Character` /
  `Location` / `Wardrobe` / `Prop` / `Style`; `ViewKey` from `CanonicalSlot` or `ViewDescriptorJson`,
  null for non-viewable kinds.
- **Fixed hierarchy per root kind** — no flat mode, no group-by selector.

## Files

- Domain: `DreamGenClone.Domain/RolePlay/CharacterImageIdentityModels.cs`, `SceneAssetModels.cs`,
  and a new `ReferenceViewDescriptor` type.
- Infrastructure: additive SQLite columns + read/write mapping in
  `CharacterImageIdentityRepository.cs` and `SceneAssetRepository.cs`.
- Web: `AssetStudio.razor` refactor + a grouped-tree component.

## Tasks (ordered)

- [ ] B124-001 Add `ReferenceViewDescriptor` (Axis, YawDeg, PitchDeg, BodyRotationDeg,
  BodyPositionKey, Label, CanonicalSlot) + JSON round-trip.
- [ ] B124-002 Add additive SQLite columns: `ViewDescriptorJson` on both tables,
  `CanonicalFullBodyAssetId` on the pack.
- [ ] B124-003 Add repository read/write mapping for the new columns.
- [ ] B124-004 Add the required-set rule: approval requires canonical face slots + canonical body base.
- [ ] B124-005 Build the grouped-tree component (group key, collapse, version/status badges, per-view
  thumbnails, empty-state "add …" hints).
- [ ] B124-006 Surface the identity store, the library, and location-profile references under the owning root.
- [ ] B124-007 Apply search and the existing filters (type / approval / character) within the tree; no flat mode.
- [ ] B124-008 [P] Tests: SQLite round-trip of the descriptor; required-set rejection; grouping of a
  pack with extended face views (pitch) and body views (rotation/position); flat-mode fallback.
- [ ] B124-009 [P] Razor diagnostics + a source-contract test for the grouped view.

## Non-goals

No studio steps (B-121), no body refs (B-122), no pose/structure tools (B-117…B-120), no LoRA
(B-123). This item only lands the model and the shell; feature code must not leak into it.

## Acceptance (the wireframe test)

1. A pack containing extended face views (e.g. "looking down 30°") and body views (rotation +
   position) round-trips and groups correctly in the tree.
2. Approval is refused when the required set (canonical face slots + canonical body base) is missing,
   naming what is missing.
3. Asset Manager shows the grouped tree as the **only** view; search and filters narrow within it.
4. The identity view sets are visible in Asset Manager — a character's actual face/body views, not
   just library assets — and a location profile with its derived control assets groups under its own
   root with no shell changes.
