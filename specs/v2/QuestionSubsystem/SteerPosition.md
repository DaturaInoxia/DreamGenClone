# Spec: Sexual Position Steer Popup System

## Overview
When the steer popup is triggered during sexual encounters (typically in BuildUp, Committed, or Approaching phases), the system generates a **configurable position selection matrix** that considers Wife Desire, Wife SelfRespect, Wife Dominance, and Other Man Dominance to provide contextually appropriate position change options. This allows users to guide the sexual encounter toward different positions that align with character psychology and scene dynamics.

---

### 1. Sexual Content Detection for Position Steering

The system must analyze the recent interaction history to determine if position steering is appropriate:

**Detection Method:**
- Analyze the last 10-15 interactions for sexual keywords and physical descriptions
- Cross-reference with the active scenario type (some scenarios support position changes)
- Check Wife Desire and current activity level
- Determine if sexual activity is already occurring or imminent

**Detection Triggers (examples):**
- Kissing, touching, or sexual contact described
- Clothing removal or partial undress
- Sexual arousal indicators
- Wife Desire >= 30 as supporting evidence
- Terms indicating intimacy or sexual activity

**Trigger Conditions:**
- Position steering should be available during BuildUp (for choosing initial position)
- Position steering should be available during Committed (for position changes)
- Position steering should be available during Approaching (for escalating positions)
- Position steering should NOT trigger during Climax (use finish system instead)
- Position steering should NOT trigger during Reset (encounter concluded)

**Fallback:**
- If detection is uncertain but Desire is moderate (>30), provide a limited position menu focused on safer/less intimate options
- If no sexual context detected, do not trigger position steering

---

### 2. Configurable Position Selection Matrix

A **configuration table** (JSON, YAML, or database-driven) defines the available positions and their conditions.

#### Matrix Structure

**Wife Desire** (X-axis, ranges: 0-29 / 30-59 / 60-100)
**Wife SelfRespect** (Y-axis, ranges: 0-29 / 30-59 / 60-100)
**Wife Dominance** (modifier: Low <40 / Medium 40-70 / High >70)
**Other Man Dominance** (modifier: Low <50 / Medium 50-75 / High >75)

**Cell Content (Each combination defines):**
- **Primary positions** (weighted by likelihood - most appropriate positions)
- **Secondary positions** (less likely but available positions)
- **Wife behavior modifier** (suggesting, accepting, resisting, enthusiastic)
- **Other man behavior modifier** (directing, suggesting, commanding, asking)
- **Excluded positions** (what's NOT available for this stat combination)
- **Transition complexity** (how difficult the position change is)

#### Example Matrix Logic

| Desire | SelfRespect | Dom: Low | Dom: Med | Dom: High | OM-Dom: Low | OM-Dom: Med | OM-Dom: High |
|--------|-------------|----------|----------|-----------|-------------|-------------|--------------|
| 60-100 | 60-100 | Primary: Missionary, Spooning<br>Secondary: Cowgirl, Lotus<br>Behavior: Mutual, she suggests | Primary: Cowgirl, Lotus<br>Secondary: Reverse Cowgirl, Missionary<br>Behavior: She takes lead, enthusiastic | Primary: Cowgirl, Reverse Cowgirl<br>Secondary: Face-to-face sitting<br>Behavior: She's in control, adventurous | Primary: Missionary, Lotus<br>Secondary: Spooning, Scissors<br>Behavior: Gentle, collaborative | Primary: Missionary, Doggy<br>Secondary: Lotus, Cowgirl<br>Behavior: Mixed, equal participation | Primary: Doggy, Missionary<br>Secondary: Cowgirl, Face-sitting<br>Behavior: He directs, she's willing |
| 60-100 | 30-59 | Primary: Missionary, Doggy<br>Secondary: Cowgirl, Standing<br>Behavior: She's eager, suggests | Primary: Doggy, Cowgirl<br>Secondary: Reverse Cowgirl, Missionary<br>Behavior: Enthusiastic, she asks | Primary: Cowgirl, Reverse Cowgirl<br>Secondary: Doggy, Face-sitting<br>Behavior: She wants to please | Primary: Missionary, Spooning<br>Secondary: Doggy, Lotus<br>Behavior: She welcomes guidance | Primary: Doggy, Missionary<br>Secondary: Cowgirl, Face-sitting<br>Behavior: She enjoys direction | Primary: Doggy, Face-sitting<br>Secondary: Cowgirl, Reverse Cowgirl<br>Behavior: He decides, she's into it |
| 60-100 | 0-29 | Primary: Doggy, Face-sitting<br>Secondary: Reverse Cowgirl, Standing<br>Behavior: Submissive, she begs | Primary: Face-sitting, Doggy<br>Secondary: Reverse Cowgirl, Cowgirl<br>Behavior: She pleases, desperate | Primary: Face-sitting, Reverse Cowgirl<br>Secondary: Doggy, piledriver<br>Behavior: She'll do anything | Primary: Doggy, Spooning<br>Secondary: Missionary, Standing<br>Behavior: She accepts, submissive | Primary: Doggy, Face-sitting<br>Secondary: Missionary, Cowgirl<br>Behavior: He guides, she follows | Primary: Face-sitting, Doggy<br>Secondary: Reverse Cowgirl, piledriver<br>Behavior: He commands, she obeys |
| 30-59 | 60-100 | Primary: Missionary, Spooning<br>Secondary: Lotus, Standing<br>Behavior: Reluctant but willing | Primary: Missionary, Lotus<br>Secondary: Spooning, Scissors<br>Behavior: Comfortable, she agrees | Primary: Cowgirl, Missionary<br>Secondary: Lotus, Spooning<br>Behavior: She's okay with it | Primary: Missionary, Spooning<br>Secondary: Lotus, Standing<br>Behavior: She's hesitant but cooperative | Primary: Missionary, Lotus<br>Secondary: Spooning, Doggy<br>Behavior: She goes along | Primary: Missionary, Doggy<br>Secondary: Lotus, Cowgirl<br>Behavior: She agrees to his direction |
| 30-59 | 30-59 | Primary: Missionary, Doggy<br>Secondary: Spooning, Standing<br>Behavior: Cooperative | Primary: Missionary, Doggy<br>Secondary: Cowgirl, Spooning<br>Behavior: Willing, she accepts | Primary: Cowgirl, Doggy<br>Secondary: Missionary, Lotus<br>Behavior: She's into it | Primary: Missionary, Spooning<br>Secondary: Doggy, Lotus<br>Behavior: She accommodates | Primary: Doggy, Missionary<br>Secondary: Cowgirl, Spooning<br>Behavior: He suggests, she agrees | Primary: Doggy, Cowgirl<br>Secondary: Missionary, Face-sitting<br>Behavior: He decides, she accepts |
| 30-59 | 0-29 | Primary: Doggy, Face-sitting<br>Secondary: Cowgirl, Standing<br>Behavior: Resigned, submissive | Primary: Doggy, Cowgirl<br>Secondary: Face-sitting, Reverse Cowgirl<br>Behavior: She'll do what he wants | Primary: Face-sitting, Cowgirl<br>Secondary: Doggy, Reverse Cowgirl<br>Behavior: She wants to please him | Primary: Doggy, Spooning<br>Secondary: Missionary, Standing<br>Behavior: She has no say | Primary: Doggy, Face-sitting<br>Secondary: Cowgirl, Missionary<br>Behavior: He directs, she complies | Primary: Face-sitting, Doggy<br>Secondary: Reverse Cowgirl, piledriver<br>Behavior: He takes control |
| 0-29 | 60-100 | Primary: Missionary, Spooning<br>Secondary: Lotus, Scissors<br>Behavior: Very reluctant | Primary: Missionary, Lotus<br>Secondary: Spooning, Scissors<br>Behavior: Uncomfortable, resists | Primary: Missionary, Lotus<br>Secondary: Spooning<br>Behavior: She needs to be convinced | Primary: Missionary, Spooning<br>Secondary: Lotus, Scissors<br>Behavior: She tries to avoid | Primary: Missionary, Spooning<br>Secondary: Lotus, Doggy (only if pushed)<br>Behavior: She resists, uncomfortable | Primary: Missionary, Lotus<br>Secondary: Doggy (if forced)<br>Behavior: She's uncomfortable, nervous |
| 0-29 | 30-59 | Primary: Missionary, Spooning<br>Secondary: Lotus, Standing<br>Behavior: Not really into it | Primary: Missionary, Lotus<br>Secondary: Spooning, Scissors<br>Behavior: Going through motions | Primary: Missionary, Cowgirl<br>Secondary: Lotus, Spooning<br>Behavior: She's not enthusiastic | Primary: Missionary, Spooning<br>Secondary: Lotus, Standing<br>Behavior: Passive, reluctant | Primary: Missionary, Doggy<br>Secondary: Lotus, Spooning<br>Behavior: She lets him lead, uninterested | Primary: Doggy, Missionary<br>Secondary: Cowgirl, Lotus<br>Behavior: He pushes, she doesn't fight |
| 0-29 | 0-29 | Primary: Doggy, Face-sitting<br>Secondary: Cowgirl, piledriver<br>Behavior: Broken, no resistance | Primary: Doggy, Face-sitting<br>Secondary: Reverse Cowgirl, piledriver<br>Behavior: She doesn't care | Primary: Face-sitting, Reverse Cowgirl<br>Secondary: Doggy, piledriver<br>Behavior: Fully submissive | Primary: Doggy, Spooning<br>Secondary: Missionary, Standing<br>Behavior: No agency | Primary: Doggy, Face-sitting<br>Secondary: Cowgirl, Missionary<br>Behavior: He uses her | Primary: Face-sitting, Doggy<br>Secondary: Reverse Cowgirl, piledriver<br>Behavior: She's fully controlled |

---

### 3. Complete Position Catalog

Each position has detailed attributes for narrative generation and stat effects.

#### Penetrative Positions

**Missionary**
- **Description**: Classic face-to-face position, one partner on top
- **Intimacy Level**: High
- **Degrade Level**: Low
- **Power Dynamic**: Generally balanced, top partner has control
- **Stat Effects**: +Connection if high SelfRespect, +Desire if high Connection
- **Requirements**: Bed or flat surface, both partners facing each other
- **Transition Difficulty**: Low (from most positions)
- **Wife Perspective Variants**: 
  - High SelfRespect: "This feels intimate," "I like looking at you"
  - Low SelfRespect: "I feel exposed," "Just get it over with"

**Doggy Style**
- **Description**: Rear entry, one partner on hands and knees
- **Intimacy Level**: Medium
- **Degrade Level**: Medium-High
- **Power Dynamic**: Top partner has significant control
- **Stat Effects**: +Desire, -SelfRespect if already low
- **Requirements**: Bed/floor, receiving partner positioned
- **Transition Difficulty**: Low-Medium
- **Wife Perspective Variants**:
  - High Desire: "I love how deep," "Don't stop"
  - Low SelfRespect: "I feel like an object," "Just use me"

**Cowgirl**
- **Description**: Female on top, facing partner
- **Intimacy Level**: Medium-High
- **Degrade Level**: Low-Medium
- **Power Dynamic**: Female in control
- **Stat Effects**: +Dominance if wife, +Connection if mutual
- **Requirements**: Partner on back, female on top
- **Transition Difficulty**: Medium
- **Wife Perspective Variants**:
  - High Dominance: "I'm in charge," "I decide the pace"
  - Low SelfRespect: "I have to do all the work," "I'm being used"

**Reverse Cowgirl**
- **Description**: Female on top, facing away from partner
- **Intimacy Level**: Low-Medium
- **Degrade Level**: Medium
- **Power Dynamic**: Female has partial control, less visual intimacy
- **Stat Effects**: +Desire, -Connection
- **Requirements**: Partner on back, female facing away
- **Transition Difficulty**: Medium
- **Wife Perspective Variants**:
  - High Desire: "This feels good," "I can go deep"
  - Low SelfRespect: "I don't want him to see my face," "Just end it"

**Spooning**
- **Description**: Side-by-side, rear entry
- **Intimacy Level**: High
- **Degrade Level**: Low
- **Power Dynamic**: Balanced, gentle
- **Stat Effects**: +Connection, +Comfort
- **Requirements**: Both on sides, cuddling position
- **Transition Difficulty**: Medium
- **Wife Perspective Variants**:
  - High SelfRespect: "This feels loving," "So intimate"
  - Low Desire: "I'm too tired," "Can we just rest"

**Lotus**
- **Description**: Sitting face-to-face, legs wrapped
- **Intimacy Level**: Very High
- **Degrade Level**: Very Low
- **Power Dynamic**: Equal, connected
- **Stat Effects**: +Connection, +SelfRespect
- **Requirements**: Both sitting, facing, legs intertwined
- **Transition Difficulty**: High
- **Wife Perspective Variants**:
  - High Connection: "I feel so close to you," "This is perfect"
  - Low SelfRespect: "I feel vulnerable," "Too intimate"

**Standing**
- **Description**: Both standing, various entry angles
- **Intimacy Level**: Medium
- **Degrade Level**: Low-Medium
- **Power Dynamic**: Generally male-led
- **Stat Effects**: +Tension, +Desire
- **Requirements**: Both upright, sufficient support
- **Transition Difficulty**: High
- **Wife Perspective Variants**:
  - High Tension: "This is exciting," "Anyone could see"
  - Low SelfRespect: "I'm being held up," "I feel unstable"

**Scissors**
- **Description**: Both on sides, legs intertwined at an angle
- **Intimacy Level**: Medium
- **Degrade Level**: Low-Medium
- **Power Dynamic**: Equal
- **Stat Effects**: +Desire, neutral on dominance
- **Requirements**: Both on sides, legs scissored
- **Transition Difficulty**: High
- **Wife Perspective Variants**:
  - High Desire: "This hits the right spot," "Interesting angle"
  - Low Comfort: "My legs hurt," "Awkward position"

#### Advanced/Degradative Positions

**Face-Sitting (Queening)**
- **Description**: Female sits on male face (oral or genital contact)
- **Intimacy Level**: Low
- **Degrade Level**: High (for male), Medium (for female)
- **Power Dynamic**: Female dominant
- **Stat Effects**: +Wife Dominance, -OM SelfRespect
- **Requirements**: Male supine, female straddling face
- **Transition Difficulty**: Medium-High
- **Exclusions**: Requires moderate+ Wife Desire, excluded if Wife SelfRespect is very high (>80)
- **Wife Perspective Variants**:
  - High Dominance: "This is power," "Look at me down there"
  - Low SelfRespect: "I'm crushing him," "I feel gross"

**Piledriver**
- **Description**: Receiving partner on shoulders/back, legs in air
- **Intimacy Level**: Very Low
- **Degrade Level**: Very High
- **Power Dynamic**: Top partner has total control
- **Stat Effects**: -SelfRespect (significant), +Desire if already high
- **Requirements**: Flexible receiving partner, strength from top
- **Transition Difficulty**: Very High
- **Exclusions**: Requires Desire >50, excluded if SelfRespect >40, excluded if Tension is low
- **Wife Perspective Variants**:
  - High Desire: "I love this," "So deep"
  - Low SelfRespect: "I'm helpless," "I feel like meat"

**Wheelbarrow**
- **Description**: Receiving partner on hands, partner holds legs
- **Intimacy Level**: Low
- **Degrade Level**: High
- **Power Dynamic**: Top partner has control
- **Stat Effects**: -SelfRespect, +Tension
- **Requirements**: Strength and coordination
- **Transition Difficulty**: Very High
- **Exclusions**: Requires Desire >60, excluded if SelfRespect >50
- **Wife Perspective Variants**:
  - High Desire: "This is wild," "I'm completely vulnerable"
  - Low SelfRespect: "I'm being carried," "I feel ridiculous"

#### Oral Positions

**69 (Side-by-Side)**
- **Description**: Mutual oral, lying on sides
- **Intimacy Level**: Medium
- **Degrade Level**: Low-Medium
- **Power Dynamic**: Equal exchange
- **Stat Effects**: +Connection, +Desire
- **Requirements**: Both on sides, head-to-genital alignment
- **Transition Difficulty**: Medium
- **Wife Perspective Variants**:
  - High Connection: "We're pleasing each other," "This feels equal"
  - Low SelfRespect: "I can't focus," "It's too much"

**69 (Her on Top)**
- **Description**: Mutual oral, female straddles male face
- **Intimacy Level**: Medium
- **Degrade Level**: Medium
- **Power Dynamic**: Female has some control
- **Stat Effects**: +Wife Dominance, +Desire
- **Requirements**: Male supine, female straddling, oral contact
- **Transition Difficulty**: Medium
- **Wife Perspective Variants**:
  - High Dominance: "I can control this," "He's focused on me"
  - Low SelfRespect: "I'm exposed," "I'm suffocating him"

**Oral (Her Giving - Kneeling)**
- **Description**: Female kneels to give oral
- **Intimacy Level**: Low-Medium
- **Degrade Level**: Medium-High
- **Power Dynamic**: Receiving partner has control
- **Stat Effects**: -SelfRespect, +OM Dominance
- **Requirements**: Female kneeling, male standing or sitting
- **Transition Difficulty**: Low
- **Wife Perspective Variants**:
  - High Desire: "I want to taste him," "I love pleasing him"
  - Low SelfRespect: "I'm on my knees," "I'm servicing him"

**Oral (Her Giving - Lying)**
- **Description**: Female lying down, male stands/kneels at head
- **Intimacy Level**: Low-Medium
- **Degrade Level**: Medium
- **Power Dynamic**: Receiving partner has control
- **Stat Effects**: -SelfRespect (less), +OM Dominance
- **Requirements**: Female supine, male positioned at head
- **Transition Difficulty**: Low-Medium
- **Wife Perspective Variants**:
  - High Connection: "I trust him," "I'm surrendering control"
  - Low SelfRespect: "I can't move," "I'm at his mercy"

**Oral (Him Giving)**
- **Description**: Male performs oral on female
- **Intimacy Level**: High
- **Degrade Level**: Low
- **Power Dynamic**: Giving partner serves receiving
- **Stat Effects**: +SelfRespect, +Connection, +Desire
- **Requirements**: Female accessible, male positioned
- **Transition Difficulty**: Low-Medium
- **Wife Perspective Variants**:
  - High SelfRespect: "He wants to please me," "I deserve this"
  - Low SelfRespect: "I'm unworthy," "Why is he doing this?"

---

### 4. Current Position Detection

When generating position options, the system must:
1. **Detect current position** from recent interactions
2. **Filter available positions** based on transition feasibility
3. **Include transition notes** if a position change is complex

**Detection Keywords (examples):**
- Missionary: "on top", "face to face", "between legs"
- Doggy: "from behind", "on hands and knees", "hands on hips"
- Cowgirl: "riding", "on top", "straddling"
- Oral: "knees", "mouth", "tongue", "sucking", "licking"

**Transition Complexity Ratings:**
- **Easy** (0-2 steps): Oral ↔ Missionary ↔ Doggy ↔ Standing
- **Moderate** (3-5 steps): Cowgirl ↔ Reverse Cowgirl ↔ Spooning
- **Difficult** (6+ steps): Lotus ↔ Scissors ↔ 69 ↔ Face-Sitting
- **Very Difficult** (complex repositioning): Piledriver ↔ Wheelbarrow

---

### 5. Dynamic Behavior Modifiers

Based on the matrix cell, generated options should reflect:

**Wife Behavior Descriptors:**
- **Suggesting**: "I want to try...", "Can we...", "I was thinking..."
- **Accepting**: "Okay," "Sure," "That sounds fine"
- **Resisting**: "I don't know if...", "I'm not sure about this..."
- **Enthusiastic**: "Yes!", "I love that!", "Let's do it!"
- **Submissive**: "Whatever you want," "If you want to..."
- **Reluctant**: "I guess so," "If we have to..."

**Other Man Behavior Descriptors:**
- **Directing**: "Let's try this," "Turn over," "Get on your..."
- **Suggesting**: "What about...", "Have you ever tried...", "I was thinking..."
- **Commanding**: "Do this now," "Get into position," "I want to..."
- **Asking**: "Can I...", "Would you like...", "What do you want..."
- **Gentle**: "If you're comfortable...", "Only if you want to..."

**Transition Narratives:**
- **Smooth transitions**: "They shift naturally into...", "Without breaking rhythm..."
- **Deliberate transitions**: "He guides her into...", "She repositions to..."
- **Abrupt transitions**: "He flips her over," "He pulls her into..."
- **Hesitant transitions**: "She slowly moves into...", "They carefully adjust to..."

---

### 6. Prompt Engineering Guidelines

The AI prompt for generating position options must include:

**Context Section:**
- Current scene position (if detectable)
- Wife stats: Desire, SelfRespect, Dominance
- Other Man stat: Dominance
- Sexual intensity level (based on detection)
- Current phase (BuildUp/Committed/Approaching)

**Matrix Instructions:**
- Reference the applicable matrix cell
- Apply behavior modifiers
- Include primary and secondary options
- Exclude forbidden positions for this stat combo
- Consider transition difficulty from current position

**Output Requirements:**
- Generate 4-8 distinct position options
- Each option: 1-2 sentences describing the position change
- Include transition notes if complex repositioning needed
- Reflect appropriate power dynamic (command vs suggest vs ask)
- Include brief wife internal thought/reaction

**Option Variation:**
- Mix of primary and secondary positions
- Mix of wife-initiated vs man-initiated changes
- Vary intensity based on stats
- Ensure at least one "safest" option if SelfRespect is high
- Include at least one escalating option if Approaching phase

**Option Structure Example:**
```
[Option 1] Doggy Style
He gently guides her onto her hands and knees. She goes willingly, feeling the anticipation build as he moves behind her. "Like this?" she asks, looking back at him over her shoulder.

[Option 2] Lotus Position
They shift to sit facing each other, her legs wrapping around his waist as she settles onto him. She feels unusually intimate, close enough to see every expression on his face. "This feels... different," she murmurs, leaning her forehead against his.

[Option 3] Reverse Cowgirl
She turns around and straddles him, facing away as she lowers herself down. It's easier not to look at him this way, she thinks as she finds her rhythm. He grips her hips, directing her movement without a word.

[Option 4] Standing
He pulls her up from the bed, pressing her against the wall as they find a new angle. She gasps at the sudden change, feeling exposed and excited. "Someone could see," she breathes, but she doesn't push him away.
```

---

### 7. Phase-Specific Position Behavior

The position steering system must adapt behavior based on the current scenario phase:

**BuildUp Phase:**
- Focus on initial penetration positions
- Lower complexity transitions preferred
- More hesitation and uncertainty in options
- Position changes should feel exploratory
- Include safer/intimate options for high SelfRespect

**Committed Phase:**
- Full range of appropriate positions available
- Mix of intimacy and intensity options
- More confident position descriptions
- Allow for experimentation
- Balance control dynamics based on stats

**Approaching Phase:**
- Focus on positions that increase intensity
- More dominant/controlling options emerge
- Higher complexity positions become more likely
- Position changes feel more deliberate
- Include degradative options if stats support

**Climax Phase:**
- DO NOT use position steering
- Use finish system instead

**Reset Phase:**
- Position steering not available
- Encounter is concluding

---

### 8. Stat Impact System

Choosing a position should cause immediate stat changes based on:

**Position-Based Impacts:**
- **High Intimacy Positions** (Lotus, Spooning): +Connection, +SelfRespect
- **High Degrade Positions** (Piledriver, Face-Sitting): -SelfRespect, +Desire
- **Wife-Dominant Positions** (Cowgirl, Face-Sitting): +Wife Dominance
- **OM-Dominant Positions** (Doggy, Missionary with OM on top): +OM Dominance
- **Balanced Positions** (Scissors, 69): Neutral on dominance, +Desire

**Behavior Modifier Impacts:**
- **Wife-initiated changes**: +Wife Dominance (small), +SelfRespect (if accepted)
- **OM-commanded changes**: +OM Dominance, -Wife SelfRespect (if low)
- **Mutual changes**: +Connection
- **Resisted changes**: +Tension, -Connection

**Transition Complexity Impacts:**
- **Smooth transitions**: +Connection, +Comfort
- **Abrupt transitions**: +Tension, -Comfort
- **Failed/awkward transitions**: -Confidence, +Embarrassment

---

### 9. Configuration Considerations

**Future Enhancements:**
- Matrix values should be adjustable without code deployment
- Ability to add/modify positions dynamically
- Ability to tune thresholds and weights
- A/B testing support for different matrix configurations
- Position unlock system (positions unlock based on stats/experience)

**Data Format Options:**
- JSON configuration file for matrix and positions
- YAML for readability and easy editing
- Database table for runtime updates
- Hardcoded defaults with optional overrides

**Extensibility:**
- Support for adding new positions without system redesign
- Configurable stat effect formulas
- Phase-specific position availability toggles
- Character trait modifiers to position preferences

---

### 10. User Interface Considerations

**Position Display:**
- Position name (clear, recognizable)
- Brief description (1-2 lines)
- Difficulty indicator (for transitions)
- Intimacy level indicator
- Preview option (optional, for clarity)

**Selection Feedback:**
- Confirm the chosen position
- Show stat impact preview
- Display transition narrative
- Allow position change confirmation

**History Tracking:**
- Track used positions in current encounter
- Track position frequency across encounters
- Identify position preferences based on choices
- Suggest positions based on history

---

## Questions for Further Refinement

1. **Number of Options**: How many position options should be generated per popup? (4? 6? 8? Variable based on phase?)

2. **Matrix Granularity**: Is the 3x3 grid (Desire × SelfRespect) sufficient, or do you want more ranges or additional axes?

3. **Position Exclusions**: Are there positions that should be completely excluded for certain character types or relationship states?

4. **Transition Narratives**: How detailed should the position change descriptions be? (Brief mention vs full transition scene?)

5. **Configuration Format**: Do you prefer JSON, YAML, or another format for the matrix and position configuration?

6. **Position Unlock System**: Should positions be locked until certain stat thresholds or encounter counts are reached?

7. **Stat Impact Magnitude**: How significant should the stat impacts be from position choices? (Minor nudges vs substantial changes?)

8. **Position History**: Should the system track and display position history, and should this affect future suggestions?