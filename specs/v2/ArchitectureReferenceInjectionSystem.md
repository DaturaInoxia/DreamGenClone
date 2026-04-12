# Context-Aware Reference System - Conceptual Design

## Core Problem

The LLM needs access to behavioral terminology, kink definitions, and character dynamics guidance—but injecting everything creates context bloat and confusion. The system must intelligently surface only relevant information based on current character stats, scenario context, and narrative state.

---

## High-Level Architecture

### Three-Layer Design

```
Layer 1: Reference Data Layer
    ↓ stores organized behavioral concepts and terminology
    
Layer 2: Relevance Engine
    ↓ determines which concepts apply to current state
    
Layer 3: Prompt Injection
    ↓ surfaces only relevant guidance into LLM prompts
```

---

## Layer 1: Reference Data Structure

### Concept Definition Model

Each behavioral concept includes:

- **Identity** - Unique ID and descriptive term
- **Category** - Groups related concepts (HusbandAwareness, Cuckolding, Humiliation, Agency, etc.)
- **Description** - Human-readable explanation of what the concept means
- **Behavioral Guideline** - Instruction for the LLM on how to portray this behavior
- **Related Terms** - Synonyms and connected concepts
- **Trigger Conditions** - Which stat ranges make this concept relevant

### Reference Collections

Organized by purpose:
- **Husband Dynamics Reference** - Awareness, participation, humiliation, voyeurism scenarios
- **Wife Dynamics Reference** - Loyalty, agency, connection, desire behaviors
- **Scenario Concepts Reference** - Specific acts, positions, settings
- **Power Dynamic Reference** - Dominance/submission, control, FLR concepts
- **Kink Terminology Reference** - Sexual practices, fetishes, lifestyle terms

### Default vs. Custom

- **Default References** - Pre-seeded comprehensive library of concepts
- **Custom References** - User-created scenario-specific concepts
- **Profile References** - Attached to specific persona/archetype profiles

---

## Layer 2: Relevance Engine

### Decision Logic Flow

The relevance engine evaluates character stats and scenario context to determine applicable concepts:

```
Input: Character stats + Scenario context
    ↓
Evaluate Each Concept's Trigger Conditions
    ↓
Match Stat Values to Ranges
    ↓
Apply Scenario Context Filters
    ↓
Prioritize by Relevance Score
    ↓
Output: Filtered concept list
```

### Relevance Factors

**Stat-Based Matching**
- Each concept defines stat thresholds that activate it
- Example: "Cuckold Husband" activates when HumiliationDesire ≥ 70
- Multiple stat conditions must all be met for concept to apply

**Context-Based Filtering**
- Scenario type (Cheating, Hotwifing, Threesome, etc.)
- Participants involved (Who is in the scene?)
- Relationship dynamics (Married, dating, strangers)
- Narrative state (Early scene, climax, resolution)

**Conflict Resolution**
- When multiple concepts apply, prioritize by:
  1. Specificity (more specific concepts override general ones)
  2. Recency (most recently relevant concepts)
  3. User preference (user can set priority weights)

**Dependency Resolution**
- Some concepts imply others
- Example: "Full MMF Participant" implies "Threesome" and "Bisexual"
- Engine includes dependent concepts automatically

### Output Structuring

Group relevant concepts by category for clarity:
- Husband awareness level
- His emotional state
- His participation level
- Wife's current stance
- Agency/perspective dynamics
- Appropriate acts/scenarios

---

## Layer 3: Prompt Injection Strategy

### Injection Points

**Character Introduction**
- When a character enters the scene
- Inject: Core identity concepts, behavioral baseline

**Interaction Start**
- Before generating an interaction
- Inject: Relevant dynamics between participants

**Scenario Transition**
- When scenario changes (e.g., from talking to sexual activity)
- Inject: New context-appropriate concepts

**Stat Change**
- When character stats shift significantly
- Inject: Behavioral shift indicators

**User Override**
- When user provides specific instruction
- Inject: Override concept, suppress conflicting concepts

### Injection Format

Behavioral guidelines formatted as:
- Concept label and term
- Concise behavioral guideline
- Practical instructions for portrayal
- Limitations/boundaries (what NOT to do)

### Content Fidelity

- **High fidelity** for personality-driven concepts (cues, mannerisms, dialogue patterns)
- **Medium fidelity** for act descriptions (position, pacing, sensory details)
- **Low fidelity** for explicit mechanical details (left to LLM's natural generation)

---

## Storage and Retrieval Strategy

### Persistence Approach

Reference data stored in database as:
- Reference records with metadata
- Concept records linked to references
- Stat trigger conditions encoded as criteria

### Caching Layer

- Cache frequently accessed reference collections
- Pre-compute relevance lookups for common stat combinations
- Invalidate cache when references or stats change

### Lookup Performance

- Index by concept ID, category, and stat ranges
- Fast query for "all concepts active for these stats"
- Hierarchical lookup for concept dependencies

---

## Customization Strategy

### User-Defined Concepts

Users can create custom concepts by:
- Defining concept metadata (ID, term, category)
- Writing behavioral guidelines
- Specifying trigger conditions
- Linking to existing concepts

### Profile Attachments

Custom concepts can be attached to:
- Specific scenario profiles
- Character archetype profiles
- Session-level preferences

### Concept Versioning

- Track concept changes over time
- Maintain historical versions for session continuity
- Allow rollback to previous definitions

---

## Integration Points

### With Role-Play Engine

When generating interactions:
- Pass character stats to relevance engine
- Get relevant concept list
- Inject behavioral guidelines into prompt
- Use concepts to guide interaction generation

### With Adaptive State

When stats update:
- Re-evaluate active concepts
- Detect concept transitions (e.g., "Wavered Wife" → "Unfaithful Wife")
- Trigger appropriate narrative responses

### With Willingness System

When determining what acts are acceptable:
- Consult willingness thresholds for current stat state
- Cross-reference with applicable concepts
- Ensure consistency between willingness and concepts

---

## Error Handling and Fallbacks

### Missing Reference Data

- If concept not found, use generic fallback
- Log missing concept for review
- Graceful degradation (no prompt failure)

### Conflicting Concepts

- Detect logical conflicts (e.g., "Faithful" and "Cheating" both active)
- Apply priority rules
- Flag for human review if unresolved

### Stat Out of Range

- Handle edge cases (stats at 0 or 100)
- Apply extreme-case concepts
- Prevent concept contradictions

---

## Performance Considerations

### Context Window Management

- Limit injected concepts to prevent token bloat
- Prioritize most critical concepts
- Use concise behavioral guidelines
- Truncate if approaching limits

### Caching Strategies

- Cache relevance results for stat combinations
- Pre-compute concept dependencies
- Lazy load heavy reference collections

### Async Operations

- Reference lookups should be non-blocking
- Stream concept injection where possible
- Parallelize relevance evaluation

---

## Monitoring and Analytics

### Concept Usage Tracking

- Track which concepts are most frequently used
- Identify gaps in reference library
- Detect concept conflicts or overuse

### Quality Metrics

- Monitor LLM adherence to behavioral guidelines
- Collect feedback on concept effectiveness
- Identify concepts needing refinement

---

## Future Extensibility

### Machine Learning Enhancement

- Could use ML to predict relevant concepts
- Learn from successful generations
- Auto-suggest new concepts based on patterns

### Community Library

- Shared concept repositories
- User-contributed definitions
- Rated/vetted concept collections

### Dynamic Concept Generation

- Generate new concepts on-the-fly based on narrative needs
- Synthesize from existing concepts
- Learn from successful generations

---

This design provides a flexible, context-aware system for surfacing behavioral terminology to the LLM without overwhelming it, while remaining customizable and maintainable.