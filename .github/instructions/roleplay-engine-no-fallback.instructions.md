---
applyTo: 'DreamGenClone.Infrastructure/RolePlay/**/*.cs,DreamGenClone.Web/Application/RolePlay/**/*.cs,DreamGenClone.Web/Domain/RolePlay/**/*.cs,DreamGenClone.Tests/RolePlay/**/*.cs,DreamGenClone.Web/Components/Pages/RolePlay*.razor,DreamGenClone.Web/Components/Pages/RolePlay*/**/*.razor'
description: 'RP engine strict config contract: no hardcoded defaults, no fallback branches, no guessed values, fail fast on missing config, and every RP behavior control must be UI-backed.'
---

# Roleplay Engine Strict Configuration Contract

## Scope
This contract applies to all RP engine behavior, not only gate thresholds.

## Required Behavior
- Use configured persisted values for RP engine decisions.
- Keep one explicit source-resolution path per RP behavior.
- Treat missing required configuration as a hard error with explicit diagnostics.
- Ensure RP behavior controls are exposed through UI-backed configuration.

## Prohibited Behavior
- No hardcoded fallback values for RP behavior logic.
- No guessed or inferred substitute values when required config is missing.
- No hidden alternate code paths that silently switch to defaults.
- No duplicate source-selection logic across services.

## UI Configuration Requirement
- If behavior is user-tunable in runtime logic, it must have a UI configuration surface.
- UI settings must persist to the canonical configuration store used by runtime.
- Runtime must read from that canonical configured source only.

## Implementation Checklist
- Before edit: identify current config source and all RP call sites.
- During edit: remove fallback/default branches and helper-level substitutes.
- After edit: verify exactly one active resolution path remains.
- Add or update tests proving missing required config fails explicitly.

## Communication Checklist
- State the exact file and method resolving RP config source.
- State whether fallback branches remain (must be none).
- State explicit failure behavior when required config is missing.
- State where the corresponding UI configuration surface exists.
