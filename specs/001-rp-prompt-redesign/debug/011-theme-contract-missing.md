# Debug 011 — Slot 12 Theme Contract Missing

**Created:** 2026-07-17

## Report

Slot 12 (Theme Contract) produces no output. No theme data appears in any prompt.

## Analysis

`Theme = new ResolvedThemeData()` — never populated. `_rpThemeService` (IRPThemeService) is already injected (nullable). `session.AdaptiveState.PrimaryThemeId` is set (e.g., "exhibitionism"). The `RPTheme` domain object has `Label`, `Description`, `PhaseGuidance` (per-phase guidance + directive text), `AIGenerationNotes` — all map directly to `ResolvedThemeData` fields.

## Plan

Add `ResolveThemeAsync` that:
1. Reads `session.AdaptiveState.PrimaryThemeId`
2. Calls `_rpThemeService.GetThemeAsync(themeId)`
3. Maps phase-specific guidance/directives and AI notes to `ResolvedThemeData`

Replace `new ResolvedThemeData()` with resolved result.

**Files:** `RolePlayContinuationService.cs` only

## Resolution

Added `ResolveThemeAsync` method. Context now uses resolved theme data.

## Validated

[ ] Pending user confirmation
