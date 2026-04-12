# Feature Specification: Adaptive Scenario Selection Engine Redesign 2

**Feature Branch**: `002-adaptive-scenario-redesign2`  
**Created**: 2026-04-11  
**Status**: Draft  
**Input**: User description: "Design Plan: Adaptive Scenario Selection Engine Redesign 2"

## Clarifications

### Session 2026-04-11

- Q: What tie-deferral rule should govern top scenario candidates during commitment evaluation? -> A: Option B (tie when top-two fit score delta <= 0.10, then require at least 1 additional interaction before re-evaluation).
- Q: What commitment threshold should trigger Build-Up -> Committed transitions? -> A: Option C (commit when fit score >= 0.60 and at least 2 build-up interactions have occurred).
- Q: How should manual scenario override behave during active phases? -> A: Option B (allow override at any phase, force Reset, then start new Build-Up with requested scenario as top priority).
- Q: What thresholds should trigger Committed -> Approaching? -> A: Option B (active scenario score >= 60, average desire >= 65, average restraint <= 45, and at least 3 interactions since commitment).
- Q: What thresholds should trigger Approaching -> Climax? -> A: Option C (active scenario score >= 80, average desire >= 75, average restraint <= 35, at least 2 interactions in approaching, with explicit user-triggered climax override allowed).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Commit to a Single Scenario Direction (Priority: P1)

As a role-play user, I want the system to commit to one scenario direction during a narrative cycle so the story feels focused and intentional rather than a blend of competing directions.

**Why this priority**: Clear narrative direction is the core value of this redesign; without commitment logic, all other improvements are diluted.

**Independent Test**: Can be fully tested by running a session where multiple scenario candidates are possible and verifying the system selects one scenario, records the commitment, and suppresses conflicting candidates until reset.

**Acceptance Scenarios**:

1. **Given** multiple scenario candidates are viable, **When** one candidate reaches commitment conditions (fit score >= 0.60 and at least 2 build-up interactions), **Then** the system commits to that single scenario and records it as the active scenario.
2. **Given** a scenario is committed, **When** additional interactions occur, **Then** the system keeps the same active scenario until transition conditions are met.
3. **Given** no candidate reaches commitment conditions, **When** interactions continue, **Then** the system remains in build-up and does not force a commitment.

---

### User Story 2 - Progress Through a Complete Narrative Cycle (Priority: P2)

As a role-play user, I want the narrative to move through build-up, commitment, approaching, climax, and reset phases so long sessions feel structured and can transition into new scenario cycles.

**Why this priority**: Phase progression enables pacing, momentum, and repeatable episodic structure across long sessions.

**Independent Test**: Can be tested by simulating interactions across a full cycle and verifying each phase transition occurs only when defined conditions are met and that reset returns the system to build-up with continuity preserved.

**Acceptance Scenarios**:

1. **Given** the session is in build-up, **When** commitment conditions are met, **Then** phase changes to committed and active scenario is set.
2. **Given** the session is committed, **When** active scenario score >= 60, average desire >= 65, average restraint <= 45, and at least 3 interactions have occurred since commitment, **Then** phase advances to approaching.
3. **Given** the session is approaching, **When** active scenario score >= 80, average desire >= 75, average restraint <= 35, and at least 2 interactions have occurred in approaching, **Then** phase advances to climax.
4. **Given** climax has completed, **When** reset is triggered, **Then** the system applies semi-reset rules and returns to build-up for the next cycle.

---

### User Story 3 - Receive Scenario-Specific Climax Guidance (Priority: P3)

As a role-play user, I want guidance to reflect the currently selected scenario type during high-intensity moments so generated content feels tailored and coherent.

**Why this priority**: Scenario-specific guidance improves quality and consistency at the most important narrative moments.

**Independent Test**: Can be tested by comparing guidance outputs across at least two different committed scenarios and verifying each output reflects only the selected scenario's framing and excludes conflicting framing.

**Acceptance Scenarios**:

1. **Given** a scenario is in approaching or climax phase, **When** guidance is generated, **Then** instructions align with the active scenario's defining dynamics.
2. **Given** competing scenario signals are present, **When** guidance is generated during committed or climax phase, **Then** conflicting scenario framing is excluded.

### Edge Cases

- What happens when two scenario candidates remain within a narrow confidence band for several interactions? The system treats candidates as tied when top-two fit score delta is <= 0.10, stays in build-up, and requires at least 1 additional interaction before re-evaluation.
- How does the system handle missing or partial character-state data? The system uses available data, lowers confidence proportionally, and avoids forced commitment until minimum confidence is met.
- What happens when the user explicitly requests a scenario that conflicts with current state patterns? The system allows override at any phase, transitions to Reset first, then starts a new Build-Up with the requested scenario as top priority and records the override event for auditability.
- What happens if a climax trigger is reached immediately after a reset due to residual values? Reset safeguards must prevent instant re-entry into climax by enforcing minimum interaction progression before climax eligibility.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST evaluate scenario candidates as mutually exclusive commitment options and maintain at most one active scenario per narrative cycle.
- **FR-002**: System MUST compute a scenario fit score for each eligible user-preference scenario using character-state alignment, narrative evidence, and preference priority.
- **FR-003**: System MUST prioritize user preference tiers so higher-priority preferences are favored when fit is otherwise comparable.
- **FR-004**: System MUST transition through narrative phases in order: Build-Up, Committed, Approaching, Climax, Reset.
- **FR-005**: System MUST apply explicit transition conditions for each phase change and record transition reasons, including Build-Up -> Committed requiring fit score >= 0.60 and at least 2 build-up interactions.
- **FR-006**: System MUST defer commitment when top-two scenario fit scores are tied with delta <= 0.10, remain in build-up, and re-evaluate only after at least 1 additional interaction unless manually overridden by user steering.
- **FR-007**: System MUST suppress non-active scenario influence after commitment while preserving enough evidence for post-cycle analysis.
- **FR-008**: System MUST generate phase-aware guidance that changes behavior between build-up, committed, approaching, climax, and reset.
- **FR-009**: System MUST generate scenario-specific guidance during approaching and climax phases that reflects the active scenario and excludes contradictory framing.
- **FR-010**: System MUST execute a semi-reset after climax that reduces elevated narrative signals while preserving continuity-relevant relationship context.
- **FR-011**: System MUST clear active scenario commitment during reset and start a new build-up cycle after reset completion.
- **FR-012**: System MUST maintain per-session scenario history with completion metadata including cycle order, completion time, interaction count, and peak intensity markers.
- **FR-013**: System MUST expose selection rationale and confidence outputs suitable for debugging and behavior review.
- **FR-018**: System MUST support manual scenario override at any phase by forcing a Reset transition before applying the requested scenario as top priority in the next Build-Up cycle.
- **FR-019**: System MUST transition from Committed to Approaching only when active scenario score >= 60, average desire >= 65, average restraint <= 45, and at least 3 interactions have occurred since commitment.
- **FR-020**: System MUST transition from Approaching to Climax only when active scenario score >= 80, average desire >= 75, average restraint <= 35, and at least 2 interactions have occurred in approaching.
- **FR-021**: System MUST allow explicit user-triggered climax override from Approaching when safety and policy checks pass, and record the override trigger reason.
- **FR-014**: Persisted feature data MUST use SQLite unless this spec explicitly states and justifies a different store (for example session storage, local storage, or another backend store).
- **FR-015**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-016**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-017**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities *(include if feature involves data)*

- **Adaptive Scenario State**: Session-level state holding current narrative phase, active scenario commitment, commitment timestamp, and completed cycle count.
- **Scenario Candidate Evaluation**: Per-candidate evaluation record containing fit score, confidence, preference tier, and rationale details.
- **Scenario Phase Transition Event**: Immutable event describing a phase change, trigger conditions, timestamp, and summarized contributing signals.
- **Scenario Completion Metadata**: Historical record for each finished cycle including completion time, interaction count, peak intensity values, and optional notes.
- **Scenario Guidance Context**: Guidance payload containing phase, active scenario, and contextual factors used to produce phase-specific instructions.

### Assumptions

- User preference tiers are already available to the adaptive engine for scenario ranking.
- Existing character-state signals remain the source of truth for behavioral context.
- A single narrative cycle is defined as the span from build-up entry to reset completion.
- Manual user steering is allowed and should override ambiguous automatic selection when needed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In at least 90% of eligible sessions, one clear scenario commitment is established within the first 6 interactions after sufficient state evidence exists.
- **SC-002**: In at least 95% of committed cycles, the active scenario remains stable until reset, unless a user explicitly overrides it.
- **SC-003**: In at least 90% of completed cycles, the system advances through all required narrative phases in the defined order without skipping mandatory transitions.
- **SC-004**: In evaluator review, at least 85% of approaching/climax outputs are judged to match the active scenario framing without contradictory scenario leakage.
- **SC-005**: In long-session tests covering at least 3 cycles, at least 90% of resets enable a new scenario build-up within 2 interactions while preserving relationship continuity signals.
