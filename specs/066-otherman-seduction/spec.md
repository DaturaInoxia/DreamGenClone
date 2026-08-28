# Feature Specification: OtherMan Seduction Archetype

**Feature Branch**: `066-otherman-seduction`  
**Created**: 2026-08-11  
**Status**: Draft  
**Input**: User description: "OtherMan Seduction Archetype feature for NTR roleplay engine"

## Research: Erotic Fiction Seduction Archetypes

The following archetypes are drawn from analysis of classic and contemporary erotic fiction tropes — specifically, the behavioral patterns competent seducers employ in the Netorare (NTR) and erotic romance genres. These are NOT generic romantic advice; they are grounded in how seduction actually operates in the genre.

### Primary Archetypes

| # | Archetype | Core Behavior | Genre Examples |
|---|-----------|---------------|----------------|
| 1 | **The Charmer / Smooth Talker** | Verbal seduction: calibrated compliments, witty banter, knows exactly what to say. Makes her feel uniquely seen, desired, and special. Uses words to create intimacy before any physical move. | Billionaire romance, "silver tongue" characters |
| 2 | **The Competent / Capable Man** | Demonstrates physical competence and reliability. Fixes broken things, performs manual labor (often shirtless), displays strength and skill. She watches, aroused by his competence and physique. Creates debt through acts of service. | Handyman, woodsman, contractor, mechanic tropes |
| 3 | **The Confidante / Emotional Connection** | Builds emotional intimacy through attentive listening, understanding her frustrations (especially about her husband/relationship), being the "shoulder to cry on." Creates the "he actually understands me" realization. Positions himself as the emotional alternative to a neglectful partner. | "Best friend's brother," coworker-confidant, neighbor |
| 4 | **The Tease / Playful Provocateur** | Uses humor, playfulness, and light provocation. Creates sexual tension through banter, teasing, and "accidental" physical contact — a hand brushing hers, standing too close, a lingering look. The "will they / won't they" dynamic. Makes her laugh, then makes her want. | Romantic comedy crossover, friends-to-lovers |
| 5 | **The Protector / Rescuer** | Creates or leverages damsel-in-distress scenarios. Saves her from danger, difficulty, or vulnerability. Triggers the gratitude-attraction pathway. Positions himself as the safe harbor in chaos. | Action-romance, thriller-romance, Western |
| 6 | **The Dominant / Assertive** | Direct physical presence, confident body language, takes what he wants (within consent boundaries). Overwhelming attraction as a force she cannot resist. Creates polarity through certainty — he knows what he wants and it's her. | Dark romance, alpha-male tropes, "claimed" narratives |
| 7 | **The Mysterious / Dangerous Stranger** | Intrigue through mystery and unpredictability. She's drawn to figure him out. Danger as aphrodisiac — the risk IS part of the attraction. Reveals himself in controlled doses, keeping her wanting more. | Bad boy, stranger-passing-through, "he's trouble" |
| 8 | **The Situational / Opportunist** | Exploits circumstance. Stuck together (snowed in, broken-down car, shared room), heightened emotional states (grief, celebration, intoxication), proximity as catalyst. The situation does the work; he just needs to be present and willing. | Forced proximity, "only one bed," vacation romance |

### Key Principle: Archetypes Are Not Exclusive

In effective erotic fiction, competent seducers typically blend 2-3 archetypes, shifting between them as the situation demands. A character might be primarily a Competent/Confidante (woodcarver who listens) or a Charmer/Tease (witty + playful). The archetypes describe *behavioral modes*, not rigid personality boxes.

The system should support this blending — a character profile defines which archetypes the OtherMan employs and in what proportion, and the prompt guidance reflects the blend.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Configure OtherMan Seduction Style Per Character (Priority: P1)

A scenario author wants to define HOW a specific OtherMan character seduces — not just THAT he pursues the Wife. The author picks from research-backed seduction archetypes (or a blend of them) and those behaviors appear in the narrative.

**Why this priority**: This is the core value proposition. Without per-character seduction style, the model defaults to the character's occupation (e.g., "woodcarver gives her wood carvings") instead of employing genre-appropriate seduction behaviors. Every session with an OtherMan character is affected.

**Independent Test**: Create a scenario with an OtherMan character assigned a seduction archetype blend. Start a session. Verify the OtherMan's dialogue and actions reflect the assigned archetype behaviors (e.g., a Competent archetype shows physical displays of skill, a Charmer uses calibrated compliments) rather than defaulting to occupation-based behavior.

**Acceptance Scenarios**:

1. **Given** a scenario with an OtherMan character configured as "Competent + Confidante", **When** the session runs through the Opening and BuildUp phases, **Then** the OtherMan's actions include physical displays of competence (fixing things, manual labor) AND emotional connection-building (listening, understanding her frustrations).
2. **Given** a scenario with an OtherMan character configured as "Charmer + Tease", **When** the session runs, **Then** the OtherMan uses verbal seduction (compliments, witty banter) and playful provocation (teasing, "accidental" contact).
3. **Given** an OtherMan character with no seduction archetype configured, **When** the session runs, **Then** the system falls back to the role-level default guidance (SteerRoleIntentCatalog) without the occupation-defaulting behavior observed today.

---

### User Story 2 - Research-Backed Role-Level Defaults (Priority: P1)

The `SteerRoleIntentCatalog` OtherMan TOWARDS intent should reflect actual erotic fiction seduction patterns, not generic romantic advice. Today's catalog entry (updated 2026-08-11 with generic "compliment her, make her laugh, help with tasks") should be replaced with research-grounded behavioral directives.

**Why this priority**: The role catalog is the fallback for any session where no per-character archetype is configured. It affects ALL OtherMan sessions. Getting this right means even unconfigured OtherMan characters seduce competently.

**Independent Test**: Inspect the catalog text. Verify it describes archetype-based behavioral modes with concrete examples drawn from genre analysis, not generic relationship advice.

**Acceptance Scenarios**:

1. **Given** the updated SteerRoleIntentCatalog, **When** reviewing the OtherMan TOWARDS intent text, **Then** it references specific archetype behaviors (e.g., "display physical competence," "create emotional intimacy through attentive listening") with genre-appropriate framing.
2. **Given** an OtherMan character with no per-character archetype configured, **When** steering options are generated for the TOWARDS direction, **Then** the model receives seduction guidance grounded in erotic fiction tropes, not generic courtship advice.

---

### User Story 3 - Seduction Guidance in Continuation Prompts (Priority: P2)

During normal continuation (not just steering), the OtherMan character's prompt should carry role-specific seduction guidance so that the model generates competent seduction behavior on every turn, not just when the user is actively steering.

**Why this priority**: Steering is intermittent; continuation is constant. The OtherMan should seduce competently in every generated response. However, P1 stories (the data model and catalog) must be in place first since the continuation prompt guidance depends on the archetype data.

**Independent Test**: With an OtherMan character configured with a seduction archetype, run several continuations without issuing any steering commands. Verify the OtherMan's generated dialogue and actions consistently reflect the archetype's behavioral patterns.

**Acceptance Scenarios**:

1. **Given** an OtherMan character configured as "Dominant + Charmer", **When** the session runs normal continuations (no steering), **Then** the OtherMan's narrative actions include confident physical presence, direct intent, and calibrated verbal seduction.
2. **Given** an OtherMan character configured as "Tease + Situational", **When** the session runs continuations, **Then** the OtherMan exploits proximity, creates playful tension, and uses situational context as seduction leverage.

---

### User Story 4 - Scenario Author UI for Archetype Selection (Priority: P3)

A scenario author needs a UI to assign seduction archetypes to OtherMan characters in the scenario editor. The UI should present the research-backed archetypes with descriptions, allow multi-select (blending), and show a preview of the resulting behavioral guidance.

**Why this priority**: The data model and prompt injection must work first. The UI is the last layer — authoring convenience that depends on P1 and P2 being stable.

**Independent Test**: Open the scenario editor, navigate to an OtherMan character, see the archetype selector, pick 2-3 archetypes, save. Verify the selection persists and the preview shows the blended guidance text.

**Acceptance Scenarios**:

1. **Given** the scenario editor with an OtherMan character, **When** the author opens character settings, **Then** a seduction archetype section is visible with all 8 archetypes and their descriptions.
2. **Given** the archetype selector, **When** the author selects "Competent" and "Confidante", **Then** a preview shows the combined behavioral guidance that will appear in prompts.
3. **Given** selected archetypes, **When** the author saves the scenario and re-opens it, **Then** the archetype selections are persisted and restored.

---

### Edge Cases

- What happens when a scenario has multiple OtherMan characters with different seduction archetypes? Each should receive its own archetype guidance independently.
- What happens when an OtherMan character's role changes (e.g., from OtherMan to Neutral)? The seduction archetype guidance should only apply when the role is OtherMan.
- What happens when no archetype is configured? Fall back to the role-level `SteerRoleIntentCatalog` TOWARDS intent (which itself is now research-backed).
- What happens when all 8 archetypes are selected? The system should handle the extreme case gracefully — the blended guidance should remain coherent, not contradictory.
- How does the seduction archetype interact with B-077 gap-aware steering? The archetype defines *how* the OtherMan seduces; gap-aware steering defines *what outcome* to steer toward. They are complementary — the archetype is the behavioral palette, gap awareness is the tactical objective.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support 8 seduction archetypes for the OtherMan role: Charmer, Competent, Confidante, Tease, Protector, Dominant, Mysterious, Situational — each with a prose description of the behavioral mode grounded in erotic fiction genre analysis.
- **FR-002**: System MUST allow each OtherMan character to be assigned one or more seduction archetypes that blend into a coherent behavioral directive.
- **FR-003**: System MUST store seduction archetype assignments on the Character entity within the scenario — NOT in session-scoped state — so the same character behaves consistently across sessions.
- **FR-004**: System MUST inject per-character seduction guidance into the continuation prompt for the OtherMan actor, sourced from the character's assigned archetype blend (or the role-level catalog fallback if unassigned).
- **FR-005**: The `SteerRoleIntentCatalog` OtherMan TOWARDS intent MUST be updated to reflect the research-backed archetypes, replacing the 2026-08-11 generic courtship text with genre-grounded seduction behavioral directives.
- **FR-006**: The role-level catalog OtherMan TOWARDS intent MUST serve as the fallback when a character has no archetype configured — no other fallback path may exist.
- **FR-007**: Seduction archetype guidance MUST only apply to characters with the OtherMan role. Characters with other roles (Wife, Husband, Unknown) MUST NOT receive seduction archetype injection.
- **FR-008**: The per-character archetype guidance and the B-077 gap-aware steering directive MUST NOT conflict. The archetype defines *behavioral style*; B-077 defines *gap-closing tactical objective*. Both may co-exist in the prompt.
- **FR-009**: System MUST provide a UI in the scenario editor character settings for selecting seduction archetypes, including: archetype name, description, multi-select capability, and a live preview of the blended guidance text.
- **FR-010**: Persisted feature data MUST use SQLite unless this spec explicitly states and justifies a different store.
- **FR-011**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-012**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.

### Key Entities

- **SeductionArchetype**: A named behavioral mode (e.g., "Charmer", "Competent") with a prose description of the seduction pattern. Defined in code as a static catalog (8 entries). Each archetype has: a unique identifier, a display name, a genre-grounded behavioral description including concrete example behaviors.

- **Character.SeductionArchetypes**: A list of archetype identifiers stored on the Character entity. Represents which archetypes this OtherMan character employs and in what blend. Persisted as part of the scenario JSON in SQLite. When empty, the role-level catalog fallback applies.

- **OtherManSeductionGuidance**: A computed prompt text derived from the character's archetype blend. Generated at prompt-build time. Contains the prose behavioral directive injected into the continuation prompt via the existing slot architecture.

- **SteerRoleIntentCatalog.OtherMan.TOWARDS**: The updated role-level default intent text. The single fallback source for any OtherMan character without per-character archetypes configured.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In sessions with an archetype-configured OtherMan, the OtherMan character's generated actions and dialogue reflect the assigned archetype behaviors in at least 80% of turns, as measured by manual review of generated narrative output.
- **SC-002**: In sessions with NO archetype configured, the OtherMan character no longer defaults to occupation-based seduction (e.g., "gives her wood carvings") and instead employs genre-appropriate seduction behaviors from the role-level catalog.
- **SC-003**: Scenario authors can configure seduction archetypes for an OtherMan character in under 1 minute, from opening the character settings to saving.
- **SC-004**: The archetype guidance text for each of the 8 archetypes is distinct and recognizable — a reviewer can identify which archetype(s) are active from reading the prompt text alone.
- **SC-005**: Existing sessions and scenarios without archetype configuration continue to function without degradation — the feature is purely additive and the catalog fallback preserves existing behavior quality.

---

## Architecture Decisions

### Where Archetype Guidance Lives

| Layer | What | Purpose |
|-------|------|---------|
| **SteerRoleIntentCatalog** (Domain) | Updated OtherMan TOWARDS intent | Role-level default for all OtherMan characters. Fallback when no per-character archetypes configured. |
| **Character entity** (Domain/Scenarios) | `SeductionArchetypes` list | Per-scenario, per-character archetype blend. Source of truth for THIS OtherMan's seduction style. |
| **Continuation prompt** (Application) | Injected via existing slot architecture | Per-turn behavioral guidance. Uses character archetypes if present, catalog fallback otherwise. |

### Prompt Injection Design

The per-character seduction guidance is injected through the existing prompt slot system. Two options exist:

1. **Extend `CharacterDataSlot` (Slot 5, Zone B)**: Add the archetype guidance to the existing `AppendCharacterRoleIntents()` method. This is the simplest approach — the role intent already carries the character's narrative job; the archetype guidance enriches it with behavioral specifics. The guidance appears alongside other character data in Zone B.

2. **New dedicated slot**: Create a new prompt slot (e.g., `SeductionGuidanceSlot`, Zone C, near `BehavioralFrames` or `ScenarioGuidance`). This isolates the archetype injection from character data and allows finer control over positioning.

**Recommendation**: Extend `CharacterDataSlot` (Option 1) for P1/P2. The archetype guidance is inherently character-linked; co-locating it with the character's role intent keeps the prompt coherent. A dedicated slot (Option 2) can be deferred to a later iteration if isolation proves necessary.

### Relationship to B-077 (Gap-Aware Steering)

B-077 and this feature are complementary, not overlapping:

| Aspect | B-077 (Gap-Aware Steering) | This Feature (Seduction Archetype) |
|--------|---------------------------|-----------------------------------|
| **What it controls** | Tactical objective: *close the willingness gap* | Behavioral style: *how the OtherMan seduces* |
| **When it fires** | Steering option generation only | Every continuation turn |
| **Data source** | Willingness computation + active theme's semantic mappings | Character's archetype configuration |
| **Output** | Gap-closing event hints in steer prompt | Behavioral style guidance in continuation prompt |

Both may appear in the same prompt without conflict — one says "steer toward Loyalty ↓ events," the other says "use Competent + Confidante behaviors."

---

## Assumptions

- The 8 archetypes defined here are the initial set. Additional archetypes may be added later based on usage data and user feedback.
- The archetype catalog is code-defined (like `SteerRoleIntentCatalog`) rather than database-configurable, since the archetypes represent genre analysis findings that don't change per-session.
- The archetype blend weighting (e.g., "60% Competent, 40% Confidante") is a future enhancement. The initial implementation treats all selected archetypes equally.
- The scenario editor UI (P3) updates the existing character settings panel; it does not require a new page or modal.
