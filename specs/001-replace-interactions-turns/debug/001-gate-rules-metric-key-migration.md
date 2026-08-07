# 001 — Gate Rules Metric Key Migration Gap (InteractionsSinceCommitment → TurnsSinceCommitment)

**Created:** 2026-08-05
**Status:** Fixed (data migration applied, awaiting runtime confirmation)

## Report

- **Session:** `1c0ae0e3-9e36-4258-b90a-67656ae9ec36` (BuildUp)
- **Symptom:** UI "Simple Status" reported `BuildUp commit is blocked: InteractionsSinceCommitment value is unavailable.` Next step suggested populating `InteractionsSinceCommitment`, implying "no turn counting."
- **Gate audit (RolePlayDebugEvents `AdaptiveCommitGateEvaluated`, 2026-08-05T04:25:36Z):**
  - `interactionCount: 2`, `committed: false`
  - Rule SortOrder 11 `InteractionsSinceCommitment >= 12`, `Actual: null`, `Status: MetricUnavailable`
  - Reason: `BuildUp profile gate blocked commit: metric 'InteractionsSinceCommitment' unavailable.`

## Analysis

- **Root cause:** `001-replace-interactions-turns` (B-044) renamed the code constant `NarrativeGateMetricKeys.InteractionsSinceCommitment` → `TurnsSinceCommitment` and migrated V2 table columns + theme-machine gate JSON, but **never migrated the stored `MetricKey` string values**:
  - `RPThemeNarrativeGateRules.MetricKey` — 60 rules / 12 themes still used `InteractionsSinceCommitment`.
  - `NarrativeGateProfiles.RulesJson` — both profiles still contained `"MetricKey":"InteractionsSinceCommitment"` (thresholds in interaction units).
- Engine (`ScenarioSelectionService.cs` L751, `ScenarioLifecycleService.cs` L430) and UI (`RolePlayWorkspace.razor` L3167) only populate the metric dictionary under `TurnsSinceCommitment`. Per the no-fallback hard rule, the evaluator fails fast (`MetricUnavailable`) instead of translating — correct code behavior, stale data.
- **Not** a turn-counting bug: 5 completed turns; `TurnCountInPhase` incremented 1→2 after first-scenario-selection reset (turn 4).
- Thresholds were stored in interaction units (~3 responses/turn), so a ÷3 ceiling conversion preserves pacing intent (per `data-model.md` §1 `(x+2)/3`).

## Plan

1. `UPDATE RPThemeNarrativeGateRules SET MetricKey='TurnsSinceCommitment', Threshold=(Threshold+2)/3 WHERE MetricKey='InteractionsSinceCommitment';`
2. Rewrite `NarrativeGateProfiles.RulesJson` (both profiles): `InteractionsSinceCommitment` → `TurnsSinceCommitment`, thresholds ÷3 ceiling.
3. (Optional, deferred) Idempotent startup migration in `SqlitePersistence.cs`; cleanup dual old+new `RolePlayV2AdaptiveStates` columns.

## Resolution

- Backup created: `DreamGenClone.Web/data/dreamgenclone.dev.db.bak-20260805-gatemetric`.
- `exec artifacts/tmp/dbquery/queries/fix_gate_rules_metric_keys.sql` → **60 rows affected**.
- `fix_gate_profiles_metric_keys.py` → **2 profiles updated, 10 rules migrated**.
- Verified:
  - 0 rows match `MetricKey LIKE '%Interaction%'`.
  - Metric key summary: `TurnsSinceCommitment=65`, no `InteractionsSinceCommitment`.
  - Theme `threesome-spontaneous-exclusion-v3`: BuildUp→Committed now `TurnsSinceCommitment >= 4` (was 12), Committed→Approaching `>= 4`, Approaching→Climax `>= 4`, Climax→Reset `>= 24`, Reset→BuildUp `>= 4`.
  - Profiles: Default `8→3, 8→3, 8→3, 12→4, 6→2`; Slow Burn `10→4, 8→3, 12→4, 6→2, 12→4`. Comparators now unescaped (`">="` vs `"\u003E="`) — semantically identical for `System.Text.Json`.

## Validated

- [ ] pending — user to confirm: session now evaluates gate correctly (will report `TurnsSinceCommitment is 2, needs >= 4` and commit after 2 more post-selection turns).
- [ ] pending — restart/refresh web app so any cached profile/theme rules reload.
