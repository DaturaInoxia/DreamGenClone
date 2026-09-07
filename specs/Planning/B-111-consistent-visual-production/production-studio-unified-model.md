# B-111 — Unified Production Studio: Data + UI Model

**State:** authoritative design (user-driven, 2026-09-05). Supersedes the split screen inventory in
`ui-contract.md §3` and the two-image-model assumption. Consistent with contracts C1–C7.
**Decisions by user (2026-09-05):** Option B (one unified image table — no migration, new data only);
**all gaps G1–G7 included now**; unified status set with Shortlisted; text-first references; one
workbench engine with two entry points; pluggable vision→prompt compiler.

> **Why this doc exists:** the UI/workflow is the product. This is data- and image-heavy, so the data
> model and the surfaces that manage it must be designed together, once, before building.

---

## 1. The one decision that shapes everything — unify the image model (Option B)

Today there are **two** image tables — `SceneImageRecord` (moment/studio) and `SceneAsset`
(asset-manager references). They are **replaced by one** `ProducedImage` model. No migration (no data
kept; new RP sessions only), so this is a clean cutover, not a dual-read.

### `ProducedImage` — the single image record (I1 + G2 + G4)
Every image — a reference candidate, an edit attempt, a moment story-image, a promoted reference —
is one `ProducedImage` row with **uniform** fields:

| Field | Purpose |
|---|---|
| `Id` | identity |
| `Kind` | `ReferenceCandidate` / `EditAttempt` / `MomentImage` / `PromotedReference` |
| `OwnerScope` | discriminated owner: `(SessionId, InteractionId)` for a moment · `(BatchId, TargetRef)` for an asset |
| `ReferenceKind?` | for asset images: `CharacterFace` / `CharacterBody` / `Wardrobe` / `Location` |
| `Status` | **unified** (§2) |
| `ParentImageId?` | **lineage** — the attempt this was edited from (tree) (G4) |
| `VisionSource` | `Typed` / `BeatMetadata` / `SystemData` — where the vision came from |
| `VisionText` | the raw vision (typed, or pulled from beat/system) |
| `PromptCompiled` / `PromptEdited` | compiled prompt + the user's manual edit (both kept) |
| `NegativePrompt`, `Seed`, `ModelId`, `EndpointId` | reproducibility |
| `AppliedReferencesJson` | **provenance (G2)** — the exact references (id + version + kind + strategy) used to make this image |
| `IdentityStrategy?` | `TextOnly` / `ReferenceEdit` / `IpAdapter` / `PuLid` / … per applied reference |
| `CostJson` | generation cost (I3) |
| `StoragePath` | git-ignored image bytes |
| `RefusalMode?` | `None`/`EmptyOutput`/`PolicyError` (T6) |
| `ScoreJson?` | identity/adherence/diversity when scored (P4/G6) |
| `CreatedUtc`, `UpdatedUtc` | |

**Consequence:** the P2 work (batch, candidate fields on `SceneAsset`, curation, promotion) **folds
onto `ProducedImage`** — a candidate is a `ProducedImage{Kind=ReferenceCandidate}`. `ReferenceBootstrapBatch`
stays (it groups candidates); `SceneAsset`/`SceneImageRecord` are retired.

### Reference (the approved, reusable thing) is separate from images
A **`Reference`** = mandatory **text block** + optional **image(s)** (promoted `ProducedImage`s),
per kind (Face/Body/Wardrobe/Location), versioned + approvable. **Text-only reference = text block,
no image** (first-class). This is contract C1; references are consumed by the Workbench's apply panel.

---

## 2. Unified status set (fixes the Shortlisted miss)
Every `ProducedImage` moves through **one** status set, everywhere (moments, assets, candidates):

`Undecided → Shortlisted → Accepted → (Rejected | Revoked)`

- **Shortlisted** (G-user): a maybe; kept for later compare. *(This was dropped from the P2 candidate
  enum — corrected here.)*
- **Accepted**: the chosen/final for its group. **Approved reference** promotion happens from an
  Accepted asset image.
- **Rejected / Revoked**: out; Revoked = un-finalize a prior Accepted.

---

## 3. One workbench engine, two entry points

```
ENTRY A — Asset Manager (build references)        ENTRY B — Moment deck (produce story images)
  vision = user types                               vision = beat/moment compiler metadata
        │                                                 │
        └──────────────►  SHARED PROMPT COMPILER  ◄────────┘   (model-aware, best-practices, governed)
                          → compiled prompt (editable)
                          → generate → ProducedImage (prompt in metadata)
                          → apply references: identity + location + wardrobe (text-only OR image)
                          → identity available at CREATE and EDIT
```
Only the **vision source** is pluggable. Compile → editable prompt → generate → metadata is identical.

---

## 4. The five surfaces (information architecture for volume — G3, G5)

All reuse the same components. Built once.

| # | Surface | Job | Handles volume via |
|---|---|---|---|
| **S1** | **Dashboard / Inbox** (G5) | "what needs me" — pending review, failed/refused, references awaiting approval; storage/counts | actionable queues, not a dump |
| **S2** | **Library / Browser** (G3) | browse ALL `ProducedImage` + references | **facets** (status·kind·character·location·model·date·batch), **bulk ops** (accept/reject/delete/tag), **lazy thumbnails + paging**, **archive/cleanup** |
| **S3** | **Review Deck** | review one group (moment or asset): **cycle** · **compare** · **compare-against-reference** (G6) · set status · metadata+prompt panel · **lineage tree** (G4) | virtualized, keyboard nav |
| **S4** | **Create/Edit Workbench** | vision→compiled-editable-prompt→generate; **apply references** panel (identity/location/wardrobe, text-or-image, incl. **text-source picker** I2); identity at create+edit; iterate | one image at a time |
| **S5** | **Queue / Runs** | what's generating/queued/failed + **cost** (I3) + endpoint cold/warm (honest state) | grouped by endpoint |

**Aggregate views** (G7) hang off S2: a **Character** shows its Face+Body+Wardrobe references and
versions together; a **Location** shows its plate+references.

---

## 5. Shared components (built once, reused across S1–S5 and both entry points — C7 U2)
- **`ImageDeck`** — cycle/compare/status/lineage over a group of `ProducedImage`.
- **`CreateEditWorkbench`** — vision→compile→edit→generate→iterate; identity at create+edit.
- **`ReferenceApplyPanel`** — pick character/location/wardrobe; each text-only or image; text-source
  picker (PhysicalAttributes / scenario location / beat / typed).
- **`ImageMetadataPanel`** — prompt (compiled+edited), negative, seed, model, endpoint, **applied
  references**, cost, refusal, score. ("view what will be submitted", C7 U6.)
- **`CompareView`** — side-by-side (reuse existing `ToggleComparison`); **+ compare-against-reference**.
- **`ImageBrowser`** — faceted, bulk-op, lazy-thumbnail grid.
- **`StatusControl`** — the unified status set with confirm on destructive.
- **`LineageTree`** — the edit-attempt tree; mark chosen/final.

---

## 6. Gap coverage (all in now, per user)
| Gap | Covered by |
|---|---|
| G1 two image models | §1 unified `ProducedImage` (Option B) |
| G2 provenance | `AppliedReferencesJson` on every image |
| G3 volume mgmt | S2 Library (facets/bulk/lazy/archive) |
| G4 lineage | `ParentImageId` + `LineageTree` in S3 |
| G5 needs-attention | S1 Dashboard/Inbox |
| G6 consistency verify | `CompareView` compare-against-reference; `ScoreJson` later |
| G7 character aggregate | §4 aggregate views |
| I1 uniform contract | §1 fields |
| I2 text-source picker | `ReferenceApplyPanel` |
| I3 cost/state | `CostJson`, S5, metadata panel |
| I4 safety/undo | `StatusControl` confirm + Revoked |

---

## 7. Build sequence (foundation-up; each unit verified fast, per speed rules)
1. **PS-1 — `ProducedImage` domain + unified table + repository** (retire `SceneImageRecord`/`SceneAsset`;
   fold P2 candidate/batch onto it). *Biggest unit; foundational.*
2. **PS-2 — `Reference` model** (text block mandatory + optional images, 4 kinds, versioned) + promotion
   onto it (fold P2-U5a).
3. **PS-3 — shared compiler seam** (vision source pluggable: typed vs beat metadata) → compiled editable prompt.
4. **PS-4 — `CreateEditWorkbench` + `ReferenceApplyPanel` + `ImageMetadataPanel`** (create/edit, identity at both).
5. **PS-5 — `ImageDeck` + `CompareView` + `StatusControl` + `LineageTree`** (Review Deck / S3).
6. **PS-6 — `ImageBrowser` + facets/bulk/lazy** (Library / S2) + aggregate views.
7. **PS-7 — S1 Dashboard/Inbox + S5 Queue/Runs** (cost + honest state).
8. **PS-8 — wire both entry points** (Asset Manager = references; Moment deck = story images) + gate.

Each unit reuses components from earlier units (no duplication). RunTray/ProductionWorkspace polish
folds into S5/S3.

> **This doc is the authoritative build spec.** The phase task files (P2/P4) now execute these PS units.
