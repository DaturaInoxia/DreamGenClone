# Feature Specification: Wife Resistance & Cheating Motivation Gap

**Feature Branch**: `001-wife-resistance-motivation`  
**Created**: 2026-06-07  
**Status**: Draft  
**Input**: User description: "Wife resistance & cheating motivation gap — add real resistance counterweight (the Wife says 'we shouldn't' but never genuinely resists because the escalation engine has no counterweight) and cheating motivation drivers (emotional neglect, sexual frustration, validation-seeking) so the Wife holds boundaries until configured motivational conditions are met."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Wife Genuinely Resists Advances Until Motivation Conditions Are Met (Priority: P1)

A user runs a seduction-themed roleplay session where the Wife character has high Loyalty and Restraint. Currently, the Wife says "we shouldn't" as flavour text but the narrative always escalates forward because the engine has no counterweight to escalation pressure. With this feature, the Wife will genuinely resist — the narrative will reflect that she holds her boundaries — when her resistance profile says she should. The user experiences a more realistic tension where the Wife's boundaries have real weight, making the eventual crossing of those boundaries (when motivation conditions are met) feel earned and meaningful.

**Why this priority**: This is the core gap — "we shouldn't" with no follow-through. Every session with a Wife character is affected. Without this, resistance is cosmetic and escalation feels inevitable rather than earned.

**Independent Test**: Create a session with a Wife character having Loyalty=75 and Restraint=70. Run the BuildUp and Committed phases. The prompt must contain an authoritative resistance directive that tells the AI the Wife is holding firm boundaries. The escalation guidance must not push past this resistance band. Verify in the generated narrative that the Wife's resistance is reflected in her actions — she deflects, redirects, or holds the line.

**Acceptance Scenarios**:

1. **Given** a Wife character with Loyalty=75, Restraint=70, and a seeded default resistance profile, **When** the prompt is built during the Committed phase, **Then** the prompt contains an explicit resistance directive stating she holds firm boundaries, and escalation guidance does not push past her current resistance band.
2. **Given** a Wife character with Loyalty=25, Restraint=15 (low resistance), **When** the prompt is built, **Then** the resistance directive reflects a permissive or receptive band, and escalation guidance pushes forward normally.
3. **Given** a Wife with moderate Restraint=55, **When** the OtherMan character has high PersistencePastLimits, **Then** the resistance directive acknowledges the pressure but maintains the appropriate band — she doesn't break just because he's persistent.

---

### User Story 2 - Multiple Motivational Drivers Influence Wife's Receptivity (Priority: P2)

A user wants to model *why* a married woman would cross the line — not just whether she has high or low loyalty. The system must recognise that infidelity has many plausible drivers, and the Wife's resistance band should shift based on which drivers are active in the current scenario.

The full catalog of affair-motivation drivers includes:

| # | Driver | How It Lowers Resistance | Implementation Path |
|---|---|---|---|
| 1 | **Emotional Neglect** | Husband is cold, distant, or checked out | Husband `Attentiveness` dimension (this iteration) |
| 2 | **Sexual Frustration** | Dead bedroom, mismatched libido, husband uninterested | Husband `IntimacyAvailability` dimension (this iteration) |
| 3 | **Validation Seeking** | Wife's SelfRespect is eroded and the OtherMan fills a void the Husband leaves | Wife `SelfRespect` stat × Husband `Attentiveness` gap (this iteration) |
| 4 | **Persistent Temptation** | OtherMan relentlessly pursues, wears down resistance over time | OtherMan `PersistencePastLimits` dimension — *already modeled, acknowledged* |
| 5 | **Emotional Connection to OtherMan** | Genuine bond and chemistry with the OtherMan builds over time | Wife stat values (Desire, Loyalty) + encounter history — *partially modeled, strengthened by this feature* |
| 6 | **Revenge** | Husband cheated first — the Wife rationalises her own infidelity as payback | Scenario-level flag (future iteration) |
| 7 | **Marital Breakdown** | Chronic fighting, resentment, separate lives — the marriage is functionally over | Marriage-quality score on scenario (future iteration) |
| 8 | **Midlife Crisis** | Wife wants to feel alive, desired, or reclaim lost youth | Scenario metadata flag (future iteration) |
| 9 | **Substance / Lowered Inhibitions** | Alcohol, drugs, or an emotionally raw moment create a window of weakness | Event-level trigger in semantic engine (future iteration) |
| 10 | **Financial / Power Imbalance** | Coercion, obligation, or leverage forces compliance | Edge case — low priority (future iteration) |

**In this iteration**, the motivation score that selects the Wife's resistance band is computed from **four profile-level inputs** that cover the most common RP scenarios:

1. **Husband Attentiveness** — how emotionally present and engaged he is (drivers #1, #3)
2. **Husband IntimacyAvailability** — how sexually engaged he is in the marriage (driver #2)
3. **Wife SelfRespect** — how much she values her own dignity and needs (driver #3)
4. **OtherMan PersistencePastLimits** — how aggressively he pursues despite resistance (driver #4 — already modeled, factored into the motivation score)

These four inputs combine into a single **motivation score** that shifts the Wife's resistance band up or down from her baseline. A Wife with Loyalty=70 whose husband is neglectful and sexually absent will resolve to a more permissive band than the same Wife with an attentive, engaged husband. But her Loyalty stat still anchors the band — motivation shifts it, it does not replace it. Drivers #5–#10 are explicitly acknowledged but deferred to future iterations.

**Why this priority**: The "why would she cheat" question is what makes infidelity narratives psychologically credible. Without motivation drivers, resistance either blocks everything (frustrating when the user *wants* the affair to happen) or means nothing (because escalation always wins). The four profile-level drivers cover ~80% of common RP scenarios and are implementable without schema changes (behavioral dimensions only).

**Independent Test**: Configure a Husband profile with Attentiveness=15, IntimacyAvailability=10; Wife with Loyalty=70, SelfRespect=30; OtherMan with PersistencePastLimits=85. Build the prompt for a Committed-phase turn. The motivation score must be high (strong drivers), and the Wife's resolved resistance band must be at least two bands more permissive than it would be with all motivation inputs at neutral (50). Conversely, configure all four inputs at neutral and verify the Wife resolves to her raw Loyalty band with no shift.

**Acceptance Scenarios**:

1. **Given** Husband Attentiveness=15, IntimacyAvailability=10, Wife Loyalty=70, SelfRespect=30, OtherMan Persistence=85, **When** the motivation score is computed, **Then** the Wife's resolved resistance band is at least two bands more permissive than the band for Loyalty=70 with all-neutral motivation inputs.
2. **Given** Husband Attentiveness=85, IntimacyAvailability=80 (great husband), Wife Loyalty=40 (already low loyalty), **When** the motivation score is computed, **Then** the motivation signal is near-zero or slightly negative — the Wife's low resistance comes from her own stats, not from marital deficit. The resistance band reflects her Loyalty=40 without further relaxation.
3. **Given** Wife SelfRespect=20 (eroded boundaries), Husband Attentiveness=25 (neglectful), **When** the prompt is built, **Then** the resistance directive acknowledges the validation-seeking driver: she is not just disloyal, she is filling a void the husband created.
4. **Given** OtherMan PersistencePastLimits=90, **When** the prompt is built, **Then** the resistance directive includes a note that persistent pursuit is wearing down her resolve — this is a distinct motivation from marital deficit.
5. **Given** all four profile-level motivation inputs at their default/neutral values (50), **When** the motivation score is computed, **Then** it is zero and the Wife resolves to her raw Loyalty band with no shift — the resistance profile alone determines the directive.

---

### User Story 3 - Configure Resistance Profiles Through the UI (Priority: P3)

An administrator or power user wants to define different resistance profiles — for example, a "Strictly Faithful" profile where even low resistance bands still impose firm boundaries, or a "Vulnerable Marriage" profile where even moderate loyalty allows quick receptivity. They navigate to the Theme Profiles page, select the new "Resistance" tab, and create, edit, and delete resistance profiles with configurable bands mapping stat values to resistance directives. A seeded default profile ensures new users get sensible behaviour out of the box.

**Why this priority**: The repo's hard rules require every RP behaviour control to be UI-backed persisted configuration. The resistance and motivation thresholds must be user-configurable, not hardcoded. This story delivers the configuration surface.

**Independent Test**: Navigate to Theme Profiles → Resistance tab. Create a new profile with name "Test Resistance", set TargetStatName to "Restraint", define three threshold bands (0–30 permissive, 31–70 moderate, 71–100 firm), and save. Verify the profile appears in the list, survives page reload, and can be edited and deleted. Verify the seeded default profile "Married Woman Resistance" is present on first run.

**Acceptance Scenarios**:

1. **Given** a fresh database, **When** the Resistance tab is first loaded, **Then** a seeded default profile "Married Woman Resistance" exists with contiguous bands covering 0–100 and a TargetStatName of "Loyalty".
2. **Given** the Resistance tab open, **When** the user clicks "New", fills in the form with a name and valid threshold JSON, and clicks "Create Profile", **Then** the profile is saved and appears in the list.
3. **Given** an existing resistance profile, **When** the user edits the thresholds and clicks "Save Changes", **Then** the updated profile is persisted and reflects in subsequent prompt builds.
4. **Given** an existing non-seeded resistance profile, **When** the user clicks "Delete" and confirms, **Then** the profile is removed from the list and the default profile is auto-selected.

---

### User Story 4 - Wife Boundary-Holding Dimensions Flow Into Behavioral Frame (Priority: P3)

The Wife has new behavioral dimensions — `BoundaryFirmness` (how firmly she holds the line when she says no) and `SeductionReceptivity` (how vulnerable she is to persistent pursuit) — that are configured on her CharacterProfile. These dimensions flow through the existing behavioral-frame pipeline and appear in the prompt as authoritative per-character behavioral descriptions, shaping how the AI writes her internal state and actions.

**Why this priority**: Behavioral dimensions are the established mechanism for per-character narrative steering. Adding Wife boundary-holding dimensions gives users direct control over the Wife's resistance personality beyond just stat values, and the dimensions drift automatically via stat-to-dimension mappings during play.

**Independent Test**: Create a Wife CharacterProfile with BoundaryFirmness=85 and SeductionReceptivity=15. Load it in a session. Verify the prompt's HARD CONSTRAINT behavioral frame line for the Wife includes tier-4 text describing her as firmly holding boundaries and tier-1 text describing her as not easily swayed by pursuit.

**Acceptance Scenarios**:

1. **Given** a Wife profile with BoundaryFirmness=85, **When** the behavioral frame is generated, **Then** the Wife's frame text includes the Tier-4 description for BoundaryFirmness: she firmly enforces her stated limits and will not be argued past them.
2. **Given** a Wife profile with SeductionReceptivity=20, **When** the behavioral frame is generated, **Then** the frame text includes the Tier-1 description: she is largely immune to persistent pursuit and does not find pressure flattering.
3. **Given** Wife Restraint drops by 20 during play, **When** stat-to-dimension drift is applied, **Then** BoundaryFirmness increases by the configured slope (e.g., +18 for a +0.90 slope), and SeductionReceptivity adjusts per its drift rules.
4. **Given** the CharacterProfile edit form for Wife role, **When** the user views the encounter stats section, **Then** BoundaryFirmness and SeductionReceptivity appear as sliders with live tier-text preview.

---

### Edge Cases

- What happens when a session has characters but none with the Wife role? The resistance directive is skipped entirely — no Wife, no resistance gating. The prompt builds normally for the roles present.
- What happens when the Wife exists but her configured TargetStatName value falls outside all threshold bands? The profile validation guarantees contiguous 0–100 coverage on save, so a band always matches. If data corruption occurs, the resistance directive falls back to an empty string (no resistance line injected).
- What happens when Husband Attentiveness or IntimacyAvailability are not configured (EncounterStats absent)? The behavioral frame generator already defaults missing dimensions to 50 (neutral), so no artificial resistance change occurs.
- What happens when OtherMan PersistencePastLimits is not configured? Same as Husband dims — defaults to 50 (neutral), contributing zero to the motivation score. No artificial resistance change.
- What happens during the Climax phase when resistance would normally block escalation? The resistance directive is still authoritative — it outranks the escalation push. If the resistance band says "firm boundaries", the AI must reflect that even in Climax. The user can override via Steer commands.
- What happens when both Wife resistance is high AND the motivation score is high (conflicting signals)? The resistance directive takes priority — motivation relaxes resistance bands but does not eliminate them. A Wife with Loyalty=90 will resist regardless of Husband neglect; the neglect just means her resistance is 80 instead of 95.
- What happens with deferred drivers (#5–#10) in this iteration? They are catalogued but produce zero motivation signal. The resistance band resolves solely from the four implemented profile-level inputs. Users who want revenge/midlife/substance drivers must wait for those separate features; they can still simulate the *effect* by manually lowering the Wife's Loyalty or Restraint stats, or selecting a more permissive ResistanceProfile.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a configurable Resistance Profile (persisted to SQLite) that maps a target stat value range (0–100, contiguous bands) to resistance directive text injected into the continuation prompt, following the exact same domain/persistence/service/UI pattern as the existing Willingness Profile.
- **FR-002**: System MUST seed a default Resistance Profile ("Married Woman Resistance") on first run with contiguous bands covering 0–100, using "Loyalty" as the default target stat.
- **FR-003**: System MUST inject a per-character resistance directive into the continuation prompt when a Wife-role character is present, using the configured Resistance Profile to resolve the directive text from a motivation score computed from four profile-level inputs: (a) the Wife's current resistance stat value per the ResistanceProfile TargetStatName, (b) the Husband's Attentiveness dimension value, (c) the Husband's IntimacyAvailability dimension value, and (d) the Wife's SelfRespect stat value. The OtherMan's PersistencePastLimits dimension (already modeled) MUST also be factored into the motivation score. The resistance band is selected solely by the configured ResistanceProfile bands — no hardcoded thresholds.
- **FR-004**: System MUST make escalation guidance target-aware — the escalation guidance logic MUST check the target Wife's resolved resistance band and MUST NOT push escalation past a firm-resistance band; it MUST stop referencing the legacy non-canonical "Tension" stat.
- **FR-005**: System MUST add two new Wife behavioral dimensions (BoundaryFirmness, SeductionReceptivity) and two new Husband behavioral dimensions (Attentiveness, IntimacyAvailability) to the BehavioralDimensionCatalog, each with four-tier descriptive text. The existing OtherMan PersistencePastLimits dimension MUST be factored into the motivation score computation but requires no catalog changes.
- **FR-006**: System MUST include the new behavioral dimensions in CharacterProfile validation (ValidateStats) and the UI encounter-stats form, and they MUST flow through the existing CharacterBehavioralFrameGenerator into the prompt as HARD CONSTRAINT per-character frame text.
- **FR-007**: System MUST add optional stat-to-dimension drift rules for the new dimensions so that when Wife stats change during play (e.g., Restraint drops), BoundaryFirmness and SeductionReceptivity drift correspondingly.
- **FR-008**: System MUST provide full CRUD UI for Resistance Profiles via a new "Resistance" tab on the Theme Profiles page, matching the existing "Willingness" tab pattern (list, create/edit form with JSON threshold editor, save, delete).
- **FR-009**: System MUST persist the selected Resistance Profile ID on the session's adaptive state (AdaptiveScenarioState.SelectedResistanceProfileId) in the RolePlayV2AdaptiveStates table, with a nullable TEXT column.
- **FR-010**: System MUST purge all existing roleplay sessions at cutover (no backfill), following the B-038 cutover pattern, because new adaptive-state columns and validation rules are additive and the seeded default profile covers all new sessions.
- **FR-011**: System MUST NOT add new canonical stats (Attentiveness, IntimacyEngagement, etc.) to AdaptiveStatCatalog — motivation drivers MUST be modeled as behavioral dimensions only, avoiding the RequiredStats auto-reject cascade that would reject all existing sessions.
- **FR-012**: System MUST use the configured Resistance Profile bands as the sole source of resistance thresholds — no hardcoded fallback values, no code-only defaults for resistance gating, in compliance with the repo's no-fallback rule for RP engine behavior.
- **FR-013**: Persisted feature data (Resistance Profiles, session adaptive state) MUST use SQLite.
- **FR-014**: Application logging MUST use Serilog with structured message templates; profile save/load and resistance directive resolution paths MUST emit Information-level logs.
- **FR-015**: Log levels MUST be configurable via settings without code changes.
- **FR-016**: System MUST display the active ResistanceProfile name and the Wife's current resolved resistance band on the RP workspace adaptive panel, alongside the existing WillingnessProfile readout, so the user can see which resistance profile is governing the session and what band is currently active.

### Key Entities

- **ResistanceProfile**: A named, persisted configuration that maps a character stat value range (0–100 in contiguous bands) to a resistance directive text. Attributes: unique name, target stat name (e.g., "Loyalty"), whether it is the default profile, and an ordered list of resistance threshold bands. Each band defines a stat-value range (min–max), a resistance level label, a prompt guideline directive, and optional example scenarios. Relationship: one session's adaptive state references one ResistanceProfile.
- **Wife Behavioral Dimensions** (BoundaryFirmness, SeductionReceptivity): Per-Wife encounter-stat values stored in CharacterProfile.EncounterStats (JSON). BoundaryFirmness governs how firmly the Wife enforces her stated limits. SeductionReceptivity governs how vulnerable she is to persistent pursuit. Both have four-tier descriptive text in the catalog and drift rules in StatToDimensionMappings.
- **Husband Behavioral Dimensions** (Attentiveness, IntimacyAvailability): Per-Husband encounter-stat values stored in CharacterProfile.EncounterStats (JSON). Attentiveness measures emotional presence and engagement in the marriage. IntimacyAvailability measures sexual engagement and availability. Both affect the motivation score that relaxes or tightens the Wife's resistance band.
- **Motivation Score**: Not a persisted entity — a runtime computation derived from four profile-level inputs using a simple equal-weight average. Formula: `motivationScore = ((100 − Husband.Attentiveness) + (100 − Husband.IntimacyAvailability) + (100 − Wife.SelfRespect) + OtherMan.PersistencePastLimits) / 4`. All four inputs are normalized so higher = more motivation to cross boundaries: Husband neglect and Wife low self-respect are inverted (100 − value), OtherMan persistence is direct (value). Missing inputs default to 50 (neutral). The computed score (0–100) shifts the Wife's resistance band up or down from her baseline — motivation shifts, never replaces, the Loyalty-anchored band. The four inputs cover drivers #1–#4 of the affair-motivation catalog. Drivers #5–#10 are acknowledged but deferred. No hardcoded motivation thresholds — the score-to-band mapping lives entirely in the configured ResistanceProfile.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a session with a high-resistance Wife (Loyalty ≥ 70), at least 80% of AI-generated Wife responses in the BuildUp and Committed phases reflect genuine boundary-holding behaviour (deflection, redirection, explicit "no") rather than token resistance followed by compliance.
- **SC-002**: When all four profile-level motivation inputs signal strong drivers (Husband Attentiveness ≤ 25, IntimacyAvailability ≤ 25, Wife SelfRespect ≤ 30, OtherMan PersistencePastLimits ≥ 80), the Wife's resolved resistance band shifts at least two bands more permissive than the same Wife Loyalty value with all inputs at neutral (50).
- **SC-003**: A user can create a new Resistance Profile, populate its threshold bands, save it, and have it active for the next prompt build in under 3 minutes from navigating to the Resistance tab.
- **SC-004**: The seeded default Resistance Profile provides sensible, usable resistance gating out of the box for all five Loyalty stat bands (low/medium-low/medium/medium-high/high), verified by prompt inspection for each band.
- **SC-005**: After the cutover purge, all newly created sessions include the SelectedResistanceProfileId in their adaptive state without manual user intervention — the default profile is auto-selected.
- **SC-006**: Zero hardcoded resistance threshold values exist in code — all thresholds originate from the persisted ResistanceProfile bands, verified by code review of the resistance resolution path.
- **SC-007**: The full 10-driver affair-motivation catalog is documented in the specification with clear implementation paths; the four profile-level drivers (#1–#4) are implemented in this iteration, and the remaining six (#5–#10) are explicitly acknowledged with their future implementation path (scenario-level flags, event-level triggers, or metadata).

## Clarifications

### Session 2026-06-07

- Q: How do the four profile-level motivation inputs combine into a single motivation score? → A: Simple equal-weight average: `motivationScore = ((100 − Attentiveness) + (100 − IntimacyAvailability) + (100 − SelfRespect) + PersistencePastLimits) / 4`. All inputs normalized so higher = more motivation (Husband neglect and Wife low self-respect are inverted; OtherMan persistence is direct). Formula fixed in code, not configurable per profile.
- Q: How does the motivation score map to resistance band selection? → A: Add motivation score to effective stat value before ResistanceProfile band lookup: `effectiveStat = min(targetStatValue + motivationScore, 100)`. The ResistanceProfile's existing contiguous bands then resolve the effective stat to the appropriate resistance directive. No separate band-shift concept — motivation inflates the stat, the profile does the rest.
- Q: What format should the resistance directive use in the prompt, and where should it appear? → A: HARD CONSTRAINT line, positioned before escalation guidance so it visibly outranks push-forward lines. Format: `HARD CONSTRAINT — {WifeLabel} resistance directive (authoritative, overrides escalation guidance): {resistanceBandText}`. Placed immediately after the per-character current-state HARD CONSTRAINT lines in the prompt section order.
- Q: How does a session select which ResistanceProfile to use? → A: Auto-select the default ResistanceProfile on session create (matching WillingnessProfile convention — no per-session dropdown). The active ResistanceProfile name and current resolved band MUST be displayed on the RP workspace adaptive panel alongside the WillingnessProfile readout, so the user can see which resistance profile is governing the session.
