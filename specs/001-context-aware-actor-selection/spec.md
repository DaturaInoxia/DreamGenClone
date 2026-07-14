# Feature Specification: Context-Aware Actor Selection

**Feature Branch**: `001-context-aware-actor-selection`  
**Created**: 2026-07-14  
**Status**: Draft  
**Input**: User description: "RP Engine: Context-Aware Actor Selection — LLM-first location detection, location-gated character availability, and LLM-driven actor selection for overflow continue"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Location Detects Where Characters Are (Priority: P1)

A roleplay author creates a scenario with multiple locations (Home, Beach, Office) and characters with location affinities (Wife at Home, Lifeguard at Beach). During a session, when the narrative moves the story from Home to the Beach, the system automatically detects the location change from the story text. Characters who are narratively present at the new location become available; characters who shouldn't be there are excluded.

**Why this priority**: Location detection is currently broken (disabled in production), making all downstream character-availability features non-functional. Without working location detection, there is no way to gate characters by location. This is the foundation for everything else.

**Independent Test**: Start a session with a scenario that has Home and Beach locations. Narrate moving from one to the other. Verify the system correctly identifies the current location from the narrative text within one turn. Verify the location change is logged as a debug event.

**Acceptance Scenarios**:

1. **Given** a session starting at "Home" with a scenario that defines both "Home" and "Beach", **When** the user narrates "We drive to the beach", **Then** the system detects the location change to "Beach" within one subsequent turn.
2. **Given** a session with no location detection model configured, **When** an interaction completes, **Then** the system logs a warning and preserves the previous location unchanged (no silent fallback to guessing).
3. **Given** a session where the location detection call fails or times out, **When** the detection runs, **Then** the system reports the failure explicitly and keeps the previous location value unchanged.
4. **Given** a session where location services are disabled via configuration, **When** interactions complete, **Then** no location detection runs and no errors are produced.

---

### User Story 2 - Characters Appear Only Where They Belong (Priority: P1)

A scenario author defines that the "Lifeguard" character is Required at "Beach", Excluded from "Home", and has no affinity for other locations. During a session, the system uses the detected location to determine which characters are narratively present and should be considered for speaking turns. The Lifeguard never appears in Home scenes and is always available at the Beach.

**Why this priority**: Location-gated character availability is the core open-world mechanic. Without it, characters appear in nonsensical locations (Lifeguard in the Living Room). Combined with Story 1, these two stories form the MVP.

**Independent Test**: Configure a scenario with Home (Wife Required, Lifeguard Excluded) and Beach (Wife Preferred, Lifeguard Required). Start at Home — verify Lifeguard is not in the available character pool. Move to Beach — verify Lifeguard becomes available and Wife remains available.

**Acceptance Scenarios**:

1. **Given** a character with "Required" affinity for the current location, **When** the available character pool is computed, **Then** that character is always included regardless of other factors.
2. **Given** a character with "Excluded" affinity for the current location, **When** the available character pool is computed, **Then** that character is never included.
3. **Given** a character with "Preferred" affinity for the current location, **When** the available character pool is computed, **Then** the preference is noted but does not force inclusion or exclusion.
4. **Given** a character with no affinity for the current location, **When** the available character pool is computed, **Then** inclusion depends on whether the character's `TrueLocation` (per `RolePlayScenePresenceHelper.IsActorInScene`) matches `CurrentSceneLocation` — i.e., the LLM's `PerCharacterLocations` output placed them there during background detection.
5. **Given** the narrative is in the Aftermath phase (couple-only interaction), **When** the available character pool is computed, **Then** the location-gating pipeline is bypassed and only the couple characters are returned.

---

### User Story 3 - Time-of-Day Gates Character Availability (Priority: P2)

A scenario author defines that the "Neighbor" character is only available at "Home" during the "Evening" time of day. During a session, the system automatically detects the time of day from the narrative text. The Neighbor is excluded from the available pool during Morning/Afternoon at Home but appears when the time shifts to Evening.

**Why this priority**: Time-of-day gating adds depth to the open-world model, enabling characters that appear only at specific times (night guard, evening neighbor). It builds on the location gating from Story 2.

**Independent Test**: Configure a character with Home + Evening affinity. Start session at Home with time set to Afternoon — verify character is excluded. Change time to Evening (via narrative or manual override) — verify character becomes available.

**Acceptance Scenarios**:

1. **Given** a character with a location affinity that includes a time-of-day restriction, **When** the current time of day matches, **Then** the affinity applies normally.
2. **Given** a character with a location affinity that includes a time-of-day restriction, **When** the current time of day does not match, **Then** the affinity is treated as if it doesn't exist for that turn.
3. **Given** the user manually sets the time of day to "Night", **When** auto-detection runs, **Then** the manual setting is preserved and not overwritten.
4. **Given** the user switches time-of-day from manual "Night" back to "Auto", **When** the next interaction completes, **Then** auto-detection resumes and may update the time.

---

### User Story 4 - Smarter Actor Selection for Overflow Continue (Priority: P2)

When a user clicks the overflow continue ("...") button, the system selects which character speaks next based on narrative context — not just a simple rotation. The system considers: who is present at the current location, what just happened in the story, which themes are active, who hasn't spoken recently, and scene-level dynamics. When an AI model is available, it reorders candidates for maximum dramatic impact. When no model is configured, a deterministic scoring system provides reasonable defaults.

**Why this priority**: This is the user-facing payoff. Stories 1-3 set up the data (location, availability, time); this story delivers the improved selection experience. Without Stories 1-3, this would be a shallow improvement. With them, it's transformative.

**Independent Test**: In a session with 5 characters at a location, click overflow continue 3 times. Verify that the character selection changes based on context (not the same rotation each time). Verify that the debug log shows the selection source (AI model, scoring, or cache).

**Acceptance Scenarios**:

1. **Given** an AI model is configured for actor selection and the narrative context has changed since the last selection, **When** overflow continue is clicked, **Then** the system calls the AI model to rank available characters and returns the top candidates.
2. **Given** no AI model is configured for actor selection, **When** overflow continue is clicked, **Then** the system uses deterministic scoring based on location match, recency, and affinity status to rank candidates.
3. **Given** the AI model call fails or times out, **When** overflow continue is clicked, **Then** the system falls back to the scoring order and logs the failure explicitly with source marked as "Fallback".
4. **Given** the narrative context has not changed since the last actor selection (same phase, location, time-of-day, and available character set), **When** overflow continue is clicked, **Then** the system reuses the cached ordering (rotated by recency) without making a new AI call.
5. **Given** the persona (POV character) rules apply (first 6 interactions or even turn count), **When** overflow continue is clicked, **Then** the persona is inserted at the correct position regardless of AI or scoring results.

---

### User Story 5 - Authors Configure Location Affinities and Time (Priority: P3)

A scenario author, while editing a scenario in the UI, can assign per-character location affinities: for each location in the scenario, mark a character as Required, Preferred, Excluded, or no affinity. They can also optionally restrict affinities to specific times of day. Additionally, they can set a default starting time of day for the scenario.

**Why this priority**: This is the authoring experience that enables Stories 2 and 3. It can be built in parallel with the engine work but must be complete before the feature ships.

**Independent Test**: Open the scenario editor, navigate to a character with locations defined. Set Beach to "Required" with time "Afternoon". Save. Reopen — verify settings persisted. Create a new session with this scenario — verify the affinity is active.

**Acceptance Scenarios**:

1. **Given** a scenario with defined locations, **When** editing a character in the scenario editor, **Then** the author can set an affinity (Required/Preferred/Excluded/None) for each location with an optional time-of-day restriction.
2. **Given** a scenario with a default time of day set to "Morning", **When** a new session starts, **Then** the session begins with "Morning" as the current time.
3. **Given** a scenario with a default starting location set, **When** a new session starts, **Then** the session's initial detected location is seeded to that location rather than being empty.

---

### User Story 6 - User Controls in Workspace (Priority: P3)

During an active roleplay session, the user sees the current detected time of day and can manually override it. They can also adjust per-character settings: whether a character participates in auto-continue, a response priority value (0–100, see FR-008), and how many characters appear per overflow batch.

**Why this priority**: These are quality-of-life controls that give users agency over the automated systems. They can be built after the core engine work.

**Independent Test**: Open a workspace during a session. Verify time-of-day is displayed. Change it manually to "Night" — verify it stays. Change back to "Auto" — verify auto-detection resumes. Toggle a character's auto-participate setting — verify it takes effect on the next overflow click.

**Acceptance Scenarios**:

1. **Given** an active session with auto-detected time "Afternoon", **When** the user views the workspace, **Then** the current time is displayed with an "Auto" indicator.
2. **Given** the user manually sets time to "Evening", **When** the next overflow continue is clicked, **Then** the manual setting persists and is not overwritten by auto-detection, and the affected character availability reflects the new time.
3. **Given** a character with "Participate in auto-continue" disabled, **When** overflow continue is clicked, **Then** that character is excluded from the candidate pool.
4. **Given** the batch size is set to 2, **When** overflow continue is clicked, **Then** at most 2 characters are selected to speak (plus persona if applicable).

---

### Edge Cases

- What happens when a character has conflicting affinities for the same location at the same time (e.g., Required at Home AND Excluded at Home in Evening)? The Excluded rule takes precedence — the character is excluded for that time slot.
- What happens when a character has multiple affinity entries for the same location with different TimeOfDay values (e.g., Excluded at Home in Morning, Preferred at Home in Evening)? Each entry applies during its time slot only; the active entry is selected by exact-time match over wildcard (null TimeOfDay acts as a fallback rule).
- What happens when no characters are available at the current location (all excluded or not present)? The system falls back to the existing default continue actor behavior.
- What happens when the location name in the narrative doesn't exactly match any scenario location? The LLM should still detect the closest match; if it can't, no location change is recorded.
- What happens when location detection runs as a background job but the user immediately clicks overflow continue? The previous turn's detected location is used (one-turn lag is accepted).
- What happens when the SemanticEventRecord has null ActorName (existing data)? A one-time idempotent C# migration on app startup populates `ActorName` from `RolePlayInteractions.ActorName` via JOIN for null rows (batched, safe to re-run, mirrors the `EnsureAdaptiveStateSchemaAsync` pattern); new events always include it.
- What happens when the time-of-day keyword detection matches multiple time periods (e.g., "morning" and "night" in the same context)? The most recent mention wins.

## Clarifications

### Session 2026-07-14

- Q: Can a character have different affinities for the same location at different times of day (e.g., Excluded at Home in Morning but Preferred at Home in Evening)? → A: Yes — multiple affinity entries per (character, location) are allowed, each with a distinct or null TimeOfDay; conflicts resolved by Excluded > Required > Preferred precedence, then exact-time match over wildcard.
- Q: What signals trigger actor selection cache invalidation (and a new AI call)? → A: Composite fingerprint of narrative phase + current location + current time-of-day + sorted set of available characters. Excludes per-character stat deltas to avoid over-invalidation.
- Q: What is the range and unit of the per-character ResponsePriority override? → A: Integer 0–100, applied as an additive boost to the base score alongside recency/affinity weights; large enough to influence ordering near ties, not large enough to bypass hard gates (location match ±1000, exclusion penalties).
- Q: Is PreferredPosition (First/Last) a hard post-sort rule or a scoring hint? → A: Scoring hint — a small additive boost if First, small penalty if Last. AI/scoring may still override; does not force a fixed slot.
- Q: How is the ActorName backfill for existing SemanticEventRecord rows triggered? → A: One-time idempotent C# migration on app startup: checks for null ActorName rows, JOINs to RolePlayInteractions.ActorName, updates in batches; safe to re-run. Mirrors the EnsureAdaptiveStateSchemaAsync pattern.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST detect the current scene location from narrative text using AI-based analysis, running as a background job after each interaction.
- **FR-002**: System MUST support per-character location affinities (Required, Preferred, Excluded) with optional time-of-day restrictions. Multiple affinity entries per (character, location) are allowed, each with a distinct or null TimeOfDay, enabling time-specific rules (e.g., Excluded at Home in Morning, Preferred at Home in Evening). Conflicts are resolved by precedence: Excluded > Required > Preferred, then exact-time match over wildcard (null TimeOfDay).
- **FR-003**: System MUST filter available characters based on detected location and affinity rules before actor selection.
- **FR-004**: System MUST auto-detect the time of day (Morning, Afternoon, Evening, Night) from narrative text using keyword matching.
- **FR-005**: System MUST allow users to manually override the time of day, which suppresses auto-detection until the user switches back to "Auto".
- **FR-006**: System MUST select overflow continue actors using AI-driven ranking when a model is configured, with deterministic scoring as the base path when no model is available. Scoring factors include: location presence (±1000), affinity strength (±200 to ±500), time-of-day match/mismatch (±100 to −500), recency (0–200), user response priority (0–100 additive), and preferred-position hint (±50).
- **FR-007**: System MUST cache the AI-generated actor ordering and reuse it when narrative context has not changed, avoiding redundant AI calls. Context change is defined by a composite fingerprint: narrative phase + current location + current time-of-day + sorted set of available characters. Per-character stat deltas do not invalidate the cache.
- **FR-008**: System MUST support per-character turn overrides including auto-participate toggle, response priority (integer 0–100, additive boost to base score), and preferred turn position (First/Last) applied as a scoring hint (small additive boost if First, small penalty if Last) rather than a hard sort rule.
- **FR-009**: System MUST support a configurable batch size (1-6) for overflow continue actor selection.
- **FR-010**: System MUST explicitly log the source of every actor selection (AI, Cache, Scoring, Fallback) and every location detection result.
- **FR-011**: System MUST preserve existing persona (POV character) insertion rules: persona leads first 6 interactions, then appears on even turn counts.
- **FR-012**: System MUST preserve the Aftermath phase couple-only behavior, bypassing the new selection pipeline for those interactions.
- **FR-013**: System MUST fail explicitly when AI services are unavailable, rather than silently falling back to hidden defaults — failures are logged and the previous state is preserved.
- **FR-014**: System MUST provide a scenario editor UI for assigning per-character location affinities with optional time-of-day restrictions.
- **FR-015**: System MUST provide workspace controls for time-of-day display/override, per-character auto-participate toggles, and batch size adjustment.
- **FR-016**: Persisted feature data MUST use SQLite, including location affinity definitions, adaptive state (current location, time of day), character turn overrides, and semantic event actor names.
- **FR-017**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-018**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Location detection correctly identifies the current scene location from narrative text in at least 90% of canonical unambiguous location-change scenarios. The canonical corpus for verification includes ~10 transition phrasing patterns: "We drive to [X]", "arriving at [X]", "back at [X]", "heading to [X]", "walking into [X]", "stepping into [X]", "returning to [X]", "we reached [X]", "pulled up to [X]", "made our way to [X]" — each evaluated against each scenario location name.
- **SC-002**: Characters with Excluded affinity for the current location never appear in the available actor pool for that location.
- **SC-003**: Characters with Required affinity for the current location are always included in the available actor pool when at that location.
- **SC-004**: Overflow continue actor selection produces a different ordering (not a fixed rotation) when narrative context changes, as measured across 5 consecutive clicks with context shifts.
- **SC-005**: AI actor selection calls complete within 5 seconds for scenes with up to 10 available characters.
- **SC-006**: When AI actor selection is unavailable, overflow continue still produces a reasonable ordering within 200 milliseconds.
- **SC-007**: Scenario authors can configure location affinities for all characters across all scenario locations in under 2 minutes for a typical 5-character, 3-location scenario. This is a manual UX measurement (no automated test coverage); the UI agent must execute the Phase 7 / US5 manual smoke step (configure affinities for a 5-character × 3-location scenario) and record the measured time in the Phase 9 smoke report.
- **SC-008**: Users can manually override time-of-day and see the override reflected in character availability on the next overflow click.
- **SC-009**: The Aftermath phase and persona insertion rules continue to work exactly as before, with no regressions.

## Assumptions

- AI models for location detection and actor selection are configured separately by the user in Model Manager, using dedicated function slots (`RolePlayLocationDetection` and `RolePlayActorSelection`).
- Location detection runs as a background job with one-turn lag — the current turn uses the previous turn's detected location. This is an acceptable tradeoff for not blocking user interactions.
- Time-of-day detection uses keyword matching rather than AI for simplicity and speed. AI-based time detection could enhance this in a future version.
- Location names in character affinities match the display names used in scenario location definitions. If a location name is edited after affinities are set, the affinity will break and need re-mapping.
- The `EnableLocationServices` configuration toggle controls whether any location detection runs. When disabled, no detection occurs and all characters are treated as not-in-scene.
- Existing sessions without location affinity data or time-of-day settings will function with neutral defaults — all characters available at all locations until affinities are configured.

## Key Entities

- **CharacterLocationAffinity**: Links a character to a location with an affinity type (Required/Preferred/Excluded) and optional time-of-day restriction. Multiple entries per (character, location) are allowed, each keyed by a distinct or null TimeOfDay. Conflicts resolved by Excluded > Required > Preferred precedence, then exact-time match over wildcard.
- **TimeOfDay**: An enumerated value (Morning, Afternoon, Evening, Night) representing the narrative time.
- **AvailableCharacter**: A computed result representing a character's availability for a given turn, including their location status, affinity match, and time-of-day relevance.
- **ActorSelectionResponse**: The ordered list of characters selected to speak, with metadata about the selection source (AI/Cache/Scoring/Fallback).
- **CharacterTurnOverride**: Per-character settings controlling auto-participate behavior, response priority (integer 0–100, additive boost to the scoring base path), and preferred turn position (First/Last) applied as a scoring hint (small additive boost/penalty) that AI/scoring may still override.
- **SemanticEventRecord** *(extended)*: Gains an ActorName field to track which character triggered each semantic event.
