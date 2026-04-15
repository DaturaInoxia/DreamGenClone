# Character Stats Reference

## Stat Descriptions and Engine Behaviors

| Stat | Scale | Engine Behavior | Sexual Behavior - Low Values | Sexual Behavior - High Values |
|------|-------|----------------|------------------------------|------------------------------|
| **Desire** | Low: Calm, detached; High: Strong attraction, urgency | Primary intensity driver. Higher values raise effective style intensity and reinforce escalation themes. | **Will do:** Basic, vanilla encounters; requires motivation; may need external arousal; passive participant. | **Will do:** Initiates encounters; eager escalation; pursues risky opportunities; intense enthusiasm; may push boundaries. |
| **Restraint** | Low: Impulsive, permissive; High: Cautious, guarded | Restraint brake. Lower values permit bolder progression; higher values slow escalation and preserve caution. | **Will do:** Impulsive sexual decisions; risky public encounters; cheating without hesitation; degrading acts if aroused; rapid escalation. | **Will do:** Maintains boundaries; requires safety/trust; reluctant public acts; resists temptation; slow escalation; preserves dignity. |
| **Tension** | Low: Stable, safe; High: Volatile, high-stakes | Conflict pressure. Higher values bias output toward volatility, uncertainty, and risk-laden beats. | **Will do:** Comfortable, private encounters; stable relationships; avoids risk; prefers familiar partners; safe scenarios. | **Will do:** Risky public encounters; stranger hookups; cheating excitement; volatile dynamics; dangerous situations; risk-laden beats. |
| **Connection** | Low: Defensive, suspicious; High: Open, reassured | Safety confidence. Higher values enable openness and vulnerability; lower values reinforce defensiveness. | **Will do:** Transactional sex; emotionally distant encounters; avoids vulnerability; keeps walls up; minimal intimacy; may cheat without guilt. | **Will do:** Vulnerable intimacy; emotional sex; deep connection in encounters; trusting partner; mutual vulnerability; explores together. |
| **Dominance** | Low: Passive, cornered; High: Decisive, in control | Control signal. Higher values favor decisive initiative; lower values bias toward passivity and reduced control. | **Will do:** Submissive encounters; follows lead; receptive to direction; may be guided into acts; passive participation; allows others to decide. | **Will do:** Initiates encounters; directs partner; chooses sexual acts; takes control; decides pace and actions; steers outcomes. |
| **Loyalty** | Low: Detached, opportunistic; High: Steadfast, committed | Relationship gatekeeper. Lower values lower cheating threshold; higher values reinforce exclusivity. | **Will do:** Cheating without hesitation; multiple partners; infidelity as norm; opportunistic encounters; ignores relationship commitment. | **Will do:** Exclusivity enforced; resists cheating; requires consent for non-monogamy; committed sexual encounters; guilt over straying. |
| **SelfRespect** | Low: Boundary erosion, self-compromise; High: Firm boundaries, self-valuing | Boundary guard. Lower values enable self-compromise in sexual contexts; higher values maintain firm boundaries. | **Will do:** Degrading sexual acts; boundary compromise; accepts humiliation; people-pleasing sex; lacks self-valuing in encounters; low standards. | **Will do:** Maintains standards; refuses degrading acts; sets boundaries; self-valuing in sex; principled choices; preserves dignity. |

## Stat Value Ranges

| **Stat**       | **Low (≤40)**                               | **Moderate (40–60)**                     | **High (≥70)**                           | **Extreme (≥85)**                               |
|----------------|----------------------------------------------|-------------------------------------------|-------------------------------------------|--------------------------------------------------|
| **Tension**    | Relaxed, calm                                | Normal baseline                           | Stressed, anxious                         | Panicking, overwhelmed                           |
| **Restraint**  | Impulsive, unrestrained                      | Normal self-control                       | Holding back, tense                       | Extreme control, frozen                          |
| **Connection** | Distant, disconnected                        | Normal bond                               | Close, bonded                             | Deeply attached                                  |
| **Desire**     | Low motivation                               | Normal interest                           | Strong attraction                         | Urgent, overwhelming                             |
| **Dominance**  | Passive, yielding                            | Balanced agency                           | Assertive, decisive                       | Controlling, dominant                            |
| **Loyalty**    | Emotionally distant, justifies betrayal      | Normal commitment, some temptation        | Strongly committed, resists temptation    | Unwavering dedication, guilt over minor slips     |
| **SelfRespect**| Seeks validation, poor boundaries            | Normal self-esteem, occasional doubt      | Strong boundaries, self-worth, assertive | Deep dignity, uncompromising self-worth          |

## Core Formulas

**Cheating Decision Formula:**
```
Cheating Threshold = Loyalty - (Desire / 2) + (Restraint / 2) - (Tension / 3)
```
- Lower threshold = more likely to cheat
- High Desire pushes toward cheating
- High Restraint opposes cheating
- High Tension adds excitement that can override loyalty

**Agency Score Formula:**
```
Agency Score = Dominance - Restraint + (Desire / 3)
```
- Higher agency = proactive, initiates encounters
- High Dominance increases agency
- Low Restraint increases agency
- High Desire increases agency

*See [EngineFormulas.md](EngineFormulas.md) for additional formula reference.*