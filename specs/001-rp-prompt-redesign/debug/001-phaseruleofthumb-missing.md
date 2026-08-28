# Debug 001: PhaseRuleOfThumb Missing

**Date:** 2026-07-17
**Session:** new session (after fresh build)

## Report
Runtime error: `MissingPromptConfig: WritingStyle.PhaseRuleOfThumb is missing or empty. Session phase: 'Opening'. FR-014 requires a PhaseRuleOfThumb row for every phase.`

Happened immediately on first continuation attempt with a fresh build + new session.

## Analysis
Root cause: Three issues layered:

1. **`PhaseRuleOfThumb` was hardcoded to `string.Empty`** in `RolePlayContinuationService.cs:596` — the context builder never resolved it from the database.
2. **`IPhaseRuleOfThumbRepository` was not injected** into `RolePlayContinuationService` — the resolution code path didn't exist.
3. **Seed data had placeholder text** — the `SqlitePersistence.cs` seed used `INSERT OR IGNORE` with generic placeholder text instead of the GAP-6 spec content. Existing DBs would never get updated.

## Plan
1. Inject `IPhaseRuleOfThumbRepository` into `RolePlayContinuationService`
2. Add fail-fast resolution in `BuildPromptViaBuilderAsync` — call `GetByPhaseAsync(phase)` and throw if missing
3. Replace all 6 seed texts with GAP-6 content using `INSERT OR REPLACE`
4. Add stub repository to affected test

## Resolution
- Added `IPhaseRuleOfThumbRepository` to constructor + field
- Added resolution call in context builder with explicit fail-fast message
- Replaced seed data: `INSERT OR IGNORE` → `INSERT OR REPLACE` with GAP-6 text for all 6 phases (Opening, BuildUp, Committed, Approaching, Climax, Reset)
- Added `StubPhaseRuleOfThumbRepository` to `RolePlayContinuationNarrativeValidationTests.cs`
- Added using `DreamGenClone.Infrastructure.Persistence`

## Validated
- [x] 2026-07-17 — Build 0 errors, 104 tests pass
- [x] User confirmed fixed with new session

