# Research: DreamGenClone RolePlay v2 Unified Scenario Intelligence

**Phase**: 0 - Outline & Research  
**Date**: 2026-04-12  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

---

## R-001: Deterministic Two-Stage Scenario Selection

**Decision**: Use Stage A willingness tier (Desire-threshold based) as a hard pre-gate, then Stage B multi-stat eligibility plus fit-score ranking with deterministic ordering.

**Rationale**: This matches FR-006 while preventing ineligible scenarios from entering rank competition and preserving reproducibility.

**Alternatives considered**:
- Single-pass weighted scoring - rejected because it blurs willingness gating and weakens explainability.
- Manual-only scenario selection - rejected because it conflicts with adaptive automation goals.

---

## R-002: Near-Tie Commitment Handling

**Decision**: Apply hysteresis commitment: a candidate must exceed the near-tie lead threshold for configurable N consecutive evaluations before commitment.

**Rationale**: Prevents scenario flapping under noisy or oscillating user input and aligns with clarified requirement for sustained-lead commitment.

**Alternatives considered**:
- Immediate winner-take-all on each turn - rejected because it causes unstable commitments.
- Never commit under near-tie conditions - rejected because it can stall progression.

---

## R-003: Narrative Phase Lifecycle Governance

**Decision**: Implement strict ordered transitions BuildUp -> Committed -> Approaching -> Climax -> Reset -> BuildUp, with explicit transition evidence and reason codes.

**Rationale**: Enforces FR-003 and FR-005 while enabling deterministic replay and diagnostics.

**Alternatives considered**:
- Flexible jump transitions - rejected because it violates ordered lifecycle requirement.
- Time-based auto-transition only - rejected because interaction evidence and thresholds are required inputs.

---

## R-004: Guidance Budget Composition Policy

**Decision**: Allocate prompt budget by reserved minimum quotas for scenario guidance, concept injection, and willingness guidance, then consume a shared overflow pool by deterministic priority and tie-break keys.

**Rationale**: Guarantees baseline representation for each guidance source while remaining adaptable under high-pressure budget conditions.

**Alternatives considered**:
- Strict fixed split only - rejected because it wastes unused capacity.
- Global priority queue only - rejected because lower-priority guidance can starve.

---

## R-005: Decision Option Transparency Default

**Decision**: Default decision-option transparency to Directional mode, while still supporting Hidden and Explicit modes via override.

**Rationale**: Balances immersion and user steering while satisfying FR-019 and clarified default behavior.

**Alternatives considered**:
- Hidden default - rejected because it reduces predictable steering.
- Explicit default - rejected because it can reduce narrative immersion.

---

## R-006: Manual Override Authorization and Auditability

**Decision**: Permit manual scenario override only for session owner and operator/admin roles; reject all unauthorized attempts with structured audit events.

**Rationale**: Supports controlled reset behavior without introducing broad privilege risks.

**Alternatives considered**:
- Any participant may override - rejected because it weakens session control.
- Admin-only override - rejected because it limits valid owner recovery workflows.

---

## R-007: Session Compatibility Policy (No Legacy Support)

**Decision**: Support v2 schema sessions only; pre-v2 payloads are rejected with explicit unsupported-version errors and recovery guidance.

**Rationale**: Matches clarified scope decision and avoids in-release migration complexity.

**Alternatives considered**:
- Automatic migration - rejected because legacy support is explicitly out of scope.
- Silent fallback defaults - rejected because it risks hidden data corruption.

---

## R-008: Persistence and Logging Best Practices for v2

**Decision**: Persist all v2 feature state in SQLite with schema version fields and compatibility checks; emit Serilog structured events for selection, transition, injection, decision outcomes, rejections, and errors.

**Rationale**: Aligns with constitution principles VIII and IX and FR-029 through FR-032.

**Alternatives considered**:
- In-memory-only adaptive state - rejected because persistence and reload integrity are mandatory.
- Flat text logging - rejected because it limits diagnostics and tunability.
