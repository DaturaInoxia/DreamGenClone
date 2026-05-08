# Feature Specification: Finishing Move System – Catalog and Matrix Redesign

**Feature Branch**: `007-finishing-move-catalog`  
**Created**: 2026-05-05  
**Status**: Draft  
**Input**: User description: "Finishing Move System Catalog and Matrix Redesign – Approach 2 (Catalog + Matrix)"

## User Scenarios & Testing *(mandatory)*

### User Story 1 – Curator configures finishing move catalog entries (Priority: P1)

A content curator navigates to the Theme Profiles management page and opens the Finishing Moves section. They see five new catalog tabs — Locations, Facial Types, Receptivity Levels, His Control Levels, and Transition Actions — alongside the existing Matrix tab. Each tab shows a table of seed-populated entries and allows adding, editing, or disabling individual entries. Each entry has one or more adaptive-stat band eligibility fields (e.g., "Desire Band", "Other Man Dominance Band") that control when the entry appears in a live session.

**Why this priority**: This is the primary curated-data management surface. Without it the catalog data cannot be maintained.

**Independent Test**: Navigate to Theme Profiles → Finishing Moves. Verify each of the five new tabs is present, loads its seed data, and allows saving an edited entry.

**Acceptance Scenarios**:

1. **Given** the app is running, **When** a curator opens the Finishing Moves section, **Then** six tabs are visible: Matrix, Locations, Facial Types, Receptivity Levels, His Control Levels, Transition Actions.
2. **Given** a Locations tab is open, **When** the curator edits an entry and saves, **Then** the change persists and the updated entry appears on next load.
3. **Given** a catalog entry has an empty eligibility band field, **When** the entry is displayed in the table, **Then** it shows "(any)" to indicate it matches all bands.
4. **Given** a curator deletes a catalog entry, **When** the list refreshes, **Then** the deleted entry no longer appears.

---

### User Story 2 – Matrix tab shows renamed "Other Man Dominance" column (Priority: P1)

A curator opening the Finishing Move Matrix tab sees the column header and form label for the dominance dimension reads "Other Man Dominance Band" (previously "Dominance Band"). The stored band ranges have been updated to `0-29`, `30-59`, `60-100`.

**Why this priority**: The rename is a prerequisite for clear curation intent and a database schema change required by the catalog architecture.

**Independent Test**: Open the Matrix tab, verify the column header reads "Other Man Dominance Band" and band values are `0-29 / 30-59 / 60-100`.

**Acceptance Scenarios**:

1. **Given** the Matrix tab is open, **When** a curator views the row table, **Then** the column header reads "Other Man Dominance Band" (not "Dominance Band").
2. **Given** the matrix seed data has loaded, **When** a curator inspects band values, **Then** all values are in the set `{0-29, 30-59, 60-100}` rather than the old ranges.
3. **Given** a curator adds a new matrix row, **When** they pick the dominance band, **Then** the dropdown shows three options: `0-29`, `30-59`, `60-100`.

---

### User Story 3 – Climax-phase prompt includes catalog context (Priority: P2)

During a live roleplay session that reaches the climax phase, the prompt assembled by the engine contains two sections: one from the matched Finishing Move Matrix row (existing behavior, updated for the renamed column) and five additional sections — one per catalog type — listing only the entries eligible for the session's current adaptive stats.

**Why this priority**: This is the consumer of all the catalog work; without it the data has no effect on the narrative.

**Independent Test**: Start a session, advance to climax phase, open diagnostic/prompt log and confirm both the matrix section and five catalog sections are present.

**Acceptance Scenarios**:

1. **Given** a session is in climax phase and a matrix row matches the current stats, **When** the prompt is assembled, **Then** the prompt contains a "Finishing Move – Matrix" section from the matched row.
2. **Given** a session is in climax phase, **When** the prompt is assembled, **Then** the prompt contains five additional sections labelled "Finishing Move – Locations", "…Facial Types", "…Receptivity", "…His Control", "…Transitions".
3. **Given** an adaptive stat (e.g., Desire = 20), **When** catalog entries are filtered, **Then** only entries with `0-29` Desire eligibility or empty eligibility are included in the prompt.
4. **Given** no catalog entries match the current bands, **When** the prompt is assembled, **Then** the section for that catalog type is omitted rather than shown empty.
5. **Given** no Locations entry in the climax prompt has Category = Facial, **When** the prompt is assembled, **Then** the Facial Types section is omitted.

---

### User Story 4 – Seed data auto-populates all catalogs on first run (Priority: P2)

When the application starts with an empty database, all five new catalog tables and the updated matrix table are populated with production-quality seed data automatically. A curator sees meaningful, well-described entries in each tab without any manual import step.

**Why this priority**: Without seeds the UI has nothing to show and runtime eligibility filtering has no entries to return.

**Independent Test**: Delete the dev database, start the app, open Finishing Moves — all five tabs show populated entries.

**Acceptance Scenarios**:

1. **Given** a fresh database, **When** the app starts, **Then** `RPFinishLocations` contains at least 15 entries across all five location categories.
2. **Given** a fresh database, **When** the app starts, **Then** `RPFinishFacialTypes` contains at least 6 entries.
3. **Given** a fresh database, **When** the app starts, **Then** `RPFinishReceptivityLevels` contains exactly 8 levels (Begging through Enduring) with physical and narrative cues.
4. **Given** a fresh database, **When** the app starts, **Then** `RPFinishHisControlLevels` contains exactly 3 levels with example dialogue.
5. **Given** a fresh database, **When** the app starts, **Then** `RPFinishTransitionActions` contains at least 6 entries.
6. **Given** an existing database from a previous version, **When** the app starts, **Then** the schema migration renames the `DominanceBand` column to `OtherManDominanceBand` and all matrix band values are updated to the new ranges without data loss of other columns.

---

### Edge Cases

- What happens when the matrix migration runs on a database that already has the `OtherManDominanceBand` column? The migration must detect this and skip safely.
- What happens when an eligibility band field is null vs. empty string? Both must be treated as "any band eligible".
- What happens when no matrix row matches the current stats at climax phase? The matrix section is omitted; the five catalog sections still appear if they have eligible entries.
- What happens when a catalog entry is disabled (`IsEnabled = false`)? It must never appear in the prompt and must be visually distinct (grayed out) in the UI table.
- What happens when the Facial Types section is included but all matched locations are non-facial? The section must be suppressed (not just empty).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide five new independent catalog tables — Locations, Facial Types, Receptivity Levels, His Control Levels, and Transition Actions — alongside the existing Finishing Move Matrix.
- **FR-002**: Each catalog entry MUST have one or more eligibility band fields (comma-separated band key strings such as `0-29`, `30-59`, `60-100`) governing which adaptive stat ranges it is eligible for; an empty/null field MUST mean "eligible for all bands".
- **FR-003**: The `RPFinishLocations` catalog MUST include a `Category` field with values in `{Internal, External, Facial, OnBody, Withdrawal}`.
- **FR-004**: The existing Finishing Move Matrix table MUST be migrated: the dominance band column MUST be renamed from `DominanceBand` to `OtherManDominanceBand` and band values updated to the three-tier standard (`0-29`, `30-59`, `60-100`). The migration MUST be idempotent — if the column already has the new name, the migration MUST skip safely without error.
- **FR-005**: All five new catalog types and the updated matrix MUST be accessible through CRUD operations (list, save/upsert, delete) exposed via the theme service interface.
- **FR-006**: All new catalog tables and their seed data MUST be initialized automatically on application start for a fresh database, without any manual import step.
- **FR-007**: When the climax phase prompt is assembled, the engine MUST query all five catalog tables, filter by the session's current adaptive stat bands, and emit a separate labeled prompt section for each catalog type that has at least one eligible entry.
- **FR-008**: The Facial Types prompt section MUST be suppressed unless at least one eligible Location entry in the same prompt has `Category = Facial`.
- **FR-009**: Disabled entries MUST be excluded from all prompt assembly queries and MUST be visually distinguished in the management UI.
- **FR-010**: Band eligibility matching MUST be encapsulated in a single reusable helper; duplicate band-parsing logic in multiple locations is forbidden.
- **FR-011**: The matrix prompt section MUST continue to include optional curator-override location annotation fields; these are additive alongside the catalog, not replacements.
- **FR-012**: Persisted feature data MUST use SQLite unless this spec explicitly states and justifies a different store.
- **FR-013**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-014**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-015**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities

- **RPFinishLocation**: A named physical location for a finishing move scene. Has a category (Internal/External/Facial/OnBody/Withdrawal), eligibility bands for Desire, SelfRespect, and OtherManDominance, a sort order, and an enabled flag.
- **RPFinishFacialType**: A sub-type describing the specific nature of a facial finishing move. Has physical cues, and eligibility bands for Desire, SelfRespect, and OtherManDominance.
- **RPFinishReceptivityLevel**: One of eight named levels describing the wife character's receptivity state (Begging → Enduring). Has physical cues, a narrative cue, and eligibility bands for Desire and SelfRespect.
- **RPFinishHisControlLevel**: One of three levels describing how much the male character directs the scene (Asks / Leads / Commands). Has example dialogue and an eligibility band for OtherManDominance.
- **RPFinishTransitionAction**: A named transition action for the finishing move sequence. Has transition text and eligibility bands for Desire, SelfRespect, and OtherManDominance.
- **RPFinishingMoveMatrixRow** *(updated)*: The curated matrix combining all three band dimensions into a single row with narrative guidance and optional location override annotations. The dominance dimension column is renamed from `DominanceBand` to `OtherManDominanceBand`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A curator can open the Finishing Moves section and view, add, edit, and disable entries in all five new catalog tabs without leaving the Theme Profiles page.
- **SC-002**: A fresh database startup produces fully populated seed data across all five catalog tables and the matrix table with no manual steps.
- **SC-003**: All existing finishing-move-related tests pass after the schema migration and rename changes.
- **SC-004**: During a climax-phase session, the assembled prompt includes labeled sections for each eligible catalog type and the matrix row, verifiable in the application log.
- **SC-005**: The eligibility filter produces correct results across all three band tiers for every catalog type, as verified by automated tests on the seed service output.

## Assumptions

- All five catalog types are global (not scoped per profile), matching the existing matrix pattern.
- The matrix migration that drops and recreates `RPFinishingMoveMatrixRows` may discard previously stored matrix data; this is acceptable per the "start over" design decision.
- Band eligibility is stored as a plain comma-separated string of band key values (e.g., `"0-29,30-59"`); an empty or null value means all bands are eligible.
- The three standard band tiers (`0-29`, `30-59`, `60-100`) apply uniformly to Desire, SelfRespect, and OtherManDominance stats throughout this feature.
- `RPFinishReceptivityLevel` uses only Desire and SelfRespect bands (OtherManDominance is not relevant to receptivity).
- `RPFinishHisControlLevel` uses only OtherManDominance band (Desire and SelfRespect are not relevant to control level).
- The Facial Types catalog section is conditionally rendered — only when at least one eligible Location in the climax prompt has `Category = Facial`.
- Matrix `PrimaryLocations`, `SecondaryLocations`, and `ExcludedLocations` columns are kept as optional curator-override annotations, not removed in this feature.
- Seed data counts: ≥15 Locations, ≥6 Facial Types, exactly 8 Receptivity Levels, exactly 3 His Control Levels, ≥6 Transition Actions.
