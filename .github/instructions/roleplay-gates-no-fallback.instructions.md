---
applyTo: 'DreamGenClone.Infrastructure/RolePlay/**/*.cs,DreamGenClone.Web/Application/RolePlay/**/*.cs,DreamGenClone.Web/Domain/RolePlay/**/*.cs,DreamGenClone.Tests/RolePlay/**/*.cs'
description: 'Roleplay gate source rules: no fallback/default threshold paths, use configured values only, fail fast when missing, never reintroduce hidden fallback logic.'
---

# Roleplay Gate Source Contract

## Required Behavior
- Use only configured values for narrative gate thresholds.
- Resolve threshold source from explicit configured data, then stop.
- Keep one source resolution path for each gate evaluation.

## Prohibited Behavior
- No default fallback values for gate thresholds.
- No fallback to another profile when configured source exists.
- No hidden substitute values in helper methods.
- No silent recovery that changes threshold source.

## Implementation Checklist
- Before edit: identify current threshold source and all call sites.
- During edit: remove fallback branches and unreachable backup logic.
- After edit: verify threshold source appears exactly once in runtime decision path.
- Validation: add or update test coverage proving no fallback path exists.

## Communication Checklist
- State the exact source used for threshold values.
- State whether any fallback branch exists (must be none).
- If data is missing, report explicit failure path instead of adding defaults.
