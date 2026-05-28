# Research: Semantic Telemetry and Event-Driven Evidence

**Phase**: 0 - Outline and Research  
**Date**: 2026-05-18  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

---

## R-001: Canonical Semantic Configuration Source

**Decision**: Resolve semantic event mapping, confidence constraints, cap/cooldown parameters, and lock behavior exclusively from persisted RP configuration used by existing theme/session services.

**Rationale**: The feature requires no fallback/default behavior and UI-backed control. Persisted configuration provides one explicit source path and auditability.

**Alternatives considered**:
- Hardcoded runtime dictionaries: rejected because they create hidden defaults and bypass UI-backed configuration.
- Appsettings backup values: rejected because they introduce fallback behavior and source ambiguity.

---

## R-002: Semantic Scope for v1

**Decision**: Semantic processing uses the latest interaction only and contributes evidence-only updates.

**Rationale**: This is explicitly required by FR-013 and FR-014 and keeps runtime bounded and deterministic.

**Alternatives considered**:
- Full conversation history semantic aggregation: rejected as out of scope and higher risk for non-deterministic drift.

---

## R-003: Confidence Validation Boundary

**Decision**: Treat out-of-range confidence as a semantic-step failure for that interaction, emit explicit diagnostics, and apply zero semantic deltas.

**Rationale**: Matches clarification and FR-005a fail-fast contract.

**Alternatives considered**:
- Clamp confidence to nearest boundary: rejected because it mutates invalid input into implicit fallback behavior.
- Ignore only offending event and continue others: rejected because partial continuation weakens explicit-failure semantics for the interaction step.

---

## R-004: Telemetry Shape for Debug Observability

**Decision**: Emit a per-interaction semantic telemetry record with detected events, confidence, applied/capped/suppressed deltas, suppression reasons, and explicit no-contribution state.

**Rationale**: Supports SC-001 and SC-006 by making evidence flow inspectable without source-code inspection.

**Alternatives considered**:
- Aggregate-only totals: rejected because they cannot explain why evidence changed or was suppressed.
- Omit semantic section when empty: rejected because it hides no-contribution cases and violates explicit observability intent.

---

## R-005: Cap/Cooldown Enforcement Position

**Decision**: Apply cap/cooldown checks during semantic delta application before evidence commit and before ordering/fit consumption.

**Rationale**: Guarantees bounded evidence growth and a single deterministic final evidence snapshot for downstream decisions.

**Alternatives considered**:
- Post-hoc correction after ranking: rejected because ranking/fit would already have consumed invalidly high values.

---

## R-006: Blocked Theme Lock Enforcement

**Decision**: Lock enforcement happens before semantic commit and remains authoritative even when semantic confidence is positive.

**Rationale**: Required by FR-008 and regression lock safety expectations.

**Alternatives considered**:
- Allow temporary semantic accumulation and clamp at selection stage: rejected because it permits hidden progression and weakens lock invariants.

---

## R-007: Integration Point for Ranking/Fit Impact

**Decision**: Integrate semantic evidence in the existing evidence pipeline used by theme ordering and candidate fit so both consume the same final snapshot.

**Rationale**: Meets FR-010 and prevents duplicate scoring logic.

**Alternatives considered**:
- Separate semantic adjustment only in ranking: rejected because candidate fit behavior could diverge.
- Separate semantic adjustment only in candidate fit: rejected because ordering could diverge.

---

## R-008: Fail-Fast Diagnostic Contract

**Decision**: Semantic processing failures produce structured diagnostics identifying interaction, reason code, and zero-delta enforcement.

**Rationale**: Required for FR-005, SC-002, and operational debugging.

**Alternatives considered**:
- Log warning and continue silently: rejected as hidden fallback behavior.

---

## R-009: Required Test Coverage Matrix

**Decision**: Implement six mandatory test groups: mapping correctness, cap/cooldown, fail-fast, corruption progression by semantic events, blocked-theme lock regression, and end-to-end ranking/fit.

**Rationale**: Directly satisfies FR-011 and user-requested coverage areas.

**Alternatives considered**:
- Unit tests only: rejected because orchestration and ordering/fit integration would remain unverified.

---

## R-010: No-Fallback Enforcement Checklist

**Decision**: Treat no-fallback verification as a release gate with explicit evidence:

1. Show semantic configuration source resolution path.
2. Show exactly one active decision path.
3. Show no fallback/default branch remains.
4. Show missing required config fails explicitly.
5. Show configuration is UI-backed persisted data.

### Verification Evidence (2026-05-18)

1. Source resolution path:
	- Semantic mapping is resolved only via `IRPThemeService.ResolveSemanticEventMappingsByProfileAsync(selectedProfileId)` inside adaptive semantic processing.
2. Exactly one active decision path:
	- Runtime semantic mapping selection does not branch to appsettings/global/default maps.
	- The active profile id is required (`SelectedRPThemeProfileId`) and used as the only lookup key.
3. No fallback/default branch remains for semantic mapping behavior:
	- Missing RP theme service, missing selected profile id, unknown event id, and missing mapped theme all throw explicit `InvalidOperationException` with semantic reason codes.
4. Missing required configuration fails explicitly:
	- `semantic_config_missing` is emitted as hard failure for missing semantic configuration dependencies.
5. UI/config surface linkage:
	- Semantic mappings are persisted and loaded through RP theme/profile data, aligning runtime behavior with persisted UI-backed configuration rather than hidden code defaults.

**Rationale**: Aligns implementation with repository non-negotiable RP contract.

---

## R-011: Semantic Stat Mapping Source and Gate Consumption

**Decision**: Resolve semantic stat mappings exclusively from `IRPThemeService.ResolveSemanticStatMappingsByProfileAsync(selectedProfileId)` and apply deltas during adaptive interaction processing before lifecycle gate evaluation maps character stats to V2 snapshots.

**Rationale**: Preserves one explicit configuration source path, ensures no hidden runtime defaults, and guarantees gate metrics consume post-semantic stat state.

**Alternatives considered**:
- Hardcoded semantic stat defaults when mapping is missing: rejected as fallback behavior.
- Applying stat mappings after gate evaluation: rejected because gate decisions would use stale pre-semantic stats.

### Verification Evidence (2026-05-19)

1. Source resolution path:
	- Semantic stat mapping is resolved only via `ResolveSemanticStatMappingsByProfileAsync` in adaptive semantic processing.
2. Exactly one active decision path:
	- Per-event stat mapping uses the selected profile map only; there is no secondary lookup path.
3. No fallback/default branch remains:
	- Missing stat mapping for a semantic event triggers explicit fail-fast with reason code (`semantic_unknown_event`) and message indicating missing stat mapping.
4. Missing required configuration fails explicitly:
	- Unknown event, confidence mismatch, missing mapped theme, and missing profile dependencies all throw explicit failures and mark semantic step unsuccessful.
5. Gate consumption of post-semantic stats:
	- Lifecycle tests verify transition outcome changes only after Desire crosses threshold, and stays blocked when semantic delta is suppressed.
