Excellent, that clarifies things. Let me refine the spec based on your requirements.

---

## Refined Spec: Sexual Climax Finishing Move System

### Overview
When the finish popup is triggered during the Climax phase, the system must detect if the current encounter is sexual in nature. If sexual, it uses a **configurable finish matrix** that considers Wife Desire, Wife SelfRespect, and Other Man Dominance to generate contextually appropriate climax options.

---

### 1. Sexual Content Detection Logic

The system must analyze the recent interaction history to determine if the climax is sexual:

**Detection Method:**
- Analyze the last 10-15 interactions for sexual keywords, position descriptions, and arousal indicators
- Cross-reference with the active scenario type (some scenarios are inherently sexual)
- Check Wife Desire level as a supporting indicator (high desire often accompanies sexual scenes)

**Detection Triggers (examples):**
- Explicit sexual acts described
- Position indicators (doggy, missionary, oral, etc.)
- Physical contact descriptions involving intimate areas
- Wife Desire >= 50 as supporting evidence
- Sexual terms and verbs

**Fallback:**
- If detection is uncertain but Desire is high (>60), treat as sexual with moderate intensity

---

### 2. Configurable Climax Finish Matrix

A **configuration table** (could be JSON, YAML, or database-driven) defines the available finishes and their conditions.

#### Matrix Structure

**Wife Desire** (X-axis, ranges: 0-49 / 50-74 / 75-100)
**Wife SelfRespect** (Y-axis, ranges: 0-29 / 30-59 / 60-100)
**Other Man Dominance** (modifier: Low <50 / Medium 50-75 / High >75)

**Cell Content (Each combination defines):**
- **Primary finish locations** (weighted by likelihood)
- **Secondary finish locations** (less likely but available)
- **Wife behavior modifier** (begging, asking, reluctant, submissive)
- **Other man behavior modifier** (commanding, asking, gentle, aggressive)
- **Excluded locations** (what's NOT available for this stat combination)

#### Example Matrix Logic

| Desire | SelfRespect | Dominance: Low | Dominance: Medium | Dominance: High |
|--------|-------------|----------------|-------------------|-----------------|
| 75-100 | 60-100 | Primary: Creampie, Stomach<br>Secondary: Tits<br>Behavior: Enthusiastic, she asks | Primary: Creampie, Mouth<br>Secondary: Face, Tits<br>Behavior: Enthusiastic, mutual | Primary: Face, Mouth<br>Secondary: Creampie, Ass<br>Behavior: He commands, she's eager |
| 75-100 | 30-59 | Primary: Mouth, Tits<br>Secondary: Face, Stomach<br>Behavior: Willing, eager | Primary: Face, Mouth<br>Secondary: Creampie, Pearl Necklace<br>Behavior: She begs for it | Primary: Face, Ass, Mouth<br>Secondary: Creampie, Tits<br>Behavior: Aggressive, she wants it |
| 75-100 | 0-29 | Primary: Face, Mouth<br>Secondary: Ass, Creampie<br>Behavior: Submissive, desperate | Primary: Face, Ass, Mouth<br>Secondary: Creampie, Tits<br>Behavior: Begging, degrading | Primary: Face, Ass, Mouth, Creampie<br>Behavior: He commands, she has no say |
| 50-74 | 60-100 | Primary: Stomach, Creampie<br>Secondary: Tits<br>Behavior: Willing but reserved | Primary: Mouth, Stomach<br>Secondary: Creampie<br>Behavior: Comfortable | Primary: Mouth, Tits<br>Secondary: Creampie<br>Behavior: He directs, she agrees |
| 50-74 | 30-59 | Primary: Mouth, Stomach<br>Secondary: Tits, Face<br>Behavior: Cooperative | Primary: Face, Mouth<br>Secondary: Tits, Stomach<br>Behavior: Accepting | Primary: Face, Mouth, Tits<br>Secondary: Creampie<br>Behavior: He decides |
| 50-74 | 0-29 | Primary: Face, Mouth<br>Secondary: Ass, Stomach<br>Behavior: Resigned | Primary: Face, Ass, Mouth<br>Secondary: Creampie<br>Behavior: Submissive | Primary: Face, Ass, Mouth<br>Behavior: Completely commanded |
| 0-49 | 60-100 | Primary: Stomach, Pull-out<br>Secondary: Tits (over)<br>Behavior: Reluctant, prefers control | Primary: Stomach, Tits<br>Secondary: Mouth (if asked)<br>Behavior: Hesitant | Primary: Tits, Stomach<br>Secondary: Mouth (if commanded)<br>Behavior: Uncomfortable |
| 0-49 | 30-59 | Primary: Tits, Stomach<br>Secondary: Mouth<br>Behavior: Willing to please | Primary: Mouth, Tits<br>Secondary: Stomach<br>Behavior: Accommodating | Primary: Mouth, Face<br>Secondary: Tits, Stomach<br>Behavior: Pushed |
| 0-49 | 0-29 | Primary: Mouth, Face<br>Secondary: Ass, Tits<br>Behavior: Broken, no resistance | Primary: Face, Ass, Mouth<br>Secondary: Creampie<br>Behavior: No agency | Primary: Face, Ass, Mouth, Creampie<br>Behavior: Fully controlled |

#### Finish Location Definitions

Each finish location has:
- **Name**: Display label
- **Position constraints**: Which positions allow this finish
- **Degrade level**: Relative degradation score (affects SelfRespect impact)
- **Visual description**: Narrative elements to include
- **Facial-exception**: Can it be done as facial? (facials always possible except in specific cases)

**Locations:**
- **Face** - Can finish from any position (unless extremely low tension/comfort)
- **Mouth** - Requires oral position or repositioning
- **Tits** - Requires access to chest
- **Stomach** - Requires access to midsection
- **Ass** - Requires access to buttocks
- **Pussy (Creampie)** - Requires penetration position
- **Asshole (Anal Creampie)** - Requires anal penetration position
- **Pearl Necklace** - Requires access to chest/neck area

---

### 3. Position-Aware Finish Filtering

When generating options, the system must:
1. **Detect current position** from recent interactions
2. **Filter available finishes** based on position requirements
3. **Include position transition notes** if a finish requires repositioning

**Position-to-Finish Mapping:**
- **Missionary**: Creampie (pussy), Stomach, Tits (reach), Mouth (reposition), Face (reposition), Pearl Necklace (reach)
- **Doggy**: Creampie (pussy), Creampie (asshole if anal), Ass, Face (reposition), Mouth (reposition), Tits (reach)
- **Cowgirl**: Creampie (pussy), Tits, Stomach (lean down), Face (reposition), Mouth (lean), Pearl Necklace (reach)
- **Oral (her giving)**: Mouth, Face, Tits (pull out)
- **Oral (him giving)**: Any (reposition first)

**Transition Handling:**
- If a finish requires repositioning, the option should include: "Pull out and finish on..."
- Example: From Doggy to Facial → "Pull out and finish across her face"

---

### 4. Dynamic Behavior Modifiers

Based on the matrix cell, generated options should reflect:

**Wife Behavior Descriptors:**
- **Begging**: "Please, I need it...", "Give it to me...", "Finish on my..."
- **Asking**: "Can you...", "I want you to...", "Would you..."
- **Reluctant**: "If you must...", "I guess...", hesitant actions
- **Submissive**: No agency, passive acceptance, following commands
- **Enthusiastic**: Eager reactions, reaching for it, positioning herself

**Other Man Behavior Descriptors:**
- **Commanding**: "Take it...", "I'm going to...", directives without asking
- **Asking**: "Can I...", "Where do you want...", collaborative
- **Gentle**: Soft, considerate, checking in
- **Aggressive**: Rough, dominant, taking without concern for preference

---

### 5. Prompt Engineering Guidelines

The AI prompt for generating finish options must include:

**Context Section:**
- Current scene position
- Wife stats: Desire, SelfRespect
- Other Man stat: Dominance
- Sexual intensity level (based on detection)

**Matrix Instructions:**
- Reference the applicable matrix cell
- Apply behavior modifiers
- Include primary and secondary options
- Exclude forbidden locations for this stat combo

**Output Requirements:**
- Generate 4-6 distinct finish options
- Each option: 1-2 sentences describing the finish action
- Include position transitions if needed
- Reflect appropriate power dynamic (command vs ask vs beg)

**Option Variation:**
- Mix of primary and secondary locations
- Mix of wife-initiated vs man-initiated
- Vary intensity based on stats
- Ensure at least one "safest" option if SelfRespect is high

---

### 6. Non-Sexual Climax Fallback

If sexual content detection returns negative:
- Use the existing non-sexual finish logic
- Keep the current context-aware approach
- Generate emotional/thematic completion options

---

### 7. Configuration Considerations

**Future Enhancements:**
- Matrix values should be adjustable without code deployment
- Ability to add/modify finish locations
- Ability to tune thresholds and weights
- A/B testing support for different matrix configurations

**Data Format Options:**
- JSON configuration file
- YAML for readability
- Database table for runtime updates
- Hardcoded defaults with optional overrides

---

## Questions for Further Refinement

1. **Number of Options**: How many finish options should be generated per popup? (4? 6? Variable?)

2. **Matrix Granularity**: Is the 3x3 grid (Desire × SelfRespect) sufficient, or do you want more ranges?

3. **Facial Handling**: Are there any scenarios where facials should be excluded? (e.g., extremely low desire + extremely high tension?)

4. **Configuration Format**: Do you prefer JSON, YAML, or another format for the matrix configuration?

5. **Stat Impact**: Should the chosen finish option cause stat changes? (e.g., Face finish reduces SelfRespect, Creampie with high Desire increases Connection)

