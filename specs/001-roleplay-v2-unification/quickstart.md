# Quickstart: DreamGenClone RolePlay v2 Unified Scenario Intelligence

**Feature Branch**: `001-roleplay-v2-unification`  
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Data Model**: [data-model.md](data-model.md)

---

## Goal

Implement and validate the unified v2 release that combines scenario commitment lifecycle, concept reference injection, and stat-altering decision points with canonical stats:
Desire, Restraint, Tension, Connection, Dominance, Loyalty, SelfRespect.

## Implementation Order

### Step 1: Domain and State Foundations

1. Add/extend domain models for AdaptiveScenarioState, transition events, cycle metadata, and CharacterStatProfileV2.
2. Add formula version references and UnsupportedSessionError structures.
3. Enforce canonical stat validation and non-v2 rejection behavior.

### Step 2: Scenario Selection and Lifecycle

1. Implement two-stage gating (willingness tier then eligibility).
2. Add deterministic fit-score ranking with tie-break keys.
3. Add hysteresis commitment behavior (lead threshold over N consecutive evaluations).
4. Implement ordered phase transitions and reset-to-buildup cycle continuity.

### Step 3: Concept Injection Engine

1. Implement deterministic relevance and conflict resolution.
2. Implement guidance budget policy: reserved quotas plus shared overflow pool.
3. Add deterministic truncation tie-break handling.

### Step 4: Decision Point and Mutation Flow

1. Implement context-aware decision point generation triggers.
2. Set default transparency to Directional mode.
3. Persist selected outcomes, stat deltas, and fallback custom responses.

### Step 5: Authorization, Diagnostics, and Logging

1. Enforce manual override authorization (session owner, operator/admin only).
2. Emit structured Serilog events for selection, transitions, injection, decisions, and failures.
3. Ensure log levels are externally configurable without code changes.

### Step 6: Persistence and Test Fixtures

1. Persist v2 entities to SQLite with compatibility checks.
2. Add acceptance fixtures for threshold boundaries, tie cases, and unsupported-version paths.
3. Verify deterministic outputs on repeated identical inputs.

## Verification Commands

```powershell
# Build all projects
 dotnet build DreamGenClone.sln

# Run full tests
 dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj

# Optional: run targeted role-play suite
 dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter RolePlay
```

## Acceptance Smoke Sequence

1. Start with a v2-compatible session and competing scenario signals.
2. Verify one scenario commits only after hysteresis criteria are met.
3. Drive lifecycle through BuildUp, Committed, Approaching, Climax, Reset, BuildUp.
4. Trigger concept injection repeatedly with identical state and verify deterministic selected sets.
5. Trigger a decision point, choose an option, and verify stat deltas persist.
6. Attempt manual override as unauthorized actor and verify rejection/audit.
7. Attempt to load a non-v2 payload and verify explicit unsupported-version error with no partial state mutation.

## Expected Outcomes

- Stable single-scenario commitment per cycle.
- Ordered phase progression with auditable transition reasons.
- Deterministic concept injection and budget handling.
- Reliable stat-mutation decision flow with directional default transparency.
- Observable and tunable runtime behavior via structured logs.

## Execution Notes (2026-04-12)

- `dotnet build DreamGenClone.sln` completed successfully after v2 implementation updates.
- `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter RolePlay` passed: 100 total, 0 failed.
- `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj` passed: 306 total, 0 failed.
- Known warnings during build/test were pre-existing analyzer warnings unrelated to v2 functional behavior (nullable/analyzer suggestions).
