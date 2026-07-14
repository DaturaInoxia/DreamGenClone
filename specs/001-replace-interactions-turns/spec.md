# Feature Specification: Replace Interactions with Turns Throughout RP Engine and Data Model

**Feature Branch**: `001-replace-interactions-turns`  
**Created**: 2026-07-13  
**Status**: Draft  
**Input**: User description: "B-044 — Replace interactions with turns throughout RP engine and data model"

## Clarifications

### Session 2026-07-13

- Q: Are gate threshold values divided by 3 during migration, or is the numeric value preserved? → A: All stored gate values (session counters, gate JSON thresholds, and config options) divide by 3 and round up. Turns is a first-class citizen stored in its own right, just like interactions. The engine logic must use Turns directly — never compute from interaction counts via a formula (interactions ÷ 3). Interaction counts must not be used for gate decisions at all.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Consistent Turn-Based Phase Advancement (Priority: P1)

A user running a roleplay session experiences phase transitions (e.g., Approaching → BuildUp, BuildUp → Climax) based on turn counts. All phase-advancement thresholds and counters use "turns" as the consistent unit of measurement, with 1 turn representing approximately 3 raw AI interactions. The adaptive panel displays "Turns" labels and turn-based counts everywhere phase progress is shown. The underlying engine counts turns (not raw interaction messages) for all gate evaluations, ensuring phase pacing aligns with user-perceived narrative beats rather than internal batch sizes.

**Why this priority**: This is the core value of the change — making phase pacing predictable and consistent for users. The current mixed naming (some fields say "turn", others say "interaction") causes confusion and inconsistent behavior. This story delivers the fundamental nomenclature and counting change.

**Independent Test**: Start a new session with a scenario that has phase gates, advance through turns, and verify that (a) the adaptive panel shows "Turns" labels with counts that increment once per turn (not once per interaction), (b) phase transitions trigger at the expected turn thresholds, and (c) existing session data is migrated with values divided by 3 and rounded up.

**Acceptance Scenarios**:

1. **Given** a scenario's gate threshold set to 9 (pre-migration interaction-based value), **When** the data migration runs, **Then** the stored threshold becomes 3 turns (9 ÷ 3 = 3, ceiling division).
2. **Given** a session in Approaching phase with 2 interactions completed (pre-migration), **When** the data migration runs, **Then** the stored counter becomes 1 turn (ceiling of 2 ÷ 3 = 1).
3. **Given** a session with gates configured, **When** the user advances a turn (via Continue, SubmitPrompt, or ContinueAs), **Then** the turn counter increments by exactly 1 and the adaptive panel displays the updated count with a "Turns" label.
4. **Given** a session where the turn count reaches the configured threshold, **When** the next turn completes, **Then** the phase advances exactly as before the rename, with no change to actual transition timing or behavior.

---

### User Story 2 - Theme Data Migration and UI Theme Management (Priority: P2)

All existing theme definitions, theme profiles, and theme machine gate rule configurations stored in the database are migrated from interaction-based naming to turn-based naming. The theme management UI (ThemeProfiles page, RPThemeDetail page, gate rule editor) is updated to display and edit turn-based values. Every gate rule JSON blob stored on themes is rewritten so `minimumInteractions` becomes `minimumTurns`, and all UI metric selectors, help text, and labels in the theme management screens use "Turn" terminology. Administrators opening the theme editor after migration see consistent turn-based naming without any legacy "interaction" references.

**Why this priority**: Theme data is the configuration backbone for all sessions. Without migration, existing themes would reference a metric key (`InteractionsSinceCommitment`) that no longer exists in the renamed code, breaking gate evaluation for every session using those themes. The UI update is essential so administrators can configure themes correctly after the rename. This is P2 because it depends on the domain renames (P1) being complete first.

**Independent Test**: Open the Theme Profiles page after migration, edit a theme's gate rules, and verify that (a) existing gate rules show `minimumTurns` in their JSON configuration, (b) the metric selector dropdown shows "Turns Since Commitment", (c) help text and labels use "Turn"/"Turns", and (d) creating a new gate rule writes `minimumTurns` (not `minimumInteractions`).

**Acceptance Scenarios**:

1. **Given** a theme with a Climax → Reset gate rule containing `"minimumInteractions": 9` in its stored JSON, **When** the data migration runs, **Then** the stored JSON is rewritten to `"minimumTurns": 3` (value divided by 3 with ceiling rounding, 9 ÷ 3 = 3).
2. **Given** the Theme Profiles page is open after migration, **When** viewing the gate rule editor for any theme, **Then** the metric selector shows "Turns Since Commitment" and the threshold field is labeled with "Turns" units.
3. **Given** the RPThemeDetail page is open, **When** creating a new gate rule with a turn-count threshold, **Then** the rule is saved with `minimumTurns` in its JSON, not `minimumInteractions`.
4. **Given** the gate rule editor help text, **When** the user views documentation for the Climax → Reset rule, **Then** the text references "TurnsSinceCommitment" (not "InteractionsSinceCommitment").
5. **Given** `appsettings.json` is opened after the change, **When** inspecting phase-related configuration keys, **Then** all keys use `*Turn*` naming (e.g., `BuildUpMinTurnsBeforeCommit`, `CompletedScenarioThemeCooldownTurns`).

---

### User Story 3 - Adaptive Panel and Configuration UI Labels (Priority: P2)

All user-facing labels in the RP workspace adaptive panel and admin configuration screens that previously used "interaction" to mean a phase-advancement unit are updated to use "turn" instead. This covers the adaptive panel's phase progress display and the debug panel's gate evaluation view.

**Why this priority**: Users need to see consistent terminology during live sessions. This is P2 alongside the theme management UI because both are presentation-layer changes that depend on P1 domain renames.

**Independent Test**: Open the RP workspace adaptive panel during a live session. Verify all phase progress labels use "Turns" (e.g., "Turns 5/12"). Open the debug panel and verify gate evaluation displays use turn-based labels.

**Acceptance Scenarios**:

1. **Given** the adaptive panel is open during a session, **When** viewing phase progress for a committed scenario, **Then** the label reads "Turns X/Y" where X is turns completed and Y is the turn threshold.
2. **Given** the adaptive panel is open, **When** viewing the Approaching phase counter, **Then** the label reads "Turns X/Y" (not "Interactions X/Y").
3. **Given** the debug panel gate evaluation section, **When** inspecting gate threshold details, **Then** all turn-count fields are labeled with "Turns" units.

---

### User Story 4 - Prompt and Log Message Consistency (Priority: P3)

Internal log messages, prompt injection diagnostic text, and encounter summary templates that reference "interaction" when meaning "turn" are updated to use "turn" terminology. This ensures debugging output, trace logs, and diagnostic views are consistent with the renamed data model.

**Why this priority**: This is developer-facing and diagnostic-only. It doesn't affect user-visible behavior or configuration. It's P3 because it's important for maintainability but has no direct user impact.

**Independent Test**: Run a session with verbose logging enabled, trigger phase transitions, and inspect log output. Verify that phase-related log messages use "turn" (e.g., "TurnCountInPhase=5") rather than "interaction". Inspect encounter summary text for correct terminology.

**Acceptance Scenarios**:

1. **Given** verbose logging is enabled, **When** a phase transition occurs, **Then** log messages use "TurnCountInPhase" and "TurnsSinceCommitment" (not "InteractionCountInPhase" or "InteractionsSinceCommitment").
2. **Given** an encounter summary is generated, **When** viewing the summary text, **Then** it references "turn X in phase" (not "interaction X in phase").
3. **Given** the roleplay assistant prompt diagnostic section, **When** the cooldown status is output, **Then** the label matches the underlying field name (e.g., `TurnsInCurrentState` displayed with "Turns" label).

---

### Edge Cases

- What happens when a pre-migration session has an interaction count that doesn't divide evenly by 3? → Round up (ceiling division), so 1 interaction → 1 turn, 4 interactions → 2 turns.
- What happens when a session has 0 interactions in the current phase? → Migration sets the turn count to 0.
- What happens if the gate configuration JSON still references `minimumInteractions` after migration? → The migration renames the JSON key to `minimumTurns` AND divides the numeric value by 3 with ceiling rounding. The engine accepts both `minimumInteractions` (legacy, pre-migration only) and `minimumTurns` (post-migration) during a transition window, but all newly written or migrated gates use `minimumTurns` with the converted value.
- What happens if an existing gate rule in the database has a `minimumInteractions` value in its JSON blob? → The migration must rewrite all gate JSON blobs across all theme definitions: (a) rename the key `minimumInteractions` → `minimumTurns`, AND (b) divide the numeric value by 3 with ceiling rounding. This applies to every theme's stored gate configuration, not just session-level data.
- What happens if a theme profile has an old `ThemeSelectionInteractionsPerTheme` column? → That column was already migrated to `ThemeSelectionTurnsPerTheme` in a prior migration; this feature verifies the column exists under the new name and does not re-migrate it.
- What happens to theme machine gate config validation in `RPThemeService`? → The validation code that checks for `minimumInteractions` in gate JSON must be updated to check for `minimumTurns` instead, with backward-compatibility to accept legacy `minimumInteractions` during the transition period.
- What happens when `RolePlayInteraction` entities (the session timeline messages) are confused with turn counts? → The spec explicitly does NOT rename `RolePlayInteraction`, `Interactions` list, `InteractionId`, or any data-model concept that refers to the individual AI-generated message entities. Only phase-advancement counting fields are renamed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST rename all domain model fields that count phase-advancement units from `*Interaction*` to `*Turn*`, specifically: `InteractionCountInPhase` → `TurnCountInPhase`, `InteractionsSinceCommitment` → `TurnsSinceCommitment`, `InteractionsInApproaching` → `TurnsInApproaching`, `InteractionsInCurrentEncounter` → `TurnsInCurrentEncounter`.
- **FR-002**: System MUST rename secondary domain fields: `CompletionCooldownInteractions` → `CompletionCooldownTurns`, `EncounterSummaryRecord.InteractionCountInPhase` → `TurnCountInPhase`, `ScenarioHistoryEntry.InteractionCount` → `TurnCount`.
- **FR-003**: System MUST rename the contract field `LifecycleInputs.InteractionsSinceCommitment` → `TurnsSinceCommitment`.
- **FR-004**: System MUST rename the constant `NarrativeGateMetricKeys.InteractionsSinceCommitment` → `TurnsSinceCommitment`.
- **FR-005**: System MUST rename configuration options: `AdaptiveEarlyTurnInteractionThreshold` → `AdaptiveEarlyTurnThreshold`, `AdaptivePerInteractionTotalDeltaBudget` → `AdaptivePerTurnTotalDeltaBudget`, `CompletedScenarioThemeCooldownInteractions` → `CompletedScenarioThemeCooldownTurns`, `BuildUpMinInteractionsBeforeCommit` → `BuildUpMinTurnsBeforeCommit`. The numeric default values in `StoryAnalysisOptions` MUST be divided by 3 (ceiling) so existing deployments that relied on the old defaults see equivalent turn thresholds.
- **FR-006**: System MUST rename the enum value `TransitionTriggerType.InteractionCountGate` → `TurnCountGate`.
- **FR-007**: System MUST rename all corresponding SQLite database columns (`InteractionCountInPhase`, `InteractionsSinceCommitment`, `InteractionsInApproaching`, `CompletionCooldownInteractions`, `InteractionCount`) in tables `RolePlayV2AdaptiveStates`, `RolePlayV2ThemeScores`, `RolePlayV2ScenarioHistory`, and `RolePlayV2EncounterSummaries` to their `*Turn*` equivalents.
- **FR-008**: System MUST provide a data migration that: (a) renames all DB columns listed in FR-007 from `*Interaction*` to `*Turn*` equivalents and divides existing numeric values by 3 with ceiling rounding; (b) rewrites all gate configuration JSON blobs stored on theme definitions (in the `RPThemes` table) so the `minimumInteractions` JSON property becomes `minimumTurns` AND the numeric threshold value is divided by 3 with ceiling rounding; (c) verifies the `RPThemeProfiles` table already has `ThemeSelectionTurnsPerTheme` (previously migrated) and does not duplicate that migration.
- **FR-008a**: Turns is a first-class stored unit. The RP engine MUST read and write `Turn*` fields directly as persisted values. Gate evaluation, phase-advance counters, and cooldown logic MUST NOT compute turns from interaction counts at runtime (e.g., no `interactions / 3` formulas in gate comparison code). Interaction counts are permitted only on the canonical `RolePlayInteraction` timeline; they MUST NOT feed phase-advancement decisions.
- **FR-009**: System MUST update all RP engine service code, repository code, and test code to use the renamed field names, variable names, and constant names.
- **FR-010**: System MUST update all UI labels, help text, and display strings in Blazor components to use "Turn"/"Turns" terminology where phase-advancement counts are displayed. This includes: the adaptive panel in `RolePlayWorkspace.razor`, the gate rule editor and metric selectors in `ThemeProfiles.razor` and `RPThemeDetail.razor`, the debug panel in `RolePlayDebug.razor`, and the session list in `Home.razor`.
- **FR-011**: System MUST update log messages and diagnostic output to use "Turn" terminology (e.g., "TurnCountInPhase" instead of "InteractionCountInPhase").
- **FR-012**: System MUST update the encounter summary template text to reference "turn" instead of "interaction" for phase counters.
- **FR-013**: System MUST NOT rename data model entities that refer to individual AI-generated messages: `RolePlayInteraction`, `Interactions` list property, `InteractionId`, `InteractionType`, `InteractionCommandService`, `SemanticInteractionAnalysisJobHandler`, and related concepts must remain unchanged.
- **FR-014**: System MUST update the roleplay assistant prompt diagnostic section to align label text with the renamed field names.
- **FR-015**: System MUST accept both `minimumInteractions` (legacy, pre-migration data not yet migrated) and `minimumTurns` (post-migration) as JSON property names in theme gate configuration during the migration transition window, preferring `minimumTurns` when both are present. When `minimumInteractions` is read as a fallback (un-migrated data), the engine MUST divide the value by 3 with ceiling rounding before comparing against `Turn*` fields — this is the ONLY permitted interaction-to-turn conversion, and only for reading un-migrated legacy data. The `RPThemeService` gate config validation must check for `minimumTurns` first and fall back to `minimumInteractions` for backward compatibility. After the migration runs, all stored data uses `minimumTurns` and no runtime conversion is needed.
- **FR-016**: Persisted feature data MUST use SQLite unless this spec explicitly states and justifies a different store.
- **FR-017**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-018**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-019**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities

- **AdaptiveScenarioState (V2)**: The runtime state tracking per-session phase progression. Key renamed fields: `TurnCountInPhase` (was `InteractionCountInPhase`), `TurnsSinceCommitment` (was `InteractionsSinceCommitment`), `TurnsInApproaching` (was `InteractionsInApproaching`), `TurnsInCurrentEncounter` (was `InteractionsInCurrentEncounter`).
- **ThemeScoreState (V2)**: Per-theme scoring state within a session. Renamed field: `CompletionCooldownTurns` (was `CompletionCooldownInteractions`).
- **ScenarioHistoryEntry**: Record of a completed scenario within a session. Renamed field: `TurnCount` (was `InteractionCount`).
- **EncounterSummaryRecord**: Summary of an encounter within a phase. Renamed field: `TurnCountInPhase` (was `InteractionCountInPhase`).
- **LifecycleInputs**: Contract carrying inputs to the scenario lifecycle evaluator. Renamed field: `TurnsSinceCommitment` (was `InteractionsSinceCommitment`).
- **NarrativeGateMetricKeys**: Constants defining metric key strings for gate evaluation. Renamed: `TurnsSinceCommitment` (was `InteractionsSinceCommitment`).
- **TransitionTriggerType**: Enum classifying what triggered a phase transition. Renamed value: `TurnCountGate` (was `InteractionCountGate`).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All phase-advancement domain fields, database columns, and configuration keys use "Turn" terminology with zero remaining `*Interaction*` names in phase-counting code paths.
- **SC-002**: Existing sessions survive the data migration without data loss — all migrated turn counts correctly reflect ceiling division of the original interaction counts by 3.
- **SC-003**: Phase transitions trigger at the same narrative points as before the rename — because stored thresholds are divided by 3 and counters incremented per turn (1 turn ≈ 3 interactions), the effective pacing is unchanged. Turns is a first-class stored unit; the engine reads `Turn*` fields directly without any interaction-to-turn formula.
- **SC-004**: The adaptive panel, gate rule editor, and theme management UI display "Turns" labels with 100% consistency — no "Interactions" label remains where a turn count is shown.
- **SC-005**: All existing automated tests pass after the rename without adjusting assertion values (field renames are compile-time safe).
- **SC-006**: The build compiles with zero errors and zero warnings related to the renamed fields.
- **SC-007**: `appsettings.json` keys referencing phase-advancement units use `*Turn*` naming with zero remaining `*Interaction*` keys in that category.
- **SC-008**: All existing theme gate configuration JSON blobs in the database have `minimumTurns` (not `minimumInteractions`) after migration, verified by querying every theme's gate rules. The numeric values reflect ceiling division by 3 (e.g., a pre-migration threshold of 9 becomes 3).
- **SC-009**: The theme management UI (ThemeProfiles and RPThemeDetail pages) renders without errors after the rename, and creating a new gate rule produces a JSON blob with `minimumTurns`.

## Assumptions

- The 1 turn ≈ 3 interactions ratio is an established convention in the codebase. This feature makes Turns a first-class stored unit: gate thresholds, phase counters, and cooldown values are all stored and compared as Turns. Interaction counts on the canonical `RolePlayInteraction` timeline are NOT used for gate decisions.
- All existing stored gate values (session counters, gate JSON thresholds, and config option defaults) are in interaction-based units and MUST be divided by 3 with ceiling rounding during migration. The migration is one-way: after migration, all values are Turns and no further interaction-to-turn conversion is ever performed at runtime.
- `RolePlayInteraction` entities and the `Interactions` list on `RolePlaySession` are the canonical session timeline and are NOT part of this rename. These represent individual AI-generated messages, not phase-advancement units.
- The `InteractionEvidenceSignal` field on `AdaptiveStateV2` is a keyword-hit signal accumulator and is NOT a turn counter — it is not renamed.
- The `appsettings.json` keys being renamed are documented in `StoryAnalysisOptions` and are expected to be updated in deployment configurations. Default values in code MUST be the converted turn equivalents of the prior interaction defaults.
