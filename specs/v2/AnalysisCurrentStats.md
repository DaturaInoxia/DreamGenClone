Great question! This is a crucial design decision. Let me analyze the current stat system and make recommendations.

---

## Analysis of Current Stats

The existing 5 stats are actually **well-chosen and complementary**:

| Stat | What It Does | Gaps/Overlaps |
|------|--------------|---------------|
| **Desire** | Drives willingness for sexual acts | ✅ Perfect for willingness scaling |
| **Restraint** | Internal brakes against acting | ✅ Works for resisting temptation |
| **Tension** | Risk-seeking, excitement | ✅ Covers stranger/risk appetite |
| **Connection** | Trust requirement | ✅ Covers who she'll engage with |
| **Dominance** | Agency, initiation | ✅ Covers active vs passive behavior |

**What's Missing:**
- **Loyalty** - Commitment to primary partner/monogamy
- This is the **big gap** for your use case (cheating vs consensual non-monogamy)

---

## My Recommendation: Add Loyalty, Then Stop

### Add 1 Stat: Loyalty

```csharp
new AdaptiveStatDefinition
{
    Name = "Loyalty",
    Description = "Commitment to primary partner and monogamy.",
    LowMeaning = "Compromised commitment, open to infidelity or non-monogamy.",
    HighMeaning = "Unwaveringly faithful, only participates with spousal consent."
}
```

**Why Loyalty is essential:**
- **Clear distinction** between cheating (Loyalty low) and hotwife (Loyalty high but consensual)
- **Intuitive** for users to understand
- **No overlap** with existing stats
- **Fundamental** to the scenarios you're describing

### Optional: Add 1 More Stat: Self-Respect

If you want nuance around degradation/humiliation scenarios:

```csharp
new AdaptiveStatDefinition
{
    Name = "SelfRespect",
    Description = "Dignity and self-worth; tolerance for being objectified or degraded.",
    LowMeaning = "Low self-esteem, willing to be used or humiliated.",
    HighMeaning = "Strong sense of worth, maintains dignity, resists degradation."
}
```

**Why Self-Respect adds value:**
- Influences willingness for degrading acts (glory holes, humiliation, rough play)
- Separates "high desire" from "willing to be degraded"
- Adds nuance: a character can have high Desire but high Self-Respect = adventurous but dignified

---

## Why I Recommend Stopping at 6-7 Stats

| Concern | Explanation |
|---------|-------------|
| **Cognitive Load** | 6-7 stats is the sweet spot. More than 8 becomes hard for users to reason about |
| **Interaction Complexity** | Each new stat exponentially increases possible stat combinations |
| **Prompt Bloat** | Every stat gets injected into prompts; too many = context bloat and confusion |
| **Tuning Difficulty** | More stats = harder to balance and get predictable behavior |
| **Ambiguity** | New stats often overlap with existing ones, causing confusion |
| **Diminishing Returns** | The current 5 stats + Loyalty cover most scenarios well |

---

## Alternative: Derived/Computed Values Instead of More Stats

Instead of adding permanent stats, you can **compute values on the fly**:

### Example: Computed Decision Scores

```csharp
public static class StatDecisionEngine
{
    // Cheating propensity: Lower = more likely to cheat
    public static int ComputeCheatingResistance(Dictionary<string, int> stats)
    {
        return stats["Loyalty"] 
             - (stats["Desire"] / 2) 
             + (stats["Restraint"] / 2) 
             - (stats["Tension"] / 3);
    }
    
    // Agency: Higher = more proactive
    public static int ComputeAgency(Dictionary<string, int> stats)
    {
        return stats["Dominance"] 
             - stats["Restraint"] 
             + (stats["Desire"] / 3);
    }
    
    // Risk tolerance: Higher = more willing to take risks
    public static int ComputeRiskTolerance(Dictionary<string, int> stats)
    {
        return stats["Tension"] 
             - (stats["Restraint"] / 2) 
             - (stats["Connection"] / 4);
    }
    
    // Vulnerability to degradation: Higher = more willing to be degraded
    public static int ComputeDegradationTolerance(Dictionary<string, int> stats)
    {
        return 100 - stats["SelfRespect"] 
             + (stats["Desire"] / 2) 
             - (stats["Dominance"] / 2);
    }
    
    // Trust requirement for third parties: Higher = needs more familiarity
    public static int ComputeConnectionThreshold(Dictionary<string, int> stats)
    {
        return stats["Connection"] 
             - (stats["Tension"] / 2) 
             + (stats["Restraint"] / 3);
    }
}
```

**Benefits of derived values:**
- No permanent stat bloat
- Easier to tune formulas
- Can be tweaked per scenario
- Stored only when needed

---

## Proposed Final Stat Set (6-7 Stats)

### Option 1: Minimal (6 Stats)
```
1. Desire      - Sexual intensity and willingness
2. Restraint   - Internal brakes
3. Tension     - Risk-seeking
4. Connection  - Trust requirement
5. Dominance   - Agency/initiation
6. Loyalty     - Commitment to primary partner ✅ NEW
```

### Option 2: With Nuance (7 Stats)
```
1. Desire      - Sexual intensity and willingness
2. Restraint   - Internal brakes
3. Tension     - Risk-seeking
4. Connection  - Trust requirement
5. Dominance   - Agency/initiation
6. Loyalty     - Commitment to primary partner ✅ NEW
7. SelfRespect - Dignity/degradation tolerance ✅ OPTIONAL
```

---

## What This Enables for Your Scenarios

### Scenario: Glory Hole with Stranger
| Stat | Value | Effect |
|------|-------|--------|
| Loyalty | <40 | Cheating allowed |
| Desire | 85+ | High drive |
| Restraint | <40 | Low brakes |
| Tension | 70+ | Stranger/risk acceptable |
| Connection | <30 | Familiarity not needed |
| Dominance | Variable | Active vs passive seeking |
| SelfRespect | <50 | Degradation acceptable |

### Scenario: Hotwife Threesome with Husband
| Stat | Value | Effect |
|------|-------|--------|
| Loyalty | 90+ | Consensual non-monogamy, not cheating |
| Desire | 70+ | Enthusiastic participation |
| Restraint | <50 | Willing to explore |
| Tension | 60+ | Excited by new experience |
| Connection | 50+ | Comfortable with third |
| Dominance | Variable | Active or passive |
| SelfRespect | 60+ | Maintains dignity |

### Scenario: Affair with Coworker
| Stat | Value | Effect |
|------|-------|--------|
| Loyalty | 50-60 | Compromised, cheating possible |
| Desire | 70+ | Strong attraction |
| Restraint | <50 | Can't resist temptation |
| Tension | 50+ | Risk adds excitement |
| Connection | 70+ | Trust built over time |
| Dominance | <40 | Needs to be seduced |
| SelfRespect | 50+ | Feels some guilt |

---

## Summary Recommendation

### ✅ Add to AdaptiveStatCatalog:
```csharp
public static readonly IReadOnlyList<AdaptiveStatDefinition> CanonicalStats =
[
    // ... existing 5 stats ...
    new AdaptiveStatDefinition
    {
        Name = "Loyalty",
        Description = "Commitment to primary partner and monogamy.",
        LowMeaning = "Compromised commitment, open to infidelity or non-monogamy.",
        HighMeaning = "Unwaveringly faithful, only participates with spousal consent."
    },
    // OPTIONAL: SelfRespect for degradation scenarios
    new AdaptiveStatDefinition
    {
        Name = "SelfRespect",
        Description = "Dignity and self-worth; tolerance for being objectified or degraded.",
        LowMeaning = "Low self-esteem, willing to be used or humiliated.",
        HighMeaning = "Strong sense of worth, maintains dignity, resists degradation."
    }
];
```

### ❌ Don't Add More Than 7 Stats:
- 6-7 is the sweet spot
- More = too complex
- Use derived formulas for nuanced decisions

### 💡 Use Derived Decision Engine:
Compute scores like `CheatingResistance`, `Agency`, `RiskTolerance`, `ConnectionThreshold` on the fly instead of storing them as stats.

This gives you **rich behavioral complexity** without **stat bloat**!