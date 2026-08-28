# Feature Specification: B-042 — Unify Character Stats Profiles with Encounter Behavior Profiles

**Feature Branch**: `development`  
**Created**: 2026-05-27  
**Status**: Draft  
**Backlog**: B-042

---

## Background & Context

Two separate profile systems currently exist that both describe the same character archetypes from different angles:

1. **`BaseStatProfile`** — a character preset carrying the 7 canonical stats (Desire, Restraint, Tension, Connection, Dominance, Loyalty, SelfRespect). Used to seed character starting stats at session creation. Filtered by TargetRole and TargetGender.

2. **`HusbandAwarenessProfile`** — a husband-only preset carrying encounter-behavioral dimensions (Awareness, Acceptance, Voyeurism, Participation, Encouragement, RiskTolerance). The stats are translated at prompt-build time into a "Partner/husband behavioral frame" text block that is injected twice as a HARD CONSTRAINT into the continuation prompt.

These two systems describe the same archetypes but store them separately. "Cuckold Husband" as a character stat archetype implicitly carries the same behavioral picture as the "Curious Observer" awareness profile. The split forces users to maintain two profiles per character, creates confusion about which profile drives what behavior, and prevents wife/otherman roles from ever having behavioral framing.

Additionally the existing awareness profiles have their Notes fields populated with the stat-generated text (manually copied) plus custom additions, because the current logic uses Notes as a full override that bypasses all stat computation. This means stat changes don't automatically update the prompt.

---

## Clarifications

### Session 2026-05-27

- Q: When a user applies a profile in session creation, should stat seeding and encounter profile binding be atomically coupled or independently decoupled? → A: Atomically coupled — single profile picker; Apply seeds the character's canonical stats AND sets the encounter profile binding in one action; no way to have different profiles for stats vs behavior after applying.
- Q: How should the live preview panel display the behavioral frame? → A: Two panels — top panel shows a labeled list (each dimension name + its resolved tier sentence, for editing clarity); bottom panel shows the exact concatenated paragraph that will be injected into the prompt.
- Q: When a user changes a character's encounter profile mid-session in the workspace adaptive panel, when should the change be persisted to DB? → A: Lazy persist — applied in-memory immediately (next continuation uses the new frame) and written to DB as part of the normal session state save that occurs during continuation; no explicit save call triggered.
- Q: Should profile names be enforced as unique per TargetRole? → A: Yes — unique per role, case-insensitive; saving a profile with a name that already exists for the same TargetRole is blocked with a validation error.
- Q: Should the migration automatically merge overlapping BaseStatProfile and HusbandAwarenessProfile entries that describe similar archetypes? → A: No — keep separate; migrated profiles are retained as distinct rows regardless of name similarity; the 25 new seeded archetypes replace both old sets, and users may manually delete redundant migrated entries.

---

## Design Decisions (confirmed in design discussion)

1. **One profile entity for all character traits** — `BaseStatProfile` and `HusbandAwarenessProfile` are retired and replaced by a single unified `CharacterProfile` entity that holds both canonical stats AND encounter behavioral dimensions, categorized by type.

2. **Per-role behavioral dimensions** — Husband, Wife, and OtherMan each have their own set of encounter behavioral dimensions with role-appropriate names and tier descriptions. Dimensions are defined in a code-side `BehavioralDimensionCatalog` (not a DB table) so tier descriptions can be maintained alongside code.

3. **Stats drive text, Notes are additive only** — the behavioral dimensions always generate their tier text. An optional `AdditionalNotes` field appends to the generated text instead of replacing it. A `FullOverride` flag (for edge cases like Swinger Fantasy) allows Notes to be the complete behavioral frame instead.

4. **Live UI preview** — the profile CRUD UI shows a two-panel live preview updating in real-time as dimension sliders are adjusted: an upper labeled-list panel (each dimension name + resolved tier sentence, for editing clarity) and a lower exact-text panel showing the verbatim paragraph that will be injected into the prompt.

5. **Encounter profile per character at session creation** — the session stores a `Dictionary<characterId, profileId>` so each character in the session can have its own encounter behavioral profile. Replaces the single `HusbandAwarenessProfileId` on `AdaptiveScenarioState`.

6. **Unified seeded archetypes** — all existing separate archetypes from both systems are merged into a single set of named combined profiles. Existing standalone awareness profiles are retired after their dimensions are absorbed.

7. **All UI surfaces updated in one implementation** — Profile CRUD, scenario editor, theme profiles page, RP session creation, RP workspace adaptive panel, and all prompt injection points are all updated together to avoid partial states.

---

## Behavioral Dimensions by Role

### Husband (replacing current awareness stats, `HumiliationDesire` removed as dead code)

| Dimension | 0 end | 100 end |
|---|---|---|
| **Awareness** | Completely oblivious | Fully aware and present |
| **Acceptance** | Would react with anger | Fully at ease |
| **Voyeurism** | No desire to watch | Actively positions to watch, will not interrupt |
| **Participation** | Will not participate | Co-primary participant |
| **Encouragement** | No sign of approval | Openly encourages and facilitates |
| **RiskTolerance** | Shuts down at any exposure risk | Comfortable with significant exposure |

### Wife (new — encounter-specific, no overlap with canonical 7 stats)

| Dimension | 0 end | 100 end |
|---|---|---|
| **DiscoveryCaution** | Reckless — makes no effort to hide the encounter | Highly cautious — actively manages every detail to avoid discovery |
| **Exhibitionism** | Deeply private — distressed if seen or heard | Actively enjoys being seen and heard during the encounter |
| **EmotionalEngagement** | Purely transactional — zero emotional connection to the other man | Developing genuine emotional feelings for the other man |
| **PostEncounterGuilt** | No guilt display — behaves completely normally with husband after | Overwhelmed — visibly guilty, over-compensating, emotionally withdrawn |

### OtherMan (new — encounter-specific, no overlap with canonical 7 stats)

| Dimension | 0 end | 100 end |
|---|---|---|
| **HusbandAwareness** | Doesn't know the husband exists — treats the encounter as uncomplicated | Fully aware of the husband and uses that knowledge explicitly in his approach |
| **MarriageContextUse** | Never references the marriage or husband | Actively exploits the married context as part of the seduction |
| **DiscoveryRisk** | No concern about being discovered — reckless | Highly careful — actively manages risk of discovery throughout |
| **PersistencePastLimits** | Respects every stated or implied limit immediately | Persistently pushes past resistance and stated limits |

---

## Tier Description Structure (per dimension)

Each dimension has 4 tiers (thresholds: ≤20, ≤50, ≤75, >75) — same structure as the existing husband awareness code but data-driven from `BehavioralDimensionCatalog` rather than hardcoded switch statements.

Tier text is written as a directive to the LLM. Example for Wife `DiscoveryCaution`:
- Tier 1 (0–20): "She is making no effort to conceal this encounter — she may be loud, unconcerned about being heard, and take no precautions."
- Tier 2 (21–50): "She is mildly cautious but not actively managing discovery risk."
- Tier 3 (51–75): "She is careful — she keeps noise down, is aware of time, and would quickly adjust if risk increased."
- Tier 4 (76–100): "She is highly vigilant — she is managing every sensory detail, checking for sounds, and would stop immediately at any sign of detection."

---

## Unified Seeded Archetypes

### Husband Profiles (unified)

| Archetype Name | Canonical Stats | Encounter Dims |
|---|---|---|
| Oblivious / Inattentive Husband | Desire=35, Restraint=65, Tension=20, Connection=25, Dominance=55, Loyalty=50, SelfRespect=60 | Awareness=10, Acceptance=15, Voyeurism=5, Participation=0, Encouragement=5, RiskTolerance=10 |
| Suspicious Husband | Desire=30, Restraint=55, Tension=80, Connection=40, Dominance=55, Loyalty=60, SelfRespect=50 | Awareness=45, Acceptance=20, Voyeurism=25, Participation=0, Encouragement=5, RiskTolerance=20 |
| Caring / Supportive Husband | Desire=50, Restraint=60, Tension=25, Connection=90, Dominance=45, Loyalty=95, SelfRespect=80 | Awareness=50, Acceptance=65, Voyeurism=30, Participation=20, Encouragement=55, RiskTolerance=35 |
| Cuckold Husband | Desire=85, Restraint=40, Tension=50, Connection=60, Dominance=20, Loyalty=80, SelfRespect=40 | Awareness=85, Acceptance=70, Voyeurism=80, Participation=20, Encouragement=45, RiskTolerance=40 |
| Fantasy-Driven / Hotwife Husband | Desire=80, Restraint=35, Tension=40, Connection=65, Dominance=55, Loyalty=65, SelfRespect=50 | Awareness=95, Acceptance=90, Voyeurism=85, Participation=70, Encouragement=80, RiskTolerance=65 |
| Swinger — Full Participant | Desire=90, Restraint=25, Tension=20, Connection=70, Dominance=60, Loyalty=75, SelfRespect=85 | Awareness=100, Acceptance=100, Voyeurism=10, Participation=100, Encouragement=90, RiskTolerance=75 |
| Controlling Husband | Desire=45, Restraint=50, Tension=40, Connection=50, Dominance=90, Loyalty=70, SelfRespect=70 | Awareness=60, Acceptance=25, Voyeurism=20, Participation=15, Encouragement=10, RiskTolerance=20 |
| Shocked / Confused Husband | Desire=55, Restraint=65, Tension=85, Connection=35, Dominance=30, Loyalty=55, SelfRespect=40 | Awareness=70, Acceptance=15, Voyeurism=40, Participation=5, Encouragement=5, RiskTolerance=15 |

### Wife Profiles (unified — existing canonical stats + new encounter dims)

| Archetype Name | Canonical Stats (unchanged) | Encounter Dims |
|---|---|---|
| Loyal Good Wife | Desire=40, Restraint=85, Tension=30, Connection=90, Dominance=45, Loyalty=95, SelfRespect=80 | DiscoveryCaution=90, Exhibitionism=5, EmotionalEngagement=75, PostEncounterGuilt=95 |
| Prude Wife | Desire=15, Restraint=95, Tension=45, Connection=85, Dominance=60, Loyalty=95, SelfRespect=85 | DiscoveryCaution=95, Exhibitionism=0, EmotionalEngagement=60, PostEncounterGuilt=100 |
| Shy / Reserved Wife | Desire=35, Restraint=90, Tension=70, Connection=75, Dominance=30, Loyalty=85, SelfRespect=65 | DiscoveryCaution=85, Exhibitionism=5, EmotionalEngagement=70, PostEncounterGuilt=85 |
| Curious / Exploring Wife | Desire=50, Restraint=55, Tension=50, Connection=65, Dominance=40, Loyalty=65, SelfRespect=60 | DiscoveryCaution=65, Exhibitionism=20, EmotionalEngagement=60, PostEncounterGuilt=60 |
| Cheating Wife | Desire=60, Restraint=70, Tension=65, Connection=30, Dominance=50, Loyalty=25, SelfRespect=55 | DiscoveryCaution=75, Exhibitionism=35, EmotionalEngagement=45, PostEncounterGuilt=40 |
| Neglected Wife | Desire=80, Restraint=60, Tension=55, Connection=25, Dominance=40, Loyalty=50, SelfRespect=45 | DiscoveryCaution=50, Exhibitionism=45, EmotionalEngagement=80, PostEncounterGuilt=50 |
| Empowered / Confident Wife | Desire=65, Restraint=40, Tension=30, Connection=70, Dominance=70, Loyalty=70, SelfRespect=90 | DiscoveryCaution=40, Exhibitionism=60, EmotionalEngagement=40, PostEncounterGuilt=20 |
| Slut Wife / Hotwife | Desire=70, Restraint=20, Tension=35, Connection=70, Dominance=65, Loyalty=80, SelfRespect=75 | DiscoveryCaution=15, Exhibitionism=85, EmotionalEngagement=25, PostEncounterGuilt=5 |
| Nymphomaniac Wife | Desire=85, Restraint=5, Tension=20, Connection=30, Dominance=40, Loyalty=15, SelfRespect=25 | DiscoveryCaution=5, Exhibitionism=95, EmotionalEngagement=10, PostEncounterGuilt=0 |

### OtherMan Profiles (unified — existing canonical stats + new encounter dims)

| Archetype Name | Canonical Stats (unchanged) | Encounter Dims |
|---|---|---|
| The Nice Guy | Desire=50, Restraint=75, Tension=40, Connection=80, Dominance=30, Loyalty=75, SelfRespect=60 | HusbandAwareness=75, MarriageContextUse=10, DiscoveryRisk=80, PersistencePastLimits=10 |
| The Nerd | Desire=70, Restraint=80, Tension=60, Connection=50, Dominance=20, Loyalty=85, SelfRespect=40 | HusbandAwareness=70, MarriageContextUse=15, DiscoveryRisk=75, PersistencePastLimits=15 |
| The Young Eager Guy | Desire=85, Restraint=40, Tension=50, Connection=60, Dominance=45, Loyalty=40, SelfRespect=55 | HusbandAwareness=30, MarriageContextUse=20, DiscoveryRisk=35, PersistencePastLimits=55 |
| The Charmer | Desire=60, Restraint=50, Tension=20, Connection=75, Dominance=60, Loyalty=30, SelfRespect=85 | HusbandAwareness=60, MarriageContextUse=50, DiscoveryRisk=50, PersistencePastLimits=45 |
| The Experienced Older Man | Desire=75, Restraint=70, Tension=25, Connection=50, Dominance=75, Loyalty=35, SelfRespect=85 | HusbandAwareness=80, MarriageContextUse=40, DiscoveryRisk=65, PersistencePastLimits=35 |
| The Jedi Master | Desire=70, Restraint=50, Tension=20, Connection=85, Dominance=90, Loyalty=15, SelfRespect=10 | HusbandAwareness=90, MarriageContextUse=85, DiscoveryRisk=40, PersistencePastLimits=80 |
| The Confident Cocky Guy | Desire=75, Restraint=35, Tension=15, Connection=40, Dominance=80, Loyalty=25, SelfRespect=95 | HusbandAwareness=25, MarriageContextUse=30, DiscoveryRisk=25, PersistencePastLimits=70 |
| The Bull | Desire=90, Restraint=20, Tension=25, Connection=30, Dominance=95, Loyalty=20, SelfRespect=90 | HusbandAwareness=10, MarriageContextUse=15, DiscoveryRisk=5, PersistencePastLimits=90 |

---

## User Stories

### US1 — Unified Character Profile CRUD (P1)

A user managing profiles navigates to the Configuration section and sees a single "Character Profiles" tab. All profiles are listed filterable by role (Husband / Wife / OtherMan / Any). Each profile shows both the Character Stats group (7 canonical stats with sliders) and the Encounter Behavior group (role-specific behavioral dimensions with sliders). A live preview panel shows the generated behavioral frame text updating in real-time as slider values change. The user can save a combined profile and see it available immediately in session creation and scenario setup.

**Acceptance Scenarios**:
1. **Given** a Husband profile is open, **When** the user moves the Voyeurism slider to 85, **Then** the live preview shows the Tier 4 voyeurism sentence immediately
2. **Given** a Wife profile is open, **When** the user moves PostEncounterGuilt to 95, **Then** the preview shows the guilt tier text
3. **Given** a new profile is saved, **When** the user opens session creation, **Then** the profile appears in the character profile picker filtered by role
4. **Given** a profile has AdditionalNotes set, **When** the preview renders, **Then** the generated dimension text appears first followed by the additional notes

### US2 — Session Creation with Per-Character Encounter Profiles (P1)

A user creating a new RP session selects character profiles for each role. For each character the profile picker shows unified profiles filtered by role. The selected profile seeds both the canonical character stats (Desire, Restraint, etc.) AND binds the encounter behavioral profile for that character. The session stores the profile binding so the correct behavioral frame text is generated at prompt time.

**Acceptance Scenarios**:
1. **Given** a session is being created, **When** the wife character picker shows profiles, **Then** only Wife-role profiles appear
2. **Given** the user selects "Cuckold Husband" for the husband character, **When** the session starts, **Then** the husband's canonical stats are seeded from the profile AND the behavioral dimensions are bound for prompt injection
3. **Given** a session is active, **When** a continuation is generated, **Then** the husband behavioral frame and wife behavioral frame both appear as HARD CONSTRAINTs in the prompt

### US3 — Behavioral Frame in RP Workspace Adaptive Panel (P2)

A user in an active RP session can see the current behavioral frame text for each character in the adaptive panel. This shows what behavioral instructions the LLM is currently receiving, and allows the user to switch to a different encounter profile for any character mid-session.

**Acceptance Scenarios**:
1. **Given** the adaptive panel is open, **When** the user views a character, **Then** the current behavioral frame text is displayed
2. **Given** the user changes a character's encounter profile mid-session, **When** the next continuation is generated, **Then** the new behavioral frame is used; the change is applied in-memory immediately and persisted to DB as part of that continuation's normal state save
3. **Given** a character has no encounter profile selected, **When** a continuation is generated, **Then** no behavioral frame is injected for that character (no empty block, no error)

### US4 — Existing Profiles Migrated and Old System Retired (P1)

After deployment, existing `HusbandAwarenessProfiles` data is migrated: existing profiles' Notes field becomes AdditionalNotes on unified profiles; their stat values are preserved. The standalone `HusbandAwarenessProfiles` table is no longer used. Sessions previously holding `HusbandAwarenessProfileId` on `AdaptiveScenarioState` are migrated to use `CharacterEncounterProfileIds` instead. The old profile CRUD UI tab is removed.

**Acceptance Scenarios**:
1. **Given** the app starts after migration, **When** the Character Profiles page loads, **Then** the 4 existing husband profiles appear as unified profiles with both stat groups populated
2. **Given** an existing session with `HusbandAwarenessProfileId="x"` is loaded, **When** the session resumes, **Then** the husband's behavioral frame is generated from the migrated profile
3. **Given** the old "Husband Awareness Profiles" UI tab, **When** the feature is deployed, **Then** the tab no longer exists

### US5 — Prompt Injection Updated for All Roles (P1)

The continuation prompt for any active session injects behavioral frame text for every character that has an encounter profile bound. Each frame is labeled with the character's name. All frames are injected as HARD CONSTRAINTs both early in the prompt and again immediately before the writing directive (matching the current double-injection pattern).

**Acceptance Scenarios**:
1. **Given** a session has husband, wife, and otherman profiles all bound, **When** a continuation is generated, **Then** the prompt contains three behavioral frame HARD CONSTRAINT blocks, one per character
2. **Given** the wife profile has FullOverride=true with custom AdditionalNotes, **When** the prompt is built, **Then** the AdditionalNotes text (not generated tier text) is used as the wife's behavioral frame
3. **Given** a dimension value of 85, **When** the frame is generated, **Then** Tier 4 text is used (>75 threshold)

### US6 — BehavioralDimensionCatalog Defines All Tier Descriptions (P2)

A developer adding a new behavioral dimension or updating tier wording changes only the `BehavioralDimensionCatalog` static class. No generator code needs editing. The UI live preview reads from the same catalog. Adding a new role's dimensions requires only adding entries to the catalog and a corresponding stats group in the profile entity.

**Acceptance Scenarios**:
1. **Given** the catalog defines Wife `DiscoveryCaution` Tier 3 text, **When** a Wife profile with DiscoveryCaution=60 is previewed in the UI, **Then** the Tier 3 text appears in the live preview
2. **Given** the catalog is updated with new tier text for any dimension, **When** the app runs, **Then** all prompt generation and UI preview uses the updated text without any other code changes

---

## Functional Requirements

- **FR-001**: A single `CharacterProfile` entity MUST replace both `BaseStatProfile` and `HusbandAwarenessProfile`; it carries canonical stats, encounter behavioral dimensions, AdditionalNotes, and FullOverride
- **FR-002**: Behavioral dimensions MUST be organized by TargetRole; each role has its own named dimension set defined in `BehavioralDimensionCatalog`
- **FR-003**: The `BehavioralDimensionCatalog` MUST define 4 tier thresholds per dimension (≤20, ≤50, ≤75, >75) with LLM-directive tier text; the catalog is the single source for all tier descriptions
- **FR-004**: Dimension stats MUST always generate tier text; AdditionalNotes is appended after, never replacing generated text unless FullOverride=true
- **FR-005**: The profile CRUD UI MUST show Character Stats and Encounter Behavior as two labeled groups within one form
- **FR-006**: The profile CRUD UI MUST render a two-panel live preview updating as slider values change: (1) a labeled-list panel showing each dimension name and its resolved tier sentence; (2) an exact-text panel showing the verbatim injected paragraph identical to what the prompt builder will use
- **FR-007**: Session creation MUST offer a single character profile picker per character, filtered by role; selecting and applying a profile seeds canonical stats AND binds the encounter profile atomically — there is no separate picker for stats vs encounter behavior
- **FR-008**: `AdaptiveScenarioState` MUST replace `HusbandAwarenessProfileId` (string) with `CharacterEncounterProfileIds` (Dictionary<characterId, profileId>)
- **FR-009**: The continuation prompt builder MUST inject behavioral frame text for every character that has an encounter profile bound, as HARD CONSTRAINTs, applied twice (early and immediately before writing directive)
- **FR-010**: Each behavioral frame HARD CONSTRAINT block MUST be labeled with the character's name/role
- **FR-011**: The RP workspace adaptive panel MUST display the current behavioral frame text per character and allow mid-session encounter profile switching; a profile change MUST be applied in-memory immediately (so the next continuation uses the new frame) and persisted to DB lazily as part of the continuation's normal session state save — no explicit save button required
- **FR-012**: Migration MUST convert existing `HusbandAwarenessProfiles` records to unified `CharacterProfiles`; existing sessions' `HusbandAwarenessProfileId` MUST be migrated to `CharacterEncounterProfileIds`
- **FR-013**: Seeded archetype profiles MUST be provided for all three roles (Husband ×8, Wife ×9, OtherMan ×8) as defined in this spec
- **FR-014**: The "Balanced Baseline" `BaseStatProfile` with non-canonical stat names MUST be deleted during migration
- **FR-015**: The old "Husband Awareness Profiles" UI tab MUST be removed and replaced by the unified "Character Profiles" tab
- **FR-016**: Persisted feature data MUST use SQLite
- **FR-017**: Application logging MUST use Serilog; major execution paths MUST emit Information-level logs
- **FR-018**: Log levels MUST be configurable via settings without code changes
- **FR-019**: Profile names MUST be unique per TargetRole (case-insensitive); `SaveAsync` MUST reject a save with a `ValidationException` if a different profile with the same name and TargetRole already exists

---

## Edge Cases

- Character with no encounter profile bound: no behavioral frame injected — no empty block, no error, prompt continues normally
- FullOverride=true with empty AdditionalNotes: treat as no override (fall back to generated text to avoid empty HARD CONSTRAINT)
- Session created before migration: `HusbandAwarenessProfileId` is present, `CharacterEncounterProfileIds` is absent — migration layer resolves on first load
- Profile with TargetRole="Any": dimensions from no role-specific set are shown; only canonical stats apply
- Migration produces duplicate-named profiles (e.g., two husband archetypes with similar names from the two old tables): this is expected and allowed — no automatic merge; the name-uniqueness constraint (FR-019) applies only to new saves, not to migrated rows; users may delete unwanted migrated profiles manually after reviewing them
- Duplicate profile name within same TargetRole: save is blocked with a validation error; uniqueness is case-insensitive; "Any" role profiles share a uniqueness namespace with each other but not with role-specific profiles
- Dimension value exactly at threshold boundary (e.g., exactly 20): inclusive lower bound (≤20 = Tier 1)
- Wife or OtherMan profile used as husband character: UI should not allow it; role filter enforced at session creation picker

---

## Out of Scope

- Making behavioral dimension tier descriptions editable via DB/UI (catalog is code-defined; editing requires a code change)
- Behavioral stats participating in gate threshold evaluation (they drive text only, not engine gate logic)
- Adding new character roles beyond Husband / Wife / OtherMan (can be added later via catalog extension)
- Automatic stat adjustment based on behavioral profile selection (seeding is separate from binding)
