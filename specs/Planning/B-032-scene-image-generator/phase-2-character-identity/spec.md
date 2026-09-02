# Phase 2 Specification - Character Identity

**Status:** Ready for implementation
**Depends on:** Phase 1 implemented baseline

## Goal

Recurring characters remain recognizable and correctly assigned across controlled renders, poses,
and views using persisted, auditable identity inputs.

## Non-Goals

- Exact hand/contact geometry.
- Location continuity and multi-camera blocking, delivered in Phase 3.
- Automatic validation/repair, delivered in Phase 4.
- Automatic creation of identity references from story text.
- Silent prompt-only generation when controlled identity inputs are missing.

## User Stories

### US2.1 - Curate an identity pack

The user can upload, inspect, approve, supersede, and delete face/full-body/wardrobe reference
assets tied to a character profile. The UI displays provenance, consent, dimensions, checksum, and
approval state.

### US2.2 - Configure identity conditioning

The user can configure one selected identity mechanism and all artifacts/strengths through Model
Manager. Missing or incompatible settings produce actionable errors.

### US2.3 - Render two controlled identities

The user can request an identity-controlled render for two visible actors. The system binds each
pack version to a distinct actor region and preserves immutable provenance.

### US2.4 - Compare and approve evidence

The user can view the frozen identity matrix, record per-constraint pass/fail notes, and approve a
conditioning profile only after its gate passes.

### US2.5 - Record a LoRA decision

The system records `NotRequired`, `Required`, or `Deferred` with evidence. If required, LoRA
artifacts and training provenance are persisted and re-evaluated with the same matrix.

## Functional Requirements

- **FR2-001:** Persist identity packs independently from render records and tie each pack to one
  character profile identifier.
- **FR2-002:** Reference bytes live under the configured scene-image root; SQLite stores safe
  relative paths and metadata.
- **FR2-003:** Compute SHA-256, byte length, dimensions, and media type at ingest.
- **FR2-004:** Require provenance and consent state before an asset can be approved.
- **FR2-005:** A pack version is immutable after approval; changes create a new version.
- **FR2-006:** Require exactly one approved canonical face reference; full-body and wardrobe
  references are optional capabilities declared by the resolved profile.
- **FR2-007:** Persist the selected mechanism, workflow revision, checkpoint family, artifacts,
  strengths, and capability flags in Model Manager.
- **FR2-008:** Resolve exactly one identity profile. Missing, disabled, unknown, or incompatible
  configuration fails explicitly.
- **FR2-009:** Identity-controlled requests name exact pack versions and region assets.
- **FR2-010:** Two-person controlled renders require two non-overlapping actor regions.
- **FR2-011:** Never substitute prompt descriptors, Qwen, another adapter, or another pack when a
  required controlled input is absent.
- **FR2-012:** Persist an immutable render-attempt record with all references, artifacts, masks,
  prompt, negative prompt, seed, and output checksum.
- **FR2-013:** Existing `PromptOnly` rendering remains unchanged and explicitly labeled.
- **FR2-014:** Qwen editing stays a user-invoked derived-image operation.
- **FR2-015:** Persist matrix cases and score each output for likeness, ownership, wardrobe, pose,
  view, anatomy, and leakage.
- **FR2-016:** The LoRA decision must reference the completed matrix and rationale.
- **FR2-017:** Deleting an in-use approved pack is blocked; superseding it does not rewrite old
  render provenance.
- **FR2-018:** All mutations and jobs emit structured logs and scene-image debug events.

## Acceptance Scenarios

1. Uploading an invalid/non-image asset is rejected without creating an approved record.
2. Approving a pack without provenance, consent state, or canonical face fails with field-level
   guidance.
3. A controlled request with a missing pack version fails before enqueue.
4. A handler encountering a missing adapter artifact marks the attempt failed and names the
   missing configured artifact; it does not render prompt-only.
5. Two actors with overlapping or absent regions cannot compile.
6. Re-running one matrix case with the same frozen inputs reproduces the same submitted workflow
   and provenance values.
7. A successful output is linked to its prompt, pack versions, conditioning profile, region masks,
   workflow revision, checkpoint, and seed.
8. Old outputs remain inspectable after a pack or profile is superseded.

## Exit Gate

- One selected/pinned identity mechanism passes the 12-output matrix: at least 10/12 overall,
  at least 5/6 composition cells for both identities, and 12/12 correct ownership.
- No controlled request has a prompt-only or alternate-adapter fallback.
- The LoRA decision and evidence are persisted.
- Narrow, solution, and full tests pass; the manual proof report is complete.
