# Feature Specification: Wife-Husband Aftermath Closure

**Feature Branch**: `001-husband-aftermath`  
**Created**: 2026-07-04  
**Status**: Draft  
**Input**: User description: "Wife-Husband Aftermath Closure: Add AftermathCoupleInteraction time-skip phase and [Aftermath:husband-contrast] marker for post-encounter closure turns (get dressed, return to husband, act normal, then advance time)"

## Clarifications

### Session 2026-07-04

- Q: In the aftermath closure turn, who is the user's persona (POV character)? → A: Persona is excluded from the actor list regardless of identity — observes only; actor filter is wife + husband by relation, not by persona-match.
- Q: When both markers are present but the encounter ends in a non-Climax phase, should the chain still be `CloseScene → AftermathCoupleInteraction → AdvanceTime` (full), or `AftermathCoupleInteraction → None` (aftermath only)? → A: Full chain only in Climax; in non-Climax phases run `AftermathCoupleInteraction → None` (aftermath only; AdvanceTime leg stays Climax-locked).
- Q: Should the CloseScene closure-prose rewrite (FR-010) apply to ALL multi-encounter themes or only to themes carrying `[Aftermath:husband-contrast]`? → A: All multi-encounter themes (marker-absent included) — the closure complaint applies to every multi-encounter theme, not just aftermath-opted ones.
- Q: How should the system identify the "wife" character in the actor filter (FR-008)? → A: The "wife" is the character whose spouse relation points at the session's persona — reuses the existing spouse-resolution lookup (no new identification logic).
- Q: When both markers are present but the encounter ends in a non-Climax phase, should the chain still be `CloseScene → AftermathCoupleInteraction → AdvanceTime` (full), or `AftermathCoupleInteraction → None` (aftermath only)? → A: Full chain only in Climax; in non-Climax phases run `AftermathCoupleInteraction → None` (aftermath only; the AdvanceTime leg stays Climax-locked).
- Q: Should the CloseScene closure-prose rewrite (FR-010) apply to ALL multi-encounter themes or only to themes carrying `[Aftermath:husband-contrast]`? → A: All multi-encounter themes (marker-absent included) — the closure complaint applies to every multi-encounter theme, not just aftermath-opted ones.
- Q: How should the system identify the "wife" character in the actor filter (FR-008)? → A: The "wife" is the character whose spouse relation points at the session's persona — reuses the existing spouse-resolution lookup (no new identification logic).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Post-Encounter Closure with Husband (Priority: P1)

As a roleplay author, I want sex encounters to end with a proper closure turn — the wife getting dressed, returning to her husband, and acting normal — so that the narrative doesn't jump abruptly from intimate encounter to time advancement without any resolution.

**Why this priority**: This is the core value of the feature. Without it, encounters feel incomplete and the time skip feels jarring. Every theme that opts into this marker needs this closure turn to fire.

**Independent Test**: Can be fully tested by creating a theme with the `[Aftermath:husband-contrast]` marker in Climax phase guidance, playing through an encounter boundary, and verifying that after the encounter ends, a closure turn appears featuring the wife getting dressed and interacting with her husband before any time advance occurs.

**Acceptance Scenarios**:

1. **Given** a theme with `[Aftermath:husband-contrast]` in Climax guidance and `[ClimaxMode:multi-encounter]`, **When** an encounter completes (boundary detected), **Then** the system inserts three phases in sequence: CloseScene (close the encounter), AftermathCoupleInteraction (get dressed, return to husband), then AdvanceTime (time skip).
2. **Given** a theme with `[Aftermath:husband-contrast]` but without multi-encounter, **When** an encounter completes in any non-Reset phase, **Then** the system inserts AftermathCoupleInteraction as a standalone closure turn, then returns to natural pacing.
3. **Given** a theme without `[Aftermath:husband-contrast]`, **When** an encounter completes, **Then** the existing behavior is unchanged — CloseScene → AdvanceTime → None for multi-encounter, natural pacing otherwise.

---

### User Story 2 - Author Opt-In via Theme Marker (Priority: P2)

As a theme author, I want to control whether the aftermath closure fires by adding or removing the `[Aftermath:husband-contrast]` marker in my theme's phase guidance text — no new UI required, matching how `[ClimaxMode:multi-encounter]` already works.

**Why this priority**: The marker is the sole enabling mechanism — without opt-in, no theme activates aftermath behavior. This ensures backward compatibility and gives authors explicit control.

**Independent Test**: Can be tested by editing a theme's phase guidance to add/remove the marker and verifying that aftermath behavior only activates when the marker is present.

**Acceptance Scenarios**:

1. **Given** a theme without the marker in phase guidance, **When** an encounter ends, **Then** no aftermath closure turn fires.
2. **Given** a theme with the marker, **When** the phase changes to Reset, **Then** the marker is ignored (no aftermath during Reset phase).
3. **Given** a theme with the marker in BuildUp or Committed phase guidance, **When** an encounter ends in that phase, **Then** the aftermath closure turn fires (marker works in any non-Reset phase).

---

### User Story 3 - Husband-Wife Actor Focus (Priority: P3)

As a roleplay author, I want the aftermath closure turn to focus exclusively on the wife and husband characters — with the user's persona excluded from authoring and observing only — so the "contrast" between the secret encounter and ordinary married life is sharp and clear.

**Why this priority**: The narrative purpose of the aftermath is the wife-husband contrast. Including other characters would dilute this effect. However, the core closure timing (Story 1) is more fundamental.

**Independent Test**: Can be tested by triggering aftermath in a scenario with 3+ characters (including the persona) and verifying only wife and husband appear in the overflow actor candidates; the persona is never selected.

**Acceptance Scenarios**:

1. **Given** a scenario with wife, husband, additional characters, and a persona (regardless of persona identity), **When** aftermath fires, **Then** only wife and husband — identified by spouse relation, not by persona-match — are selected as overflow actors for the closure turn; the persona is excluded.
2. **Given** a scenario where the husband character cannot be identified (no clear spouse), **When** aftermath fires, **Then** the aftermath leg aborts with a diagnostic log and no turn is generated — the system does not silently substitute other actors or the persona.

---

### Edge Cases

- What happens when the theme has both `[ClimaxMode:multi-encounter]` and `[Aftermath:husband-contrast]` markers and the encounter ends in Climax? The CloseScene → AftermathCoupleInteraction → AdvanceTime chain executes in order.
- What happens when the theme has both markers but the encounter ends in a non-Climax phase (e.g., BuildUp)? Only `AftermathCoupleInteraction → None` runs — the AdvanceTime leg is Climax-locked and does not fire in non-Climax phases, preserving the existing natural-pacing semantics outside Climax.
- What happens during the aftermath turn if the model attempts to trigger another encounter? Detection is re-entry guarded while the state machine is active (CurrentTimeSkipPhase != None); natural pacing resumes only after the aftermath leg completes.
- What happens when a Fast Pacing directive would normally fire during the aftermath leg? Fast Pacing is suppressed during AftermathCoupleInteraction only, so the closure turn gets the narrative room it needs.
- What happens in scenarios without a husband (e.g., no character with spouse relation)? The aftermath leg aborts explicitly with a diagnostic log and the state machine clears — no fallback actors, no silent substitution of the persona, no silent continuation.
- What happens when the `encounter-completed` semantic mapping is missing from a theme that uses `[Aftermath:husband-contrast]`? The system fails fast with a configuration error, matching the existing strict enforcement for `[ClimaxMode:multi-encounter]`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a new time-skip phase `AftermathCoupleInteraction` as part of the existing time-skip state machine, positioned between CloseScene and AdvanceTime when both markers are active.
- **FR-002**: System MUST detect the `[Aftermath:husband-contrast]` marker in theme phase guidance text for any non-Reset phase, using the same pattern as the existing `[ClimaxMode:multi-encounter]` marker detection.
- **FR-003**: System MUST generalize encounter-boundary detection to fire for any non-Reset phase carrying the aftermath marker, not only the Climax phase.
- **FR-004**: When only the aftermath marker is present (no multi-encounter), System MUST run AftermathCoupleInteraction as a standalone closure turn followed by a return to normal pacing (no AdvanceTime leg).
- **FR-005**: When both aftermath and multi-encounter markers are present in Climax, System MUST sequence CloseScene → AftermathCoupleInteraction → AdvanceTime → None. In non-Climax phases where both markers are present, System MUST run `AftermathCoupleInteraction → None` only — the AdvanceTime leg stays Climax-locked and MUST NOT fire in BuildUp, Committed, or other non-Climax phases.
- **FR-006**: System MUST persist the `LastEncounterEvidenceSpan` (the AI-detected evidence span of what just happened) at detection time so the aftermath directive can reference it verbatim.
- **FR-007**: System MUST emit a closure directive during AftermathCoupleInteraction instructing the wife to get dressed, return to the normal setting, interact with her husband, act normal, and conceal evidence — with the contrast between secret reality and ordinary performance as the narrative point.
- **FR-008**: System MUST restrict overflow actor selection during AftermathCoupleInteraction to the wife and husband only — the wife identified as the character whose spouse relation points at the session's persona (husband), reusing the existing spouse-resolution lookup; the husband identified as the session's persona itself (excluded from the actor list as per the persona-exclusion rule but used for relation matching). System MUST exclude the user's persona from the actor list. If either the wife or husband cannot be identified via the existing lookup, System MUST explicitly abort (diagnostic log, state machine clear) with no silent fallback to other actors or to the persona.
- **FR-009**: System MUST suppress Fast Pacing directives during the AftermathCoupleInteraction phase so that pacing instructions do not conflict with the closure directive.
- **FR-010**: System MUST rewrite the existing CloseScene directive text to include explicit closure content (bodies settle, characters separate, get dressed, return to ordinary setting) for ALL multi-encounter themes — including those that do not carry the `[Aftermath:husband-contrast]` marker. Marker-absent themes get the rewritten closure prose in their CloseScene leg but do not get the AftermathCoupleInteraction leg.
- **FR-011**: System MUST fail fast with a configuration error when a theme carries `[Aftermath:husband-contrast]` but lacks an `encounter-completed` semantic mapping.
- **FR-012**: System MUST enforce re-entry guard: no new encounter-boundary detection fires while any time-skip phase is active (CurrentTimeSkipPhase != None).
- **FR-013**: Persisted feature data MUST use SQLite (existing RolePlayV2AdaptiveStates table) for the new `LastEncounterEvidenceSpan` column.
- **FR-014**: Application logging MUST use Serilog with structured message templates; major execution paths MUST emit Information-level logs including aftermath leg transitions, actor filter decisions, and abort conditions.

### Key Entities

- **Aftermath Closure Phase**: A distinct state in the time-skip sequence (`AftermathCoupleInteraction`) where the wife returns to her husband after an encounter — positioned between scene close and time advance.
- **Encounter Evidence Record**: The verbatim description of what just happened, captured at encounter-boundary detection, used to generate a context-aware closure directive referencing "what she just did."
- **Theme Marker (`[Aftermath:husband-contrast]`)**: A text marker placed by theme authors in phase guidance, analogous to `[ClimaxMode:multi-encounter]`. Its presence in any non-Reset phase enables aftermath closure for that phase.
- **Aftermath Closure Directive**: A prompt instruction that fires during the aftermath phase to emit the wife-husband contrast directive — the wife gets dressed, returns to normal setting, and interacts with her husband while concealing what happened.

## Assumptions

- The existing `encounter-completed` semantic mapping infrastructure is sufficient to detect encounter boundaries in non-Climax phases (BuildUp, Committed) without requiring new mapping definitions.
- Theme authors understand the `[Aftermath:husband-contrast]` marker convention from the existing `[ClimaxMode:multi-encounter]` pattern and do not need a separate UI control.
- The "husband" character is the session's persona; the wife is the character whose spouse relation points at the persona, identified via the existing spouse-relation lookup in the scenario — the same mechanism used during opening narrative generation.
- Scenarios without a clear spouse relation are authoring edge cases; a diagnostic log entry is sufficient (no in-app UI notification required for v1).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Themes with the `[Aftermath:husband-contrast]` marker produce a closure turn (wife dresses, returns to husband, acts normal) between encounter end and time advance — 100% of the time when both markers are present in Climax.
- **SC-002**: Themes without the aftermath marker exhibit zero behavioral change — existing multi-encounter and single-encounter flows remain identical to current production behavior.
- **SC-003**: The CloseScene directive text for all multi-encounter themes now includes explicit closure prose (get dressed, return to ordinary setting), improving narrative coherence for marker-absent themes as well.
- **SC-004**: Missing spouse scenarios produce a clean diagnostic log entry and abort without generating erroneous output — no silent fallback to other actors.
- **SC-005**: All existing multi-encounter time-skip behavior continues to function without regression — themes without the aftermath marker produce identical narrative output to current production behavior.
