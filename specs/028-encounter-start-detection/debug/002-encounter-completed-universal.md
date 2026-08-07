# 002 — Make Encounter-Completed Detection Universal (symmetric with encounter-start)

**Created:** 2026-08-06
**Status:** Implemented, awaiting runtime confirmation

## Report

- **Request:** User asked why encounter-end requires a theme `encounter-completed` semantic mapping while encounter-start fires for all themes, and approved making encounter-end universal too.
- **Asymmetry:** `TryDetectEncounterStartAsync` is universal (global `EncounterStartConfidenceThreshold`); `TryDetectEncounterBoundaryAsync` returned early when the theme had no `encounter-completed` mapping, and `EnsureEncounterCompletedMappingAsync` fail-fast-threw for themes with `[ClimaxMode:multi-encounter]`/`[Aftermath:husband-contrast]` markers but no mapping.

## Analysis

- The mapping carries the per-theme confidence window (`ConfidenceMin`/`ConfidenceMax`) used by the boundary detector, plus it is the UI-backed config that multi-encounter/aftermath features relied on.
- The B-059 design intended BOTH events to be mapping-gated; the implemented encounter-start deviated to universal. Making encounter-completed universal restores symmetry.

## Plan (approved "go")

Themes WITH a mapping keep per-theme thresholds; themes WITHOUT one use a global `EncounterEndConfidenceThreshold` (0.70). Relax the fail-fast to a warning.

## Resolution

1. `RolePlayMemoryOptions.cs`: added `EncounterEndConfidenceThreshold { get; init; } = 0.70m`.
2. `appsettings.json` + `appsettings.Development.json`: added `"EncounterEndConfidenceThreshold": 0.70` under `RolePlayMemory`.
3. `RolePlayEngineService.cs` `TryDetectEncounterBoundaryAsync` (~L5352): removed the `mapping is null → return` gate; now `confMin = mapping?.ConfidenceMin ?? (global 0.70)`, `confMax = mapping?.ConfidenceMax ?? 1.0m`; detection filter uses `confMin`/`confMax`.
4. `RolePlayEngineService.cs` `EnsureEncounterCompletedMappingAsync` (~L5153): three `throw` paths → `LogWarning` (kept the `_rpThemeService is null` fail-fast).
5. Build: Web ✅ 0 errors. Encounter/MultiEncounter tests 84/84 pass.

## Behavior matrix

| Theme | Before | After |
|---|---|---|
| Has `encounter-completed` mapping | per-theme ConfMin/Max | unchanged |
| No mapping | no boundary detection | detection via global 0.70 |
| No mapping + multi/aftermath marker | fail-fast throw | works via global; logs warning |

## Evidence (web app log, session 7235f5b4 before shutdown)

- `allowedEventIds=["encounter-started"]` with `ContextTurns=4` (start-detection window fix active), response `{"events":[]}` — correctly no detection for flirtation/exhibitionism content, no 400/timeout.
- `RolePlayV2 BuildUp commit gate passed ... ProfileName=Theme Local Rules ... InteractionCount=4` — gate-metric fix confirmed working.
- BuildUp→Committed transitioned (Phase=Committed in DIAG:PipelineSave).

## Validated

- [ ] pending — user to play a session to actual sexual contact: `EncounterStartDetected` should fire; then a later completion (male ejaculation / interruption) should fire `EncounterBoundaryDetected` even for a theme WITHOUT an `encounter-completed` mapping (e.g., threesome-spontaneous-exclusion-v3).
