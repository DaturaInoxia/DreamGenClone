# Debug 002: ProfileDefaultRuleOfThumb Missing

**Date:** 2026-07-17
**Session:** new session (after Debug 001 fix)

## Report
Runtime error: `MissingPromptConfig: WritingStyle.ProfileDefaultRuleOfThumb is missing or empty. FR-014 requires a profile default Rule-of-Thumb.`

Occurred on fresh build + new session after Debug 001 was fixed.

## Analysis
Same root cause pattern as Debug 001 — `ProfileDefaultRuleOfThumb` was also hardcoded to `string.Empty` in the context builder with no resolution. The value should come from the session's selected steering profile via `ISteeringProfileService.GetAsync()`.

Additionally: `Description`, `Example`, and `StyleHint` were also hardcoded to `string.Empty`.

## Plan
1. Create `ResolveWritingStyleAsync` helper method in `RolePlayContinuationService`
2. Resolve steering profile from `session.SelectedSteeringProfileId` via `_steeringProfileService.GetAsync()`
3. Populate all four fields: `Description`, `Example`, `ProfileDefaultRuleOfThumb`, `StyleHint`
4. Fail-fast if `ProfileDefaultRuleOfThumb` is still empty after resolution

## Resolution
- Created `ResolveWritingStyleAsync` method that:
  - Resolves steering profile from session's selected profile
  - Resolves scenario narrative style for StyleHint
  - Fail-fasts with explicit message if `ProfileDefaultRuleOfThumb` is empty
- Replaced the `new ResolvedWritingStyleData { ... all empty ... }` block with `await ResolveWritingStyleAsync(...)`

## Validated
- [x] 2026-07-17 — Build 0 errors, 104 tests pass
- [x] User confirmed fixed with new session

