# Scope and Goals

## Problem Statement

RolePlay V2 has a question subsystem that can generate context-aware choices and apply stat deltas, but runtime behavior has not been consistently aligned with implementation expectations. The primary pain points are cadence predictability and observability of what the engine actually did.

## In Scope

- Decision/question generation cadence in RolePlay V2.
- Actor targeting for generated questions and applied stat changes.
- Transparency modes for stat impact display.
- Persistence and migration support for question metadata.
- Runtime diagnostics to explain why a question did or did not appear.

## Out of Scope

- New scenario taxonomy redesign.
- Complete UX redesign of roleplay workspace.
- Non-roleplay subsystems.

## Primary Goals

1. Ensure cadence behavior is deterministic and explainable.
2. Ensure actor targeting uses context, not persona-only assumptions.
3. Ensure each surfaced question has traceable generation reasoning.
4. Ensure persisted question context is migration-safe.
5. Ensure test coverage catches cadence, targeting, and persistence regressions.

## Success Metrics

- At least 95% of eligible interaction windows either surface a question or emit explicit skip reasons.
- Zero unresolved migration/runtime column errors in startup logs.
- Question ownership and target display accuracy at 100% in sampled runtime sessions.
- No unplanned regression in existing decision mutation tests.

## Constraints

- Must preserve current RolePlay V2 public behavior unless explicitly changed by gated phase rollout.
- Must remain compatible with existing SQLite session stores.
- Must avoid destabilizing currently working continuation flow.
