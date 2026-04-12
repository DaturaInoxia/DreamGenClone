# Feature Specification: DreamGenClone RolePlay v2 Unified Scenario Intelligence

**Feature Branch**: `001-roleplay-v2-unification`  
**Created**: 2026-04-12  
**Status**: Draft  
**Input**: User description: "Build a single-release v2 for DreamGenClone RolePlay that unifies Adaptive Scenario Selection, Context-Aware Reference Injection, and Context-Aware Stat-Altering Question System, with canonical stats expanded to include Loyalty and SelfRespect."

## Clarifications

### Session 2026-04-12

- Q: How should Loyalty and SelfRespect be initialized during legacy session migration? -> A: No legacy support.
- Q: When top scenario candidates remain near tie across turns, what commitment rule applies? -> A: Use hysteresis with threshold lead held for N consecutive evaluations before commitment.
- Q: Who can invoke manual scenario override? -> A: Session owner and operator/admin only.
- Q: How should prompt budget be allocated across scenario, concept, and willingness guidance? -> A: Use reserved minimum quotas plus a shared overflow pool consumed by priority.
- Q: Which default transparency mode should be used for decision options in normal runtime? -> A: Directional.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Commit to one coherent scenario direction (Priority: P1)

As a role-play user, I want the engine to commit to one scenario direction per cycle so the narrative feels intentional instead of blending conflicting directions.

**Why this priority**: Narrative coherence and directional commitment are the core value of v2 and directly shape user-perceived quality.

**Independent Test**: Create a session with competing scenario signals, run multiple interactions, and verify one scenario is committed, persisted, and kept active until valid transition or reset conditions are met.

**Acceptance Scenarios**:

1. **Given** multiple viable scenario candidates, **When** one candidate crosses commitment criteria, **Then** the system sets it as the only active scenario for that cycle.
2. **Given** an active scenario is committed, **When** additional interactions occur, **Then** competing scenario influence is suppressed until allowed transition or reset.
3. **Given** no candidate meets commitment criteria, **When** interactions continue, **Then** the engine remains in BuildUp and does not force commitment.
4. **Given** top candidates are within the near-tie band, **When** no candidate holds a threshold lead for the configured consecutive evaluations, **Then** the engine remains in BuildUp without commitment.

---

### User Story 2 - Progress through a full phase lifecycle (Priority: P1)

As a role-play user, I want the narrative to progress through BuildUp, Committed, Approaching, Climax, and Reset so long sessions have pacing and momentum.

**Why this priority**: Lifecycle progression creates episodic structure and predictable cadence needed for long-form role-play.

**Independent Test**: Simulate a full narrative cycle and verify transitions occur in valid order with rationale captured and no illegal phase jumps.

**Acceptance Scenarios**:

1. **Given** BuildUp, **When** commitment conditions are met, **Then** phase becomes Committed and active scenario is recorded.
2. **Given** Committed, **When** approaching thresholds are met, **Then** phase becomes Approaching.
3. **Given** Approaching, **When** climax thresholds are met, **Then** phase becomes Climax.
4. **Given** Climax complete, **When** reset executes, **Then** phase becomes Reset and returns to BuildUp for the next cycle.

---

### User Story 3 - Get context-appropriate reference injection (Priority: P1)

As a role-play user, I want prompt guidance to include only relevant behavioral concepts so outputs remain coherent without context bloat.

**Why this priority**: Reference relevance and bounded context volume directly affect consistency and quality of narrative outputs.

**Independent Test**: Replay identical session state multiple times and verify deterministic concept selection, conflict handling, and bounded guidance payload.

**Acceptance Scenarios**:

1. **Given** active character state and phase, **When** relevance is evaluated, **Then** only matching concepts are selected.
2. **Given** conflicting concepts apply, **When** resolution runs, **Then** stable priority rules produce a consistent final set.
3. **Given** prompt budget caps, **When** selected concepts exceed limits, **Then** lower-priority concepts are truncated first using deterministic tie-breaking.

---

### User Story 4 - Use stat-altering narrative choices (Priority: P2)

As a role-play user, I want natural-language options that alter stats so I can steer narrative evolution without writing explicit stat instructions.

**Why this priority**: Choice-driven stat mutation improves control and accessibility while preserving immersion.

**Independent Test**: Trigger decision points under known conditions and verify selected options apply intended stat deltas and produce expected downstream behavior.

**Acceptance Scenarios**:

1. **Given** a tipping-point context, **When** a decision point appears, **Then** options reflect current scenario and phase.
2. **Given** an option is chosen, **When** execution completes, **Then** associated stat changes are persisted with auditable rationale.
3. **Given** freeform text input is chosen, **When** custom response is submitted, **Then** fallback interpretation applies and mapped effects are persisted.
4. **Given** no explicit transparency override is configured, **When** options are presented, **Then** directional cues are shown by default without full numeric stat deltas.

---

### User Story 5 - Persist v2 state reliably (Priority: P2)

As an operator, I want v2 state persisted consistently so active v2 sessions remain stable across reloads.

**Why this priority**: Reliable persistence is mandatory for trust and operational continuity within v2-supported sessions.

**Independent Test**: Persist and reload v2 sessions across interactions and verify phase, scenario, stats, and history remain consistent without data loss.

**Acceptance Scenarios**:

1. **Given** persisted v2 session state, **When** reloaded, **Then** active phase, active scenario, canonical stats, and cycle history remain consistent.
2. **Given** a session payload that predates v2 schema requirements, **When** load is attempted, **Then** the system rejects it with an explicit unsupported-version error.
3. **Given** profile updates and version changes within v2, **When** sessions are reopened, **Then** references resolve without session corruption.

---

### User Story 6 - Observe and debug engine decisions (Priority: P3)

As a developer/operator, I want transparent rationale for selection, transitions, and injections so behavior can be tuned with low regression risk.

**Why this priority**: Explainability and observability reduce tuning time and improve confidence during rollout.

**Independent Test**: Execute scenario selection, phase transitions, concept injection, and decision outcomes while diagnostics are enabled and verify each path produces traceable rationale artifacts.

**Acceptance Scenarios**:

1. **Given** scenario selection occurs, **When** diagnostics are reviewed, **Then** top candidates, fit scores, and ranking rationale are visible.
2. **Given** a phase transition occurs, **When** transition history is inspected, **Then** thresholds and triggering evidence are recorded.
3. **Given** concept injection executes, **When** diagnostics are enabled, **Then** selected and skipped concepts plus budget usage are visible.

### Edge Cases

- Top two scenario candidates remain near tie for multiple turns and never satisfy sustained-lead hysteresis.
- Session payload predates v2 schema and lacks required canonical stats.
- Rapid contradictory user choices produce stat oscillation over short intervals.
- Reset is immediately followed by high residual scores that might force premature recommitment.
- Active scenario conflicts with an explicit manual override request.
- Unauthorized actor attempts manual scenario override.
- Concept reference library changes during an in-progress session cycle.
- Decision point trigger occurs but no valid options satisfy prerequisites.
- Guidance payload budget is exceeded by combined scenario framing, concept injection, and willingness guidance.
- Reserved quota and overflow-pool competition produce equal-priority contention within a single cycle.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support exactly one active scenario per narrative cycle.
- **FR-002**: System MUST evaluate scenario candidates with a fit score derived from state alignment, narrative evidence, and willingness stage output, and apply deterministic tie ordering for exact score ties.
- **FR-003**: System MUST enforce ordered phase progression: BuildUp, Committed, Approaching, Climax, Reset.
- **FR-004**: System MUST support tunable transition thresholds and interaction-count gates for phase changes.
- **FR-004a**: System MUST use hysteresis for scenario commitment, requiring a candidate to exceed the near-tie threshold lead for a configurable N consecutive evaluations before commitment.
- **FR-005**: System MUST preserve explicit rationale for every phase transition.
- **FR-006**: System MUST apply a two-stage decision model: Stage A willingness tier from Desire thresholds, then Stage B scenario eligibility from multi-stat gates.
- **FR-007**: System MUST treat Desire, Restraint, Tension, Connection, Dominance, Loyalty, and SelfRespect as canonical v2 stats.
- **FR-008**: System MUST support per-session stat snapshots for all active characters and profile references.
- **FR-009**: System MUST generate scenario-specific guidance for Approaching and Climax phases.
- **FR-010**: System MUST suppress contradictory scenario framing once a scenario is committed for the current cycle.
- **FR-011**: System MUST execute semi-reset behavior after climax while preserving continuity-relevant context.
- **FR-012**: System MUST maintain scenario cycle history with completion metadata and phase timeline summaries.
- **FR-013**: System MUST provide deterministic relevance evaluation for concept injection when input state is unchanged.
- **FR-014**: System MUST model behavioral concepts with trigger conditions, categories, and priority metadata.
- **FR-015**: System MUST resolve concept conflicts using stable, deterministic priority rules.
- **FR-016**: System MUST enforce guidance budget limits and deterministic truncation when selected concepts exceed limits.
- **FR-016a**: System MUST allocate prompt budget using reserved minimum quotas for scenario guidance, concept injection, and willingness guidance.
- **FR-016b**: System MUST provide a shared overflow pool distributed by deterministic priority once reserved quotas are satisfied.
- **FR-016c**: System MUST apply deterministic tie-breaking for equal-priority overflow candidates.
- **FR-017**: System MUST support injection triggers at interaction start, phase change, significant stat change, and manual override.
- **FR-017a**: System MUST authorize manual scenario override only for session owner and operator/admin roles.
- **FR-017b**: System MUST reject unauthorized override attempts with explicit errors and audit records.
- **FR-018**: System MUST provide context-aware narrative decision points at defined trigger moments.
- **FR-019**: System MUST support hidden, directional, and explicit transparency modes for decision options.
- **FR-019a**: System MUST default to directional transparency mode when no explicit override is configured.
- **FR-020**: System MUST persist option outcomes and resulting stat deltas with auditable rationale.
- **FR-021**: System MUST accept freeform custom responses when listed options are insufficient and apply fallback interpretation rules.
- **FR-022**: System MUST provide baseline tunable formulas for Cheating Propensity, Agency, and scenario fit.
- **FR-023**: System MUST version formula configuration and record the active formula version per session.
- **FR-024**: System MUST require Desire, Restraint, Tension, Connection, Dominance, Loyalty, and SelfRespect for all supported sessions.
- **FR-025**: System MUST reject non-v2 session payloads with explicit unsupported-version errors and recovery guidance.
- **FR-026**: System MUST treat legacy-session migration and backward compatibility as out of scope for v2.
- **FR-027**: System MUST preserve version history for profile and concept reference updates affecting active sessions.
- **FR-028**: System MUST expose diagnostics for candidate scoring, phase transitions, selected concepts, and decision outcomes.
- **FR-029**: System MUST persist feature state in the project-standard relational session store with explicit schema versioning and compatibility checks.
- **FR-030**: System MUST emit structured logs with contextual properties across scenario evaluation, phase transitions, unsupported-version rejections, and injection flows.
- **FR-031**: System MUST log major execution paths at operational level and emit actionable failure events for diagnostics.
- **FR-032**: System MUST support runtime-configurable log verbosity and category filtering without code modification.
- **FR-033**: System MUST document v2 scope boundaries, explicitly separating included capabilities from deferred work.
- **FR-034**: System MUST treat the full safety-guardrail subsystem as a deferred follow-up requirement set and track it explicitly.
- **FR-035**: System MUST include acceptance test fixtures covering profile examples, threshold boundaries, unsupported-version rejection paths, and hysteresis tie-resolution cases.

### Key Entities *(include if feature involves data)*

- **AdaptiveScenarioState**: Per-session lifecycle aggregate containing active scenario, current phase, counters, and cycle metadata.
- **ScenarioCandidateEvaluation**: Candidate evaluation record containing fit score, confidence, tier gate results, and rationale attributes.
- **NarrativePhaseTransitionEvent**: Immutable transition record with source phase, target phase, trigger evidence, and transition rationale.
- **ScenarioCompletionMetadata**: Per-cycle completion summary including culmination markers, reset reason, and timestamps.
- **CharacterStatProfileV2**: Canonical stat model containing Desire, Restraint, Tension, Connection, Dominance, Loyalty, and SelfRespect.
- **WillingnessTierDefinition**: Desire threshold tier definition with allowed narrative-intensity guidance.
- **ScenarioEligibilityRuleSet**: Multi-stat eligibility rules that determine whether a scenario can enter candidate ranking.
- **BehavioralConcept**: Reusable concept reference with category, trigger conditions, relevance scoring inputs, and guidance content.
- **ConceptReferenceSet**: Versioned grouping of behavioral concepts attachable to profile, scenario, or session scope.
- **GuidanceBudgetPolicy**: Budget allocation model defining reserved quotas, overflow pool size, and deterministic priority rules.
- **DecisionPoint**: Generated narrative decision context with trigger source, phase context, and option bundle.
- **OverrideAuthorizationPolicy**: Access-control rule set defining who may invoke manual override and required audit fields.
- **DecisionOption**: User-selectable narrative option containing display text, prerequisites, visibility mode, and intended stat effects.
- **FormulaConfigVersion**: Versioned formula definition set with named parameters and effective dates used by a session.
- **UnsupportedSessionError**: Structured error artifact describing schema incompatibility, rejection reason, and recovery guidance.

## Assumptions

- Existing role-play session infrastructure and adaptive-state integration surfaces remain available and extensible.
- Existing preference tiers and profile-selection workflows are reusable with v2 model extensions.
- v2 is a single release combining scenario selection, concept injection, and decision-question systems without requiring full workspace redesign.
- Manual override behavior is available to force controlled re-prioritization through reset mechanics.
- Legacy-session migration is intentionally excluded; pre-v2 payloads are unsupported in v2 runtime flows.
- Full content-safety policy subsystem and policy UI remain deferred to a follow-up phase and are not release blockers for v2 core.

## Scope Boundary Notes

- Included in v2: deterministic scenario commitment, ordered lifecycle transitions, concept budget composition, decision-point stat mutation, v2 schema compatibility checks, diagnostics aggregation, and workspace-level diagnostics visibility.
- Deferred from v2: automated legacy-session migration, advanced policy-authoring UI, and full safety-guardrail policy runtime (content governance/rules engine).
- Operational expectation: unsupported/non-v2 payloads are rejected explicitly with recovery guidance; no best-effort partial conversion is attempted.

## Deferred Safety Follow-Up

- Follow-up item SFTY-01: define policy model and storage schema for content guardrails.
- Follow-up item SFTY-02: implement runtime moderation hooks before continuation generation.
- Follow-up item SFTY-03: expose operator-facing safety diagnostics and override audit UI.
- Follow-up item SFTY-04: add dedicated safety regression fixtures and policy acceptance tests.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At least 90% of eligible sessions establish a clear scenario commitment within the first 6 interactions after sufficient evidence exists.
- **SC-002**: At least 95% of committed cycles maintain one stable active scenario until reset unless explicit user override is triggered.
- **SC-003**: At least 90% of completed cycles traverse BuildUp, Committed, Approaching, Climax, and Reset in valid order with no illegal transitions.
- **SC-004**: At least 85% of evaluator-reviewed high-intensity outputs align with committed scenario framing and avoid contradictory narrative signals.
- **SC-005**: 100% of non-v2 session load attempts are rejected with explicit unsupported-version guidance and zero partial-state corruption.
- **SC-006**: At least 90% of decision-point interactions apply intended stat mutations and produce expected downstream narrative behavior.
- **SC-007**: At least 95% of repeated relevance evaluations from identical input state produce identical concept selections.
- **SC-008**: At least 90% of long-session validation runs complete at least 3 full cycles with coherent reset-to-rebuild pacing.
