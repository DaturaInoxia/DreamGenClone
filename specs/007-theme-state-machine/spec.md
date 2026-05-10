# Feature Specification: Theme State Machine Continuity

**Feature Branch**: `007-theme-state-machine`  
**Created**: 2026-05-08  
**Status**: Draft  
**Input**: User description: "Build a full implementation-grade plan for the chosen theme state-machine architecture with phased steps, file touchpoints, dependency order, verification, and strict no-fallback UI-backed runtime and persistence configuration requirements."

## Clarifications

### Session 2026-05-08

- Q: How should active machine scope be defined per session? -> A: Exactly one active machine per session, resolved from ActiveScenario -> RPTheme -> ThemeMachineDefinition; if resolution is ambiguous or missing, fail explicitly.
- Q: When multiple transitions from the same state are simultaneously eligible, how should the engine choose one? -> A: Require explicit priority order per transition (unique within each source state); highest priority eligible transition wins.
- Q: When a machine definition is updated after a session has already started, what should happen for that running session? -> A: Pin to the session's machine version at session start; only change via explicit migrate action.
- Q: How should ReintegrationCooldown determine eligibility to transition to NextDisappearanceEligible? -> A: Require a configured minimum number of completed interactions in ReintegrationCooldown, plus required return-beat completion flag.
- Q: What authorization scope should apply to machine configuration and migrate actions? -> A: Admin-only can create/edit/activate machine definitions and run migrate actions; runtime consumers are read-only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Configure Continuity Machines in Theme Management (Priority: P1)

A content administrator can define and maintain continuity state machines directly in theme management so continuity behavior is explicit, editable, and shared across all sessions using that theme.

**Why this priority**: If administrators cannot define and activate machine rules through the product interface, continuity logic remains hidden and cannot be managed safely.

**Independent Test**: Can be fully tested by creating a machine definition in theme management, activating it, and confirming the same definition is available to runtime evaluation without manual data edits.

**Acceptance Scenarios**:

1. **Given** an administrator is editing a theme, **When** they add states, transitions, and gate conditions for a continuity machine and save, **Then** the machine definition is persisted and shown as active for that theme.
2. **Given** a machine definition is missing required transition information, **When** the administrator attempts to activate it, **Then** activation is blocked with a clear validation message identifying what is missing.
3. **Given** an active machine definition is updated, **When** continuity is evaluated, **Then** new sessions use the updated rules while in-progress sessions continue using their pinned machine version unless explicitly migrated.
4. **Given** a non-admin user attempts to edit machine configuration or run a migrate action, **When** the request is submitted, **Then** the operation is denied and a diagnostic authorization failure is recorded.

---

### User Story 2 - Enforce Disappearance Lifecycle Deterministically (Priority: P1)

During roleplay, the system enforces a deterministic lifecycle for the infidelity brief disappearance theme so disappearance events cannot chain repeatedly without the required return and reintegration beats.

**Why this priority**: This is the core user value of the feature: continuity should be enforced by explicit state transitions rather than ad hoc prompt behavior.

**Independent Test**: Can be fully tested by running scripted sessions through all required states and verifying that blocked states prevent disallowed outcomes until obligations are satisfied.

**Acceptance Scenarios**:

1. **Given** a session starts in `PublicBaseline`, **When** the first disappearance trigger condition is met, **Then** the machine transitions to `EncounterInProgress`.
2. **Given** a session is in `ReturnBeatRequired`, **When** candidate selection and continuation guidance are generated, **Then** new disappearance beats are blocked and return-beat obligations are enforced.
3. **Given** a session is in `ReintegrationCooldown`, **When** either the configured minimum completed interactions in cooldown has not been reached or the required return-beat completion flag is false, **Then** disappearance candidates remain blocked.
4. **Given** a session is in `ReintegrationCooldown`, **When** the configured minimum completed interactions in cooldown is reached and the required return-beat completion flag is true, **Then** the machine transitions to `NextDisappearanceEligible`.

---

### User Story 3 - Fail Fast and Diagnose Continuity Issues (Priority: P2)

Operators and developers can quickly diagnose continuity behavior because machine initialization, transitions, blocked paths, and failures are captured with structured diagnostics and explicit error behavior.

**Why this priority**: Strong diagnostics and fail-fast behavior are required to prevent hidden continuity regressions and to keep machine behavior auditable.

**Independent Test**: Can be fully tested by intentionally introducing invalid or missing required machine configuration and verifying explicit failures plus diagnostic records.

**Acceptance Scenarios**:

1. **Given** a session requires a machine-enabled theme but required machine configuration is missing, **When** continuity evaluation starts, **Then** processing fails explicitly with a clear diagnostic reason and no fallback behavior is applied.
2. **Given** a persisted session has malformed machine runtime state, **When** the session is loaded for continuity evaluation, **Then** loading fails explicitly with actionable diagnostics.
3. **Given** a transition is blocked by gate conditions, **When** diagnostics are reviewed, **Then** the event includes session, theme, current state, target transition, and reason code.

---

### Edge Cases

- What happens when a session has multiple active themes? The system resolves exactly one active machine from ActiveScenario -> RPTheme -> ThemeMachineDefinition. Other theme signals may still influence non-machine behavior, but machine transitions and directives are produced only from the resolved active machine.
- What happens when machine configuration changes while a session is mid-lifecycle? The session continues with its pinned machine version. Updated definitions apply only to newly started sessions unless an explicit migrate action is executed.
- What happens when multiple transitions are eligible from the same source state? The highest-priority eligible transition is selected using explicit configured transition priorities unique within that source state.
- What happens when cooldown interactions are complete but return-beat completion is still false? Transition to `NextDisappearanceEligible` remains blocked until both conditions are satisfied.
- What happens when a non-admin tries to modify machine definitions or migrate session machine versions? The operation is rejected explicitly and recorded as an authorization failure.
- What happens when required gate inputs are unavailable for a transition decision? The transition is blocked explicitly and logged with a reason code; no guessed values are used.
- What happens when a machine state is no longer valid after configuration updates? Session loading fails explicitly and requires correction before continuation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow administrators to define theme continuity machines with named states, transitions, and gate conditions through product UI.
- **FR-002**: The system MUST persist active machine definitions so runtime and UI use one canonical configuration source keyed by RP theme.
- **FR-003**: The system MUST maintain per-session machine runtime snapshots including current state, state progression counters, last transition metadata, and the pinned machine definition version identifier.
- **FR-004**: The system MUST evaluate machine transitions during each continuity evaluation cycle using configured gate conditions and current session signals.
- **FR-005**: The system MUST enforce this transition map for the first production machine: `PublicBaseline -> EncounterInProgress -> ReturnBeatRequired -> ReintegrationCooldown -> NextDisappearanceEligible`.
- **FR-006**: The system MUST block candidate selection and narrative guidance that conflict with current machine obligations.
- **FR-007**: The system MUST include active machine obligations in continuation guidance so output reflects required continuity constraints.
- **FR-008**: The system MUST provide explicit failure behavior when required machine configuration is missing, invalid, or ambiguously resolved; no fallback or default decision path is allowed.
- **FR-009**: The system MUST record structured diagnostics for machine initialization, transition, blocked transition, and failure outcomes, including session and theme identifiers plus reason codes.
- **FR-010**: The system MUST resolve exactly one active machine per session through this single path: ActiveScenario -> RPTheme -> ThemeMachineDefinition.
- **FR-011**: The system MUST validate machine definitions before activation and prevent activation of incomplete or contradictory definitions.
- **FR-012**: The system MUST support administrator-managed seeding and updates of the infidelity brief disappearance machine through persisted configuration.
- **FR-013**: If the ActiveScenario -> RPTheme -> ThemeMachineDefinition path does not resolve to exactly one machine definition, continuity processing MUST fail explicitly with diagnostics and MUST NOT continue with guessed or secondary machine selection.
- **FR-014**: Transitions from the same source state MUST define an explicit integer priority unique within that source state.
- **FR-015**: When multiple transitions are eligible from the same source state in one evaluation cycle, the system MUST select the highest-priority eligible transition.
- **FR-016**: At session start, the continuity runtime MUST bind and persist exactly one machine definition version for the resolved active machine.
- **FR-017**: In-progress sessions MUST evaluate transitions only against their pinned machine definition version, even if a newer definition is activated later.
- **FR-018**: Switching an in-progress session to a newer machine definition version MUST occur only through an explicit migrate action; implicit or automatic version switching is not allowed.
- **FR-019**: Transition from `ReintegrationCooldown` to `NextDisappearanceEligible` MUST require both: (a) configured minimum completed interaction count in `ReintegrationCooldown`, and (b) required return-beat completion flag set to true.
- **FR-020**: The configured minimum completed interaction count for `ReintegrationCooldown` MUST be persisted in UI-editable machine configuration and MUST be treated as required configuration for transition evaluation.
- **FR-021**: Only Admin-authorized users MUST be allowed to create, edit, activate, deactivate, or migrate theme machine definitions and session machine-version bindings.
- **FR-022**: Runtime consumers and non-admin users MUST have read-only access to machine definitions and machine runtime state; unauthorized write or migrate attempts MUST fail explicitly with diagnostics.

### Key Entities *(include if feature involves data)*

- **ThemeMachineDefinition**: A persisted continuity contract for a theme, including state list, transitions, gate definitions, activation status, and version metadata.
- **ThemeMachineTransitionRule**: A transition between two states with gate conditions, blocking reason codes, and trigger metadata.
- **ThemeMachineSessionState**: Per-session runtime snapshot for an active machine, including current state, counters, last transition details, and pinned machine definition version.
- **ThemeMachineDirective**: Continuity obligations emitted to selection and continuation paths to enforce current state behavior.
- **ThemeMachineDiagnosticEvent**: Structured event describing initialization, transition, block, or failure outcomes for auditing and troubleshooting.

## Assumptions

- Admin-authorized users are responsible for configuring, activating, and migrating machine rules.
- Existing sessions can reference themes that may or may not have machine enforcement enabled.
- Continuity evaluation remains part of the existing session progression flow.
- Diagnostic outputs are available for operator and developer troubleshooting.

## Dependencies

- Theme management must expose machine configuration fields for activation and editing.
- Session continuity processing must be able to consume persisted machine definitions and runtime snapshots.
- Operational diagnostics tooling must surface machine events and failure reasons.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In scripted validation runs, 100% of machine-enabled sessions follow valid configured transitions with zero invalid state jumps.
- **SC-002**: In a controlled set of at least 30 disappearance-cycle attempts, 0 attempts bypass required return-beat and reintegration-cooldown obligations.
- **SC-003**: 100% of missing or invalid required machine configurations fail explicitly and produce diagnostic events with session, theme, and reason identifiers.
- **SC-004**: At least 95% of continuation outputs for machine-enabled scenarios include state-appropriate continuity directives in evaluation sampling.
- **SC-005**: Non-machine themes maintain baseline continuity behavior with no increase in continuity-related defect reports during rollout validation.
