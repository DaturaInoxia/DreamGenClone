# Engine Formulas Reference

This document provides additional formulas used by the engine for character behavior, scenario selection, and narrative generation.

## Formula Categories

### **Risk & Escalation Formulas**

#### Risk Appetite
Willingness to engage in risky sexual encounters

```
Risk Appetite = Tension + (Desire / 2) - (Restraint / 2) - (Loyalty / 3)
```

**Usage:**
- Determines suitability for risky/public scenarios
- High values: Public sex, stranger encounters, risky infidelity
- Low values: Private encounters, safe scenarios, familiar partners only

**Stat Impact:**
- **High Tension (+):** Excitement from risk drives willingness
- **High Desire (+):** Strong sexual motivation overrides caution
- **High Restraint (-):** Self-control limits risky behavior
- **High Loyalty (-):** Commitment reduces willingness to cheat openly

---

#### Escalation Resistance
How much a character resists sexual escalation

```
Escalation Resistance = Restraint + Loyalty - Desire
```

**Usage:**
- Controls pacing in sexual scenarios
- High resistance: Slow escalation, needs more build-up, maintains boundaries
- Low resistance: Rapid escalation, quick progression to advanced acts

**Stat Impact:**
- **High Restraint (+):** Self-control slows progression
- **High Loyalty (+):** Commitment reinforces boundaries
- **High Desire (-):** Sexual urgency overrides resistance

---

### **Manipulation & Influence Formulas**

#### Vulnerability to Manipulation
How easily can a character be influenced or manipulated?

```
Vulnerability = 100 - Dominance - (SelfRespect / 2) + (Connection / 2)
```

**Usage:**
- Determines susceptibility to seduction, grooming, or persuasion
- Used by: Jedi Master archetype, seduction scenarios, manipulative dynamics
- High vulnerability: Easily influenced, weak resistance to persuasion
- Low vulnerability: Strong resistance, hard to manipulate

**Stat Impact:**
- **High Dominance (-):** Strong will makes character harder to control
- **High SelfRespect (-):** Strong boundaries prevent manipulation
- **High Connection (+):** Emotional bonds create vulnerability

---

### **Emotional & Behavioral Formulas**

#### Emotional Volatility
How likely are rapid emotional state changes during encounters

```
Volatility = Tension - (Restraint / 2) - (Connection / 3)
```

**Usage:**
- Predicts emotional stability in scenes
- High volatility: Rapid mood changes, reactive, unpredictable
- Low volatility: Emotionally stable, consistent, predictable

**Stat Impact:**
- **High Tension (+):** Stress creates reactivity
- **High Restraint (-):** Self-control maintains emotional stability
- **High Connection (-):** Emotional bonds stabilize reactions

---

#### Intimacy Capacity
Capability for deep, vulnerable sexual intimacy

```
Intimacy = Connection + Desire + (Restraint / 2)
```

**Usage:**
- Determines suitability for romantic/emotional sexual encounters
- High capacity: Deep emotional sex, vulnerability, meaningful encounters
- Low capacity: Transactional sex, detached encounters, physical-only focus

**Stat Impact:**
- **High Connection (+):** Emotional bonds enable intimacy
- **High Desire (+):** Sexual motivation drives engagement
- **High Restraint (+):** Moderate restraint keeps it meaningful, not impulsive

---

### **Boundary & Consent Formulas**

#### Boundaries Strength
How firm and maintainable are character boundaries

```
Boundaries = SelfRespect + Restraint + (Loyalty / 2)
```

**Usage:**
- Determines what a character will/won't do
- High strength: Firm limits, refuses degrading acts, maintains dignity
- Low strength: Compromises boundaries, susceptible to degradation

**Stat Impact:**
- **High SelfRespect (+):** Strong self-worth maintains boundaries
- **High Restraint (+):** Self-control enforces limits
- **High Loyalty (+):** Commitment reinforces personal standards

---

#### Consent Threshold
How hard it is to obtain meaningful consent

```
Consent Threshold = SelfRespect + Dominance + Restraint - (Desire / 2)
```

**Usage:**
- Ethical considerations for consent quality
- High threshold: Requires significant motivation, not easily pressured
- Low threshold: May consent under pressure, ethically concerning

**Stat Impact:**
- **High SelfRespect (+):** Self-worth demands proper consent conditions
- **High Dominance (+):** Strong will ensures consent is meaningful
- **High Restraint (+):** Self-control prevents coerced consent
- **High Desire (-):** Sexual urgency may override consent quality

---

### **Power & Submission Formulas**

#### Submissiveness Capacity
Ability/willingness to be submissive in encounters

```
Submissiveness = 100 - Dominance - (SelfRespect / 2) + Connection
```

**Usage:**
- Determines suitability for submission themes
- High capacity: Naturally submissive, follows lead, enjoys surrendering control
- Low capacity: Resists submission, needs control, uncomfortable following

**Stat Impact:**
- **High Dominance (-):** Strong will resists submission
- **High SelfRespect (-):** Strong boundaries prevent surrender
- **High Connection (+):** Trust enables letting go of control

---

### **Relationship Dynamics Formulas**

#### Cuckold/Hotwife Compatibility
For husband archetypes - how suitable for cuckolding scenarios

```
Hotwife Compatibility = Desire + Connection - Dominance + (100 - Loyalty)
```

**Usage:**
- Determines husband suitability for hotwife/cuckold themes
- High compatibility: Enjoys watching, accepts wife's pleasure with others
- Low compatibility: Jealous, uncomfortable, not suitable for cuckolding

**Stat Impact:**
- **High Desire (+):** Sexual arousal from voyeurism
- **High Connection (+):** Trust in relationship enables acceptance
- **High Dominance (-):** Need for control makes cuckolding difficult
- **High Loyalty (-):** When inverted, low loyalty increases compatibility

---

### **Deception & Secret-Keeping Formulas**

#### Deception Capacity
Ability to maintain deception/lies

```
Deception Capacity = Restraint + (100 - Connection) - Tension
```

**Usage:**
- Determines ability to maintain secrets and lies
- Used by: Cheating themes, secret-keeping mechanics
- High capacity: Maintains facade, doesn't crack under pressure
- Low capacity: Gives away secrets, shows guilt easily

**Stat Impact:**
- **High Restraint (+):** Self-control maintains composure
- **Low Connection (+):** Emotional detachment reduces guilt indicators
- **High Tension (-):** Stress causes cracks in deception

---

## Formula Integration

### **Scenario Selection**
Multiple formulas are used together to determine scenario fit:

**For Public Infidelity Scenarios:**
- High Risk Appetite (risky behavior)
- Low Boundaries Strength (willing to compromise)
- High Deception Capacity (can hide it)

**For Hotwife/Cuckold Scenarios:**
- High Hotwife Compatibility (husband)
- Low Submissiveness Capacity (husband - not too submissive)
- Low Risk Appetite (consensual, not risky)

**For Spontaneous Exclusion Scenarios:**
- Low Escalation Resistance (wife gets carried away)
- Low Boundaries Strength (wife compromises)
- Moderate Hotwife Compatibility (husband - fantasy-driven)

### **Stat Progression During Scenarios**
Formulas should be recalculated as stats change:

**When Character Loses Inhibition:**
- Restraint decreases → Escalation Resistance decreases
- Desire increases → Risk Appetite increases
- Connection may decrease → Vulnerability increases

**When Character Experiences Shock/Confusion:**
- Tension increases → Volatility increases
- SelfRespect decreases → Boundaries Strength decreases
- Dominance decreases → Submissiveness increases

---

## Implementation Notes

### **Formula Ranges:**
- Most formulas produce values roughly 0-150
- Normalize for engine use as needed
- Consider creating thresholds (Low/Medium/High) for each formula

### **Formula Priority:**
- Core formulas (CharacterStats.md) are calculated most frequently
- These formulas provide additional context and specialized decisions
- Not all formulas apply to every scenario type

### **Stat Updates:**
- Formulas should be recalculated after stat changes
- Use formula results to guide narrative decisions
- Track formula progression through scenario phases

---

## Quick Reference

| Formula | Primary Use | Key Stats |
|----------|--------------|----------|
| Risk Appetite | Risky scenarios suitability | Tension, Desire, Restraint, Loyalty |
| Escalation Resistance | Pacing control | Restraint, Loyalty, Desire |
| Vulnerability | Manipulation susceptibility | Dominance, SelfRespect, Connection |
| Volatility | Emotional stability | Tension, Restraint, Connection |
| Intimacy Capacity | Romantic/emotional encounters | Connection, Desire, Restraint |
| Boundaries Strength | What they will/won't do | SelfRespect, Restraint, Loyalty |
| Consent Threshold | Ethical considerations | SelfRespect, Dominance, Restraint, Desire |
| Submissiveness Capacity | Submission suitability | Dominance, SelfRespect, Connection |
| Hotwife Compatibility | Cuckold themes | Desire, Connection, Dominance, Loyalty |
| Deception Capacity | Secret-keeping | Restraint, Connection, Tension |