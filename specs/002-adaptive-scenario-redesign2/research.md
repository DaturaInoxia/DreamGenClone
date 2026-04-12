# Research: Adaptive Scenario Selection Engine Redesign 2

**Phase**: 0 - Outline & Research  
**Date**: 2026-04-11  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## R-001: Scenario Commitment Model

Decision: Use a single active scenario commitment model with mutually exclusive candidates.
Rationale: The feature objective is to replace blended narrative direction with one committed scenario per cycle.
Alternatives considered: Continue Top2 blend logic (rejected: narrative ambiguity); commit all candidates above threshold (rejected: contradictory guidance).

## R-002: Build-Up to Committed Gate

Decision: Transition Build-Up to Committed when top candidate fit score is >= 0.60 and at least 2 build-up interactions occurred.
Rationale: Matches clarified requirement and prevents premature lock-in.
Alternatives considered: Lower threshold 0.55 (rejected: unstable commitments); higher threshold 0.65 (rejected: slow convergence).

## R-003: Tie Deferral Rule

Decision: Treat top-two candidates as tied when fit delta is <= 0.10; remain in Build-Up and require at least 1 additional interaction before re-evaluation.
Rationale: Reduces flip-flopping while preserving responsiveness.
Alternatives considered: Immediate commit on top score (rejected: oscillation risk); wider tie window (rejected: overly delayed commitment).

## R-004: Committed to Approaching Gate

Decision: Transition Committed to Approaching only when active scenario score >= 60, average desire >= 65, average restraint <= 45, and at least 3 interactions since commitment.
Rationale: Creates measurable mid-cycle escalation and aligns with clarified thresholds.
Alternatives considered: score-only gate (rejected: misses emotional readiness); stricter 65/70 thresholds (rejected: pacing too slow).

## R-005: Approaching to Climax Gate

Decision: Transition Approaching to Climax when active scenario score >= 80, average desire >= 75, average restraint <= 35, and at least 2 interactions in approaching.
Rationale: Provides deterministic high-intensity trigger with minimum buildup.
Alternatives considered: automatic-only without interaction count (rejected: abrupt climax); manual-only climax (rejected: over-dependence on user command).

## R-006: Manual Climax Override

Decision: Allow explicit user-triggered climax override from Approaching, with policy/safety checks and override reason logging.
Rationale: Preserves user agency while maintaining auditable behavior.
Alternatives considered: disallow override (rejected: lower control); allow from any phase (rejected: coherence risk).

## R-007: Manual Scenario Override Behavior

Decision: Allow manual scenario override at any phase, but force Reset first, then start a new Build-Up with requested scenario as top priority.
Rationale: Prevents abrupt mid-phase contradictions and matches clarified requirement.
Alternatives considered: hard switch in-place (rejected: coherence break); queue until next natural reset (rejected: delayed user intent).

## R-008: Semi-Reset Semantics

Decision: Apply semi-reset after climax: clear active scenario, lower elevated intensity-related signals, preserve relationship continuity signals, and enforce minimum progression before re-entering climax.
Rationale: Supports episodic cycles while avoiding immediate rebound.
Alternatives considered: full wipe (rejected: loses continuity); no reset (rejected: score saturation).

## R-009: Guidance Integration Point

Decision: Inject phase-aware and scenario-specific guidance into role-play continuation prompt assembly based on current narrative phase and active scenario.
Rationale: Guarantees output framing matches committed scenario during approaching/climax.
Alternatives considered: generic guidance only (rejected: mismatch risk); post-generation correction (rejected: brittle).

## R-010: Persistence Strategy

Decision: Persist new adaptive fields (phase, active scenario, history, transitions) inside existing SQLite-backed role-play session payloads.
Rationale: Existing architecture already persists adaptive state through SQLite and JSON payload boundaries.
Alternatives considered: separate scenario-cycle table (rejected: unnecessary complexity for current scope).

## R-011: Logging and Auditability

Decision: Emit Information-level logs for candidate evaluation summary, commitment, phase transitions, reset execution, and overrides, with structured fields.
Rationale: Meets constitution observability requirements and eases behavior debugging.
Alternatives considered: Debug-only logs (rejected: insufficient operational visibility).

## R-012: Determinism and Testing

Decision: Keep deterministic scoring and transition logic with fixed thresholds and ordered tie-breaking; validate through unit and integration replay tests.
Rationale: Deterministic state evolution is a constitutional requirement.
Alternatives considered: probabilistic selection (rejected: non-reproducible outcomes).

## Implementation Notes (2026-04-11)

- US2 completed with threshold-gated transitions, reset handling, and reset-first manual override semantics in adaptive state orchestration.
- US3 completed with phase-aware guidance context injection in continuation prompt assembly and explicit framing guards for committed/approaching/climax phases.
- Structured Information logs were aligned to include session, phase, active scenario, and interaction context for commitment, transitions, overrides, and guidance generation.

## Edge-Case Rationale Updates

- Manual override now always forces reset-first behavior before re-prioritizing requested scenario to avoid incoherent mid-phase pivots.
- Guidance generation excludes contradictory scenario framing when an active scenario is committed, preventing leakage in approaching/climax prompts.
- Replay determinism is verified through multi-cycle sequence tests to reduce regression risk from state-machine changes.
