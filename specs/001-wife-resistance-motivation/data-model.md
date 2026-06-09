# Data Model: Wife Resistance & Cheating Motivation Gap

**Feature**: `001-wife-resistance-motivation` | **Date**: 2026-06-07

## New Database Table

### StatResistanceProfiles

Mirrors `StatWillingnessProfiles` schema exactly.

```sql
CREATE TABLE IF NOT EXISTS StatResistanceProfiles (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT NOT NULL,
    TargetStatName TEXT NOT NULL DEFAULT 'Loyalty',
    IsDefault INTEGER NOT NULL DEFAULT 0,
    ThresholdsJson TEXT NOT NULL DEFAULT '[]',
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_StatResistanceProfiles_Name
    ON StatResistanceProfiles (Name);
```

**Columns**:
| Column | Type | Description |
|--------|------|-------------|
| Id | TEXT PK | GUID string, generated on create |
| Name | TEXT NOT NULL | Unique display name |
| Description | TEXT NOT NULL | Free-text description |
| TargetStatName | TEXT NOT NULL | Which stat drives band selection (default "Loyalty"). Must be a canonical stat name. |
| IsDefault | INTEGER | 0 or 1. Only one row may be 1 at a time (enforced by UPSERT that resets others). |
| ThresholdsJson | TEXT | JSON array of `ResistanceThreshold` objects |
| CreatedUtc | TEXT | ISO 8601 |
| UpdatedUtc | TEXT | ISO 8601 |

**Seeded default row**: "Married Woman Resistance" with TargetStatName="Loyalty", IsDefault=1, 20 contiguous threshold bands covering 0–100.

## New Column on Existing Table

### RolePlayV2AdaptiveStates.SelectedResistanceProfileId

```sql
ALTER TABLE RolePlayV2AdaptiveStates
ADD COLUMN SelectedResistanceProfileId TEXT NULL;
```

- Nullable TEXT FK to `StatResistanceProfiles.Id`
- Populated at session create from the default ResistanceProfile
- Stored in `AdaptiveScenarioState.SelectedResistanceProfileId` (C# string?)
- Saved via UPSERT in `RolePlayStateRepository.SaveAdaptiveStateAsync`
- Loaded via new ordinal in `RolePlayStateRepository.LoadAdaptiveStateAsync`

## New Domain Classes

### StatResistanceProfile

```csharp
public sealed class StatResistanceProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TargetStatName { get; set; } = "Loyalty";
    public bool IsDefault { get; set; }
    public List<ResistanceThreshold> Thresholds { get; set; } = [];
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
```

### ResistanceThreshold

```csharp
public sealed class ResistanceThreshold
{
    public int SortOrder { get; set; }
    public int MinValue { get; set; }
    public int MaxValue { get; set; }
    public string ResistanceLevel { get; set; } = string.Empty;  // e.g., "Firm Boundaries"
    public string Description { get; set; } = string.Empty;
    public string PromptDirective { get; set; } = string.Empty;  // Injected into prompt
    public List<string> ExampleScenarios { get; set; } = [];
}
```

**Validation rules** (enforced in `StatResistanceProfileService.SaveAsync`):
- Name is required and unique (case-insensitive)
- TargetStatName must be a canonical stat (from `AdaptiveStatCatalog.CanonicalStatNames`)
- Thresholds must be non-empty and cover 0–100 contiguously (first MinValue=0, last MaxValue=100, no gaps)
- Each threshold requires `ResistanceLevel` and `PromptDirective`
- MinValue ≤ MaxValue per threshold
- Only one IsDefault=1 at a time (UPSERT resets others)

## New Behavioral Dimensions (Code-Defined)

### Wife: BoundaryFirmness

```csharp
new("BoundaryFirmness", "Wife",
    Tier1: "She enforces her stated limits firmly and consistently; when she says no, she means it and will not be argued past it.",
    Tier2: "She holds her boundaries most of the time but can be swayed when pressure is sustained and the justification feels plausible.",
    Tier3: "She states limits but enforces them weakly; her no softens quickly under pressure and she rarely follows through on her objections.",
    Tier4: "She does not enforce her stated limits at all; she says no but does not follow through, and does not expect to be taken seriously.")
```

### Wife: SeductionReceptivity

```csharp
new("SeductionReceptivity", "Wife",
    Tier1: "She is largely immune to persistent pursuit; she does not find pressure flattering and is not swayed by someone wanting her badly.",
    Tier2: "She notices persistent pursuit and may find it mildly flattering, but it does not change her decisions or lower her guard significantly.",
    Tier3: "She is susceptible to persistent pursuit; being wanted intensely chips away at her resolve over time and makes her more open.",
    Tier4: "She is highly receptive to being pursued; persistence reads to her as genuine desire, and it actively erodes her resistance and draws her in.")
```

### Husband: Attentiveness

```csharp
new("Attentiveness", "Husband",
    Tier1: "He is emotionally distant and checked out — he does not notice her, ask about her day, or show interest in her inner life. She feels invisible to him.",
    Tier2: "He is intermittently attentive — he sometimes notices her but is often distracted, absorbed in his own world, or takes her for granted.",
    Tier3: "He is generally present and engaged — he notices her moods, asks about her life, and makes her feel seen most of the time.",
    Tier4: "He is deeply attentive and emotionally available — he prioritises her, notices subtle shifts, and actively nurtures their emotional connection.")
```

### Husband: IntimacyAvailability

```csharp
new("IntimacyAvailability", "Husband",
    Tier1: "He is sexually unavailable — the bedroom is dead, he shows no desire for her, and physical intimacy is absent from the marriage.",
    Tier2: "He is sporadically available — intimacy happens occasionally but feels routine, obligatory, or one-sided; she feels undesired.",
    Tier3: "He is generally available and engaged — physical intimacy is a regular part of the marriage and he shows genuine desire for her.",
    Tier4: "He is actively engaged and passionate — he initiates, expresses desire openly, and makes her feel wanted and pursued within the marriage.")
```

## Stat-to-Dimension Drift Rules (New)

Added to `StatToDimensionMappings`:

| Stat | TargetRole | Dimension | Slope | Floor | Ceiling |
|------|------------|-----------|-------|-------|---------|
| Restraint | Wife | BoundaryFirmness | +0.90 | 0 | 100 |
| Restraint | Wife | SeductionReceptivity | -0.60 | 0 | 100 |
| Loyalty | Wife | BoundaryFirmness | +0.75 | 0 | 100 |
| SelfRespect | Wife | BoundaryFirmness | +0.60 | 0 | 100 |
| Desire | Wife | SeductionReceptivity | +0.45 | 0 | 100 |

**Drift logic**: When Wife Restraint drops by 20 → BoundaryFirmness increases by 18 (0.90 × 20), SeductionReceptivity drops by 12 (-0.60 × 20). Higher Restraint = firmer boundaries, lower receptivity. Higher Loyalty = firmer boundaries. Higher SelfRespect = firmer boundaries. Higher Desire = more receptive.

No drift rules for Husband dimensions — they are set on the CharacterProfile and serve as static scenario inputs (the user sets them once; they don't drift during play).

## Motivation Score (Runtime, Not Persisted)

Computed each prompt-build for the Wife character:

```
effectiveStat = min(targetStatValue + motivationScore, 100)
  where:
    targetStatValue = Wife's current value for ResistanceProfile.TargetStatName (default: Loyalty)
    motivationScore = ((100 - H.Attentiveness) + (100 - H.IntimacyAvailability) + (100 - W.SelfRespect) + OM.PersistencePastLimits) / 4
```

Missing inputs default to 50 (neutral). The ResistanceProfile resolves `effectiveStat` to a band via its contiguous thresholds, and the band's `PromptDirective` is injected as a HARD CONSTRAINT line.
