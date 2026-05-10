# Research: Theme State Machine Continuity

**Phase**: 0 - Outline and Research  
**Date**: 2026-05-08  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

---

## R-001: Canonical Machine Configuration Source

**Decision**: Persist theme state machine definitions in SQLite, keyed to RP themes, using versioned records and UI-backed editing. Runtime reads only persisted definitions.

**Rationale**: The spec requires one canonical source, UI-backed controls, and explicit no-fallback behavior. Persisted definitions satisfy all three and avoid hidden runtime defaults.

**Alternatives considered**:
- Hardcoded C# machine maps: rejected because behavior changes would require deployments and violate UI-backed control.
- Appsettings JSON configuration: rejected because it is not per-theme editorial data and is not managed through RP theme UI.

---

## R-002: Definition Schema Strategy

**Decision**: Use explicit definition/state/transition records (`RPThemeMachineDefinition`, `RPThemeMachineState`, `RPThemeMachineTransition`) instead of a single opaque blob.

**Rationale**: Explicit records enable validation (one initial state, unique transition priority per source state), better diagnostics, and simpler migration/activation workflows.

**Alternatives considered**:
- Single JSON definition per theme: rejected because validating uniqueness and activation constraints becomes harder and less transparent.

---

## R-003: Single Active Machine Resolution Path

**Decision**: Resolve machine context only by `ActiveScenario -> RPTheme -> ThemeMachineDefinition (active version)`. If resolution is missing or ambiguous, fail explicitly.

**Rationale**: This is required by clarifications and RP no-fallback rules. It preserves deterministic behavior and removes hidden branching.

**Alternatives considered**:
- Fallback to profile-level defaults: rejected because it introduces implicit behavior.
- Resolve multiple active machines and merge outputs: rejected because the spec now requires exactly one active machine per session.

---

## R-004: Deterministic Transition Selection

**Decision**: Transition records include an explicit integer priority unique within each source state. If multiple transitions are eligible, highest priority wins.

**Rationale**: Priority-based selection is deterministic and testable, and matches the clarified requirement.

**Alternatives considered**:
- First-in-storage-order wins: rejected because storage order is brittle and less explicit.
- Largest gate margin wins: rejected because gate formula changes could change behavior unexpectedly.

---

## R-005: Session Version Pinning and Migration

**Decision**: Pin each session to one machine definition version at session start. Version changes for in-progress sessions are allowed only by explicit admin migrate action.

**Rationale**: Pinning prevents mid-session rule drift and supports deterministic replay/debugging.

**Alternatives considered**:
- Auto-follow latest version: rejected because it changes behavior during active sessions.
- Auto-switch on compatibility check: rejected because it still permits implicit runtime changes.

---

## R-006: Runtime Snapshot Persistence Shape

**Decision**: Persist machine runtime state in `RolePlayV2AdaptiveStates` as a structured JSON payload (`ThemeMachineStateJson`) parsed with strict required fields.

**Rationale**: The adaptive-state row is already the per-session V2 state anchor. A single machine-state JSON payload keeps schema evolution manageable while preserving fail-fast parse behavior.

**Alternatives considered**:
- Many dedicated columns: rejected because evolution of machine metadata would require frequent schema changes.
- Separate runtime table: rejected for now to keep state locality with existing adaptive-state load/save path.

---

## R-007: Cooldown Gate Semantics

**Decision**: Transition from `ReintegrationCooldown` to `NextDisappearanceEligible` requires both:
1. configured minimum completed interactions in cooldown, and
2. required return-beat completion flag.

**Rationale**: Matches clarified requirements and prevents premature re-entry into disappearance loops.

**Alternatives considered**:
- Real-time duration only: rejected because turn pacing is interaction-driven.
- Interaction count only: rejected because return-beat fulfillment must also be explicit.

---

## R-008: Authorization Model

**Decision**: Machine definition create/edit/activate and migrate actions are admin-only. Runtime/reader paths remain read-only.

**Rationale**: Matches clarified requirement and minimizes operational risk for continuity-critical behavior.

**Alternatives considered**:
- Operator migration rights: rejected to keep a single authority boundary.
- Theme-owner scoped rights: rejected because existing role model is currently admin/operator/session-owner oriented.

---

## R-009: Pipeline Integration Points

**Decision**: Integrate machine evaluation in `RolePlayEngineService.RunRolePlayV2PipelinesAsync` after active scenario/theme resolution and before candidate selection and lifecycle transition decisions. Emit machine directives to both:
- scenario selection constraints, and
- continuation prompt assembly.

**Rationale**: This placement ensures machine state affects both what can happen and how narration is instructed in the same cycle.

**Alternatives considered**:
- Prompt-only enforcement in continuation service: rejected because candidate selection could still violate continuity.
- Post-selection enforcement only: rejected because invalid candidates would already have influenced scoring.

---

## R-010: Diagnostics and Auditability

**Decision**: Persist structured machine diagnostics (init, transition, blocked, failure, authorization denied, migrate) via diagnostics repository and emit aligned Information/Warning/Error logs.

**Rationale**: Required for fail-fast troubleshooting and to verify no hidden fallback behavior.

**Alternatives considered**:
- Logs only with no persisted events: rejected because session-centric debugging requires queryable history.

---

## R-011: UI Editing Boundaries

**Decision**: Extend RP theme pages for machine editing and activation (`RPThemeDetail` for editing, `RPThemes` for visibility). Keep machine behavior controls persisted and editable through UI; do not add code-only toggles.

**Rationale**: Enforces UI-backed behavior control and keeps operational ownership with admins.

**Alternatives considered**:
- Separate hidden admin scripts for machine config: rejected because it bypasses product controls.

---

## R-012: Verification Strategy

**Decision**: Validate using layered checks:
1. targeted RolePlay tests,
2. migration/load-save roundtrip tests,
3. prompt contract assertions,
4. manual scripted infidelity lifecycle flow,
5. explicit missing-config failure tests.

**Rationale**: The feature spans runtime state, persistence, selection, prompting, and authorization. Single-layer testing is insufficient.

**Alternatives considered**:
- Manual verification only: rejected due high regression risk.

---

## R-013: No-Fallback and Single-Path Evidence Checklist

**Decision**: Maintain one explicit machine decision path and fail fast on missing/invalid required machine configuration.

**Evidence checklist**:

1. Value source resolution is explicit and deterministic:
	- Runtime guard resolves from `ActiveScenarioId` and theme-backed machine definitions.
	- Guard path: `RolePlayEngineService.EnsureThemeMachineResolutionGuardAsync` -> `IRPThemeService.ListMachineDefinitionsAsync` -> `IThemeMachineResolutionService.ResolveAsync`.

2. Exactly one active decision path for machine source:
	- `ThemeMachineResolutionService.ResolveAsync` is the single resolver for runtime machine definition selection.
	- Missing/ambiguous active definitions fail explicitly.

3. No fallback/default branch for changed machine behavior:
	- No profile-level, global, or hardcoded backup machine source path is used when machine definitions exist for a theme.
	- Candidate blocking, prompt directives, and evaluator behavior all consume persisted machine state/directives.

4. Missing required configuration fails explicitly:
	- Resolution guard throws when machine definitions exist but resolution does not yield one valid definition.
	- Snapshot parsing in `RolePlayStateRepository` throws on malformed/missing required machine fields.
	- Engine persists `failure` machine diagnostic events for resolution/evaluation exceptions before rethrow.

5. UI-backed configuration surface is present:
	- Machine editing/activation: `RPThemeDetail` and `RPThemes`.
	- Explicit admin migrate action exposed in theme management flow.

**Verification outcomes (2026-05-09)**:

- US3 targeted tests passed: resolution fail-fast, strict snapshot parsing, diagnostics ordering.
- Theme-machine runtime targeted regression tests passed.
- Full RolePlay regression execution completed and outcomes recorded (includes existing unrelated failures).
- Full solution build succeeded.
