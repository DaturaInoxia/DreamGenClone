# 061 — Character Identity angle state corruption

## Report

- **Reported:** 2026-09-15.
- **Symptoms:** left/right angle outputs were cross-assigned; selecting a card changed persisted state; profile views showed the wrong side; delete did not remove attempts; Review Deck was empty or mixed.

## Root cause

- `PrepareAsync` reset the persisted angle record every time a card was selected.
- `OnAngleResultAsync` used mutable page-level selected-view state, allowing late workspace results to be recorded under another view.
- One angle record's output was treated as current even when no attempt had been accepted.
- Attempts were retained as failed rows after deletion rather than removed.
- The shared workspace did not reliably carry the selected angle identity into the result callback.

## Resolution

- Selection no longer clears an existing angle output/status.
- `CharacterIdentityAngleRecord` now persists `AcceptedAttemptId`.
- Angle advancement requires explicit accepted attempts for all four views.
- Profile source resolution continues only from the accepted 3/4 view on the same side.
- Workspace result callback captures the angle view in its callback closure.
- Attempt deletion removes the attempt row and image after accepted-output protection.
- Schema/repository support added for accepted attempt identity and attempt deletion.

## Validated

- [x] Touched-file diagnostics clean.
- [x] Web project build succeeds.
- [ ] Runtime acceptance pending: generate left/right independently, accept one attempt each, confirm profile sources, delete a non-accepted attempt, and verify per-angle Review Deck contents.
