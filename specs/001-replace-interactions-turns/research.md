# Phase 0 Research: Replace Interactions with Turns Throughout RP Engine and Data Model

**Date**: 2026-07-13
**Spec**: [spec.md](./spec.md)
**Status**: Complete — all unknowns resolved; no NEEDS CLARIFICATION markers remain in the spec.

## Research Tasks

The Technical Context carried no open NEEDS CLARIFICATION items. The single open question about migration value handling (divide-vs-preserve) was resolved via the Clarifications session on 2026-07-13. The research below records the decisions and supporting evidence, organized by decision area.

---

## R1. Turns as a First-Class Stored Unit (No Runtime Formula)

**Decision**: `Turn*` fields are stored values — the engine reads and writes them directly. No runtime `interactions / 3` formula is permitted in gate evaluation, phase-advance, or cooldown logic. The only permitted interaction-to-turn conversion is the one-time migration pass and the narrow backward-compatibility read path for un-migrated legacy `minimumInteractions` JSON.

**Rationale**:
- The user clarified: "Turns is a first class citizen just like interactions, interactions counts should not be used."
- Mixing stored turns with derived-from-interactions values creates dual sources of truth, which has already caused naming inconsistencies (e.g., `ThemeMachineEvaluator.cs:304` uses variable name `interactionsGatePassed` while comparing against `TurnsInCurrentState`).
- A single source of truth (stored turns) makes audit, logging, and UI label consistency correct by construction.

**Alternatives Considered**:
- *Compute turns on demand from `Interactions.Count`*: Rejected — the user explicitly forbade interaction counts feeding gate decisions. It also breaks down for multi-character scenes where one turn generates multiple interactions (see `/memories/repo/roleplay-turn-vs-interactions.md`).
- *Store both and convert at the boundary*: Rejected — dual storage invites drift and contradicts the first-class Turns requirement.

---

## R2. Migration Value Conversion: Divide by 3, Ceiling Rounding

**Decision**: All existing stored gate values — DB column values, theme gate JSON `minimumInteractions` thresholds, and config option defaults in `StoryAnalysisOptions` / `appsettings.json` — are converted by dividing by 3 with ceiling rounding (`Math.DivRem(n + 2, 3)` equivalent). Examples: 9 → 3, 2 → 1, 4 → 2, 0 → 0.

**Rationale**:
- User clarification: "all stored gate values should divide by 3."
- 1 turn ≈ 3 interactions is the established codebase convention (documented in `/memories/repo/roleplay-turn-vs-interactions.md`).
- Because the prior `minimumInteractions` gate JSON key was *semantically* a turn threshold (it compared against `TurnsInCurrentState`, a turn-based field) but was named "interactions", the numeric value stored there actually represented intended turn thresholds as scaled-up interaction units. Dividing by 3 restores the true intended turn count.

**Alternatives Considered**:
- *Preserve numeric value (rename key only)*: Rejected by user clarification. If we kept `9` but renamed the key to `minimumTurns`, the gate would compare `TurnsInCurrentState >= 9`, jumping from ~27 interactions to 9 turns — an effective 3x behavior change. Dividing keeps the effective narrative pacing unchanged.
- *Preserve only for JSON/config, divide for DB counters*: Rejected by user clarification ("all stored gate values should divide by 3"). Dual policy would also be inconsistent and error-prone.

---

## R3. DB Column Rename Mechanics

**Decision**: Use `ALTER TABLE ... RENAME COLUMN` for the 7 column renames, matching the existing pattern at `RPThemeService.cs:4846` (`ALTER TABLE RPThemeProfiles RENAME COLUMN ThemeSelectionInteractionsPerTheme TO ThemeSelectionTurnsPerTheme`). Numeric value migration uses a separate `UPDATE` statement with `(value + 2) / 3` integer ceiling division after the rename.

**Rationale**:
- `RENAME COLUMN` preserves all data and constraints, avoiding dump/restore cycles.
- Using the same pattern as the existing theme-selection migration keeps the migration style consistent and discoverable.
- SQLite supports `RENAME COLUMN` since 3.25.0; the project's SQLite version (via `Microsoft.Data.Sqlite`) is well above this floor.

**Migration targets** (column → table → new name):
| Table | Old Column | New Column |
|-------|------------|------------|
| `RolePlayV2AdaptiveStates` | `InteractionCountInPhase` | `TurnCountInPhase` |
| `RolePlayV2AdaptiveStates` | `InteractionsSinceCommitment` | `TurnsSinceCommitment` |
| `RolePlayV2AdaptiveStates` | `InteractionsInApproaching` | `TurnsInApproaching` |
| `RolePlayV2ThemeScores` | `CompletionCooldownInteractions` | `CompletionCooldownTurns` |
| `RolePlayV2ScenarioHistory` | `InteractionCount` | `TurnCount` |
| `RolePlayV2EncounterSummaries` | `InteractionCountInPhase` | `TurnCountInPhase` |

**Idempotency**: Each `RENAME COLUMN` must be guarded with a column-existence check on the *old* name (and absence of the *new* name) to make the migration safe to re-run. Pattern is identical to the existing `ThemeSelectionInteractionsPerTheme` migration code.

**Alternatives Considered**:
- *Drop + re-create column*: Rejected — loses data.
- *Table-level dump/restore*: Rejected — unnecessarily disruptive for a column rename.

---

## R4. Theme Gate JSON Blob Migration

**Decision**: For each row in `RPThemeMachineTransitions` containing `minimumInteractions` in its `GateConfigJson` blob:
1. Parse the JSON.
2. Read `minimumInteractions` (int).
3. Compute `minimumTurns = Math.Max(0, (minimumInteractions + 2) / 3)` (ceiling division, non-negative).
4. Remove `minimumInteractions` property, add `minimumTurns` with the computed value.
5. Preserve all other properties in the blob (e.g., `requireReturnBeatCompleted`, `returnBeatCompletionSignals`).
6. Write the blob back.

**Rationale**:
- A direct key rename without value conversion would produce a 3x behavior drift (see R2).
- Preserving sibling properties is required to keep return-beat and other gate semantics intact.

**Idempotency**: Skip rows where `GateConfigJson` does not contain the literal `"minimumInteractions"` substring (fast pre-check before parsing) or already contains `"minimumTurns"`. This makes re-runs safe.

**Alternatives Considered**:
- *Rewrite whole gate config schema*: Rejected — out of scope; spec scopes this feature to renames + value conversion, not schema redesign.
- *Leave JSON untouched, convert at read time only*: Rejected — contradicts the stored-data-is-authoritative principle (R1) and leaves a dual-naming mess forever.

---

## R5. Backward-Compatibility Read Path for Un-Migrated Data

**Decision**: `RPThemeService` gate config validation and `ThemeMachineEvaluator` cooldown-read logic MUST accept both `minimumTurns` (canonical) and `minimumInteractions` (legacy, un-migrated only) during the migration transition window. Read precedence:
1. `minimumTurns` — use directly as turns.
2. `minimumInteractions` (only if `minimumTurns` absent) — divide by 3 with ceiling rounding for comparison purposes. This is the *only* runtime interaction-to-turn conversion allowed, and only on the legacy read path.

**Post-migration invariant**: After the migration pass completes, every stored `GateConfigJson` blob uses `minimumTurns` exclusively. The legacy read branch is dead code that may be removed in a follow-up cleanup task (not required for this feature).

**Rationale**:
- A migration that renames *and* converts can run while the engine is live. A small window exists where a row is read before its blob has been rewritten. The dual-read path bridges that window.
- FR-015 mandates the dual-accept contract; this research records *how* it is implemented.

**Alternatives Considered**:
- *Stop-the-world migration (block engine while rewriting)*: Rejected — local-first app, no maintenance window mechanism; migration runs at startup. Dual-read is cheaper and safe given the idempotent rewrite.
- *No backward-compat path (hard cutover)*: Rejected — would throw on any un-migrated row, breaking sessions during a partially-applied migration.

---

## R6. Out-of-Scope "Interaction" References (Not Renamed)

**Decision**: The following `*Interaction*` references represent the canonical session timeline (individual AI messages) and MUST NOT be renamed:
- `RolePlayInteraction` entity
- `RolePlaySession.Interactions` list property
- `InteractionId` (foreign key / identifier)
- `InteractionType` enum and values (`System`, `Npc`, `User`, `Custom`)
- `IInteractionCommandService`, `InteractionCommandService`
- `IInteractionRetryService`
- `ISemanticInteractionAnalysisRepository`, `SemanticInteractionAnalysisJobHandler`
- `SemanticInteractionAnalysisState`
- `DecisionTrigger.InteractionStart` (trigger enum)
- `InteractionEvidenceSignal` (keyword-hit signal accumulator on `AdaptiveStateV2`, NOT a turn counter)
- `PinnedInteractionCount`, `OutputInteractionCount`, `OutputInteractionIdsJson` (turn persistence table — these reference actual interaction rows)
- `StartInteractionIndex` / `EndInteractionIndex` on `EncounterSummaryRecord` (indices into the `Interactions` list)
- UI render-tracking variables like `_lastRenderedInteractionCount` and CSS classes like `rw-interaction`, `rw-interaction-pending`, `rw-interaction-body`.

**Rationale**: FR-013 explicitly scopes this feature to phase-advancement counting fields only. The timeline entity conflated with phase counters was the root cause of the naming inconsistency this feature fixes.

---

## R7. Config Option Default Conversion

**Decision**: Default values for the renamed `StoryAnalysisOptions` properties are divided by 3 (ceiling) so existing deployments that relied on the prior defaults see equivalent turn thresholds. The four renamed options:
- `AdaptiveEarlyTurnInteractionThreshold` (value N) → `AdaptiveEarlyTurnThreshold` (value `(N + 2) / 3`)
- `AdaptivePerInteractionTotalDeltaBudget` → `AdaptivePerTurnTotalDeltaBudget`
- `CompletedScenarioThemeCooldownInteractions` → `CompletedScenarioThemeCooldownTurns`
- `BuildUpMinInteractionsBeforeCommit` → `BuildUpMinTurnsBeforeCommit`

`appsettings.json` deployment files are not programmatically migrated — the renamed keys appear in code, and any deployment `appsettings.json` with the old keys simply stops binding (keys are optional). Document this in `quickstart.md`.

**Rationale**: User clarification covered "config options" in the divide-by-3 directive. Code-level defaults must therefore also convert, otherwise deployments using defaults would silently see a 3x behavior shift.

**Alternatives Considered**:
- *Migrate `appsettings.json` files programmatically*: Rejected — .NET config binding doesn't support in-place file rewrite, and these keys are optional with code defaults.
- *Keep defaults numerically unchanged*: Rejected — contradicts the divide-by-3 decision and would silently change pacing.

---

## R8. UI Update Surface

**Decision**: UI updates are strictly label/variable/parameter renames — no behavioral or layout changes. Specifically:
- `RolePlayWorkspace.razor`: Phase progress labels "Interactions X/Y" → "Turns X/Y"; local variables (`interactionCount`, `committedInteractions`, `interactionGap`, `interactionsMet`) → turn-named equivalents; helper method/param names (`GetEffectiveInteractionsSinceCommitment`, `BuildCommittedProgress(..., int interactionsSinceCommitment)`, etc.) → turn-named equivalents.
- `ThemeProfiles.razor` & `RPThemeDetail.razor`: Metric selector dropdown text "Interactions Since Commitment" → "Turns Since Commitment"; help text + stored-rule-documentation strings updated.
- `RolePlayDebug.razor`: `gateDetails.InteractionCount` property → `TurnCount`; JSON parse key `"interactionCount"` → `"turnCount"`; model property name + display label updated.
- `Home.razor`: Session-list cell that shows "N interactions" stays — it refers to timeline messages (R6 out-of-scope), not phase counters. No label change required.

**Rationale**: User-visible behavior must match the renamed engine. Label-only changes keep the UI risk surface minimal and tests focused.

**Alternatives Considered**:
- *Re-design adaptive panel layout*: Rejected — out of scope; spec is a rename + migration, not a UI redesign.

---

## Open Questions

None. All clarifications resolved.

## Phase 0 Exit Criteria

- [x] No NEEDS CLARIFICATION markers remain in `spec.md` or `plan.md`.
- [x] All research decisions recorded with rationale and rejected alternatives.
- [x] Migration mechanics for DB columns, theme JSON, and config defaults are concrete and reproducible.
- [x] Out-of-scope `*Interaction*` references explicitly enumerated to prevent accidental renames.
- [x] Backward-compatibility read path specified for the migration transition window.

Proceeding to Phase 1: data-model.md, contracts/, quickstart.md.