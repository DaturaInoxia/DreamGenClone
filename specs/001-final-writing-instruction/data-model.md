# Data Model: Final Writing Instruction Consolidation

**Feature**: `001-final-writing-instruction`
**Date**: 2026-07-19

---

## Entity Changes

### 1. SteeringProfile (Style Profile)

**Location**: `DreamGenClone.Domain/StoryAnalysis/SteeringProfile.cs`
**Table**: `StyleProfiles`

#### New Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `ImmersionDirective` | `string` | Yes (fail-fast) | none | Immersion rule for Character variant (e.g., "Stay inside this character's perceptions... Show, don't tell.") |
| `ActionDirective` | `string` | Yes (fail-fast) | none | Action rule for Character variant (e.g., "Respond to the scene naturally.") |
| `WordTargetMin` | `int` | Yes (fail-fast) | none | Minimum word count for Character variant |
| `WordTargetMax` | `int` | Yes (fail-fast) | none | Maximum word count for Character variant |
| `NarrativeWordTargetMin` | `int` | Yes (fail-fast) | none | Minimum word count for Narrative variant (intentionally > Character) |
| `NarrativeWordTargetMax` | `int` | Yes (fail-fast) | none | Maximum word count for Narrative variant (intentionally > Character) |

#### Validation Rules

- All six new fields MUST be non-null/non-empty/non-zero at prompt build time (FR-006).
- `WordTargetMin` MUST be > 0 and < `WordTargetMax`.
- `NarrativeWordTargetMin` MUST be > 0 and < `NarrativeWordTargetMax`.
- `NarrativeWordTargetMin` MUST be >= `WordTargetMin` (Narrative targets are longer by convention).

#### DB Migration

```sql
ALTER TABLE StyleProfiles ADD COLUMN ImmersionDirective TEXT NOT NULL DEFAULT '';
ALTER TABLE StyleProfiles ADD COLUMN ActionDirective TEXT NOT NULL DEFAULT '';
ALTER TABLE StyleProfiles ADD COLUMN WordTargetMin INTEGER NOT NULL DEFAULT 0;
ALTER TABLE StyleProfiles ADD COLUMN WordTargetMax INTEGER NOT NULL DEFAULT 0;
ALTER TABLE StyleProfiles ADD COLUMN NarrativeWordTargetMin INTEGER NOT NULL DEFAULT 0;
ALTER TABLE StyleProfiles ADD COLUMN NarrativeWordTargetMax INTEGER NOT NULL DEFAULT 0;
```

**Note**: Columns are added with empty/zero defaults to allow the ALTER on existing rows, but runtime reads treat empty/zero as missing and fail-fast (FR-006). Existing Sultry profile MUST be updated to populate these fields before the code change goes live (FR-015).

#### Existing Fields (unchanged)

- `Id`, `Name`, `Description`, `Example`, `RuleOfThumb` (now labeled "Voice" in prompts), `ThemeAffinities`, `EscalatingThemeIds`, `StatBias`, `CreatedUtc`, `UpdatedUtc`

---

### 2. NarrativeSettings (Scenario Narrative)

**Location**: `DreamGenClone.Web/Domain/Scenarios/NarrativeSettings.cs`
**Storage**: `Scenarios.PayloadJson` (JSON-serialized within the scenario payload — no schema migration)

#### New Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `Tone` | `string?` | No | null | Mood/attitude (e.g., "Erotic, conversational, playful") |
| `Register` | `string?` | No | null | Language complexity (e.g., "Low to moderate language complexity") |
| `Focus` | `string?` | No | null | Subject emphasis (e.g., "Physical pleasure") |

#### Deprecated Fields

| Field | Type | Status | Description |
|-------|------|--------|-------------|
| `NarrativeTone` | `string?` | Deprecated (retained) | Legacy combined tone string. Used as fallback when `Tone` is empty (FR-008). |

#### Existing Fields (unchanged)

- `ProseStyle`, `PointOfView`, `NarrativeGuidelines`

#### Resolution Logic (FR-008)

```
resolvedTone =
    Tone (if non-empty)
    else NarrativeTone (if non-empty)
    else null (silent omit)

resolvedRegister = Register (if non-empty) else null (silent omit)
resolvedFocus = Focus (if non-empty) else null (silent omit)
```

#### No DB Migration

`NarrativeSettings` is serialized as JSON within `Scenarios.PayloadJson`. Adding C# properties is sufficient — existing payloads without these fields deserialize to null, and the resolution logic handles null gracefully.

---

### 3. IntensityProfile (Tone Profile / Heat Level)

**Location**: `DreamGenClone.Domain/StoryAnalysis/IntensityProfile.cs` (or equivalent)
**Table**: `ToneProfiles`

#### No Schema Changes

The IntensityProfile data model is unchanged. Existing fields (`Name`, `Description`, `Intensity`, phase offsets, `SceneDirective`) remain canonical.

#### Data Changes

- **Atmospheric** row (`Id=96b9e19cd16048a49e6460d0c115e658`): DELETE from `ToneProfiles` (moved to `StyleProfiles`).
- **Sensual** row (`Id=516919b1749847e8bc14a60663695f28`): UPDATE `Description` to the cleaned heat-level-only text (see research.md R2).
- **Emotional** row (`Id=a441720bf98d49d5b599aa460114a8f6`): UPDATE `Description` to the cleaned heat-level-only text (see research.md R2).

---

### 4. New StyleProfile: Atmospheric

**Table**: `StyleProfiles` (new row)

| Field | Value |
|-------|-------|
| `Id` | new GUID |
| `Name` | "Atmospheric" |
| `Description` | "Prioritize environmental details, lighting, sounds, and atmosphere over action or dialogue. Establish the mood through sensory imagery—what characters see, hear, smell, feel. Keep physical interaction subtle or absent. Let tension emerge from setting, body language, and subtext rather than explicit activity. Slow, patient pacing with rich descriptive language." |
| `Example` | "" |
| `RuleOfThumb` | "Favor environmental immersion, sensory imagery, and slow-burn atmosphere over action or explicit activity." |
| `ImmersionDirective` | "Stay inside this character's perceptions of the environment. Show, don't tell." |
| `ActionDirective` | "Let the setting drive the scene; respond to atmosphere naturally." |
| `WordTargetMin` | 200 |
| `WordTargetMax` | 400 |
| `NarrativeWordTargetMin` | 300 |
| `NarrativeWordTargetMax` | 500 |
| `ThemeAffinities` | {} |
| `EscalatingThemeIds` | [] |
| `StatBias` | {} |

---

### 5. PromptBuildContext — New Resolved Data

**Location**: `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs`

#### New Sub-Record: `ResolvedNarrativeToneData`

```csharp
public sealed record ResolvedNarrativeToneData
{
    public string? Tone { get; init; }        // Resolved via 3-tier logic
    public string? Register { get; init; }    // From NarrativeSettings.Register
    public string? Focus { get; init; }        // From NarrativeSettings.Focus
}
```

#### New Field on `PromptBuildContext`

```csharp
public required ResolvedNarrativeToneData NarrativeTone { get; init; }
```

#### Extended `ResolvedWritingStyleData`

Add fields to carry the SteeringProfile's new configurable directives:

```csharp
public sealed record ResolvedWritingStyleData
{
    // Existing fields (unchanged)
    public required string Description { get; init; }
    public required string Example { get; init; }
    public required string ProfileDefaultRuleOfThumb { get; init; }  // labeled "Voice" in prompts
    public required string PhaseRuleOfThumb { get; init; }
    public required string StyleHint { get; init; }

    // New fields (from SteeringProfile)
    public required string ProfileName { get; init; }           // For "Prose Style: {Name} — {Description}"
    public required string ImmersionDirective { get; init; }     // Fail-fast if empty
    public required string ActionDirective { get; init; }       // Fail-fast if empty
    public required int WordTargetMin { get; init; }            // Fail-fast if <= 0
    public required int WordTargetMax { get; init; }             // Fail-fast if <= 0 or <= WordTargetMin
    public required int NarrativeWordTargetMin { get; init; }   // Fail-fast if <= 0
    public required int NarrativeWordTargetMax { get; init; }   // Fail-fast if <= 0 or <= NarrativeWordTargetMin
}
```

---

## State Transitions

### Prompt Build State Machine (unchanged)

The prompt build flow remains: `RolePlayPromptBuilder.BuildAsync` → sort slots → run each slot's `WriteAsync` → enforce budget. No new states.

### Slot 17 Internal Ordering (new)

```
Slot 17 output:
  [if phase active and Character variant]
    Scene Direction:
      {PhaseGuidanceLines}
  
  Writing Instruction:
    Prose Style: {ProfileName} — {Description}
    Voice: {RuleOfThumb}
    [if Tone present] Tone: {Tone}[ — {Register}]
    [if Focus present]   Focus: {Focus}
    Heat Level: {ResolvedLabel} — {Description}
    Pacing: {PacingText}
    POV: {POVDirective}
    [if Character variant] Immersion: {ImmersionDirective}
    Word Target: Target {Min}-{Max} words[ of scene synthesis]
    [if Character variant] Action: {ActionDirective}
    [if Narrative variant] narrative constraints block
```

### Fail-Fast Transitions

```
PromptBuild → MissingSteeringProfileField → Error (no prompt emitted)
PromptBuild → MissingIntensityProfile → Error (no prompt emitted)
```

---

## Relationships

```
Scenario ──<NarrativeSettings>── (Tone, Register, Focus, NarrativeTone[deprecated])
Scenario ──<DefaultSteeringProfileId>── SteeringProfile
SteeringProfile ── (ImmersionDirective, ActionDirective, WordTarget*, NarrativeWordTarget*)
Scenario ──<DefaultIntensityProfileId>── IntensityProfile (ToneProfile)

PromptBuildContext
  ├── ResolvedScenarioData (Scenario)
  ├── ResolvedThemeData (Theme + PhaseGuidanceLines → Scene Direction)
  ├── ResolvedIntensityData (IntensityProfile → Heat Level + Pacing)
  ├── ResolvedWritingStyleData (SteeringProfile → Prose Style, Voice, Immersion, Action, Word Targets)
  └── ResolvedNarrativeToneData (NarrativeSettings → Tone, Register, Focus) [NEW]
```
