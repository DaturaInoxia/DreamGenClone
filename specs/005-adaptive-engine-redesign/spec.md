# Feature Specification: Adaptive Engine Redesign

**Feature Branch**: `005-adaptive-engine-redesign`
**Created**: 2026-04-10
**Status**: Draft
**Input**: User description: "DreamGenClone Adaptive Engine Redesign — data-driven Theme Catalog, four profile layers (Tone, Style, Theme, Catalog), renamed stats, scenario seeding, profile-driven style resolution"

## Clarifications

### Session 2026-04-10

- Q: What StatAffinities should the 10 built-in Theme Catalog entries be seeded with? → A: Curated defaults for all 10 (see FR-002 table). Magnitudes 1–3 to avoid stat saturation from compounding. StatAffinities apply to acting character only.
- Q: How should existing ThemePreference Name values migrate to catalog-ID references? → A: Validate on load — if Name matches a catalog ID, treat as valid reference; if not, flag as "unlinked" in the UI for manual fix. No silent data loss. Startup validation logs orphans by profile ID and Name.
- Q: What format constraints should be enforced on user-provided Theme Catalog IDs? → A: Lowercase-hyphenated slug (`^[a-z0-9]+(-[a-z0-9]+)*$`), max 50 chars, unique across all entries (enabled and disabled). Auto-generated from Name as editable suggestion; locked after save. Validated at service layer, not just UI.
- Q: Should HardDealBreaker themes accumulate score then clamp, or be permanently blocked from scoring? → A: Permanent block — skip keyword scoring entirely. Add SuppressedHitCount to ThemeTrackerItem to track how many times keywords would have matched. Log at Debug level. Score stays at zero always; the invariant is never-written, not written-then-clamped.
- Q: Should StyleProfile.StatBias apply to all characters or only the acting/POV character? → A: All characters. StatBias is a global atmospheric property of the writing voice. Applied additively (not as floor or replacement) after BaseStatProfile defaults and per-character overrides. Application order: BaseStatProfile → per-character overrides → StatBias.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Data-Driven Theme Catalog Management (Priority: P1)

A content administrator opens the Theme Catalog tab in the profiles area. They see the 10 built-in themes (intimacy, trust-building, power-dynamics, etc.) listed with their keywords, weights, and stat affinities. They edit the keywords for "intimacy" to add "caress" and "embrace", increase its weight from 3 to 4, and set a stat affinity of `Desire: +3`. They save. On the next role-play interaction, the engine scores content against the updated keyword list and applies the new weight and stat affinities — without any application restart or recompile.

**Why this priority**: The Theme Catalog is the foundation every other component depends on. Theme Profiles reference catalog IDs, Style Profiles reference catalog IDs for affinities and escalation, and the Adaptive State Service scores content against catalog entries. Nothing else works correctly until the catalog is data-driven and persisted.

**Independent Test**: Can be fully tested by creating the ThemeCatalog database table, seeding 10 defaults, performing CRUD operations via the UI, and verifying the adaptive state service reads from the database instead of the hardcoded array.

**Acceptance Scenarios**:

1. **Given** the application starts with an empty ThemeCatalog table, **When** the startup pipeline runs, **Then** 10 built-in theme entries are seeded with their keywords, weights, IsBuiltIn=true, and IsEnabled=true.
2. **Given** the ThemeCatalog table already has entries, **When** the startup pipeline runs, **Then** no duplicate entries are inserted.
3. **Given** a built-in theme entry exists, **When** the user edits its Name, Keywords, Weight, or StatAffinities, **Then** the changes persist and take effect on the next interaction scoring cycle.
4. **Given** a built-in theme entry exists, **When** the user attempts to hard-delete it, **Then** the system prevents deletion and only allows disabling.
5. **Given** the user adds a new custom theme entry with a unique Id, **When** a role-play session scores its next interaction, **Then** the new theme appears in the tracker and is scored against content.
6. **Given** a theme entry is disabled, **When** the adaptive state service loads enabled themes, **Then** the disabled entry is excluded from scoring and pruned from any active tracker.
7. **Given** a theme entry was active in a session and is then disabled, **When** the session processes its next interaction, **Then** the entry is pruned from the tracker but historical scores remain in the session JSON.

---

### User Story 2 - Renamed Character Stats and ThemeProfile Rename (Priority: P1)

A user opens the Character State editor (previously labeled "Base Stats") in the profiles area. They see five stats labeled Desire, Restraint, Tension, Connection, and Dominance with updated descriptions. They navigate to the Theme Profile tab (previously "Ranking Profile") and configure their theme preferences. All labels, dropdowns, debug panels, scenario editors, and stored data reflect the new naming consistently.

**Why this priority**: The stat rename and ThemeProfile rename are foundational naming changes that cascade through domain models, persistence, services, UI, and AI prompts. Doing these first prevents confusion in all subsequent work.

**Independent Test**: Can be fully tested by verifying all UI labels, database columns, API fields, debug output, and AI prompt context lines reflect the new names. Legacy data loads correctly through migration mappings.

**Acceptance Scenarios**:

1. **Given** the application has sessions stored with old stat names (Arousal, Inhibition, Trust, Agency), **When** those sessions are loaded, **Then** legacy stat names are transparently mapped to the new names (Desire, Restraint, Connection, Dominance) via the extended legacy mapping.
2. **Given** the user opens any Character State editor, **When** they view stat labels, **Then** they see "Desire", "Restraint", "Tension", "Connection", "Dominance" with updated descriptions.
3. **Given** a feature previously labeled "Ranking Profile," **When** the user navigates to the profiles area, **Then** the tab, headings, dropdown labels, and all references read "Theme Profile."
4. **Given** the database has a RankingProfiles table with existing data, **When** the migration runs, **Then** data is copied to a ThemeProfiles table and becomes accessible under the new name.

---

### User Story 3 - Style Profile Extended Fields (Priority: P2)

A user opens the Style Profile editor and selects "Sultry." In addition to the existing Description and RuleOfThumb, they now see three new sections: Theme Affinities (showing which catalog themes this voice amplifies and by how much), Escalating Theme IDs (which themes push style intensity when this voice is active), and Stat Bias (the implied starting Character State adjustments for this voice). They see Sultry defaults to amplifying intimacy at 1.5×, voyeurism at 1.4×, and sets a starting Desire bias of +5 and Restraint bias of +5. They adjust the intimacy affinity to 1.8× and save.

**Why this priority**: Extended Style Profile fields connect the style voice into the adaptive engine. Without them, Theme Affinities, escalating theme control, and stat biasing remain hardcoded or missing.

**Independent Test**: Can be fully tested by editing style profiles via UI, verifying the three new JSON columns persist to the StyleProfiles table, and confirming the saved values load back correctly on the edit form.

**Acceptance Scenarios**:

1. **Given** the StyleProfiles table exists without the new columns, **When** the migration runs, **Then** three new columns (ThemeAffinitiesJson, EscalatingThemeIdsJson, StatBiasJson) are added with sensible defaults.
2. **Given** a user edits a Style Profile's Theme Affinities, **When** they select a catalog theme from the dropdown and set a multiplier, **Then** the affinity persists as a theme-catalog-ID-keyed entry.
3. **Given** a Style Profile has Escalating Theme IDs set, **When** the Style Resolver processes a session with that profile, **Then** it uses the profile's list instead of the hardcoded escalating theme list.
4. **Given** a Style Profile has a Stat Bias of `Desire: +5`, **When** a session is created with that profile, **Then** all characters' Desire stat starts at base + 5.
5. **Given** no Style Profile is selected for a session, **When** the Style Resolver processes escalation, **Then** it falls back to the legacy hardcoded escalating theme list.

---

### User Story 4 - Scenario Seeding at Session Creation (Priority: P2)

A user creates a new role-play session from a scenario that has a detailed plot description mentioning "secret meetings" and "forbidden desire," an opening paragraph rich with sensory language about closeness and tension, and characters described with emotional depth. The Theme Tracker initializes with pre-seeded scores: ChoiceSignal populated from the user's Theme Profile preferences, and ScenarioPhaseSignal populated from keyword analysis of the scenario's plot, settings, characters, openings, and examples — each weighted according to their signal richness (openings at 0.6×, plot at 0.4×, etc.). The Primary and Secondary themes are already meaningfully selected before the first user interaction.

**Why this priority**: Scenario seeding eliminates the cold-start problem where the first several interactions produce generic output because the Theme Tracker has no signal. This is the main engine intelligence upgrade.

**Independent Test**: Can be fully tested by creating a scenario with known keyword-dense content, starting a session, and inspecting the Theme Tracker state to verify pre-seeded scores match expected weights.

**Acceptance Scenarios**:

1. **Given** a scenario with Opening.Text containing intimacy-related keywords, **When** a session is created, **Then** the intimacy theme's ScenarioPhaseSignal is scored at 0.6× weight (the highest source weight).
2. **Given** a scenario with Plot.Description containing "forbidden" and "secret," **When** a session is created, **Then** the forbidden-risk theme's ScenarioPhaseSignal is scored at 0.4× weight.
3. **Given** a Theme Profile with `MustHave: intimacy`, **When** a session is created, **Then** the intimacy theme's ChoiceSignal is seeded at +15.
4. **Given** a Theme Profile with `HardDealBreaker: humiliation`, **When** a session is created, **Then** the humiliation theme's ChoiceSignal is forced to 0 and a blocked flag is set on the tracker item.
5. **Given** a Style Profile with `ThemeAffinities: { intimacy: 1.5 }`, **When** scenario text is scored, **Then** the intimacy theme's raw score is multiplied by 1.5.
6. **Given** a Theme Catalog entry has `StatAffinities: { Desire: +3 }`, **When** that theme scores during seeding, **Then** a +3 delta is applied to each character's Desire stat.
7. **Given** a scenario with Character.Description containing stat-relevant language, **When** a session is created, **Then** per-character stat deltas are applied at 0.3× weight from the character description.

---

### User Story 5 - Profile-Driven Style Resolution (Priority: P2)

During a role-play session, the Style Resolver computes the effective style intensity label using the Tone Profile base scale, average Desire delta, interaction progression, and theme-based adjustments. The escalating theme check now reads from the active Style Profile's EscalatingThemeIds instead of a hardcoded list. If the Primary theme matches a MustHave preference in the user's Theme Profile, it adds a +1 push toward intensity. If the Primary or Secondary theme is a HardDealBreaker, all escalation deltas for that signal line are suppressed.

**Why this priority**: This connects the profile system into the style resolution pipeline, making style adaptation responsive to user preferences and voice configuration.

**Independent Test**: Can be fully tested by passing different StyleProfile and ThemePreference combinations into the Style Resolver and verifying the output label and reason string reflect profile-driven logic.

**Acceptance Scenarios**:

1. **Given** a Style Profile with `EscalatingThemeIds: [intimacy, voyeurism]`, **When** the primary theme is "intimacy," **Then** the Style Resolver applies the +1 escalation push.
2. **Given** a Theme Profile with `MustHave: power-dynamics` and the primary theme is "power-dynamics," **When** the Style Resolver runs, **Then** an additional +1 push is applied before ceiling clamp.
3. **Given** a Theme Profile with `HardDealBreaker: humiliation` and the primary theme is "humiliation," **When** the Style Resolver runs, **Then** all escalation deltas are suppressed and the reason is tagged "dealbreaker-suppressed."
4. **Given** no Style Profile is set on the session, **When** the Style Resolver runs, **Then** it falls back to the hardcoded escalating theme list (dominance, power-dynamics, forbidden-risk, humiliation, infidelity).

---

### User Story 6 - Per-Interaction Affinity and StatAffinity Application (Priority: P3)

During each interaction in a role-play session, after keyword scoring for each theme, the adaptive state service multiplies the score delta by the active Style Profile's ThemeAffinities for that theme (if present). It also applies the ThemeCatalogEntry's StatAffinities as Character State deltas for the acting character. HardDealBreaker-blocked themes are permanently excluded from scoring — their keywords are checked only to increment a SuppressedHitCount for debug visibility, with the score remaining at zero.

**Why this priority**: This extends the per-interaction loop to leverage the new data-driven fields, completing the adaptive engine pipeline.

**Independent Test**: Can be fully tested by running interaction scoring with a known Style Profile affinity and verifying the multiplied scores and stat deltas.

**Acceptance Scenarios**:

1. **Given** a Style Profile with `ThemeAffinities: { intimacy: 1.5 }` and interaction content matching intimacy keywords, **When** the interaction is scored, **Then** the intimacy theme delta is 1.5× what it would be without the affinity.
2. **Given** a Theme Catalog entry with `StatAffinities: { Desire: +3 }` and the entry scores in an interaction, **When** stat deltas are applied, **Then** the acting character's Desire stat receives a +3 delta.
3. **Given** a Theme Profile with `HardDealBreaker: humiliation`, **When** interaction content matches humiliation keywords, **Then** the humiliation theme score remains at zero and SuppressedHitCount is incremented.

---

### User Story 7 - Theme Preference Catalog Dropdown and UI Integration (Priority: P3)

A user opens the Theme Profile editor and goes to add a new Theme Preference. Instead of a free-text Name field, they see a dropdown populated from all enabled Theme Catalog entries. They select "voyeurism" from the dropdown, set the tier to "StronglyPrefer," and save. This prevents silent dead-reference preferences when catalog entries are renamed or removed. The Style Profile editor also shows catalog-sourced dropdowns for Theme Affinities and Escalating Theme multi-select.

**Why this priority**: UI integration ensures the data-driven catalog is properly surfaced to users and prevents configuration errors from free-text mismatches.

**Independent Test**: Can be fully tested by verifying dropdowns in Theme Preference, Style Profile Theme Affinities, and Escalating Themes are all populated from the enabled ThemeCatalog entries.

**Acceptance Scenarios**:

1. **Given** the ThemeCatalog has 10 enabled entries, **When** the user opens the Theme Preference name dropdown, **Then** exactly 10 entries appear matching the enabled catalog.
2. **Given** a catalog entry is disabled, **When** the user opens the Theme Preference name dropdown, **Then** the disabled entry does not appear.
3. **Given** the Style Profile editor's Theme Affinities section, **When** the user clicks to add an affinity, **Then** a dropdown of enabled catalog entries appears for selection.
4. **Given** the Style Profile editor's Escalating Themes section, **When** the user views the multi-select, **Then** checkboxes for all enabled catalog entries are shown.

---

### User Story 8 - AI Assistant Prompt Updates (Priority: P3)

When the AI assistant generates role-play content, its system prompt includes updated references to Theme Profiles (not "Ranking Profile"), Character State stats (Desire, Restraint, Tension, Connection, Dominance), the Theme Catalog concept, the Theme Tracker with its seeded signals, and Style Profile affinities. The scenario assistant prompts include guidance about how Openings/Examples are scored at 0.6× weight and how character descriptions seed per-character state.

**Why this priority**: Prompt updates ensure the AI generates content that is consistent with the redesigned engine's mental model and terminology.

**Independent Test**: Can be fully tested by inspecting the generated system prompt strings for updated terminology and new context sections.

**Acceptance Scenarios**:

1. **Given** a role-play session with a Theme Profile selected, **When** the assistant prompt is assembled, **Then** the context line reads `[Adaptive Profiles: theme=...]` instead of `ranking=...`.
2. **Given** the updated RolePlayAssistantPrompts, **When** the EDITABLE FIELDS REFERENCE is rendered, **Then** it includes sections for Theme Catalog, Character State (five stats), and Theme Tracker.
3. **Given** the updated ScenarioAssistantPrompts, **When** guidance is rendered, **Then** it includes notes about Opening/Example 0.6× scoring weight and character description stat seeding.

---

### Edge Cases

- What happens when a Theme Catalog entry referenced by an existing ThemePreference is disabled? The engine prunes it from active scoring; the preference record remains but has no effect until the entry is re-enabled.
- What happens when an existing ThemePreference Name doesn't match any catalog ID (e.g. typo like "power dynamics" without the hyphen)? The preference is flagged as "unlinked" in the UI with a warning indicator. The original Name is shown as a hint so the user knows what was intended. They can re-link it via dropdown or delete it. A startup log entry lists all unlinked preferences by profile ID and Name.
- What happens when a Style Profile references a ThemeAffinity key that no longer exists in the catalog? The affinity is silently ignored during scoring (no error); orphaned keys can be cleaned up in the UI.
- What happens when a session was created with a catalog entry that is later deleted (user-added)? The tracker item is pruned at the next interaction; historical scores remain in session JSON.
- What happens when all themes are disabled in the catalog? The tracker has no entries to score. Primary and Secondary themes are null. The Style Resolver skips theme-based adjustments and uses only Tone base scale + Desire delta.
- What happens when a Theme Profile has more than one HardDealBreaker and both score in content? Both are permanently blocked from scoring. SuppressedHitCount increments independently for each. Escalation suppression applies if either would have been Primary or Secondary.
- What happens when a HardDealBreaker theme's SuppressedHitCount is climbing rapidly? The debug panel surfaces this count with a lock icon, signaling to the user that scenario content keeps generating material in that direction. The user can edit the scenario or switch the theme from HardDealBreaker to Dislike if soft suppression is preferable.
- What happens when the legacy stat name "Shame" appears in stored session data? The existing legacy mapping `Shame→Restraint` (already present) transparently converts it.
- What happens when Restraint scoring direction changes? Current Inhibition was scored as a negative delta. Restraint is positively framed — guilt/hesitation keywords now produce a positive delta, semantically consistent with "holding back increases."

## Requirements *(mandatory)*

### Functional Requirements

#### Theme Catalog

- **FR-001**: System MUST persist Theme Catalog entries in a SQLite table with fields: Id (immutable primary key), Name, Keywords (JSON array), Weight (integer), StatAffinities (JSON map), IsBuiltIn (boolean), IsEnabled (boolean), CreatedUtc, UpdatedUtc.
- **FR-002**: System MUST seed 10 built-in Theme Catalog entries on first startup when the table is empty, matching the current hardcoded themes with the following StatAffinities:

  | Theme | StatAffinities | Rationale |
  |---|---|---|
  | intimacy | Desire +2, Connection +2 | Physical/emotional closeness raises both wanting and bond |
  | trust-building | Connection +3, Restraint -2 | Reassurance builds bond and lowers the guard |
  | power-dynamics | Dominance +2, Tension +1 | Assertion of control raises power gradient and ambient pressure |
  | jealousy-triangle | Tension +3, Connection -1 | Rivalry creates pressure and erodes sense of safety |
  | forbidden-risk | Tension +2, Restraint +2, Desire +1 | Risk raises pressure and braking force simultaneously; forbidden quality heightens wanting |
  | confession | Connection +3, Restraint -2, Tension -1 | Revealing truth deepens bond, drops the guard, releases pressure |
  | voyeurism | Desire +2, Restraint +2 | Observing from distance heightens wanting while held-back quality keeps restraint elevated |
  | infidelity | Tension +3, Connection -2 | Betrayal creates high pressure and damages the bond |
  | humiliation | Restraint +3, Connection -2, Dominance -2 | Shame spikes braking force, severs connection, suppresses power gradient |
  | dominance | Dominance +3, Tension +1, Connection -1 | Direct exertion of control increases power gradient, creates tension, distances emotional bond |
- **FR-003**: System MUST allow editing of Name, Keywords, Weight, and StatAffinities for all Theme Catalog entries (both built-in and user-added).
- **FR-004**: System MUST prevent hard-deletion of built-in entries; only disable (IsEnabled=false) is allowed.
- **FR-005**: System MUST allow hard-deletion of user-added entries (IsBuiltIn=false).
- **FR-006**: System MUST prevent modification of the Id field after entry creation. Id MUST conform to the pattern `^[a-z0-9]+(-[a-z0-9]+)*$`, max 50 characters, and MUST be unique across all entries (both enabled and disabled, since disabled entries still hold live foreign keys in serialized session JSON). The UI MUST auto-generate an Id suggestion from the Name field (e.g., "My Custom Theme" → `my-custom-theme`) shown in an editable Id field; the user may edit before first save, after which the Id is locked. Validation MUST be enforced at the service layer (not UI only) and MUST reject invalid input with a validation error rather than silently transforming it.
- **FR-007**: System MUST load only enabled (IsEnabled=true) entries for runtime scoring via a dedicated query method.

#### Character State Rename

- **FR-008**: System MUST rename canonical stats: Arousal→Desire, Inhibition→Restraint, Trust→Connection, Agency→Dominance (Tension remains unchanged).
- **FR-009**: System MUST extend legacy-to-canonical stat mapping to include: Arousal→Desire, Inhibition→Restraint, Trust→Connection, Agency→Dominance alongside existing Shame→Restraint, Jealousy→Tension, DominanceDrive→Dominance, RiskAppetite→Dominance.
- **FR-010**: System MUST update Restraint scoring direction so that guilt/hesitation keywords produce a positive delta (increasing Restraint), replacing the previous negative-delta Inhibition scoring.

#### ThemeProfile Rename

- **FR-011**: System MUST rename the RankingProfile domain class, interface, service, and database table to ThemeProfile equivalents.
- **FR-012**: System MUST rename SelectedRankingProfileId to SelectedThemeProfileId in session and scenario models.
- **FR-013**: System MUST migrate existing RankingProfiles table data to a ThemeProfiles table.

#### Style Profile Extension

- **FR-014**: System MUST add ThemeAffinities (map of catalog theme ID → score multiplier), EscalatingThemeIds (list of catalog theme IDs), and StatBias (map of stat name → integer delta) to the Style Profile domain model.
- **FR-015**: System MUST persist the three new Style Profile fields as JSON columns in the StyleProfiles table.
- **FR-016**: System MUST populate built-in Style Profile seed data with appropriate defaults (e.g., Sultry: intimacy affinity 1.5×, Desire bias +5, Restraint bias +5).

#### Scenario Seeding

- **FR-017**: System MUST implement a SeedFromScenarioAsync method that pre-loads the Theme Tracker with ScenarioPhaseSignal scores derived from scenario text at session creation.
- **FR-018**: System MUST score scenario fields at differentiated weights: Opening/Example text at 0.6×, all other fields (Plot, Setting, Style, Characters, Locations, Objects) at 0.4×, and character stat deltas at 0.3×.
- **FR-019**: System MUST apply ThemeProfile tier boosts to ChoiceSignal at session creation: MustHave +15, StronglyPrefer +8, NiceToHave +3, Dislike -5, HardDealBreaker force-zero with blocked flag. A blocked theme MUST be permanently excluded from keyword scoring for the lifetime of the session — its score is never written, only a SuppressedHitCount is incremented when keywords would have matched.
- **FR-020**: System MUST apply StyleProfile.StatBias to all character Character State blocks at session creation. StatBias is additive, not a floor or replacement. The application order MUST be: (1) BaseStatProfile defaults, (2) per-character BaseStats overrides, (3) StyleProfile.StatBias applied additively on top of whatever values already exist. This means per-character adjustments are preserved and the voice's atmospheric uplift layers on top.
- **FR-021**: System MUST apply ThemeCatalogEntry.StatAffinities as Character State deltas when themes score during seeding.
- **FR-022**: System MUST multiply theme scores by StyleProfile.ThemeAffinities during seeding when present.

#### Style Resolution

- **FR-023**: System MUST use StyleProfile.EscalatingThemeIds for escalation checking when a Style Profile is active, falling back to the legacy hardcoded list when no profile is set.
- **FR-024**: System MUST add a +1 push when the Primary theme matches a MustHave preference in the active Theme Profile.
- **FR-025**: System MUST suppress all escalation deltas when the Primary or Secondary theme is a HardDealBreaker, tagging the reason as "dealbreaker-suppressed."

#### Per-Interaction Scoring

- **FR-026**: System MUST multiply per-interaction theme score deltas by StyleProfile.ThemeAffinities when present.
- **FR-027**: System MUST apply ThemeCatalogEntry.StatAffinities as Character State deltas for the acting character during each interaction.
- **FR-028**: System MUST permanently skip keyword scoring for HardDealBreaker-blocked themes during per-interaction scoring. When a blocked theme's keywords would have matched, the system MUST increment a SuppressedHitCount field on the ThemeTrackerItem and log at Debug level. The theme's score MUST remain at zero at all times — the invariant is "never written," not "written then clamped." The debug panel MUST surface SuppressedHitCount with a visual indicator (e.g., lock icon) so users can see that content is touching a blocked theme without it influencing engine output.

#### Engine and Service Wiring

- **FR-029**: System MUST inject IThemeCatalogService into the Adaptive State Service and remove the hardcoded ThemeRule array.
- **FR-030**: System MUST call SeedFromScenarioAsync during CreateSessionAsync when a scenario is present.
- **FR-031**: System MUST thread StyleProfile and ThemePreference data to all UpdateFromInteractionAsync and ResolveEffectiveStyle call sites.

#### UI

- **FR-032**: System MUST rename the RankingProfiles page to ThemeProfiles, updating all routes, labels, headings, and navigation links.
- **FR-033**: System MUST provide a Theme Catalog management tab with table view, inline editing, add/disable/delete controls, and dropdown-coherent field selections.
- **FR-034**: System MUST replace the free-text ThemePreference Name field with a dropdown populated from enabled Theme Catalog entries. Existing ThemePreference records whose Name matches a catalog ID are treated as valid references. Records whose Name does not match any catalog ID are displayed as "unlinked" with a warning indicator, the original Name shown as a hint, and a dropdown to re-link or a delete option. No records are silently dropped during migration.
- **FR-034a**: System MUST run a ThemePreference validation pass at startup and log (Information level) any unlinked preferences listing the profile ID and orphaned Name value, providing visibility without requiring the user to navigate to each profile.
- **FR-035**: System MUST add Theme Affinities, Escalating Themes, and Stat Bias editing controls to the Style Profile editor.
- **FR-036**: System MUST rename all "Base Stats" labels to "Character State" and update all stat labels to the new names across all UI surfaces.

#### AI Prompts

- **FR-037**: System MUST update RolePlayAssistantPrompts to reference Theme Profile, Character State, Theme Catalog, and Theme Tracker with correct terminology.
- **FR-038**: System MUST update ScenarioAssistantPrompts with guidance on Opening/Example scoring weight, Style Profile affinities, and character description stat seeding.
- **FR-039**: System MUST update the assistant context line from `ranking=...` to `theme=...`.

#### Persistence and Logging

- **FR-040**: Persisted feature data MUST use SQLite.
- **FR-041**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-042**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-043**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities

- **ThemeCatalogEntry**: A detectable content pattern in the catalog. Key attributes: Id (immutable string key), Name, Keywords list, Weight multiplier, StatAffinities map, IsBuiltIn flag, IsEnabled toggle. Referenced by ThemePreferences, StyleProfile.ThemeAffinities, and StyleProfile.EscalatingThemeIds.
- **ThemeProfile** (renamed from RankingProfile): A persistent user preference set containing ThemePreferences that survive across sessions. Key attributes: Id, Name, IsDefault, list of ThemePreferences.
- **ThemePreference**: An individual preference record inside a ThemeProfile. References a ThemeCatalogEntry by Id and assigns a tier (MustHave through HardDealBreaker).
- **StyleProfile** (extended): Writing voice configuration. Existing fields plus ThemeAffinities (catalog ID → multiplier), EscalatingThemeIds (catalog IDs), StatBias (stat name → delta).
- **ToneProfile**: Content amplitude ceiling (0–5 integer scale). Unchanged structurally.
- **BaseStatProfile**: Reusable template of default Character State starting values. Unchanged structurally.
- **Character State** (renamed from Base Stats): Five per-character evolving stats — Desire, Restraint, Tension, Connection, Dominance.
- **ThemeTracker**: Runtime signal accumulator per session. Per-theme score broken into ChoiceSignal, ScenarioPhaseSignal, InteractionEvidenceSignal, CharacterStateSignal. Produces Primary and Secondary theme selections. Each ThemeTrackerItem also carries a Blocked flag (set by HardDealBreaker) and a SuppressedHitCount (incremented when keywords match a blocked theme, providing actionable visibility without influencing engine output).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can add, edit, disable, and re-enable Theme Catalog entries entirely through the UI, with changes effective on the next interaction — no application restart required.
- **SC-002**: A session created from a keyword-rich scenario has non-zero Primary and Secondary themes in the Theme Tracker before the first user interaction.
- **SC-003**: A Theme Profile's MustHave preference produces a measurably higher ChoiceSignal seed (+15) than a StronglyPrefer preference (+8) for the same theme.
- **SC-004**: A HardDealBreaker preference results in a theme score of zero and blocked flag set, with escalation suppressed, regardless of how strongly the content matches that theme's keywords.
- **SC-005**: A Style Profile's ThemeAffinity of 1.5× produces 1.5× the theme score compared to the same content scored with no affinity (1.0×).
- **SC-006**: All UI labels, debug panels, prompts, and stored data consistently use the new terminology (Theme Profile, Character State, Desire/Restraint/Connection/Dominance) with zero instances of legacy naming in user-visible surfaces.
- **SC-007**: Legacy sessions stored with old stat names (Arousal, Inhibition, Trust, Agency) load correctly and display using new names through transparent mapping.
- **SC-008**: Style Profile's EscalatingThemeIds override the hardcoded escalation list when present, and the system falls back to the hardcoded list when no profile is set — both paths covered by automated tests.
- **SC-009**: Opening/Example text seeding produces measurably higher ScenarioPhaseSignal than Plot/Setting text for the same keyword density, reflecting the 0.6× vs 0.4× weight differentiation.
- **SC-010**: All new and modified behavior is covered by automated tests, including: catalog CRUD, scenario seeding, affinity multiplication, style resolver profile-driven paths, and HardDealBreaker enforcement.

## Assumptions

- The existing 10 hardcoded themes (intimacy, trust-building, power-dynamics, jealousy-triangle, forbidden-risk, confession, voyeurism, infidelity, humiliation, dominance) become the built-in seed data for the Theme Catalog with their current keywords and weights.
- The ThemePreference tier values (MustHave, StronglyPrefer, NiceToHave, Neutral, Dislike, HardDealBreaker) and their ChoiceSignal seed values (+15, +8, +3, 0, -5, force-zero) are fixed and not user-configurable.
- Scenario seeding field weights (0.6× for openings/examples, 0.4× for other fields, 0.3× for character stat deltas) are fixed at the values specified and not user-configurable.
- StyleProfile.ThemeAffinities multipliers are constrained to a reasonable range (0.5× to 2.0×) in the UI, though the engine accepts any positive value.
- StatBias values are constrained to -20 to +20 in the UI, matching reasonable Character State adjustment ranges.
- The CharacterStateSignal breakdown field in the Theme Tracker is reserved for future stat-to-theme feedback and is not populated in this redesign.
- The Restraint scoring direction change (from negative delta to positive delta) is a semantic alignment — the same keywords (can't, wrong, shouldn't, hesitate, guilt) now increase Restraint rather than decrease Inhibition, which is logically equivalent under the new framing.
- The PairwiseStats structure in RolePlayAdaptiveState is not affected by this redesign.
- The BaseStatProfile entity is structurally unchanged; it just has its stat names updated.
- Existing ScenarioGoals (Plot.Goals) remain separate from ThemePreferences and continue to function as narrative objectives injected into prompts.
- StatAffinities apply to the acting character only. Target character stats are not modified by theme scoring — that would require a future pairwise stat affinity system.
- Seeded StatAffinity magnitudes are intentionally modest (1–3) because themes score multiple times across a session and deltas compound. Starting too high causes stats to saturate quickly.
- power-dynamics and dominance both push Dominance+ by design. power-dynamics represents the dynamic between characters (push-pull of control), while dominance is explicit assertion. Some keyword overlap is expected; keyword lists should be tightened during implementation to reduce redundancy.
- Existing ThemePreference Name values are lowercase slugs that match current hardcoded catalog IDs exactly. The validate-on-load approach (not a schema migration) is sufficient. A separate CatalogId column is unnecessary because Name is being replaced entirely by a catalog reference — no need to keep a deprecated free-text field alive.
- StatBias application order at session creation is strictly: BaseStatProfile defaults → per-character BaseStats overrides → StyleProfile.StatBias (additive). A character with Connection=20 from a specific override preserves that value; StatBias only adds its delta on top. StatBias is atmospheric (global to the voice), not per-character-targeted.

## Scope Boundaries

### In Scope

- ThemeCatalog domain model, persistence, service, seeding, CRUD, and UI management tab
- Character State stat rename (Arousal→Desire, Inhibition→Restraint, Trust→Connection, Agency→Dominance) with legacy mapping
- RankingProfile→ThemeProfile rename across domain, persistence, services, sessions, scenarios, and UI
- StyleProfile extension with ThemeAffinities, EscalatingThemeIds, StatBias — domain, persistence, seed data, UI
- SeedFromScenarioAsync implementation with differentiated field weights
- Per-interaction affinity multiplication and StatAffinity application
- Style Resolver profile-driven escalation with MustHave push and HardDealBreaker suppression
- Engine service wiring to thread profiles through all call sites
- Continuation service rename updates
- AI assistant prompt terminology and guidance updates
- UI rename, Theme Catalog tab, Style Profile extended editor, dropdown-coherent Theme Preferences
- Comprehensive automated test coverage

### Out of Scope

- CharacterStateSignal (stat-to-theme feedback loop) — reserved for a future iteration
- Theme Catalog import/export functionality
- Theme Catalog versioning or change history
- User-facing analytics or reporting on theme scores
- Multi-user or role-based access control for catalog editing
- Any changes to the PairwiseStats structure
- Any changes to the ToneProfile or BaseStatProfile domain models (beyond label renaming)
- Performance optimization or caching strategies beyond the existing request-scoped pattern
