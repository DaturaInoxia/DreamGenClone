# B-106 Specification — Production Studio Staged Workflow

**Status:** Ready for implementation
**Depends on:** Existing B-032 Phase 2 persistence, compilers, and identity packs (all implemented)
**Owners:** B-100 workflow (US10) + B-032 execution

## Goal

A user selects a completed Moment and produces an approved scene frame by following the standard
community image workflow: **compose a base → apply character identity → run finishing edits →
compare candidates → approve one exact version**. Every step is a first-class Production Studio
action over immutable attempts with exact lineage. The user never edits JSON, provider fields, or
infrastructure configuration.

## Non-Goals

- Re-modelling production persistence. `SceneImageProductionGroup`, `SceneImageRecord`,
  `ApprovedSceneFrameDecision`, and their repositories are reused unchanged except where a
  requirement below names a specific additive field.
- Location continuity, blocking, multi-POV shot families, or the Three.js editor (B-032 Phase 3).
- LoRA training workflow changes (B-032 Phase 2 section L).
- Automated aesthetic validation or repair (B-032 Phase 4).
- Any migration or compatibility path for sessions created before the production schema.
- Changing RP engine behavior, prompts, or continuation.

## User Stories

### US6.1 — Apply character identity to a composition

The user selects a completed Composition attempt and applies the approved identity packs for every
known visible character. The result is a new immutable child attempt at the Identity stage. Missing
approved packs fail explicitly and name the character.

### US6.2 — Record an explicit identity skip

When the user deliberately produces a frame without identity, they record a skip decision and a
reason. That decision is persisted on the production group and displayed wherever identity state is
shown. There is no silent bypass.

### US6.3 — Run finishing edits inside the production group

The user runs one or more source-image edits against a selected eligible attempt — corrections,
adjustments, and an adult-content pass where the configured editor permits it. Each edit is an
immutable child attempt at the Finish stage pointing at its exact parent.

### US6.4 — Branch from any completed attempt

The user branches a new edit or a new identity pass from any completed eligible attempt, not only
from the newest one. Branching never overwrites or supersedes an existing attempt.

### US6.5 — Manage many candidates

The user reviews attempts as a branch-aware lineage tree grouped by stage, compares any two side by
side, shortlists or rejects without deleting, and approves exactly one completed attempt.

### US6.6 — Understand identity validity after edits

When a finishing edit changes pose, camera angle, or framing, the user is told that applied identity
is no longer guaranteed on that child, and can run a further identity pass from it.

## Functional Requirements

### Stage 2 — Identity

- **FR6-001:** The Identity stage shall consume the exact stored bytes of one selected completed
  Composition attempt. It shall not regenerate the image from the prompt.
- **FR6-002:** Identity shall be applied through a reference-conditioned source-image edit that
  receives ordered approved identity references. Identity-conditioned text-to-image shall not be
  accepted as the Identity stage.
- **FR6-003:** Every known visible character in the Moment shall resolve to exactly one approved
  identity pack version and its approved owned canonical face before dispatch.
- **FR6-004:** A missing, draft, superseded, or unapproved pack shall fail readiness with a message
  naming the exact character. No substitution, no partial application, no prompt-only fallback.
- **FR6-005:** Reference bindings shall be ordered and shall record ordinal, character key, pack id,
  pack version, asset id, and asset checksum on the resulting attempt.
- **FR6-006:** A successful Identity execution shall create one immutable child attempt whose stage
  is `Identity` and whose source is the exact parent attempt id.
- **FR6-007:** Re-running Identity against the same parent shall create a sibling child, never an
  overwrite.

### Identity policy and skip

- **FR6-008:** A production group whose `IdentityPolicy` is `Required` shall block approval of any
  attempt that has not passed the Identity stage, unless a skip decision exists.
- **FR6-009:** Recording a skip shall set `IdentityPolicy` to `SkippedByUser` and shall require a
  non-empty `IdentitySkipReason`. Both are persisted on the production group.
- **FR6-010:** The skip decision and reason shall be visible in the Studio wherever identity state
  is displayed, and shall be reversible only by an explicit user action that clears it.
- **FR6-011:** No code path shall infer, default, or silently apply a skip.

### Stage 3 — Finish

- **FR6-012:** A Finish edit shall start from a selected completed attempt that is either an
  Identity attempt, or a Composition attempt when the group has a recorded skip.
- **FR6-013:** Each Finish execution shall create one immutable child attempt whose stage is
  `Finish` and whose source is the exact selected parent.
- **FR6-014:** Finish edits shall be dispatched through the configured image-editor model resolved
  for the exact operation. Missing or unqualified editor configuration shall fail explicitly.
- **FR6-015:** An adult-content Finish edit shall be permitted only when the resolved editor model's
  content policy allows it; otherwise the action shall be unavailable with a stated reason.
- **FR6-016:** Each Finish attempt shall declare a change class of either `Cosmetic` or
  `Geometry`. `Geometry` covers pose, camera angle, framing, and subject placement changes.
- **FR6-017:** A `Geometry` Finish attempt whose parent carried applied identity shall be marked
  identity-stale, and the Studio shall surface that a further Identity pass is required before
  approval when `IdentityPolicy` is `Required`.

### Attempt lineage and management

- **FR6-018:** Every attempt shall expose its exact parent, stage, disposition, execution status,
  and approval state as distinct values.
- **FR6-019:** The Studio shall present attempts as a branch-aware tree grouped by stage, not a flat
  reverse-chronological list.
- **FR6-020:** The user shall be able to compare any two completed attempts side by side within the
  production group.
- **FR6-021:** Branching shall be available from any completed eligible attempt.
- **FR6-022:** Shortlist and reject shall change disposition only; they shall never delete bytes or
  break lineage.
- **FR6-023:** Approval shall remain an append-only decision over exactly one completed attempt and
  its checksum, with at most one current approval per production group.

### Snapshots, diagnostics, and configuration

- **FR6-024:** Each Identity and Finish attempt shall snapshot resolved provider, model, settings,
  compiled instruction, source checksum, and ordered reference checksums.
- **FR6-025:** Each dispatch shall emit a structured debug event capturing the exact submitted
  request without secrets.
- **FR6-026:** All behavior controls introduced here shall be UI-backed persisted configuration.
  No hardcoded runtime default, no hidden fallback branch, and no guessed substitute value.
- **FR6-027:** Missing required configuration shall fail fast with an explicit diagnostic naming the
  missing item.

### Legacy retirement

- **FR6-028:** After Stage 2 and Stage 3 reach parity, the legacy one-off generation action shall be
  removed from new-session production navigation and shall not be retained as a fallback.
- **FR6-029:** The existing standalone image-editor route may remain for non-production assets, but
  it shall not be the path by which production-group attempts are created.

## Acceptance Scenarios

1. A Moment with two known visible characters, both with approved packs, produces a Composition,
   then an Identity child attempt whose reference bindings list both characters in order with exact
   pack versions and asset checksums.
2. The same Moment with one character lacking an approved pack fails Identity readiness with a
   message naming that character, and creates no attempt.
3. Recording a skip with the reason "background extra only" sets `SkippedByUser`, persists the
   reason, permits Finish directly from Composition, and displays the skip in the Studio.
4. Attempting to record a skip with an empty reason is rejected.
5. Two Finish edits run from the same Identity attempt produce two sibling children, both retained,
   neither overwriting the other.
6. A `Geometry` Finish edit from an Identity attempt is marked identity-stale and blocks approval
   while `IdentityPolicy` is `Required`.
7. An adult-content Finish edit is unavailable, with a stated reason, when the resolved editor
   model's content policy is SFW.
8. The attempt tree shows Composition → Identity → Finish branches; any two completed attempts can
   be compared side by side; rejecting one leaves its bytes and lineage intact.
9. Approving one attempt records a decision with its exact checksum; approving another creates a new
   decision version and supersedes the prior one without mutating it.
10. A session created before the production schema produces no B-106 records and shows explicit
    create-new-session guidance.

## Exit Gate

- All acceptance scenarios verified in the running application against a live session.
- Affected focused tests, the full solution build, and the full test suite pass.
- Razor diagnostics clean on every touched component.
- Structured debug events captured for one Identity dispatch and one Finish dispatch.
- B-032 Phase 2 tasks P2-053, P2-054, and P2-055 marked complete with evidence.
- The Phase 2 release gate P2-057 → P2-059 is then executed as a separate closing step.
