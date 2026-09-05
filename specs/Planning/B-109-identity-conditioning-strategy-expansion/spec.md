# B-109 Specification — Identity Conditioning Strategy Expansion

**Status:** Ready for implementation
**Depends on:** Existing `CharacterImageIdentityPack`/`SceneImageReferenceAssetKind`/
`SceneImageIdentityMechanism` domain, `ComfyUIIdentityConditionedClient`, Model Manager identity
fields (all implemented, extended here)

## Goal

Improve face-identity conditioning fidelity and angle robustness without training, and let the
application qualify and offer more than one T2I-time identity mechanism simultaneously, each
independently configurable, with no silent fallback between them. Keep the pixel-level edit-based
Identity stage (B-106) as a separate, coexisting pipeline stage.

## Non-Goals

- LoRA. Fully out of scope per the existing B-107 deferral; nothing here creates a dependency on it
  or on `CharacterLoraDataset`.
- Replacing or deprecating the currently-configured IP-Adapter Plus Face mechanism. It remains a
  valid, independently qualified strategy; nothing forces migration.
- Rebuilding the B-106 Identity stage's pixel-edit mechanism. That package owns method 6 entirely;
  this package only clarifies how it coexists with T2I-time mechanisms.
- Multi-person regional identity changes. The existing multi-actor `IdentityControlledRequestCompiler`
  region/mask behavior is unchanged; this package's angle-aware selection and multi-reference
  averaging apply per-character, within the existing single- and multi-actor request shapes.
- Guaranteeing any new mechanism (FaceID, InstantID) passes qualification. Each is subject to the
  same isolated-pod proof discipline as the original IP-Adapter/PuLID work; a failed proof means
  that cell stays unqualified, not silently substituted.

## User Stories

### US9.1 — Angle-appropriate reference is used automatically

When a render's target POV/pose implies a head angle, the system selects the approved face-view
asset nearest that angle from the character's identity pack, instead of always using the fixed
canonical face. This applies regardless of which T2I mechanism is configured.

### US9.2 — Multiple identity mechanisms are independently qualified

An operator can configure and qualify IP-Adapter Plus Face, PuLID, IP-Adapter FaceID, and InstantID
as distinct capability cells for the same or different models. Each is enabled/disabled and
qualified independently; none is a fallback for another.

### US9.3 — A render selects its identity strategy explicitly

For a given render (one-pass generation, or a future capability), the user or the resolved
capability configuration determines exactly one qualified mechanism to use. Missing or unqualified
configuration fails explicitly, naming the character and the requested mechanism.

### US9.4 — Multi-reference conditioning strengthens angle robustness

When a mechanism's qualified capability declares multi-reference support, the compiler feeds an
ordered set of approved views (not just one) into the embedding call.

### US9.5 — The pixel-edit Identity stage remains a separate, coexisting option

A production group can use a T2I-time mechanism for its Composition/one-pass render and
independently use the B-106 Identity stage's pixel-edit graft later in the same group. Neither
displaces the other.

## Functional Requirements

### Angle-aware reference selection (method 1)

- **FR9-001:** Before compiling an identity-controlled request, resolve the character's target head
  angle from the render's POV/pose facts (reusing existing beat/POV data — no new input required).
- **FR9-002:** Select the approved `Face` asset whose `SceneImageReferenceFaceView` is nearest the
  resolved target angle, defined by a fixed, documented distance ordering over the 5 views
  (`Front`, `ThreeQuarterLeft`, `ThreeQuarterRight`, `ProfileLeft`, `ProfileRight`).
- **FR9-003:** When the pack has no approved view nearer than the canonical face for the resolved
  angle, use the canonical face — this is a selection improvement, not a requirement to have all 5
  views approved.
- **FR9-004:** Record which view was selected and why (resolved angle, chosen view) on the
  compiled request's audit trail.
- **FR9-005:** This selection applies uniformly across every T2I-time mechanism (IP-Adapter Plus
  Face, PuLID, IP-Adapter FaceID, InstantID) — it is not mechanism-specific.

### Mechanism qualification as independent strategies (methods 2, 4, 5)

- **FR9-006:** `SceneImageIdentityMechanism` gains `IpAdapterFaceId` and `InstantId` values,
  alongside the existing `IpAdapter` (Plus Face) and `PuLid`.
- **FR9-007:** Each mechanism is configured on its own `RegisteredModel`/capability cell with its
  own `IdentityAdapterRef`/artifact fields; multiple mechanisms may be enabled and qualified at the
  same time for the same or different underlying checkpoints.
- **FR9-008:** Resolving a mechanism for a render requires an exact enabled, qualified
  configuration. Missing, disabled, or unqualified configuration fails explicitly naming the
  character and requested mechanism — no fallback to a different mechanism.
- **FR9-009:** A new mechanism (`IpAdapterFaceId`, `InstantId`) is not exposed for production
  selection until it has passed the same isolated-pod proof discipline as the original IP-Adapter/
  PuLID work (frozen matrix, scored gate, recorded decision) — no shortcutting the existing Phase 2
  section C process for new mechanisms.
- **FR9-010:** PuLID qualification (already coded) follows the same frozen-matrix scoring process
  before being offered as a production-selectable strategy, even though its implementation exists.

### Multi-reference conditioning (method 3)

- **FR9-011:** A qualified capability cell may declare `SupportsMultiReference = true`. When true,
  the compiler selects an ordered set of up to 3 approved views (canonical + 2 nearest to the
  target angle) instead of exactly one.
- **FR9-012:** The workflow builder for a multi-reference-capable mechanism accepts the ordered set
  and feeds it as the mechanism's native multi-image input. If the installed custom-node version
  does not support batched reference input, this must be verified before FR9-011 is enabled for
  that mechanism — never assumed.
- **FR9-013:** Multi-reference selection records every asset used, in order, on the compiled
  request's audit trail.

### Coexistence with the B-106 Identity stage (method 6)

- **FR9-014:** Nothing in this package changes B-106's Identity-stage contract (source = exact
  Composition attempt bytes, ordered face references via the edit path). A production group may
  use any qualified T2I mechanism for Composition and independently run the B-106 Identity stage
  afterward.
- **FR9-015:** The Studio must not present T2I-time mechanism selection and the B-106 Identity
  stage as mutually exclusive choices — they operate at different pipeline stages and can both be
  used in the same production group.

## Acceptance Scenarios

1. A render targeting a three-quarter-left POV, with a pack that has an approved
   `ThreeQuarterLeft` face, selects that asset instead of the canonical `Front` face; the audit
   trail records the resolved angle and chosen view.
2. The same render, with a pack that has no `ThreeQuarterLeft` approved, falls back to the
   canonical face and records that no nearer view was available.
3. An operator qualifies PuLID as a second, independent capability cell on the same checkpoint
   already used by IP-Adapter Plus Face; both remain independently enabled and selectable.
4. Requesting a render with an unqualified `InstantId` mechanism fails explicitly, naming the
   character and the mechanism, before any provider call.
5. A capability cell with `SupportsMultiReference = true` produces a compiled request listing 3
   ordered reference assets; a cell without it lists exactly 1.
6. A production group runs a PuLID-conditioned Composition render, then separately runs the B-106
   Identity stage on that same attempt — both succeed and both are visible as distinct attempts.
7. Enabling `IpAdapterFaceId` for production selection is blocked until its isolated-pod proof
   matrix is recorded, even though the mechanism enum value already exists.

## Exit Gate

- All seven acceptance scenarios verified in the running application.
- Affected focused tests, full solution build, and full test suite pass.
- Angle-aware selection (method 1) and PuLID qualification (method 2) are both production-selectable.
- IP-Adapter FaceID and InstantID (methods 4, 5) each have a recorded proof-gate decision
  (qualified or explicitly rejected) before either is exposed for production selection.
