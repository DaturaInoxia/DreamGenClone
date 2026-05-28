# Data Model: Semantic Telemetry and Event-Driven Evidence

**Phase**: 1 - Design and Contracts  
**Date**: 2026-05-18  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

---

## Entity Overview

```text
LatestInteractionSemanticPayload
  -> SemanticEventEvidenceRecord[*]
        -> EvidenceDeltaBreakdown[*]

SelectionEvidenceSnapshot
  <- semantic evidence application + keyword evidence application
  -> Theme ordering
  -> Candidate fit evaluation

ThemeLockState
  -> lock guard in semantic evidence application

SemanticProcessingDiagnostic
  -> persisted/queryable diagnostics + debug telemetry
```

---

## Entities

### SemanticEventEvidenceRecord

**Purpose**: Represents one semantic event from latest interaction and its mapped evidence intent.

| Field | Type | Constraints | Description |
|---|---|---|---|
| InteractionId | string | Required | Interaction scope |
| EventId | string | Required | Semantic event identifier |
| Confidence | decimal | Required, configured min/max inclusive | Model confidence for event |
| MappingId | string | Required | Resolved configured mapping id |
| Direction | string | Required | Evidence direction (increase/decrease/none) |
| ThemeTargets | list<string> | Required | Themes affected by mapping |
| ProcessedUtc | DateTime | Required | Processing timestamp |

**Validation**:
- `EventId` must resolve to configured mapping via canonical source path.
- `Confidence` outside configured range fails semantic step for interaction.
- Unknown event identifiers fail semantic step with diagnostics.

---

### EvidenceDeltaBreakdown

**Purpose**: Captures applied/capped/suppressed delta outcomes for each theme update attempt.

| Field | Type | Constraints | Description |
|---|---|---|---|
| InteractionId | string | Required | Interaction scope |
| ThemeId | string | Required | Target theme |
| SourceType | string | Required (`semantic`) | Delta source |
| RawDelta | decimal | Required | Pre-constraint delta |
| AppliedDelta | decimal | Required | Delta committed to evidence |
| CappedDelta | decimal | Required | Portion blocked by cap |
| SuppressedDelta | decimal | Required | Portion blocked by cooldown/lock/guard |
| SuppressionReasonCode | string? | Optional | Structured reason for suppression |

**Validation**:
- `AppliedDelta + CappedDelta + SuppressedDelta == RawDelta` within decimal tolerance.
- Blocked theme lock must force `AppliedDelta = 0` and produce suppression reason.

---

### ThemeLockState

**Purpose**: Represents blocked-theme lock authority used during evidence application and selection eligibility.

| Field | Type | Constraints | Description |
|---|---|---|---|
| ThemeId | string | Required | Theme identifier |
| IsBlocked | bool | Required | Blocked-state flag |
| LockedEvidenceValue | decimal | Required, currently `0` | Enforced lock value |
| LockReasonCode | string | Required when blocked | Structured lock rationale |

**Validation**:
- If `IsBlocked = true`, evidence value remains at lock value despite semantic support.
- Lock state is consumed by both evidence update and eligibility evaluation.

---

### SelectionEvidenceSnapshot

**Purpose**: Final per-interaction evidence snapshot consumed by ordering and candidate fit logic.

| Field | Type | Constraints | Description |
|---|---|---|---|
| SessionId | string | Required | Session scope |
| InteractionId | string | Required | Interaction scope |
| ThemeEvidence | dictionary<string, decimal> | Required | Final evidence values |
| PrimaryThemeId | string? | Optional | Top-ranked theme |
| SecondaryThemeId | string? | Optional | Runner-up theme |
| GeneratedUtc | DateTime | Required | Snapshot time |

**Validation**:
- Snapshot is produced once per interaction cycle after keyword + semantic application.
- Ranking and candidate fit must consume same snapshot instance/version.

---

### SemanticProcessingDiagnostic

**Purpose**: Explicit success/failure and contract diagnostics for semantic processing.

| Field | Type | Constraints | Description |
|---|---|---|---|
| SessionId | string | Required | Session scope |
| InteractionId | string | Required | Interaction scope |
| Severity | string | Required (`Information`, `Warning`, `Error`) | Diagnostic level |
| ReasonCode | string | Required | Machine-readable diagnostic reason |
| Message | string | Required | Human-readable explanation |
| SemanticStepSucceeded | bool | Required | Interaction semantic-step status |
| SemanticDeltaApplied | bool | Required | Whether semantic deltas were committed |
| OccurredUtc | DateTime | Required | Event time |

**Validation**:
- Invalid payload/config/out-of-range confidence must yield `SemanticStepSucceeded = false` and `SemanticDeltaApplied = false`.

---

## Invariants and Decision Path Rules

1. Exactly one semantic configuration source path is used at runtime (no fallback/default branch).
2. Semantic processing operates on latest interaction only (v1 scope).
3. Semantic updates are evidence-only and do not mutate adaptive stats directly.
4. Confidence validation failure aborts semantic delta application for the interaction.
5. Cap/cooldown/lock guards are applied before evidence commit.
6. Blocked themes remain at locked zero evidence and remain ineligible when lock requires.
7. Theme ordering and candidate fit consume the finalized evidence snapshot with semantic updates.

---

## State/Flow Notes

1. Parse semantic payload from latest interaction.
2. Resolve event mappings and validate confidence ranges.
3. Compute raw semantic deltas per theme.
4. Apply cap/cooldown/lock guards and produce `EvidenceDeltaBreakdown` records.
5. Commit applied deltas into `SelectionEvidenceSnapshot`.
6. Emit diagnostics and debug telemetry records regardless of contribution status.
7. Execute ranking and candidate fit with updated snapshot.
