# Quickstart: Replace Interactions with Turns Throughout RP Engine and Data Model

**Date**: 2026-07-13
**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Research**: [research.md](./research.md)

> This is a rename + one-way data migration feature. There is no new user-facing UI flow to demo, no new endpoint, and no new external dependency.
> The quickest way to verify the feature is in place is to inspect the post-migration database and the adaptive panel of an existing session.

---

## 1. Prerequisites

- .NET 9 SDK
- SQLite (via `Microsoft.Data.Sqlite`; no separate install needed)
- Existing local database at `DreamGenClone.Web/data/dreamgenclone.dev.db`
- **Backup the database before running the app for the first time after this feature ships.** Migration is one-way. See §4.

---

## 2. Build

From repository root:

```
dotnet build DreamGenClone.sln
```

Expected: zero errors, zero warnings related to renamed fields.

---

## 3. Run

From repository root, use the existing startup helper (preferred):

```
helpers/start-webapp-dev-clean.ps1
```

Or directly:

```
dotnet run --project DreamGenClone.Web/DreamGenClone.csproj
```

On first startup after deploying this feature:
- The migration pass runs at persistence initialization.
- Column renames + numeric `(n + 2) / 3` conversion are applied to all `RolePlayV2*` tables.
- Every `RPThemeMachineTransitions.GateConfigJson` blob is rewritten: `minimumInteractions` → `minimumTurns` with value divided by 3 (ceiling).
- `RPThemeProfiles.ThemeSelectionTurnsPerTheme` is verified to exist (no-op).
- A migration marker is set so re-starts are no-ops.

---

## 4. Migration Safety / Rollback

- **One-way.** There is no automatic rollback. The migration does not delete data, but it overwrites numeric values (÷3 ceiling) and rewrites JSON blobs in place.
- **Before first run**: copy `DreamGenClone.Web/data/dreamgenclone.dev.db` to `dreamgenclone.dev.db.bak-pre-turns-migration`.
- **Idempotency**: re-running the migration is safe (guard checks skip already-migrated rows/columns).
- **Verification queries** (post-migration sanity check — see §5).

---

## 5. Verify Migration Succeeded

Run via the dbquery tool (`dotnet run --project artifacts/tmp/dbquery -- sql <file>`):

### 5.1 Column renames applied (no `*Interaction*` columns remain on V2 tables)

```sql
-- Should return 0 rows:
SELECT name FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name LIKE '%Interaction%';
SELECT name FROM pragma_table_info('RolePlayV2ThemeScores')     WHERE name LIKE '%Interaction%';
SELECT name FROM pragma_table_info('RolePlayV2ScenarioHistory')  WHERE name LIKE '%Interaction%';
SELECT name FROM pragma_table_info('RolePlayV2EncounterSummaries') WHERE name LIKE '%Interaction%';
```

### 5.2 New `*Turn*` columns exist

```sql
-- Each should return 1:
SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates')    WHERE name = 'TurnCountInPhase';
SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates')    WHERE name = 'TurnsSinceCommitment';
SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates')    WHERE name = 'TurnsInApproaching';
SELECT COUNT(*) FROM pragma_table_info('RolePlayV2ThemeScores')      WHERE name = 'CompletionCooldownTurns';
SELECT COUNT(*) FROM pragma_table_info('RolePlayV2ScenarioHistory')  WHERE name = 'TurnCount';
SELECT COUNT(*) FROM pragma_table_info('RolePlayV2EncounterSummaries') WHERE name = 'TurnCountInPhase';
```

### 5.3 Theme gate JSON: all gate configs use `minimumTurns`, none use `minimumInteractions`

```sql
-- Should return 0 rows:
SELECT TransitionId FROM RPThemeMachineTransitions
WHERE GateConfigJson LIKE '%minimumInteractions%';

-- Should return 0 rows (all should have minimumTurns):
SELECT TransitionId FROM RPThemeMachineTransitions
WHERE GateConfigJson NOT LIKE '%minimumTurns%';
```

### 5.4 Value conversion sanity (pre-migration 9 → post-migration 3, etc.)

If a known pre-migration threshold was 9, the post-migration row should have `minimumTurns=3`. Query a known transition:

```sql
-- Pick a transition you recorded the pre-migration value of:
SELECT TransitionId, GateConfigJson FROM RPThemeMachineTransitions
WHERE TransitionId = '<your-transition-id>';
```

### 5.5 RPThemeProfiles column already migrated

```sql
-- Should return 1:
SELECT COUNT(*) FROM pragma_table_info('RPThemeProfiles')
WHERE name = 'ThemeSelectionTurnsPerTheme';
```

---

## 6. Runtime Verification (UI)

1. Start a session against a previously-used scenario.
2. Open the **adaptive panel** in `RolePlayWorkspace`.
3. **Phase progress labels** read "Turns X/Y" — never "Interactions X/Y".
4. Advance a turn via Continue or SubmitPrompt.
5. Confirm `TurnsInPhase` increments by exactly 1 per turn (not by 1 per interaction).
6. Open **ThemeProfiles** and/or **RPThemeDetail** in the admin UI.
7. Edit a theme's gate rule: the metric selector shows **"Turns Since Commitment"** (not "Interactions Since Commitment").
8. Help text in the gate rule editor references **"TurnsSinceCommitment"**.
9. Save the rule and re-open it: the JSON contains `"minimumTurns"`, not `"minimumInteractions"`.

---

## 7. Log Verification

With verbose logging enabled (`appsettings.Development.json` set to `"Verbose"` for `RolePlay` namespace):

1. Trigger a phase transition.
2. Search logs for `TurnCountInPhase` and `TurnsSinceCommitment` parameter names.
3. Confirm **zero** hits for `InteractionCountInPhase` or `InteractionsSinceCommitment`.

---

## 8. Test Verification

Run the tests that cover the renamed paths (filtering to avoid pre-existing unrelated failures):

```
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~AdaptiveScenarioStateV2RoundTrip|DecisionPointMutation|EncounterSummaryService|PhaseLifecycleTransition|RolePlaySessionLifecycle|RolePlayThemeMachineCommand|ThemeMachineEvaluator|RPThemeMachineDefinitionValidation|ScenarioStateModel"
```

Expected: all listed classes green.

---

## 9. `appsettings.json` Migration Note for Operators

The following keys have been renamed (old → new). Deployment `appsettings.json` files are NOT programmatically rewritten — remove the old keys and add the new ones if you want to override code defaults:

| Old Key | New Key |
|---------|---------|
| `StoryAnalysis:Adaptive:AdaptiveEarlyTurnInteractionThreshold` | `StoryAnalysis:Adaptive:AdaptiveEarlyTurnThreshold` |
| `StoryAnalysis:Adaptive:AdaptivePerInteractionTotalDeltaBudget` | `StoryAnalysis:Adaptive:AdaptivePerTurnTotalDeltaBudget` |
| `StoryAnalysis:Adaptive:CompletedScenarioThemeCooldownInteractions` | `StoryAnalysis:Adaptive:CompletedScenarioThemeCooldownTurns` |
| `StoryAnalysis:Adaptive:BuildUpMinInteractionsBeforeCommit` | `StoryAnalysis:Adaptive:BuildUpMinTurnsBeforeCommit` |

Old keys silently stop binding when this feature ships. Default values in `StoryAnalysisOptions` are pre-converted (÷3 ceiling for integer thresholds; ×3 for the per-turn budget) so deployments using defaults see equivalent behavior.

---

## 10. Out of Scope (Will Not Change)

- The session timeline (`RolePlayInteraction` entities and `RolePlaySession.Interactions` list) — these represent individual AI messages, not phase-advancement units, and are explicitly NOT renamed.
- `InteractionEvidenceSignal` (keyword-hit accumulator on `AdaptiveStateV2`) — not a turn counter.
- `EncounterSummaryRecord.StartInteractionIndex` / `EndInteractionIndex` — indices into the `Interactions` list, not phase counters.
- UI session-list cells ("N interactions" on `Home.razor`) — these refer to the timeline message count and stay as-is.