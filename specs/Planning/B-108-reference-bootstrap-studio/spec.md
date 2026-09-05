# B-108 Specification — Reference Bootstrap Studio

**Status:** Ready for implementation
**Depends on:** Existing `SceneAsset` catalog, identity packs, wardrobe look versions, Asset Manager shell (all implemented)

## Goal

A user creates a consistent visual reference — for a wholly fictional character's face or body, a
wardrobe look, or a location — by generating several candidates from a description, comparing them
side by side, curating (accept/reject), optionally expanding an accepted candidate into more views,
and promoting exactly one accepted result to be the canonical reference for its target. No source
photo is required at any step.

## Non-Goals

- LoRA dataset creation or training. This package produces plain `SceneAsset` candidates; it never
  creates or requires a `CharacterLoraDataset`. LoRA activation remains B-107, fully independent.
- Full Phase 3 location continuity — coordinate frames, landmarks with dimensions, lighting/time/
  weather state variants, blocking, shots, or multi-POV. This package's location slice is a
  minimal name+description+references record only.
- Cross-image face grafting (applying an already-approved face onto a separately generated body in
  one edit call). That requires B-106 Phase A's ordered-reference edit operation, tracked there.
  This package's "expand view" step uses same-image edits only (proven, available today).
- Migrating or reconciling existing `CharacterLoraDataset` candidates into this new batch concept.
- Automated aesthetic acceptance. Every promotion is an explicit user decision.

## User Stories

### US8.1 — Generate candidates for a target

The user picks a target (a character's Face, a character's FullBody, a character's Wardrobe look,
or a Location), writes a description, chooses a candidate count, and generates that many images in
one batch without creating any LoRA-specific object.

### US8.2 — Curate candidates

The user reviews a batch as a gallery, compares any two side by side, and marks each candidate
Accepted, Rejected, or leaves it Undecided with an optional note. Rejected candidates remain
visible and are never deleted.

### US8.3 — Expand an accepted candidate into more views

The user takes one Accepted candidate and requests additional views (e.g. a different head angle,
pose, or angle-appropriate crop) via a same-image edit. Each expansion is a new candidate in the
same batch, linked to its exact source.

### US8.4 — Promote to a character's canonical face or body

The user promotes an Accepted Face or FullBody candidate into the character's identity pack,
reusing the existing draft/canonical-face/approve flow. The promoted asset's bytes are copied into
identity-pack storage; the original candidate is untouched.

### US8.5 — Promote to a character's wardrobe look

The user promotes an Accepted Wardrobe candidate into a `CharacterWardrobeLookVersion` as a new
`CharacterWardrobeAssetBinding`, reusing the existing draft/approve/supersede flow.

### US8.6 — Promote to a location's canonical reference

The user creates or selects a minimal Location profile (name, description) and promotes an
Accepted candidate into its ordered reference list.

### US8.7 — Manage many images across targets

The user filters and browses candidates across all batches and targets from Asset Manager, without
losing filter/selection state when switching between them.

## Functional Requirements

### Batches and candidates

- **FR8-001:** A `ReferenceBootstrapBatch` persists a target (character profile id + asset type, or
  a location profile id), the description, requested count, and creation time. It never requires
  or references a `CharacterLoraDataset`.
- **FR8-002:** Candidate generation compiles the description through the existing
  `SceneAssetPromptCompiler` for the resolved model family/dialect and dispatches through the
  existing image-generation client stack. No new compiler is introduced.
- **FR8-003:** Each generated candidate is a plain `SceneAsset` row tagged with its batch id,
  target, and a candidate decision of `Undecided`.
- **FR8-004:** Candidate decision (`Undecided` / `Accepted` / `Rejected`) and an optional note are
  additive fields on `SceneAsset`. Changing a decision never deletes the asset or its bytes.
- **FR8-005:** A batch's candidates remain queryable and browsable indefinitely; rejecting or
  promoting one candidate does not affect the others.

### Expansion

- **FR8-006:** Expanding an Accepted candidate creates a new candidate in the same batch via a
  same-image edit (source = the accepted candidate's exact bytes; instruction changes only the
  requested view/pose/angle). The new candidate records its exact source candidate id.
- **FR8-007:** Expansion never requires or assumes a second, separately generated image. Combining
  an accepted face with a separately generated body is out of scope here (see Non-Goals) until
  B-106 Phase A exists.

### Promotion

- **FR8-008:** Promoting a Face/FullBody candidate copies its bytes into
  `ICharacterImageIdentityService` storage via the existing upload path, tagged with the correct
  `SceneImageReferenceAssetKind`. The candidate's own `SceneAsset` row is untouched.
- **FR8-009:** Promoting a Wardrobe candidate creates a `CharacterWardrobeAssetBinding` on the
  target `CharacterWardrobeLookVersion` (creating a new Draft version if none is open), reusing the
  existing repository rules (draft-only mutation, approval immutability).
- **FR8-010:** Promoting a Location candidate creates a `ReferenceBootstrapLocationReference`
  binding on the target `ReferenceBootstrapLocationProfile`.
- **FR8-011:** Only an Accepted candidate can be promoted. Promoting a Rejected or Undecided
  candidate fails explicitly.
- **FR8-012:** Promotion is copy, not move — the source candidate and its decision history remain
  intact and inspectable after promotion.

### Minimal location domain

- **FR8-013:** `ReferenceBootstrapLocationProfile` persists an id, name, description, status
  (`Draft`/`Approved`/`Superseded`), and supersession lineage — no coordinate frame, no
  dimensions, no landmarks.
- **FR8-014:** `ReferenceBootstrapLocationReference` binds an ordinal, semantic role, and exact
  `SceneAssetId` to a profile.
- **FR8-015:** These records are named and namespaced distinctly from any future Phase 3
  `LocationProfile` aggregate. Phase 3, when it lands, must make an explicit reconciliation
  decision — no silent auto-migration is implied or performed by this package.

### Configuration and diagnostics

- **FR8-016:** Every generation and expansion dispatch uses UI-backed persisted model/profile
  configuration already required by the existing generation/editing clients. No hardcoded model,
  no hidden fallback.
- **FR8-017:** Missing or unqualified configuration (no enabled image model, no configured editor)
  fails fast naming the missing item.
- **FR8-018:** Each candidate records its exact compiled prompt, model, and provider for later
  inspection.

## Acceptance Scenarios

1. Generating 6 candidates for a new character's Face, with no identity pack yet existing, succeeds
   and produces 6 `Undecided` `SceneAsset` rows sharing one batch id — no `CharacterLoraDataset` is
   created anywhere.
2. Marking 4 Rejected and 2 Accepted leaves all 6 visible in the gallery; rejected ones are visibly
   dimmed but not deleted.
3. Expanding an Accepted Face candidate into a three-quarter-left view produces a 7th candidate in
   the same batch, recording the 6th as its exact source.
4. Promoting an Accepted Face candidate creates or updates a draft `CharacterImageIdentityPack` with
   that candidate's bytes as a `Face` asset; the original candidate row is unchanged and still
   visible in Asset Manager.
5. Promoting an Accepted Wardrobe candidate creates a `CharacterWardrobeAssetBinding` on a Draft
   `CharacterWardrobeLookVersion`.
6. Creating a Location profile named "Lakeside Cabin", generating 4 candidates, accepting 2, and
   promoting both produces a profile with 2 ordered references and no coordinate/landmark fields
   anywhere in its record.
7. Attempting to promote a Rejected or Undecided candidate fails with an explicit message.
8. Filtering Asset Manager by batch, target, and decision returns exactly the matching candidates
   across all three target kinds simultaneously.
9. A session or character with no LoRA dataset at all can still fully complete scenarios 1–6 — the
   two systems are never coupled.

## Exit Gate

- All nine acceptance scenarios verified in the running application.
- Affected focused tests, full solution build, and full test suite pass.
- Razor diagnostics clean on every touched component.
- No new dependency on `CharacterLoraDataset`/`CharacterLoraDatasetMember` exists anywhere in the
  new code.
