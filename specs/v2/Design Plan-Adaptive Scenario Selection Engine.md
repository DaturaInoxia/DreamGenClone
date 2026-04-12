# Design Plan: Adaptive Scenario Selection Engine

## Document Overview

**Project**: DreamGenClone - Adaptive Engine Redesign  
**Component**: Role Play Module - Scenario Selection System  
**Document Type**: Detailed Design Specification  
**Audience**: Implementation Team / Coding Agents  
**Status**: Ready for Implementation

---

## 1. Executive Summary

### Current System Limitations

The current adaptive engine operates on a reactive model where themes are atmospheric elements that blend together based on keyword frequency. The system continuously tracks multiple themes and selects the top two as "primary" and "secondary" based purely on keyword matching frequency in the text. This approach creates a disconnect between user preferences and narrative direction, as the system cannot actively steer toward user-desired scenario types.

### Design Goals

Transform the adaptive system from a passive theme tracker into an active scenario selection engine that:

1. **Selects and commits to single narrative scenarios** rather than blending multiple themes
2. **Uses character states to determine which scenario type is narratively appropriate**
3. **Implements a build-up → commitment → climax → reset cycle** for each scenario
4. **Prioritizes user preferences while respecting narrative coherence**
5. **Provides scenario-specific guidance during climactic moments**

### High-Level Changes

- Replace blended theme selection with exclusive scenario commitment
- Add character state-to-scenario mapping logic
- Implement narrative phase detection (build-up, committed, approaching, climax, reset)
- Add scenario-specific instruction generation for climactic moments
- Implement semi-reset mechanism to cycle between scenarios

---

## 2. Problem Analysis

### Problem 1: Theme Blending Creates Narrative Ambiguity

**Current Behavior:**
- System tracks all available themes simultaneously
- Selection rule picks top two themes based on keyword frequency
- Result is a "blend" of themes (e.g., "a mix of tension and trust elements")

**Issue:**
- User preferences (MustHave, StronglyPrefer) do not strongly influence selection
- The system reflects what is in the text rather than guiding where the story should go
- Multiple active themes make the narrative direction unclear

**Impact:**
- Story drifts rather than progressing toward intended scenarios
- User feels lack of control over narrative direction
- Climactic moments lack clear scenario context

### Problem 2: No Scenario Commitment Mechanism

**Current Behavior:**
- Themes compete continuously throughout interactions
- Theme selection can flip back and forth as keyword patterns change
- No concept of "locking in" a narrative direction

**Issue:**
- No build-up toward specific scenario types
- Narrative lacks focus and momentum
- Cannot distinguish between "exploring options" vs. "committing to a path"

**Impact:**
- Story feels aimless during build-up phase
- Climactic moments lack preparation and foreshadowing
- Users cannot anticipate which scenario type will emerge

### Problem 3: Character States Influence Intensity but Not Scenario Type

**Current Behavior:**
- Character stats (Desire, Restraint, Tension, Connection, Dominance) primarily influence intensity calculation
- Stats do not determine which scenario type makes narrative sense
- Same stat patterns could lead to different narrative outcomes

**Issue:**
- High tension could apply equally to multiple incompatible scenarios
- No logic to determine if tension implies secrecy vs. anticipation vs. conflict
- Character relationship state does not drive scenario selection

**Impact:**
- Stat evolution feels disconnected from narrative direction
- Character choices don't naturally lead to appropriate scenario types
- System misses opportunities to align narrative with character development

### Problem 4: No Narrative Cycle Management

**Current Behavior:**
- Theme scores only increase (clamped but not reset)
- System has no concept of narrative completion or transition
- No mechanism to move from one scenario to the next

**Issue:**
- Stories cannot progress through multiple distinct phases
- No way to signal completion of one scenario type
- Scores become saturated and stop being meaningful

**Impact:**
- Long sessions lose narrative momentum
- Cannot structure stories as multiple distinct encounters
- System cannot support the "episodic" structure users expect

### Problem 5: Generic Climax Guidance

**Current Behavior:**
- AI prompts include general references to theme intensity
- No scenario-specific instructions for climactic moments
- Generic guidance across all scenario types

**Issue:**
- Climactic content lacks scenario-specific context
- AI may generate content that doesn't match the selected scenario type
- Missed opportunity to provide detailed scenario framing

**Impact:**
- Climactic moments feel generic rather than tailored
- Scenario type isn't clearly established in the content
- User may feel content doesn't match intended scenario

---

## 3. Proposed Architecture

### 3.1 New Component: Scenario Selection Engine

**Purpose:** Evaluate character states and user preferences to determine the most appropriate scenario type for the current narrative context.

**Responsibilities:**
- Analyze current character states across all active characters
- Evaluate which scenario types from user preferences are narratively viable
- Calculate "fit scores" for each available scenario based on state patterns
- Select the highest-fit scenario that aligns with user preferences
- Provide confidence metrics for the selection

**Key Concepts:**
- **Scenario Type:** A mutually exclusive narrative configuration (e.g., marital infidelity with unaware partner, consensual non-monogamy with partner involvement, etc.)
- **Fit Score:** A calculated value (0.0 to 1.0) indicating how well current character states align with a scenario type
- **Narrative Viability:** Whether a scenario type makes logical sense given character relationship states and emotional context

**Input:**
- Current character states for all active characters
- User's ranked scenario preferences (MustHave, StronglyPrefer, NiceToHave, etc.)
- Current theme tracker state
- Historical context (recent interactions, established patterns)

**Output:**
- Selected scenario type identifier
- Confidence score for the selection
- Alternative scenario options with their fit scores
- Rationale for the selection (for logging/debugging)

### 3.2 New Component: Narrative Phase Manager

**Purpose:** Track and manage the narrative cycle through build-up, commitment, approaching, climax, and reset phases.

**Responsibilities:**
- Detect current narrative phase based on theme scores, character states, and interaction count
- Manage transitions between phases
- Control timing of scenario commitment and climax triggers
- Coordinate with scenario selection engine for phase-appropriate decisions

**Phases:**

1. **Build-Up Phase**
   - Character states evolving, no scenario committed yet
   - System evaluating which scenario is emerging
   - Multiple scenarios potentially viable
   - Character choices creating directional signals

2. **Committed Phase**
   - Single scenario type selected and locked
   - Character choices and content steered toward this scenario
   - Competing scenarios suppressed
   - Clear narrative direction established

3. **Approaching Phase**
   - Theme scores nearing climax threshold
   - Character states indicate readiness for climactic moment
   - Intensity increasing
   - Content focusing on scenario-specific build-up elements

4. **Climax Phase**
   - Threshold conditions met
   - Scenario-specific instructions activated
   - Full intensity content generation
   - Focus on the selected scenario's defining characteristics

5. **Reset Phase**
   - Climax completed
   - Scores partially decayed (not zeroed)
   - Character states partially reset
   - System evaluates next scenario type from preferences

**Phase Transition Logic:**
- Build-up → Committed: When a scenario type achieves sufficient fit score and user preference alignment
- Committed → Approaching: When theme score exceeds threshold and character states indicate readiness
- Approaching → Climax: When theme score, desire level, and restraint level all reach climactic thresholds
- Climax → Reset: After climactic content is generated and user indicates completion (or automatic trigger based on content)
- Reset → Build-up: After reset completes, starting cycle for next scenario

### 3.3 Enhanced Component: Character State to Scenario Mapper

**Purpose:** Define the logical rules that map specific character state patterns to scenario types.

**Responsibilities:**
- Define scenario type profiles (what character states indicate each scenario type)
- Calculate fit scores for each scenario type given current states
- Handle multiple character state combinations (e.g., wife vs. husband states in marriage scenarios)
- Provide explanations for why a scenario type is or isn't viable

**Mapping Logic Examples:**

**For scenarios involving secrecy and lack of awareness:**
- High tension in one character (indicating stress from withholding)
- Low connection between characters (emotional distance)
- Moderate to high restraint (internal conflict about actions)
- Low tension in unaware character (no suspicion)
- Fit increases as these conditions align

**For scenarios involving consensual non-monogamy without partner participation:**
- High desire in primary character (enthusiastic participation)
- Low restraint in primary character (no shame or hesitation)
- Moderate to high connection between partners (shared fantasy context)
- Desire-focused state patterns dominate
- Fit increases as enthusiasm and lack of restraint increase

**For scenarios involving consensual non-monogamy with partner participation:**
- High desire in both characters
- Low restraint in both characters
- High connection between characters (mutual understanding)
- Both characters aligned in state patterns
- Fit increases as both show high desire and low restraint

**For scenarios involving power dynamics:**
- Imbalance in dominance levels between characters
- High tension in submissive character
- Moderate restraint (controlled submission)
- Connection levels vary based on consent context
- Fit increases as dominance imbalance becomes clear

### 3.4 Enhanced Component: Scenario-Specific Guidance Generator

**Purpose:** Generate detailed, scenario-specific instructions for AI content generation, particularly during climactic moments.

**Responsibilities:**
- Maintain a library of scenario-specific guidance templates
- Generate contextual instructions based on current phase and scenario type
- Incorporate character state information into guidance
- Ensure guidance is scenario-appropriate and consistent

**Guidance Categories:**

**Build-Up Guidance:**
- Subtle hints toward the emerging scenario type
- Character dialogue and action suggestions
- Foreshadowing elements
- Establish scenario context without explicit declaration

**Committed Guidance:**
- Clear focus on the selected scenario type
- Character choices and dialogue aligned with scenario
- Specific emotional tones appropriate to scenario
- Avoid elements of competing scenarios

**Approaching Guidance:**
- Heightened intensity and focus
- Character reactions anticipating climactic moment
- Scenario-specific emotional beats
- Clear build toward threshold conditions

**Climax Guidance:**
- Detailed scenario-specific context and expectations
- Character perspectives and viewpoints appropriate to scenario
- Emotional tone and relational dynamics specific to scenario
- Avoid elements that would contradict the selected scenario type

**Reset Guidance:**
- Post-climax emotional processing
- Transition to new narrative direction
- Character reflections on completed scenario
- Setup for next scenario evaluation

### 3.5 Enhanced Component: Semi-Reset Mechanism

**Purpose:** Manage the transition between scenarios by partially resetting states while preserving narrative continuity.

**Responsibilities:**
- Reduce theme scores to non-zero baseline levels (e.g., 30% of current value)
- Partially reset character states (reduce high values, preserve moderate values)
- Clear short-term evidence accumulation
- Prepare system for evaluating next scenario type
- Log transition events for audit trail

**Reset Strategy:**

**Theme Score Reset:**
- Retain 20-30% of current theme scores
- Zero out interaction evidence signal
- Zero out scenario phase signal
- Preserve choice signal from user preferences
- Set intensity levels to "Minor" or "Moderate"

**Character State Reset:**
- Reduce extreme values toward middle range (50)
- Desire: Reduce by 20-30 points, minimum 50
- Restraint: Increase toward 50 if below, decrease toward 50 if above
- Tension: Reduce by 15-20 points
- Connection: Preserve current values (relationship continuity)
- Dominance: Reduce by 10-15 points

**Evidence Reset:**
- Clear recent evidence list (or trim to last 10 entries)
- Remove phase-specific evidence entries
- Preserve critical transition events

**Scenario Commitment Reset:**
- Clear active scenario identifier
- Reset narrative phase to Build-Up
- Increment completed scenarios counter
- Log scenario completion in history

---

## 4. Data Model Changes

### 4.1 RolePlayAdaptiveState Enhancements

**New Properties:**

- `ActiveScenarioId`: Identifier of the currently committed scenario type
- `ScenarioCommitmentTime`: Timestamp when the current scenario was committed
- `CurrentNarrativePhase`: Enum value indicating current phase (BuildUp, Committed, Approaching, Climax, Reset)
- `CompletedScenarios`: Count of scenarios completed in current session
- `ScenarioHistory`: Dictionary tracking completed scenarios with metadata (completion time, interaction count, peak score)

**Modified Behavior:**

- Theme tracker now serves as input to scenario selection rather than being the primary selection mechanism
- Primary and Secondary theme fields become secondary to ActiveScenarioId
- Theme scores reflect narrative evidence but do not solely determine direction

### 4.2 ThemeCatalogEntry Enhancements

**New Properties:**

- `ScenarioTypeClassification`: Categorizes theme as "atmospheric" or "scenario-defining"
- `DirectionalKeywords`: List of keywords that indicate intentional direction toward this scenario (not just presence of elements)

**Modified Behavior:**

- Scenario-defining themes are treated as mutually exclusive targets
- Directional keywords trigger scenario commitment when detected
- Atmospheric themes can coexist with scenario-defining themes

### 4.3 ThemeTrackerItem Enhancements

**New Properties:**

- `IsScenarioCandidate`: Boolean indicating if this theme is eligible for scenario commitment
- `NarrativeFitScore`: Calculated fit score (0.0 to 1.0) for this theme as a scenario type
- `LastCandidateEvaluationTime`: Timestamp of last fit score calculation

**Modified Behavior:**

- Score now represents narrative evidence accumulation, not selection priority
- Fit score drives scenario selection logic
- Candidate status controlled by narrative phase and user preferences

### 4.4 New Domain Model: ScenarioMetadata

**Properties:**

- `ScenarioId`: Identifier of the scenario type
- `CompletedAtUtc`: Timestamp of scenario completion
- `InteractionCount`: Number of interactions during this scenario cycle
- `PeakThemeScore`: Highest score achieved for this scenario
- `PeakDesireLevel`: Maximum desire level reached during this scenario
- `AverageRestraintLevel`: Average restraint during this scenario
- `Notes`: Optional text annotation for user reference

**Purpose:**

- Track history of completed scenarios for session analysis
- Provide data for pattern recognition and preference learning
- Enable session recap and summary generation

---

## 5. Algorithm Design

### 5.1 Scenario Selection Algorithm

**Input Processing:**

1. Gather current character states for all active characters
2. Retrieve user's ranked scenario preferences from ThemeProfile
3. Collect current theme tracker scores and evidence
4. Load recent interaction history for context

**Fit Score Calculation:**

For each scenario type in user preferences (MustHave and StronglyPrefer tiers):

1. Retrieve scenario type profile (required state patterns)
2. Calculate character state alignment score:
   - Compare each character's current states to required patterns
   - Weight each stat contribution based on scenario type requirements
   - Combine multiple character states appropriately (e.g., compare wife and husband states jointly)
3. Calculate current theme evidence score:
   - Use theme tracker score as evidence of scenario presence
4. Calculate user preference priority:
   - MustHave scenarios get priority boost
   - StronglyPrefer scenarios get moderate boost
5. Combine scores with weighted formula:
   - State alignment: 50% weight
   - Theme evidence: 30% weight
   - User preference: 20% weight
6. Clamp final score to 0.0 - 1.0 range

**Selection Logic:**

1. Sort scenarios by fit score in descending order
2. Select highest-scoring scenario if score exceeds commitment threshold (e.g., 0.6)
3. If no scenario exceeds threshold, remain in Build-Up phase
4. If multiple scenarios have similar high scores (within 0.1), defer commitment until interaction provides clearer direction
5. Log selection rationale with scores and contributing factors

**Output:**

- Selected scenario identifier (or null if no selection)
- Confidence score
- Alternative scenarios with their scores
- Detailed rationale (for debugging)

### 5.2 Narrative Phase Detection Algorithm

**Phase Transition Rules:**

**Build-Up → Committed:**
- Scenario selection algorithm returns a selected scenario with confidence ≥ threshold
- Or user explicitly selects scenario (manual override)
- Or directional keywords detected that clearly signal scenario intent

**Committed → Approaching:**
- Active scenario theme score ≥ approaching threshold (e.g., 60)
- Average desire across characters ≥ approaching threshold (e.g., 65)
- Average restraint ≤ approaching maximum (e.g., 45)
- Minimum interaction count since commitment (e.g., 3 interactions)

**Approaching → Climax:**
- Active scenario theme score ≥ climax threshold (e.g., 80)
- Average desire across characters ≥ climax threshold (e.g., 75)
- Average restraint ≤ climax maximum (e.g., 35)
- Or user explicitly triggers climax (manual override)

**Climax → Reset:**
- Climactic content generation completed
- Or user indicates completion
- Or interaction count since trigger exceeds maximum (e.g., 5 interactions)

**Reset → Build-Up:**
- Reset process completed
- New scenario evaluation ready
- Or user manually starts new scenario

**Continuous Monitoring:**

- Re-evaluate phase conditions after each interaction
- Update phase state when thresholds are crossed
- Log phase transitions with contributing factors

### 5.3 Phase-Specific Behavior

**Build-Up Phase Behavior:**

- Track all theme candidates without suppression
- Evaluate scenario fit scores continuously
- Respond to directional keywords by boosting relevant scenarios
- Provide subtle guidance toward emerging scenarios
- Allow exploration of multiple scenario options

**Committed Phase Behavior:**

- Suppress non-active scenarios (reduce their scores by 50%)
- Boost active scenario (add 25 points to score)
