# Feature Specification: Semantic Telemetry and Event-Driven Evidence

**Feature Branch**: `001-semantic-telemetry-tests`  
**Created**: 2026-05-18  
**Status**: Draft  
**Input**: User description: "Extend debug telemetry to show semantic events, confidence, applied/capped/suppressed deltas. Add unit/integration/regression tests for mapping correctness, cap/cooldown behavior, fail-fast behavior, and corruption progression driven by semantic events."

## Clarifications

### Session 2026-05-18

- Q: How should out-of-range semantic confidence values be handled? -> A: Fail the semantic processing step for that interaction, apply no semantic deltas, and emit explicit diagnostics.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Inspect Semantic Telemetry in Debug Workspace (Priority: P1)

As a roleplay workspace operator, I need per-interaction debug telemetry that clearly shows detected semantic events, confidence, and applied/capped/suppressed evidence deltas so I can verify why theme evidence changed.

**Why this priority**: Without observable telemetry, semantic behavior cannot be diagnosed, trusted, or safely tuned.

**Independent Test**: Can be fully tested by running one interaction in debug mode and confirming the telemetry trace includes event, confidence, and delta breakdown fields that explain the resulting score changes.

**Acceptance Scenarios**:

1. **Given** a single roleplay interaction that triggers a recognized semantic event, **When** debug telemetry is rendered, **Then** the output includes the event name, confidence value, and applied/capped/suppressed delta values for that interaction.
2. **Given** an interaction that does not produce usable semantic evidence, **When** telemetry is rendered, **Then** the output explicitly shows no semantic contribution rather than silently omitting the semantic section.
3. **Given** malformed semantic payload, out-of-range confidence, or missing required semantic configuration, **When** semantic processing is requested, **Then** semantic processing for that interaction fails explicitly with diagnostics, applies no semantic deltas, and does not continue with fallback values.

---

### User Story 2 - Validate Semantic Evidence Influences Theme Decisions (Priority: P1)

As a roleplay system verifier, I need semantic evidence to affect theme ordering and candidate fit decisions, including corruption progression signals, so behavioral changes can occur from meaning-based signals even when keyword hints are absent.

**Why this priority**: The feature's core value is improved decision quality from semantic interpretation rather than keyword-only matching.

**Independent Test**: Can be fully tested by supplying interactions with semantic intent and verifying theme ordering and candidate fit outcomes change in expected directions.

**Acceptance Scenarios**:

1. **Given** an interaction semantically equivalent to "lie to husband" and no corruption keyword match, **When** evidence is computed, **Then** corruption evidence increases with semantic rationale captured in telemetry.
2. **Given** two competing themes before semantic evidence is applied, **When** semantic evidence is applied, **Then** primary and secondary theme ordering updates according to resulting evidence scores.
3. **Given** a candidate selection decision point, **When** semantic evidence is present, **Then** candidate fit behavior reflects the updated evidence and confidence signal.

---

### User Story 3 - Enforce Safety Guards Against Over-Accumulation and Locked Themes (Priority: P2)

As a quality engineer, I need cap/cooldown limits and blocked-theme constraints to remain enforced despite repeated or strong semantic evidence so progression remains stable and policy-safe.

**Why this priority**: Preventing runaway accumulation and policy bypasses is required to keep adaptive behavior trustworthy in production.

**Independent Test**: Can be fully tested by replaying repeated adjacent-turn events and blocked-theme scenarios, then verifying caps, cooldown suppression, and lock behavior in telemetry and outcomes.

**Acceptance Scenarios**:

1. **Given** repeated similar semantic events in adjacent turns, **When** evidence updates are applied, **Then** cap and cooldown rules suppress additional accumulation and report suppressed delta amounts.
2. **Given** a blocked theme with lock state at zero, **When** semantic evidence supports that theme, **Then** the theme remains locked at zero and selection eligibility does not change.
3. **Given** a regression suite covering prior lock behavior, **When** semantic evidence support is introduced, **Then** prior blocked-theme protections still pass unchanged.

---

### Edge Cases

- Semantic payload exists but confidence is outside allowed range and the semantic processing step for that interaction must fail with diagnostics and zero semantic delta application.
- Semantic payload references an unknown event identifier.
- Required semantic-event mapping configuration is missing or incomplete.
- Repeated semantically similar events occur across consecutive turns near cap boundaries.
- Semantic evidence and keyword evidence disagree on direction for the same theme.
- Theme is blocked and has non-zero semantic confidence support.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST emit debug telemetry for each processed interaction that includes semantic event identifiers, confidence values, and resulting evidence deltas.
- **FR-002**: Telemetry MUST separate and report applied, capped, and suppressed portions of each semantic delta.
- **FR-003**: System MUST preserve keyword-based evidence processing while adding semantic evidence as an additive signal path.
- **FR-004**: Semantic evidence processing MUST use explicit configured mappings and constraints only.
- **FR-005**: System MUST fail fast with explicit diagnostics when semantic payloads are invalid, when any semantic confidence value is out of range, or when required semantic configuration is missing or invalid.
- **FR-005a**: On out-of-range semantic confidence, system MUST fail semantic processing for that interaction and MUST apply no semantic deltas for that interaction.
- **FR-006**: System MUST NOT apply hidden defaults, fallback branches, or guessed substitute values for semantic event mapping, confidence thresholds, caps, cooldown, or lock behavior.
- **FR-007**: System MUST enforce cap and cooldown behavior so repeated adjacent-turn semantic events do not over-accumulate evidence.
- **FR-008**: Blocked themes MUST remain locked at zero even when semantic evidence supports them.
- **FR-009**: Semantic evidence from the latest interaction MUST be sufficient to influence corruption progression evidence without requiring corruption keywords.
- **FR-010**: Candidate fit and theme ordering decisions MUST consume updated evidence outcomes produced by semantic events.
- **FR-011**: Test coverage MUST include unit, integration, regression, and end-to-end validation for mapping correctness, cap/cooldown behavior, fail-fast behavior, corruption progression, blocked-theme lock enforcement, and ranking/fit changes.
- **FR-012**: Manual debug verification MUST demonstrate one interaction trace showing semantic event details and resulting deltas in workspace diagnostics.
- **FR-013**: Semantic analysis scope for v1 MUST use the latest interaction only.
- **FR-014**: Semantic output for v1 MUST apply evidence-only updates and MUST NOT change adaptive stats directly.

### Key Entities *(include if feature involves data)*

- **Semantic Event Evidence Record**: Represents one semantic event detected from the latest interaction, including event identifier, confidence, and intended evidence direction.
- **Evidence Delta Breakdown**: Represents computed delta components for a theme update, including applied amount, capped amount, and suppressed amount with reason.
- **Theme Lock State**: Represents whether a theme is blocked and the enforced locked evidence value used by ordering and candidate selection.
- **Selection Evidence Snapshot**: Represents the per-interaction evidence state consumed by theme ordering and candidate fit decisions.
- **Semantic Processing Diagnostic**: Represents explicit success/failure diagnostics for semantic parsing, mapping validation, and no-fallback contract enforcement.

### Assumptions

- Existing keyword evidence pipeline remains active and unchanged except for additive semantic signal inclusion.
- Existing workspace debug surfaces are available to show additional semantic telemetry fields.
- Excluded items (phase-model redesign, causality chains, full-history NLP) remain out of scope for this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of debug-eligible interactions in test scenarios, telemetry includes semantic event trace with confidence and applied/capped/suppressed deltas.
- **SC-002**: 100% of invalid semantic payload or missing-required-config test cases fail explicitly with actionable diagnostics and zero fallback behavior.
- **SC-003**: In cap/cooldown regression scenarios with repeated adjacent-turn events, evidence growth remains within configured limits in 100% of runs.
- **SC-004**: In blocked-theme regression scenarios, blocked themes remain at locked zero evidence in 100% of runs even with positive semantic evidence.
- **SC-005**: In end-to-end ranking scenarios, semantic evidence changes theme ordering and candidate fit outcome in at least one controlled scenario without relying on keyword triggers.
- **SC-006**: Manual debug validation confirms one interaction trace that is understandable by a verifier without reading source code and includes rationale for final deltas.
