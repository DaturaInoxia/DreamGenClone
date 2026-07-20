# Feature Specification: Final Writing Instruction Consolidation

**Feature Branch**: `001-final-writing-instruction`  
**Created**: 2026-07-19  
**Status**: Draft  
**Input**: User description: "Consolidate all writing direction into Slot 17 Final Instruction. Rename prompt labels to writer-standard terms: Prose Style, Heat Level, Voice, Scene Direction, Tone. Add SteeringProfile fields: ImmersionDirective, ActionDirective, WordTargetMin/Max. Add NarrativeSettings fields: Tone, Register, Focus. Move Atmospheric profile from ToneProfiles to StyleProfiles. Remove writing direction from Slots 8 and 15. Move Phase Guidance from Slot 12 to Slot 17."

## Clarifications

### Session 2026-07-19

- Q: Is UI editing of the new SteeringProfile and NarrativeSettings fields in scope for this feature? → A: Yes — all UI changes are in scope. All UI changes MUST be grouped together so a specific agent can implement them, and UI work MUST be sequenced last in the implementation plan (after data model, prompt slot changes, and profile cleanup).
- Q: How should "measurable compliance" for the Scene Direction ↔ Writing Instruction ordering be defined and validated? → A: All three methods apply — (A) manual qualitative review of N sample generations per ordering against a 4-item checklist (POV, Heat, Scene Direction, Word Target); (B) automated scoring script checking objective markers (dialogue presence for narrative variant, word count, POV pronouns); (C) single-author subjective review as a final gut-check. The chosen ordering must pass all three.
- Q: What should happen to existing RP sessions that reference Atmospheric as their intensity profile after it moves to StyleProfiles? → A: No migration required. Only new RP sessions will be generated after the feature is implemented; existing sessions are not migrated and are out of scope for this feature.
- Q: Should word target be a single range per SteeringProfile, or per-variant (Character vs. Narrative)? → A: Per-variant. SteeringProfile MUST have separate `WordTargetMin/Max` (Character variant) and `NarrativeWordTargetMin/Max` (Narrative variant). Narrative targets are intentionally longer than Character targets.
- Q: Who defines what counts as "heat-level only" language for the Sensual/Emotional ToneProfile cleanup, and how? → A: Planning phase produces a cleanup spec listing each removed phrase and the rewritten description for Sensual and Emotional. The cleanup is expected to be model-generated — the model that originally analyzed the data and suggested the changes should know what needs to change. Implementation follows the model-produced cleanup spec.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Writer-Standard Terminology in Prompts (Priority: P1)

As a role-play writer, I want the prompt labels to use writer-standard terms (Prose Style, Heat Level, Voice, Scene Direction, Tone) so that the terminology in prompts matches my craft vocabulary and is immediately understandable.

**Why this priority**: Terminology is the user-facing surface of the prompt system. Misaligned labels cause confusion about what each prompt section controls. Fixing labels requires no data model changes and delivers immediate clarity.

**Independent Test**: Start a role-play session and inspect the built prompt. Verify labels use "Prose Style" (not "Writing Style"), "Heat Level" (not "Intensity level"), "Voice" (not "Profile Default"), "Scene Direction" (not "Phase Rule of Thumb"), and "Tone" (not buried in "Style Hint").

**Acceptance Scenarios**:

1. **Given** a Character-variant prompt is built, **When** the prompt is inspected, **Then** the writing instruction section uses the label "Prose Style:" for the style profile description.
2. **Given** a Character-variant prompt is built, **When** the prompt is inspected, **Then** the intensity section uses the label "Heat Level:" instead of "Intensity level:".
3. **Given** a prompt is built with a style profile that has a Rule of Thumb, **When** the prompt is inspected, **Then** that section is labeled "Voice:" instead of "Profile Default:".
4. **Given** a prompt is built with a phase active, **When** the prompt is inspected, **Then** the phase guidance is labeled "Scene Direction:" instead of "Phase Rule of Thumb:".
5. **Given** a scenario has narrative tone configured, **When** the prompt is inspected, **Then** the tone is labeled "Tone:" as a distinct, visible section (not buried within another label).

---

### User Story 2 - Single Authoritative Writing Instruction (Priority: P1)

As a role-play writer, I want ALL writing direction (Prose Style, Voice, Tone, Heat Level, Pacing, POV, immersion rules, word targets, action directives) consolidated into one block at the end of the prompt so that the model receives a single, coherent writing instruction rather than scattered direction across multiple slots.

**Why this priority**: Research shows models attend strongly to content at the end of the prompt (recency). Scattered writing direction across Slots 8, 12, 15, and 17 causes instruction dilution and reduces compliance. Consolidation is the core architectural change.

**Independent Test**: Start a role-play session and inspect the built prompt. Verify that Slots 8 and 15 contain no writing direction text. Verify Slot 17 contains ALL writing direction components (Prose Style, Voice, Tone, Heat Level, Pacing, POV, immersion directive, word target, action directive) in a single consolidated block.

**Acceptance Scenarios**:

1. **Given** a Character-variant prompt is built, **When** Slot 8 (WritingStyle) is inspected, **Then** it contains no prose style description, no foundation text, no tone text, and no phase direction — only contextual/structural data or is empty.
2. **Given** a prompt is built, **When** Slot 15 (IntensityPacing) is inspected, **Then** it contains no heat level label, no heat contract text, and no pacing directive — only structural data such as available positions.
3. **Given** a Character-variant prompt is built, **When** Slot 17 (Final Instruction) is inspected, **Then** it contains Prose Style, Voice, Tone, Heat Level, Pacing, POV directive, immersion directive, word target, and action directive in a single consolidated block.
4. **Given** a Narrative-variant prompt is built, **When** Slot 17 is inspected, **Then** it contains narrative-specific constraints (zero dialogue, no new events, synthesis-only, physical detail checklist) in addition to the shared components.
5. **Given** a prompt is built, **When** inspecting Slots 8, 15, and 17, **Then** no writing direction content is duplicated across slots.

---

### User Story 3 - Configurable Writing Directives (Priority: P2)

As a role-play writer configuring a style profile, I want to specify immersion directives, action directives, and word targets through the UI so that each style profile can enforce its own writing rules without hardcoded defaults.

**Why this priority**: The immersion directive ("Stay inside this character's perceptions"), action directive ("Respond to the scene naturally"), and word targets are currently hardcoded. Making them configurable allows different style profiles to have different writing rules and eliminates hidden fallbacks. UI editing is in scope so writers can manage these without DB access.

**Independent Test**: Edit a style profile's immersion directive, action directive, and word targets via the Style Profile management UI. Start a session with that profile and verify the built prompt uses the configured values.

**Acceptance Scenarios**:

1. **Given** a style profile has an immersion directive configured, **When** a prompt is built using that profile, **Then** Slot 17 includes that exact immersion directive text.
2. **Given** a style profile has an action directive configured, **When** a prompt is built using that profile, **Then** Slot 17 includes that exact action directive text.
3. **Given** a style profile has a Character word target range of 150-300 words, **When** a Character-variant prompt is built using that profile, **Then** Slot 17 specifies "Target 150-300 words."
4. **Given** a style profile has a Narrative word target range of 300-500 words, **When** a Narrative-variant prompt is built using that profile, **Then** Slot 17 specifies "Target 300-500 words of scene synthesis."
5. **Given** a style profile is missing its Character word target range, **When** a Character-variant prompt build is attempted, **Then** the system fails fast with a diagnostic error — no default word count is substituted.
6. **Given** a style profile is missing its Narrative word target range, **When** a Narrative-variant prompt build is attempted, **Then** the system fails fast with a diagnostic error — no default word count is substituted.
7. **Given** a writer opens the Style Profile management UI, **When** they view a style profile, **Then** the UI exposes editable fields for Immersion Directive, Action Directive, Character Word Target Min/Max, and Narrative Word Target Min/Max.
8. **Given** a writer edits and saves the new fields via the UI, **When** the style profile is reloaded, **Then** the saved values persist and are reflected in the next built prompt.

---

### User Story 4 - Narrative Tone Decomposition (Priority: P2)

As a role-play writer configuring a scenario, I want to specify Tone, Register, and Focus as separate fields through the UI rather than one combined "Narrative Tone" string so that each aspect can be independently configured and clearly labeled in the prompt.

**Why this priority**: The current single `NarrativeTone` field conflates tone ("Erotic, playful"), register ("low language complexity"), and focus ("physical pleasure"). Separating them enables precise control and clearer prompt labeling. UI editing is in scope so writers can configure these without DB access.

**Independent Test**: Configure a scenario with distinct Tone, Register, and Focus values via the Scenario narrative settings UI. Start a session and verify each appears as a distinct element in Slot 17.

**Acceptance Scenarios**:

1. **Given** a scenario has Tone configured as "Erotic, conversational, playful", **When** a prompt is built, **Then** Slot 17 displays "Tone: Erotic, conversational, playful".
2. **Given** a scenario has Register configured as "Low to moderate language complexity", **When** a prompt is built, **Then** Slot 17 includes the register text alongside Tone.
3. **Given** a scenario has Focus configured as "Physical pleasure", **When** a prompt is built, **Then** the focus is included in the writing instruction context.
4. **Given** a scenario has only the legacy `NarrativeTone` field populated (new fields are empty), **When** a prompt is built, **Then** the system falls back to the legacy field for backward compatibility.
5. **Given** a writer opens the Scenario narrative settings UI, **When** they view the narrative configuration, **Then** the UI exposes editable fields for Tone, Register, and Focus (with the legacy NarrativeTone field deprecated/hidden or shown as read-only).

---

### User Story 5 - Profile Categorization Cleanup (Priority: P3)

As a role-play writer, I want Intensity profiles to contain only heat-level content and Style profiles to contain only prose-style content so that each profile system has a clear, non-overlapping purpose.

**Why this priority**: The "Atmospheric" profile is miscategorized as an Intensity profile when it describes prose craft (environmental details, atmosphere, pacing). This creates confusion and overlap with the "Sultry" Style profile. Cleanup is a data fix, not a code change.

**Independent Test**: Inspect the available Intensity profiles — verify "Atmospheric" is no longer listed. Inspect the available Style profiles — verify "Atmospheric" is now listed with its prose-craft description.

**Acceptance Scenarios**:

1. **Given** the profile data is cleaned up, **When** listing Intensity profiles, **Then** "Atmospheric" is not present.
2. **Given** the profile data is cleaned up, **When** listing Style profiles, **Then** "Atmospheric" is present with its original description (environmental details, lighting, atmosphere, slow pacing, rich descriptive language).
3. **Given** the "Sensual" Intensity profile, **When** its description is inspected, **Then** it contains only heat-level language — no prose-style terms like "evocative language" or "deliberate pacing".
4. **Given** the "Emotional" Intensity profile, **When** its description is inspected, **Then** it contains only heat-level language — no prose-style terms like "meaningful dialogue" or "conversation".

---

### User Story 6 - Scene Direction Rename and Placement (Priority: P1)

As a role-play writer, I want the phase guidance renamed to "Scene Direction" and positioned relative to the Writing Instruction block in the way that produces the best model compliance, so that the model effectively follows both the scene's purpose and the writing rules.

**Why this priority**: The label "Phase Rule of Thumb" is not a writer-standard term. Renaming to "Scene Direction" aligns with craft vocabulary. The relative ordering of Scene Direction and Writing Instruction is a research question — both primacy (start of block) and recency (end of block) affect model attention. The final ordering will be determined through analysis during planning and validated by integration testing.

**Independent Test**: Start a session in a phase that has scene direction configured. Inspect the built prompt and verify: (1) the label is "Scene Direction" not "Phase Rule of Thumb", and (2) the Scene Direction and Writing Instruction appear in the order determined by the planning-phase research.

**Acceptance Scenarios**:

1. **Given** a prompt is built during an active phase, **When** the prompt is inspected, **Then** the scene guidance is labeled "Scene Direction" (not "Phase Rule of Thumb").
2. **Given** a prompt is built during an active phase, **When** Slot 12 (Theme Contract) is inspected, **Then** it does NOT contain scene direction prose (only theme name, description, directives, AI guidance notes, and steering rank).
3. **Given** a prompt is built without an active phase, **When** Slot 17 is inspected, **Then** no Scene Direction section appears (the Writing Instruction stands alone).
4. **Given** integration testing is complete, **When** the chosen ordering is evaluated, **Then** the ordering demonstrates measurable compliance improvement over the baseline (pre-consolidation).

---

### Edge Cases

- What happens when a style profile has all new fields (ImmersionDirective, ActionDirective, WordTargetMin/Max, NarrativeWordTargetMin/Max) empty? System must fail fast with a clear error identifying which profile and which field is missing.
- What happens when a scenario has only the legacy `NarrativeTone` field and the new `Tone`/`Register`/`Focus` fields are all empty? System must gracefully use the legacy field.
- What happens when both legacy `NarrativeTone` and new fields are populated? New fields take precedence.
- What happens when no Intensity profile is resolved? System must fail fast — Heat Level is a required component of Slot 17.
- What happens during token budget trimming? Slot 17 (Zone C, end of prompt) must not be trimmed — it is the authoritative writing instruction. Slots 8 and 15 (Zone B) are trimmable.
- What happens when the user provides explicit direction via Slot 16? Slot 16 content remains separate from Slot 17 — user direction is input, not writing instruction. Slot 17's Action Directive may reference it ("Follow the user's direction above.").
- What happens to existing RP sessions created before this feature that reference Atmospheric as an intensity profile or rely on the pre-consolidation slot layout? Out of scope — only new RP sessions generated after the feature is implemented are supported. Existing sessions are not migrated.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Final Instruction slot (Slot 17) MUST consolidate ALL writing direction into a single block containing: Prose Style name + description, Voice, Tone + Register, Heat Level label + contract, Pacing, POV directive, immersion directive, word target, and action directive.
- **FR-002**: Slots 8 (WritingStyle) and 15 (IntensityPacing) MUST NOT emit any writing direction text after consolidation — they become purely contextual/structural slots.
- **FR-003**: Slot 12 (Theme Contract) MUST NOT emit scene direction prose — scene direction moves to Slot 17. The relative ordering of Scene Direction and Writing Instruction within Slot 17 is a research question to be resolved during planning and validated through integration testing.
- **FR-004**: Prompt-facing labels MUST use writer-standard terminology: "Prose Style" (not "Writing Style"), "Heat Level" (not "Intensity level"), "Voice" (not "Profile Default" / "Foundation"), "Scene Direction" (not "Phase Rule of Thumb"), "Tone" (as a distinct labeled section).
- **FR-005**: The SteeringProfile data model MUST support configurable ImmersionDirective, ActionDirective, WordTargetMin, WordTargetMax (Character variant), and NarrativeWordTargetMin, NarrativeWordTargetMax (Narrative variant) fields. Narrative targets MUST be longer than Character targets by convention.
- **FR-006**: Missing SteeringProfile fields (ImmersionDirective, ActionDirective, WordTargetMin, WordTargetMax, NarrativeWordTargetMin, NarrativeWordTargetMax) MUST cause a fail-fast error during prompt building — no hardcoded fallback values are permitted.
- **FR-007**: The NarrativeSettings data model MUST support separate Tone, Register, and Focus fields, with the existing NarrativeTone field deprecated but retained for backward compatibility.
- **FR-008**: When building Slot 17, if new NarrativeSettings fields (Tone, Register, Focus) are populated, they MUST be used; if empty, the system MUST fall back to the deprecated NarrativeTone field; if all are empty, the tone section MUST be silently omitted.
- **FR-009**: The "Atmospheric" profile MUST be moved from ToneProfiles (Intensity) to StyleProfiles (Style) — it describes prose craft, not heat level.
- **FR-010**: The "Sensual" and "Emotional" ToneProfile descriptions MUST be cleaned to contain only heat-level language, removing prose-style terms. The planning phase MUST produce a cleanup spec listing each removed phrase and the rewritten description for both profiles. The cleanup is expected to be model-generated (the model that originally analyzed the data and suggested the changes). Implementation MUST follow the model-produced cleanup spec.
- **FR-011**: The Character-variant Slot 17 MUST include a POV directive derived from the character's perspective mode (first-person or third-person).
- **FR-012**: The Narrative-variant Slot 17 MUST include narrative-specific constraints: zero dialogue, no new events, synthesis-only, and a physical detail checklist.
- **FR-013**: The Scene Direction and Writing Instruction blocks within Slot 17 MUST be positioned in the order determined by planning-phase research on model attention patterns (primacy vs. recency). The chosen ordering MUST be validated through integration testing using three methods: (A) manual qualitative review of N sample generations per ordering against a 4-item checklist (POV, Heat, Scene Direction, Word Target); (B) automated scoring script checking objective markers (dialogue presence for narrative variant, word count, POV pronouns); (C) single-author subjective review. The chosen ordering must pass all three methods and demonstrate compliance no worse than the pre-consolidation baseline.
- **FR-014**: No writing direction content MUST be duplicated between Slots 8, 12, 15, and 17 — each piece of writing direction lives in exactly one slot (Slot 17).
- **FR-015**: Existing style profiles in the database MUST be updated to populate all new required SteeringProfile fields before the code change goes live.
- **FR-016**: The word target range displayed in Slot 17 MUST be sourced from the SteeringProfile. For Character-variant prompts, it MUST use `WordTargetMin`/`WordTargetMax`. For Narrative-variant prompts, it MUST use `NarrativeWordTargetMin`/`NarrativeWordTargetMax` (intentionally longer than Character).
- **FR-017**: The Style Profile management UI MUST expose editable fields for ImmersionDirective, ActionDirective, Character Word Target Min/Max (`WordTargetMin`/`WordTargetMax`), and Narrative Word Target Min/Max (`NarrativeWordTargetMin`/`NarrativeWordTargetMax`), with persistence to the StyleProfiles table.
- **FR-018**: The Scenario narrative settings UI MUST expose editable fields for Tone, Register, and Focus, with persistence to the scenario's NarrativeSettings payload. The legacy NarrativeTone field MUST be deprecated in the UI (hidden or read-only).
- **FR-019**: All UI changes for this feature MUST be grouped into a single implementation phase sequenced last, after data model changes, prompt slot changes, and profile data cleanup, so they can be implemented by a dedicated agent.
- **FR-020**: Persisted feature data MUST use SQLite unless this spec explicitly states and justifies a different store.
- **FR-021**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-022**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-023**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities

- **SteeringProfile (Style Profile)**: Defines a writing style with Prose Style description, Voice (Rule of Thumb), Immersion Directive, Action Directive, and per-variant Word Target ranges (Character: `WordTargetMin/Max`; Narrative: `NarrativeWordTargetMin/Max`, intentionally longer). Drives the Prose Style, Voice, immersion, action, and word target components of Slot 17.
- **IntensityProfile (Tone Profile / Heat Level)**: Defines a heat level with label and writing contract text describing the intensity of content. Drives the Heat Level component of Slot 17. Must contain only heat-level language.
- **NarrativeSettings**: Scenario-level configuration for Tone, Register, and Focus. Drives the Tone component of Slot 17. Legacy NarrativeTone field retained for backward compatibility.
- **Slot 17 (Final Instruction)**: The consolidated slot at the end of the prompt containing all writing direction plus scene direction. The relative ordering of its Writing Instruction and Scene Direction blocks is a research question — both affect the final content the model reads before generating.
- **Slot 8 (WritingStyle, contextual)**: After consolidation, a contextual/structural slot in Zone B with no writing direction text.
- **Slot 15 (IntensityPacing, structural)**: After consolidation, a structural slot in Zone B containing only available positions — no heat or pacing text.
- **Phase Directive (Scene Direction)**: Scene-level guidance describing what the current phase should accomplish. Renamed from "Phase Rule of Thumb" to "Scene Direction." Relative ordering with Writing Instruction is a research question — both attention patterns (primacy and recency) are relevant and the final order will be validated through integration testing.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Writers can identify the purpose of every prompt section from its label alone — each label maps to a recognized writer's craft term (Prose Style, Heat Level, Voice, Scene Direction, Tone).
- **SC-002**: All writing direction for a turn appears in exactly one location (Slot 17) — no reader must cross-reference multiple slots to understand what writing rules are in effect.
- **SC-003**: Changing a style profile's immersion directive, action directive, or word target takes effect on the next built prompt without code changes or server restart.
- **SC-004**: A missing required SteeringProfile field produces a clear, actionable error message identifying the specific profile and missing field before any prompt is sent to the model.
- **SC-005**: The "Atmospheric" profile no longer appears in Intensity/Heat Level selection — it is only available as a Style/Prose Style option.
- **SC-006**: The ordering of Scene Direction and Writing Instruction, once determined by planning-phase research, is validated through three integration testing methods: (A) manual qualitative review of N sample generations per ordering against a 4-item checklist (POV, Heat, Scene Direction, Word Target); (B) automated scoring script checking objective markers (dialogue presence for narrative variant, word count, POV pronouns); (C) single-author subjective review. The chosen ordering must pass all three methods and demonstrate compliance no worse than the pre-consolidation baseline.
