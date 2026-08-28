# Feature Specification: Stat-Driven Character Instruction Text & Encounter Dimension Drift

**Feature Branch**: `001-stat-char-text-drift`
**Created**: 2026-05-30
**Status**: Draft
**Backlog**: B-043
**Input**: Character profile stat-driven instruction text generation and encounter dimension drift

## Clarifications

### Session 2026-05-30

- Q: Should encounter dimension drift apply only to semantic-scored stat deltas, or to any stat value change including manual UI assignments? → A: Full raw delta — any stat value change (manual or auto-scored) triggers drift proportionally with the ±5-per-dimension-per-interaction cap enforced via slope values.
- Q: When a user re-binds a different encounter profile to a character mid-session (switching archetypes with drifted RuntimeEncounterStats already present), what happens to RuntimeEncounterStats? → A: Reset — RuntimeEncounterStats is reset to the new profile's EncounterStats values on profile rebind.
- Q: On session start before any stat delta fires, should the behavioral frame use the last-saved RuntimeEncounterStats or fall back to the static profile? → A: Use last-saved RuntimeEncounterStats immediately on session start if present; static profile is fallback only when RuntimeEncounterStats is null.

---

## Summary

Enhance the roleplay engine's character system so that each character's current stat values continuously influence the LLM directives injected into prompts. Each canonical stat has four narrative text bands selected by the runtime value; out-of-neutral stats produce a synthesized per-character constraint sentence injected alongside the behavioral frame on every continuation. Canonical stat changes also drift the encounter dimensions that underpin the behavioral frame, keeping the frame coherent with the character's actual evolving state. Stat reduction (from 7 to 5) is a prerequisite step.

## User Scenarios & Testing *(mandatory)*

---

### User Story 1 — Prude Wife Transforms Into Highly Sexual Character (Priority: P1)

A user starts a session with a Prude Wife character profile (Desire ≈ 15, Restraint ≈ 95, SelfRespect ≈ 85). Over many interactions, semantic scoring drives Desire up and Restraint down. After significant drift, the LLM directives for the wife should reflect her transformed state — she is instructed as eager, uninhibited, and boundary-eroding — without any manual profile change by the user.

**Why this priority**: This is the primary stated use case and the core transformation arc the engine is designed to support. Without this, the behavioral frame always contradicts the actual stat state, making the roleplay engine incoherent at the character level.

**Independent Test**: Can be tested end-to-end by running a session with a Prude Wife preset, applying 8–10 sequential Desire +10 and Restraint -10 deltas via stat scoring, then inspecting the generated prompt text. Delivers the full transformation arc value independently.

**Acceptance Scenarios**:

1. **Given** a Wife character with Desire=15, Restraint=95 and a bound Prude Wife encounter profile, **When** the session begins, **Then** the behavioral frame reads "deeply private, distressed if seen" tier text for Exhibitionism and DiscoveryCaution dimensions, and no stat state text is injected (all stats are in neutral or baseline ranges for their Prude preset).

2. **Given** the same session after 10 interactions have driven Desire to 80 and Restraint to 20, **When** a continuation is generated, **Then** the behavioral frame for the wife reflects high-Exhibitionism and low-DiscoveryCaution tier text (drifted from the Prude baseline), and the stat state text synthesizes her Desire=80 and Restraint=20 into a single characterization constraint injected into the prompt (e.g., "she craves physical intensity with urgency; she has almost no capacity to hold back").

3. **Given** a session where Desire has reached 80 and was saved, **When** the session is closed and reopened, **Then** the wife's RuntimeEncounterStats and stat values are loaded from the previous save and the behavioral frame on the first continuation still reflects the drifted state — not the original Prude profile.

---

### User Story 2 — Protective Husband Transforms Into Submissive Cuck (Priority: P2)

A user starts with a husband character configured with a Caring/Supportive or Cuckold profile (Dominance ≈ 65, SelfRespect ≈ 75). As the session progresses and the husband defers, backs off, and accepts the wife's behavior, semantic scoring drives Dominance down. The husband's encounter dimensions (Acceptance, Voyeurism, Participation, Encouragement) drift upward automatically. The behavioral frame reflects an increasingly passive, accepting, watching husband without manual intervention.

**Why this priority**: The husband transformation arc is the second primary use case. Dominance is the only stat tracking the husband's arc; without this story, the husband's profile is static regardless of what happens in the narrative.

**Independent Test**: Can be tested by running a session with a husband character, applying 6 sequential Dominance -8 deltas, and asserting that RuntimeEncounterStats for Acceptance and Voyeurism have increased from their initial bound-profile values. Delivers the husband arc value independently.

**Acceptance Scenarios**:

1. **Given** a Husband character with Dominance=65 and a Caring/Supportive encounter profile (Acceptance initially tier 2, Voyeurism initially tier 1), **When** 6 interactions apply Dominance -8 each, **Then** the husband's RuntimeEncounterStats show Acceptance and Voyeurism values that resolve to higher tier text, and the behavioral frame reflects this in the generated prompt.

2. **Given** a Husband character whose Dominance has fallen to 20, **When** a continuation is generated, **Then** the stat state text synthesizes Dominance=20 into a husband-specific constraint (e.g., "he is passive and deferential; he will follow any lead and does not assert his own preferences").

3. **Given** a Husband character with Dominance in the neutral band (35–65), **When** a continuation is generated, **Then** no stat state text is injected for Dominance — only the behavioral frame is present.

---

### User Story 3 — Stat Reduction: Removing Unused Stats (Priority: P1)

The engine currently tracks 7 stats (including Tension and Connection) that are orphaned — they accumulate scoring but produce no prompt output and have thin narrative justification for the intended use case. These must be removed cleanly from all layers so the system reflects the 5-stat design going forward.

**Why this priority**: P1 because it is a prerequisite for everything else — the text band catalog and drift rules are designed for 5 stats, and leaving orphaned stats in the codebase creates confusion, dead code paths, and test maintenance burden.

**Independent Test**: Can be tested by verifying the build passes with no references to Tension or Connection stats, the cheating formula uses only 3 terms, and all existing tests pass with Tension/Connection test cases removed or updated.

**Acceptance Scenarios**:

1. **Given** the codebase after stat reduction, **When** `dotnet build` is run, **Then** there are zero compile errors, zero references to `Tension` or `Connection` stat fields on `CharacterStatProfileV2`, and zero uses of `AverageTension` or `AverageConnection` in prompt or guidance construction.

2. **Given** the ScenarioGuidanceGenerator cheating pressure calculation after stat reduction, **When** evaluated with Loyalty=70, Desire=60, Restraint=50, **Then** the formula `70 - (60/2) + (50/2) = 70 - 30 + 25 = 65` produces "moderate-high loyalty pressure" guidance text — without any Tension term.

3. **Given** existing saved sessions with Tension and Connection values in `CharacterSnapshotsJson`, **When** those sessions are loaded after the update, **Then** the system loads successfully, Tension/Connection fields are silently dropped on the next save, and no errors are thrown.

---

### User Story 4 — All Active Out-of-Neutral Stats Produce Coherent Prompt Text (Priority: P2)

For any character (Wife, Husband, or OtherMan) where multiple stats are simultaneously outside the neutral band (35–65), the engine produces a single coherent synthesized characterization sentence — not a list of disconnected fragments — that is injected as a constraint into the continuation prompt.

**Why this priority**: If the synthesis produces contradictory or fragment-heavy text, it degrades LLM output quality. The synthesis must read as a single coherent directive.

**Independent Test**: Can be tested by constructing a Wife snapshot with Desire=82, Restraint=12, Loyalty=15 (three out-of-neutral stats) and asserting the synthesized sentence is a single coherent string covering all three behavioral signals.

**Acceptance Scenarios**:

1. **Given** a Wife character with Desire=82, Restraint=12, and Loyalty=15 (all outside neutral band), **When** stat state text is generated, **Then** a single synthesized sentence is produced that incorporates the behavioral signals for all three stats and reads as coherent LLM directive prose (not a bullet list or three separate sentences).

2. **Given** a character where all 5 stats are in the neutral band (35–65), **When** stat state text is generated, **Then** no stat state text line is injected into the prompt for that character.

3. **Given** a character where only one stat (e.g., Loyalty=10) is outside the neutral band, **When** stat state text is generated, **Then** a single sentence is injected covering only that stat's behavioral signal.

---

### Edge Cases

- What happens when a character has no bound encounter profile (no `CharacterEncounterProfileIds` entry)? RuntimeEncounterStats is initialized using BehavioralDimensionCatalog defaults (50 for all dimensions) on first stat delta.
- What happens when a drift rule would push a dimension below 0 or above 100? The result is clamped to the floor/ceiling defined per rule.
- What happens when a stat delta of 0 is applied? No drift calculation is performed; RuntimeEncounterStats is unchanged.
- What happens if `CharacterSnapshotsJson` contains a character snapshot with missing `RuntimeEncounterStats`? The field is null on deserialization; lazy initialization fires on first stat delta in that session.
- What happens when the user rebinds a different encounter profile to a character that already has drifted `RuntimeEncounterStats`? `RuntimeEncounterStats` is reset to the new profile's `EncounterStats` values; all prior drift is discarded.
- What happens if the same character label appears in both `CharacterBehavioralFrames` and `CharacterStatStateTexts`? Both are injected in order: behavioral frame first, then stat state text on the immediately following line.
- What happens for OtherMan characters regarding drift? No `StatToDimensionMappings` rules exist for OtherMan; his encounter dimensions are not drifted. His stat text bands (Desire, Dominance) still produce synthesized stat state text if outside neutral band.

---

## Requirements *(mandatory)*

### Functional Requirements

**Stat Reduction**

- **FR-001**: The system MUST remove `Tension` and `Connection` from `CharacterStatProfileV2`. Existing persisted snapshots containing those fields MUST deserialize without error; the fields are silently dropped on next save.
- **FR-002**: The cheating pressure formula MUST use exactly three terms: `Loyalty - (Desire/2) + (Restraint/2)`. No Tension or Connection term is included.
- **FR-003**: All keyword-category and semantic-event mutation rules for Tension and Connection MUST be removed from the adaptive state service. No scoring, no decay, no baseline logic for these stats.
- **FR-004**: `ScenarioGuidanceInput` MUST remove `AverageTension` and `AverageConnection` properties. All construction sites for this record MUST be updated accordingly.

**Stat Text Band Catalog**

- **FR-005**: The system MUST provide a static catalog (`CharacterStatTextCatalog`) defining 4 text bands for each of 5 stats (Desire, Restraint, Loyalty, SelfRespect, Dominance) for each of 3 roles (Wife, Husband, OtherMan) — 15 entries total. Each band contains LLM-directive prose specific to the role and stat value range.
- **FR-006**: Band thresholds MUST follow the same tier resolution as `BehavioralDimensionCatalog`: value ≤20 → Band1, ≤50 → Band2, ≤75 → Band3, >75 → Band4.
- **FR-007**: The catalog MUST expose `ResolveText(statName, targetRole, value)` returning the band text for that combination, or null for unknown stat/role combinations.
- **FR-008**: The catalog MUST expose `IsNeutralBand(value)` returning true when 35 ≤ value ≤ 65. Stat state text MUST NOT be generated for stats in the neutral band.

**Encounter Dimension Drift**

- **FR-009**: `CharacterStatProfileV2` MUST include a `RuntimeEncounterStats` dictionary (string → int) representing per-character mutable encounter dimension values that evolve during play.
- **FR-010**: The system MUST provide a static `StatToDimensionMappings` catalog defining drift rules per role. Each rule maps a (stat, role) pair to a dimension name, slope, floor, and ceiling. The Wife and Husband roles MUST have rules defined; OtherMan has no rules.
- **FR-011**: After any canonical stat value change — whether from semantic scoring, manual UI assignment, or any other source — `StatToDimensionMappings.ApplyDelta()` MUST be called for the affected stat with the raw delta. The resulting dimension change MUST be `Clamp(current + Round(slope × statDelta), floor, ceiling)`.
- **FR-012**: A single stat delta MUST produce dimension changes no greater than ±5 per dimension per interaction (enforced via slope values in the mapping catalog, not a separate clamp layer).
- **FR-013**: `RuntimeEncounterStats` MUST be initialized from the character's bound `CharacterProfile.EncounterStats` on the first stat delta applied to a character in any session. If no profile is bound, BehavioralDimensionCatalog defaults (50 for all dimensions of the character's role) are used. When a different encounter profile is bound to a character (profile rebind mid-session or at session start), `RuntimeEncounterStats` MUST be reset to the new profile's `EncounterStats` values immediately; prior drift is discarded.

**Behavioral Frame Generation**

- **FR-014**: `IBehavioralFrameGenerator.GenerateFramesAsync` MUST accept an optional `IReadOnlyDictionary<string, CharacterStatProfileV2>` parameter for character runtime snapshots.
- **FR-015**: When a runtime snapshot exists for a character and its `RuntimeEncounterStats` is non-null and non-empty, `CharacterBehavioralFrameGenerator` MUST use `RuntimeEncounterStats` values for dimension tier resolution instead of the static `CharacterProfile.EncounterStats`. This applies from the very first continuation of a resumed session — `RuntimeEncounterStats` restored from `CharacterSnapshotsJson` MUST be used immediately without waiting for a stat delta.
- **FR-016**: When no runtime snapshot exists, or `RuntimeEncounterStats` is null or empty, the generator MUST fall back to the static `CharacterProfile.EncounterStats` values — preserving exact existing behavior for characters with no runtime state.

**Synthesized Stat State Text Injection**

- **FR-017**: `ScenarioGuidanceContext` MUST include a `CharacterStatStateTexts` dictionary (character label → synthesized sentence). This is an empty dictionary when no characters have any out-of-neutral stats.
- **FR-018**: `ScenarioGuidanceContextFactory` MUST build `CharacterStatStateTexts` from runtime snapshots: per character, collect all stats outside the neutral band, resolve their band text from `CharacterStatTextCatalog`, and combine them into a single synthesized characterization sentence.
- **FR-019**: The prompt injection MUST place the stat state text immediately after the behavioral frame constraint line for the same character, formatted as: `HARD CONSTRAINT — enforce in this response: {label} current state: {statStateText}`.
- **FR-020**: `RolePlayContinuationService` MUST pass the character runtime stat snapshots into `ScenarioGuidanceInput` so they reach the factory and frame generator.

**Persistence**

- **FR-021**: `RuntimeEncounterStats` MUST be persisted as part of `CharacterSnapshotsJson` in `RolePlayV2AdaptiveStates`. No new DB column is required.
- **FR-022**: `RuntimeEncounterStats` MUST survive session end and session reload. On session resume, the last-saved drifted dimension values MUST be restored and used immediately for frame generation.
- **FR-023**: Resetting a character's stats (explicit user "reset character" action) MUST also clear `RuntimeEncounterStats` so the next stat delta re-initializes from the bound profile.

**Logging**

- **FR-024**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-025**: Major execution paths (stat delta application, drift calculation, stat state text synthesis, frame generation with runtime stats) MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-026**: Log levels MUST be configurable via settings (including Verbose) without code changes. Drift calculations MUST be logged at Debug level.

---

### Key Entities

- **CharacterStatProfileV2**: Runtime per-character stat tracker. Gains `RuntimeEncounterStats` (mutable dimension dict). Loses `Tension` and `Connection` properties. Lives in `CharacterSnapshotsJson` (JSON column, no schema change).
- **CharacterStatTextCatalog**: Static code-defined catalog. 15 entries (5 stats × 3 roles), each with 4 band texts. Resolved by (statName, targetRole, value). New in Domain.StoryAnalysis.
- **StatToDimensionMappings**: Static code-defined catalog. Per-role drift rules mapping canonical stat deltas to encounter dimension adjustments. New in Domain.StoryAnalysis.
- **ScenarioGuidanceInput**: Application contract record. Loses `AverageTension`, `AverageConnection`. Gains `CharacterRuntimeStats` (optional, for passing snapshots to frame generator and factory).
- **ScenarioGuidanceContext**: Application contract record. Gains `CharacterStatStateTexts` (character label → synthesized sentence).
- **BehavioralDimensionCatalog** (existing): Unchanged in structure. Its tier resolution is now driven by `RuntimeEncounterStats` values rather than static profile values when runtime state is present.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After 10 interactions driving Wife Desire from 15 to 80+, the behavioral frame for that character changes from the bound Prude Wife profile tier text to a tier text consistent with high Exhibitionism and low DiscoveryCaution — without any manual profile edit by the user.
- **SC-002**: The synthesized stat state text for a Wife character with three out-of-neutral stats (e.g., Desire=82, Restraint=12, Loyalty=15) is a single coherent sentence of at most 60 words injected as one HARD CONSTRAINT line in the prompt.
- **SC-003**: The build produces zero errors and zero warnings after stat reduction. No references to `Tension` or `Connection` stat fields remain in the codebase after Phase 1.
- **SC-004**: All new unit tests pass covering stat text band resolution, encounter dimension drift, behavioral frame generation with runtime stats, and cheating formula simplification.
- **SC-005**: Session resume after drift: a character whose Exhibitionism drifted from 20 to 65 in a previous session has Exhibitionism=65 as the starting value in the resumed session (not reset to the bound profile's 20).
- **SC-006**: Stat state text is injected only when at least one stat is outside the 35–65 neutral band. Characters with all stats in the neutral range produce no additional constraint line beyond the behavioral frame.
- **SC-007**: OtherMan characters: Dominance and Desire stat state text injects when out-of-neutral, but no encounter dimension drift occurs for OtherMan regardless of stat changes.

---

## Assumptions

- B-042 is complete and deployed: `IBehavioralFrameGenerator`, `CharacterBehavioralFrameGenerator`, `ScenarioGuidanceContextFactory`, and the behavioral frame injection pipeline in `RolePlayContinuationService` and `RolePlayAssistantPrompts` are all in place and working.
- The character's bound encounter profile ID is available in the session adaptive state (via `CharacterEncounterProfileIds` on the session) at the time of the first stat delta — this is the source for `RuntimeEncounterStats` initialization.
- `RuntimeEncounterStats` restored from `CharacterSnapshotsJson` on session load is passed directly into `ScenarioGuidanceInput` and used by the frame generator on the first continuation without requiring a stat delta to occur first. The `RolePlayContinuationService` reads runtime snapshots from the loaded adaptive state regardless of whether any delta has fired in the current session.
- "Synthesized sentence" means the factory concatenates individual band texts with appropriate connective language (e.g., "; " separator or rephrasing). The exact synthesis approach (concatenation vs. template-built sentence) is an implementation detail left to the planning phase, provided the output reads as coherent prose.
- Semantic event scoring that drives Tension and Connection mutations will be removed alongside the stat fields. The keyword categories for those stats are also removed. Any semantic analysis model weights for Tension/Connection categories are considered out of scope for this feature — they can be cleaned up separately if they exist in model training data.
- The "reset character" action referenced in FR-023 is an existing user-facing control in the UI. If it does not yet exist, clearing `RuntimeEncounterStats` is handled by the same code path that resets canonical stats to baseline.

---

## Scope Boundaries

**Included**:
- Stat reduction (remove Tension, Connection from all layers)
- Cheating formula simplification (3-term)
- `CharacterStatTextCatalog` with 4 bands × 5 stats × 3 roles
- `StatToDimensionMappings` with Wife and Husband drift rules
- `RuntimeEncounterStats` on `CharacterStatProfileV2`
- Dynamic behavioral frame generation using `RuntimeEncounterStats`
- Synthesized stat state text injection per character per continuation
- Cross-session persistence of `RuntimeEncounterStats` in `CharacterSnapshotsJson`

**Excluded**:
- Profile ladder / archetype label auto-advance (B-022, separate backlog item, depends on this feature)
- UI visualization of stat state, transformation progress, or ladder position
- Agency formula changes (unchanged: `Dominance - Restraint + Desire/3`)
- Willingness tier gating changes
- Semantic analysis model retraining or weight removal for Tension/Connection categories
- OtherMan encounter dimension drift rules
- Any character creation or profile management UI changes

---

## Dependencies

- **Depends on**: B-042 (completed) — behavioral frame pipeline must be in place
- **B-022 depends on this**: Profile ladder feature is a planned follow-on
