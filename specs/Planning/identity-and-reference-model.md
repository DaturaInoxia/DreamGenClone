# Character Reference Model & Component Architecture

**State:** Foundation analysis — read this before changing any identity/reference/LoRA plan.
**Created:** 2026-09-11. **Grounds:** `identity-lora-program-map.md` and the B-121/B-122/B-123 plans.

The plans kept treating capabilities (pose, analysis, upscale, faceid, compilers) as a flat list of
optional features. That is why they felt disconnected. This document does the deep analysis: it
defines the **underlying reference model** everything else hangs off, and the **component
architecture** that serves it. If this model is wrong, every plan above it drifts.

---

## 1. The problem, stated precisely

Three structural facts in the code (verified 2026-09-11) make the current plans insufficient:

| Fact | Where | Consequence |
|---|---|---|
| Face views are a **fixed 5-value enum** — `Front`, `ThreeQuarterLeft/Right`, `ProfileLeft/Right` | `SceneImageReferenceFaceView` | Cannot represent "looking up", "looking down", or any intermediate yaw between front and profile. |
| **Full-body assets have no view descriptor at all** — `FaceView` is "only meaningful for Face assets" | `SceneImageReferenceAsset.FaceView`, `SceneAsset.FaceView` | Cannot express "body base / angle / rotation / position", which the LoRA set fundamentally needs. |
| The Asset Manager list is a **flat table** (Name/Kind/Status/Created) filtered only | `AssetStudio.razor` | Hundreds of rows; no grouping by character → pack → view set. |
| Quality/analysis are **external CLIs or non-blocking notes**, not wired | `ReferenceImageQualityAnalyzer`, `tools/consistency-scoring`, `tools/eye-validation` | The user cannot run and read the analysis in the UI. |
| The identity pack has a **singular** canonical face | `CharacterImageIdentityPack.CanonicalFaceAssetId` | No notion of a required **view set** (face *and* body). |

Everything the user asked for — more face profiles, body angles/rotations/positions, grouped asset
navigation, and the analysis/upscale/faceid/compiler stack — is blocked by one or more of these.

**Resolution:** these are not fixed piecemeal by the feature plans. The reference model and the
Asset Manager shell are built **first** as B-124 (stage 1 of `identity-lora-program-map.md` §5).
Features then consume a stable schema and shell; they never define either.

---

## 2. The reference model

A reference asset answers one question: **"what does the character look like from this view?"** The
current model encodes only a coarse yaw bucket for faces, and nothing for body pose. Generalise it.

### 2.1 ReferenceViewDescriptor (the core type)

One descriptor for both face and body views. The old enum stays as the **canonical slot**; the
descriptor carries the fine-grained view.

```
ReferenceViewDescriptor
  Axis             Face | Body            which part of the character this view shows
  YawDeg           int                  -90 (left profile) … 0 (front) … +90 (right profile)
  PitchDeg         int                  -90 (looking up) … 0 (level) … +90 (looking down)   [Face]
  BodyRotationDeg  int?                 torso rotation relative to camera                   [Body]
  BodyPositionKey  string?              standing | sitting | kneeling | lying | …           [Body]
  Label            string               human label, e.g. "looking down 30°"
  FaceCanonicalSlot SceneImageReferenceFaceView?  canonical face contract — null for body/extended face views
  BodyCanonicalSlot SceneImageReferenceBodyView?  canonical body contract — null for face/extended body views
```

`SceneImageReferenceBodyView` is a new five-value canonical-slot enum: `Front`,
`ThreeQuarterLeft`, `ThreeQuarterRight`, `ProfileLeft`, `ProfileRight`. It is deliberately separate
from `SceneImageReferenceFaceView`: both are bounded compiler/approval contracts, while yaw, pitch,
rotation and position remain unbounded descriptor data. Exactly one canonical-slot field may be set,
and it must match `Axis`; extended views set neither.

Full-body assets also carry a required `SceneImageReferenceBodyState` (`Clothed` | `Unclothed`).
This is an asset-state contract, not a viewing angle: B-122 requires matching canonical angle sets in
both states, and the two must never be inferred from prompts, filenames or image analysis.

### 2.2 Canonical slots become a *minimum*, not the whole set

- A valid **face** view set must contain the five canonical slots (they are the multi-angle
  compiler's contract and must not change). It **may** contain any number of **extended** views:
  looking up/down at several pitches, and intermediate yaws between front and 3/4, between 3/4 and
  profile.
- A valid **body** view set must contain a base (front) plus the angle set. It **may** contain
  rotations (torso turned, back view) and positions (sitting, kneeling, lying, all-fours).

This is the single change that makes "many more face profiles" and "body base + angles + rotations +
positions" first-class instead of special cases.

### 2.3 Storage (additive, no fallback)

- **Keep** `SceneImageReferenceFaceView` as the canonical-slot enum — the render compiler's
  angle-matching reads it and must keep working unchanged.
- **Add** to `SceneImageReferenceAsset` and to `SceneAsset`:
  - `ViewDescriptorJson` (the descriptor above, serialised)
  - `BodyView` (`SceneImageReferenceBodyView?`) for the canonical body-slot contract. `FaceView`
    remains the canonical face-slot contract; the JSON carries the fine-grained and extended data.
  - `BodyState` (`SceneImageReferenceBodyState?`), required exactly when the asset is `FullBody`.
- **Body views** use `AssetKind = FullBody` with `ViewDescriptorJson` carrying rotation/position;
  `BodyView` carries a canonical slot when applicable, and `FaceView` stays null for body assets.
- **Canonical view set** — add `CanonicalFullBodyAssetId` (mirror of the existing
  `CanonicalFaceAssetId`) and a required, persisted `PackScope` (`FaceOnly` | `BodyComplete`). There
  is no inferred/default scope: migrated existing packs are explicitly written as `FaceOnly`, B-121
  creates `FaceOnly` drafts, and B-122 creates or promotes `BodyComplete` drafts. Approval validates
  the declared scope: `FaceOnly` requires all five canonical face slots; `BodyComplete` requires all
  five canonical face slots, matching clothed and unclothed five-slot body sets, and the canonical
  body base. `CanonicalFullBodyAssetId` points to the approved unclothed `Front` asset, the invariant
  anatomical base; the clothed front remains a required canonical reference. B-123 rejects any pack
  whose persisted scope is not `BodyComplete`.

### 2.4 Why this and not a bigger enum

Extending the enum to "FrontUp10, FrontUp20, …" is unbounded and breaks the compiler's
angle-matching switch. The enum is a *contract* (the render path's minimum); the view set is *data*.
Contract stays enum; variety stays data. That boundary is what lets the user add "looking down 30°"
without a code change.

---

## 3. Component architecture (the full stack, not just pose)

Pose/ControlNet were one example. The complete stack that serves the reference model, with owner,
code today, and gap:

| Component | What it is | Owner | Code today | Gap |
|---|---|---|---|---|
| **Eye analysis** | iris level/symmetry | B-121 | `tools/eye-validation/measure_iris.py` (CLI only) | in-app subprocess + override UI |
| **Quality analysis** | sharpness / density / aspect | B-121 | `ReferenceImageQualityAnalyzer` | wire into curation & the view-set grid |
| **Identity / faceid scoring** | similarity to canonical face | B-123 | `tools/consistency-scoring identity` (CLI only) | wire in; frame-normalised band |
| **Pose-adherence scoring** | render joint geometry vs skeleton | B-123 | none | build (raster-IoU is known-bad) |
| **Upscaler** | 4× → target long edge | B-121 | `identity-two-character/runners/run_upscale.py` (test runner) | in-app ComfyUI `UpscaleModelLoader` + Lanczos |
| **FaceID conditioning** | IP-Adapter / PuLid render identity | B-111 P3 | `SceneImageIdentityMechanism` enum + host nodes | render wiring + strategy selection |
| **Prompt compilers** | per-family prompt build (SDXL/Pony/Flux/Qwen edit) | B-032 / B-112 | `SceneImagePromptCompilers` + registry | extend to body & cell contexts |
| **Pose** | library / extract / edit / apply | B-118 → B-117 | none | build |
| **Structure** | depth / canny / segmentation | B-120 → B-119 | preprocessors on host only | build |
| **Asset Manager grouping** | grouped navigation | **B-124** (new) | flat table | build |

Every row above is a **tool the user drives**, per the program map's principles — the scorers report
into the UI for a human verdict, the upscaler is a one-click action on a chosen image, the compilers
produce an editable prompt, the faceid/path is chosen per render.

---

## 4. Asset Manager refactor (grouping, not hundreds of rows)

The list page becomes **grouped only** — the flat table is removed, because at the eventual image
count a flat list is unusable.

### 4.1 Group hierarchy (the tree)

```
Character (Dean)
 ├─ Identity pack v8  [Draft badge]
 │    ├─ Face view set    → [Front][3/4L][3/4R][ProfL][ProfR][Up 30°][Down 30°][…]
 │    ├─ Full-body set    → [Base front][3/4L][3/4R][ProfL][ProfR][Back][sitting][kneeling][…]
 │    └─ Wardrobe         → […]
 ├─ Derived assets        → [depth][canny][pose skeleton][segmentation]   (B-120)
 └─ LoRA dataset v1       → [30+6 cells, one thumbnail each]              (B-123)

Location (Lakeside Cabin)
 └─ Location profile v1   [Approved]
      ├─ Reference        → [plate]
      └─ Derived          → [depth][canny][segmentation]                  (B-120)

Wardrobe / Prop / Style   → same pattern: root → version/profile → assets
```

### 4.2 Mechanics

- The group key is `(RootKind, RootId, AssetKind, ViewKey?)` where `RootKind` ∈ `Character` /
  `Location` / `Wardrobe` / `Prop` / `Style`. `RootId` is always the **immediate versioned/profile
  aggregate that owns the asset**: identity-pack id for character identity assets, location-profile
  id for location assets, wardrobe-look-version id for wardrobe assets, and the corresponding
  version/profile id for props and styles. The tree projection separately carries the stable
  top-level owner id/name used for the outer node. `ViewKey` comes from the axis-appropriate
  canonical slot or a normalized descriptor key; body keys include state (for example,
  `body:unclothed:front` or `body:clothed:rotation:-45:standing`). It is null for kinds that have no
  view axis.
- The shell reads multiple stores through one explicit projection whose row identity is
  `(SourceStore, SourceAssetId)`. That row identity prevents collisions and is not a second grouping
  mode. Each projected row must provide the group-key fields, top-level owner metadata,
  version/status metadata and an asset role (`Reference`, `Derived`, `Wardrobe`, `DatasetCell`).
- **Root-agnostic**: the shell groups by root kind, so locations and any future type plug into the
  same tree without changing the shell. `ReferenceViewDescriptor` applies only to *viewable* kinds
  (face/body); locations and props group by root + kind + role, not by view.
- The identity view sets (`SceneImageReferenceAsset`) must be **surfaced in Asset Manager** — today
  the page lists only `SceneAsset` (the library), while the actual face/body views live in the
  identity store. The refactor unifies both under the owning root (character, location, wardrobe).
- Group headers show the pack version + status; groups are collapsible; empty states say what is
  missing ("no profile-right view yet — add one").
- The hierarchy is **fixed per root kind** (e.g. `Character → pack → view set`;
  `Location → profile → reference/derived`); there is no flat mode. The existing filters (type /
  approval / character / search) apply **within** the tree.

This is tracked as **B-124** — it is a prerequisite for the identity workstream to be usable at
all, not a polish item.

---

## 5. The prerequisite chain (this is what "makes sense" when analysed)

```
        ┌─────────────────────────────────────────────────────────────┐
        │ 1. Character FACE PROFILE — front (generate or upload)        │  B-121 Step 1
        └───────────────────────────┬─────────────────────────────────┘
                                    ▼
        ┌─────────────────────────────────────────────────────────────┐
        │ 2. FACE VIEW SET — 3/4 L/R, profile L/R, AND extended:        │  B-121 (extended)
        │    looking up / down, intermediate yaws                       │
        └───────────────────────────┬─────────────────────────────────┘
                                    ▼
        ┌─────────────────────────────────────────────────────────────┐
        │ 3. IDENTITY PACK (face) — promote the accepted view set        │  B-121
        └───────────────────────────┬─────────────────────────────────┘
                                    ▼
        ┌─────────────────────────────────────────────────────────────┐
        │ 4. CHARACTER BODY — base + angles + rotations + positions      │  B-122 Phase 0 (extended)
        │    (clothed AND unclothed)                                     │
        └───────────────────────────┬─────────────────────────────────┘
                                    ▼
        ┌─────────────────────────────────────────────────────────────┐
        │ 5. CHARACTER LoRA IMAGE PROFILE — the dataset is built from    │  B-123
        │    the face + body view sets, cell by cell                     │
        └─────────────────────────────────────────────────────────────┘
```

Everything in stages 1–4 is a **prerequisite** to stage 5. This is not automation: each stage is a
set of UI tools the user drives, and each produces a view set that the next stage consumes. The
Asset Manager grouping (B-124) is the lens through which all of these are navigated — which is why
it is a prerequisite rather than a polish item.

---

## 6. Delta this analysis imposes on the existing plans

| Plan | Change |
|---|---|
| **B-121** | (a) Face step must produce a **view set**, not just 5 slots — add extended views (up/down, intermediate yaw). (b) The angle step and the promote step operate on `ViewDescriptorJson`, not only the enum. |
| **B-122** | Body refs carry typed `BodyView` + `BodyState` for matching clothed/unclothed canonical sets and `ViewDescriptorJson` for extended rotation/position data. Promotion produces an explicit `BodyComplete` pack. |
| **B-123** | The coverage plan's face-angle and body-framing axes **read from the view sets** produced by B-121/B-122 — the cells are projections of the reference model, not a separately invented matrix. |
| **B-117/B-118/B-120/B-119** | Unchanged — they are component tools; the reference model does not touch their internal scope. |
| **B-124 (new)** | Asset Manager grouping + surfacing the identity view sets. |

## 7. Decisions (resolved 2026-09-11 — recommended approaches confirmed)

1. **View model — the split.** Keep `SceneImageReferenceFaceView` as the canonical-slot contract
   (the multi-angle compiler's minimum, unchanged); add `ViewDescriptorJson` for the **extended** set
   (up/down pitch, intermediate yaw). Additive; the compiler keeps working.
2. **Body descriptor — the same split.** `SceneImageReferenceBodyView` carries the base + angle
  minimum, `SceneImageReferenceBodyState` distinguishes the required clothed/unclothed sets, and
  `BodyRotationDeg` + `BodyPositionKey` remain free data for rotations/positions.
3. **Approval is staged and explicit.** Required `PackScope` is `FaceOnly` or `BodyComplete`; approval
  validates the declared scope and never infers it. B-121 produces `FaceOnly`, B-122 produces
  `BodyComplete`, and B-123 requires `BodyComplete`.
4. **Asset Manager refactor (B-124) — early.** The grouped navigation precedes B-123's cell
   workspace; it is the lens through which every view set is navigated.
5. **Identity scoring split — confirmed.** FaceID/IP-Adapter **render** wiring stays **B-111 P3**;
   the scoring-CLI **wiring into the UI** is **B-123**. Neither builds the other.
