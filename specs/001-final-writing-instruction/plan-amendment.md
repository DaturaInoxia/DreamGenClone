# Plan Amendment: Corrective Re-architecture

**Feature**: `001-final-writing-instruction`  
**Date**: 2026-07-22  
**Context**: The original consolidation of all writing direction into Slot 17 caused model repetition. The combined block of 10 directives at the prompt's end creates competing instructions where style/pacing/immersion directives telling the model to "linger, breathe, stay inside character" drown out the operational "advance the scene" directive. Additionally, the consolidation used StyleProfile.Description as Prose Style and NarrativeSettings.Tone as Tone, creating mismatched ownership — these should all come from the ToneProfile (intensity profile) since they must be consistent with Heat Level.

---

## Pre-Handoff Analysis Findings

### Critical Issues Discovered

#### A. Domain Class Name Mismatch

The plan refers to "ToneProfile" but the actual domain class is **`IntensityProfile`** (`DreamGenClone.Domain/StoryAnalysis/IntensityProfile.cs`). The DB table is `ToneProfiles` but the C# class is `IntensityProfile`. The plan must use the correct class name when adding the 5 new properties.

#### B. Atmospheric Already Exists as IntensityLevel.Intro

The plan proposes Atmospheric as a "6th ToneProfile" but it already exists in the ladder:
- `IntensityLevel.Intro = 0` → label "Atmospheric" (see `IntensityLadder.cs:30`)
- The existing Atmospheric ToneProfile row (`96b9e19cd16048a49e6460d0c115e658`) is already in `ToneProfiles` table with `Intensity = 'Intro'`
- `IntensityProfileService.CreateAsync` explicitly **blocks** creating Atmospheric profiles: `if (intensity == IntensityLevel.Intro) throw new InvalidOperationException("Atmospheric is narrative-only...")`

**Action needed**: The plan should say "populate the existing Atmospheric row with the 5 new directives" — not "create a 6th profile." The row already exists; it just needs the new columns populated.

#### C. Description Swapping Logic in ResolveIntensityAsync

`RolePlayContinuationService.ResolveIntensityAsync` (line ~955) has phase-offset logic that **swaps the entire profile** when the phase-adjusted level differs from the base level:
```csharp
if (resolvedLevel != profile.Intensity)
{
    var matchingProfile = allProfiles.FirstOrDefault(p => p.Intensity == resolvedLevel);
    if (matchingProfile is not null)
        description = matchingProfile.Description;
}
```

This means if a session has Emotional profile but the phase offset pushes it to Sensual, the code currently swaps the **Description** only. After the amendment, this swap must also swap the 5 new directive columns (ProseStyleDirective, VoiceDirective, etc.) — otherwise the model gets Emotional's Prose Style with Sensual's Heat Level.

**Action needed**: Update `ResolveIntensityAsync` to swap all 5 directives when the phase-adjusted level differs, not just `Description`.

#### D. IntensityProfileService.CreateAsync Signature

The `CreateAsync` method has a fixed parameter list and does not accept the 5 new directive fields. The plan must update:
- `IIntensityProfileService.CreateAsync` interface
- `IntensityProfileService.CreateAsync` implementation
- `SqlitePersistence.SaveToneProfileAsync` SQL and parameter binding
- `SqlitePersistence.LoadToneProfileAsync` reader mapping

#### E. POC Limit Enforcement

`IntensityProfileService.CreateAsync` enforces: `if (characterProfileCount >= PocDefaultProfiles.Length) throw`. The POC limit is 5 profiles (excluding Atmospheric). Adding Atmospheric as a usable character profile would require either:
- Lifting the POC limit
- Keeping Atmospheric as narrative-only (current behavior)

**Decision needed**: Should Atmospheric be selectable as a character intensity profile, or stay narrative-only? The plan currently implies it becomes a full character profile.

#### F. NarrativeSettings Deprecation Ripple Effects

`NarrativeSettings.NarrativeTone` is referenced in **27 files** including:
- `RolePlayAdaptiveStateService.cs:2280` — used for scenario keyword scoring
- `ScenarioAdaptationService.cs:149` — used in scenario adaptation
- `ScenarioTokenCounter.cs:53` — used for token counting
- `RolePlayWorkspace.razor:1108` — UI display
- `ScenarioEditor.razor:243` — UI editing (currently disabled)
- `InteractionRetryService.cs:389` — retry prompt context
- `StoryEngineService.cs:150` — story mode

Marking these `[Obsolete]` is safe, but the prompt builder must stop reading them. The other usages (scoring, token counting, UI display) can continue to read the deprecated fields — they're not prompt-related.

#### G. ResolvedWritingStyleData Has Required Fields That Will Lose Their Source

`ResolvedWritingStyleData` currently has `required` fields:
- `Description` (from StyleProfile.Description)
- `ProfileDefaultRuleOfThumb` (from StyleProfile.RuleOfThumb)
- `ProfileName` (from StyleProfile.Name)

If we stop reading these from StyleProfile, they become empty → fail-fast at prompt build. The plan must either:
- Remove these fields from `ResolvedWritingStyleData` and update all callers
- Make them optional (nullable) and stop emitting them in slots
- Populate them from the ToneProfile instead

**Recommended**: Remove `Description`, `ProfileDefaultRuleOfThumb`, `ProfileName` from `ResolvedWritingStyleData`. Keep `ImmersionDirective`, `ActionDirective`, `WordTarget*`, `Example`, `PhaseRuleOfThumb`, `StyleHint` (these stay on StyleProfile).

#### H. SlotContractTests and PromptBuilderTests Construct ResolvedIntensityData Directly

Both test files construct `ResolvedIntensityData` with no-arg constructor:
```csharp
Intensity = new ResolvedIntensityData(),
```

Adding 5 `required` fields will break these tests. The plan must update:
- `SlotContractTests.cs:96` and `:1084`
- `PromptBuilderTests.cs:94`

#### I. DB Migration Pattern

The existing migration pattern in `SqlitePersistence.cs` (lines 1176-1202) uses `pragma_table_info` checks before ALTER. The 5 new columns should follow the same pattern:
```csharp
var ensureCmd = connection.CreateCommand();
ensureCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ToneProfiles') WHERE name='ProseStyleDirective'";
if (Convert.ToInt32(await ensureCmd.ExecuteScalarAsync(cancellationToken)) == 0)
{
    var alterCmd = connection.CreateCommand();
    alterCmd.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN ProseStyleDirective TEXT NOT NULL DEFAULT ''";
    await alterCmd.ExecuteNonQueryAsync(cancellationToken);
}
// Repeat for VoiceDirective, ToneDirective, FocusDirective, HeatLevelDirective
```

### Summary of Required Plan Updates

| Issue | Action |
|-------|--------|
| A. Class name | Use `IntensityProfile` not `ToneProfile` in C# code references |
| B. Atmospheric | Populate existing row, don't create new. Decide: character-usable or narrative-only? |
| C. Description swapping | Update `ResolveIntensityAsync` to swap all 5 directives on phase offset |
| D. Service signature | Update `CreateAsync`, `SaveToneProfileAsync`, `LoadToneProfileAsync` |
| E. POC limit | Decide on Atmospheric as character profile |
| F. NarrativeSettings | Mark `[Obsolete]`, stop reading in prompt builder only |
| G. ResolvedWritingStyleData | Remove orphaned required fields |
| H. Tests | Update test constructors for new required fields |
| I. DB migration | Follow existing `pragma_table_info` pattern |

---

## Phase 1: Data Model

### 1.1 ToneProfiles — Add 5 Directive Columns

**Table**: `ToneProfiles`  
**Purpose**: ToneProfile becomes the single source for all intensity-dependent writing directives. Each profile gets a complete, non-contradictory set.

```sql
ALTER TABLE ToneProfiles ADD COLUMN ProseStyleDirective TEXT NOT NULL DEFAULT '';
ALTER TABLE ToneProfiles ADD COLUMN VoiceDirective TEXT NOT NULL DEFAULT '';
ALTER TABLE ToneProfiles ADD COLUMN ToneDirective TEXT NOT NULL DEFAULT '';
ALTER TABLE ToneProfiles ADD COLUMN FocusDirective TEXT NOT NULL DEFAULT '';
ALTER TABLE ToneProfiles ADD COLUMN HeatLevelDirective TEXT NOT NULL DEFAULT '';
```

**Fail-fast**: Prompt build fails if any column is empty for the active profile.

### 1.2 StyleProfiles — Drop Orphaned Columns

**Rationale**: `Description` and `RuleOfThumb` were used as Prose Style and Voice. With those moved to ToneProfiles, these columns become unused.

```sql
-- Phase 1: Null-safe (keep columns but stop reading them in code)
-- Phase 2 (after verification): 
-- ALTER TABLE StyleProfiles DROP COLUMN Description;
-- ALTER TABLE StyleProfiles DROP COLUMN RuleOfThumb;
```

**Remaining StyleProfile fields**: `Id`, `Name`, `Example`, `ImmersionDirective`, `ActionDirective`, `WordTargetMin`, `WordTargetMax`, `NarrativeWordTargetMin`, `NarrativeWordTargetMax`, `ThemeAffinities`, `EscalatingThemeIds`, `StatBias`, `CreatedUtc`, `UpdatedUtc`

### 1.3 NarrativeSettings — Deprecate Tone / Focus / Register

**Rationale**: These were scenario-level overrides for what is now intensity-dependent. No backward compatibility.

**Action**: 
- Set `[Obsolete]` on `NarrativeSettings.Tone`, `NarrativeSettings.Focus`, `NarrativeSettings.Register`, `NarrativeSettings.NarrativeTone`
- Prompt builder stops reading these fields entirely
- No schema migration needed (JSON-serialized)

### 1.4 Atmospheric — Populate Existing Row

Atmospheric already exists as `IntensityLevel.Intro` in `ToneProfiles` (row `96b9e19cd16048a49e6460d0c115e658`). It is currently narrative-only (`IntensityProfileService.CreateAsync` blocks creating Intro profiles). 

**Action**: Populate the existing row's 5 new directive columns. Keep it narrative-only for now — do not lift the POC limit or allow it as a character intensity profile. This can be revisited separately.

### 1.5 IntensityProfile Domain Class — Add 5 Properties

**File**: `DreamGenClone.Domain/StoryAnalysis/IntensityProfile.cs`

```csharp
public string ProseStyleDirective { get; set; } = string.Empty;
public string VoiceDirective { get; set; } = string.Empty;
public string ToneDirective { get; set; } = string.Empty;
public string FocusDirective { get; set; } = string.Empty;
public string HeatLevelDirective { get; set; } = string.Empty;
```

### 1.6 ResolvedIntensityData — Add 5 Fields

**File**: `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs`

```csharp
// NEW — from IntensityProfile columns
public required string ProseStyleDirective { get; init; }
public required string VoiceDirective { get; init; }
public required string ToneDirective { get; init; }
public required string FocusDirective { get; init; }
public required string HeatLevelDirective { get; init; }
```

### 1.7 ResolvedWritingStyleData — Remove Orphaned Fields

**File**: `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs`

Remove these `required` fields (their source columns are being dropped from StyleProfiles):
- `Description`
- `ProfileDefaultRuleOfThumb`
- `ProfileName`

Keep these (still sourced from StyleProfile):
- `Example`, `ImmersionDirective`, `ActionDirective`, `WordTargetMin`, `WordTargetMax`, `NarrativeWordTargetMin`, `NarrativeWordTargetMax`, `PhaseRuleOfThumb`, `StyleHint`

### 1.8 ResolveIntensityAsync — Swap All 5 Directives on Phase Offset

**File**: `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` (line ~955)

When the phase-adjusted level differs from the base level, currently only `Description` is swapped. After the amendment, all 5 directives must swap to match the resolved level's profile:
```csharp
if (resolvedLevel != profile.Intensity)
{
    var matchingProfile = allProfiles.FirstOrDefault(p => p.Intensity == resolvedLevel);
    if (matchingProfile is not null)
    {
        description = matchingProfile.Description;
        proseStyleDirective = matchingProfile.ProseStyleDirective;
        voiceDirective = matchingProfile.VoiceDirective;
        toneDirective = matchingProfile.ToneDirective;
        focusDirective = matchingProfile.FocusDirective;
        heatLevelDirective = matchingProfile.HeatLevelDirective;
    }
}
```

### 1.9 Persistence Layer Updates

**Files**: 
- `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` — `SaveToneProfileAsync`, `LoadToneProfileAsync`, migration
- `DreamGenClone.Infrastructure/StoryAnalysis/IntensityProfileService.cs` — `CreateAsync` signature
- `DreamGenClone.Application/StoryAnalysis/IIntensityProfileService.cs` — interface

**Migration pattern** (follows existing `pragma_table_info` approach):
```csharp
// For each of the 5 new columns:
var ensureCmd = connection.CreateCommand();
ensureCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ToneProfiles') WHERE name='ProseStyleDirective'";
if (Convert.ToInt32(await ensureCmd.ExecuteScalarAsync(cancellationToken)) == 0)
{
    var alterCmd = connection.CreateCommand();
    alterCmd.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN ProseStyleDirective TEXT NOT NULL DEFAULT ''";
    await alterCmd.ExecuteNonQueryAsync(cancellationToken);
}
```

---

## Phase 2: Tone Profile Data — All 6 Profiles

### 2.1 Suggestive

```
Prose Style: Playful, charged prose. Attraction conveyed through subtext and what goes unsaid.
Voice: Express desire through suggestion and implication. Bodies speak where words do not.
Tone: Flirtatious and suggestive. Light but electric.
Focus: The thrill of the unspoken. Lingering glances, charged proximity, the anticipation of what might happen.
Heat Level: Suggestive only. Flirtation, teasing dialogue, casual touch, brief kisses. Maintain erotic tension through subtext — no explicit physical content.
```

### 2.2 Emotional

```
Prose Style: Intimate, tender prose. Connection revealed through small gestures and vulnerability.
Voice: Favor emotional depth. Reveal the internal experience of intimacy.
Tone: Intimate and warm. Emotionally charged but restrained.
Focus: The deepening bond between characters. Trust, vulnerability, the risk of opening up.
Heat Level: Emotional intimacy. Tender gestures, eye contact, hand-holding, closeness. Physical expressions are meaningful but limited — let emotional connection lead.
```

### 2.3 Sensual

```
Prose Style: Sensory-driven prose. The body's experience of desire — what is touched, tasted, felt.
Voice: Build anticipation through physical detail. Describe each escalation as it happens.
Tone: Warm and intimate. The heat of a touch, the weight of a look.
Focus: The progression of physical intimacy. From tension to touch to the edge of surrender.
Heat Level: Sensual intimacy. Passionate kissing, caressing, exploration. Describe physical sensation and responsiveness. Avoid graphic anatomical detail.
```

### 2.4 Erotic

```
Prose Style: Direct, visceral prose. Bodies and pleasure described without euphemism.
Voice: Stay grounded in physical reality. Describe each act as it unfolds.
Tone: Raw and intimate. The physical truth of the moment.
Focus: Physical pleasure. Arousal, penetration, response, climax — what the body experiences.
Heat Level: Explicit intimacy. Clear descriptions of sexual acts, arousal, penetration, and orgasm. Direct language. Graphic when the scene demands it.
```

### 2.5 Hardcore

```
Prose Style: Unflinching, explicit prose. Anatomical detail, raw language, no softening.
Voice: Prioritize physical description and sensation. Bodies in motion, pleasure unfiltered.
Tone: Raw, intense, unapologetically explicit.
Focus: Maximum physical detail. Every act, every reaction, every sensation — breasts, genitals, penetration, oral sex, orgasms, cum shots.
Heat Level: Hardcore. Graphic descriptions of all sexual acts and anatomy. Raw language throughout. Emphasis on visual detail, physical sensation, and female pleasure. No romantic filters — pure, intense erotica.
```

### 2.6 Atmospheric

```
Prose Style: Environment-aware prose. The world described through what characters see, hear, and feel in their surroundings.
Voice: Let setting reflect inner state. Use environment to amplify mood without halting the scene.
Tone: Immersive and sensory. The weight of the air, the quality of light, the texture of the space.
Focus: The world around the characters. Setting as a character in its own right — what the environment reveals about the moment.
Heat Level: Atmospheric. Physical intimacy is implied through environment and subtext rather than direct description. Tension emerges from setting and body language.
```

### 2.7 Focus vs Heat Level Distinction

| | Focus | Heat Level |
|---|---|---|
| **What it answers** | What should the writing emphasize? | How explicit should the physical content be? |
| **Scope** | Narrative/subject emphasis | Physical explicitness boundary |
| **Example (Erotic)** | "Physical pleasure — what the body experiences" | "Explicit intimacy — direct descriptions of sexual acts, graphic when needed" |
| **Example (Hardcore)** | "Maximum physical detail — every act, every sensation" | "Graphic descriptions of all sexual acts and anatomy. Raw language throughout" |

### 2.8 Voice Directive Pacing Check

**Principle**: Voice sets narrative mode, not tempo. Pacing is handled by `SceneDirection.Pacing`. Voice must not re-introduce pacing implications.

| Profile | Voice | Check |
|---------|-------|-------|
| Suggestive | `Express desire through suggestion and implication. Bodies speak where words do not.` | ✅ No pacing implication |
| Emotional | `Favor emotional depth. Reveal the internal experience of intimacy.` | ✅ No pacing implication |
| Sensual | `Build anticipation through physical detail. Describe each escalation as it happens.` | ✅ "As it happens" = real-time, not delay |
| Erotic | `Stay grounded in physical reality. Describe each act as it unfolds.` | ✅ "As it unfolds" = match the action |
| Hardcore | `Prioritize physical description and sensation. Bodies in motion, pleasure unfiltered.` | ✅ No pacing implication |
| Atmospheric | `Let setting reflect inner state. Use environment to amplify mood without halting the scene.` | ✅ Explicit "without halting" guard |

---

## Phase 3: Prompt Slot Architecture

### 3.1 Zone B — WritingStyleSlot (Order 8, restored)

**Purpose**: Frame directives. Model sees these first — sets the mood boundary, not the action.

```
Style Guide:
  Prose Style: {Intensity.ProseStyleDirective}
  Voice: {Intensity.VoiceDirective}
  Tone: {Intensity.ToneDirective}
  Focus: {Intensity.FocusDirective}
  Heat Level: {Intensity.HeatLevelDirective}
  Pacing: {PacingText from SceneDirection.Pacing}
  POV: {POV text from ActorProfile.PerspectiveMode}
  Immersion: {StyleProfile.ImmersionDirective}
  Word Target: Target {StyleProfile.WordTargetMin}-{StyleProfile.WordTargetMax} words.
```

**IsTrimEligible**: true

### 3.2 Zone C — FinalInstructionSlot (Order 17, stripped)

**Purpose**: Operational directives. Model sees this last — pure recency.

**Character variant:**
```
Theme Contract: {ActiveTheme.Label}
  {ActiveTheme.Description}

Scene Guidance:
  {PhaseGuidanceLine1}
  ...

Scene Direction:
  {PhaseDirectiveLine1}
  ...

Action: {StyleProfile.ActionDirective}
```

**Narrative variant:**
```
Action:
  HARD CONSTRAINT: Zero dialogue...
  Synthesize only what the characters have already expressed in this turn.
  ...
  Physical Detail Checklist...
```

### 3.3 Unchanged Slots
- IntensityPacingSlot (Order 15) — unchanged
- UserDirectionSlot (Order 16) — unchanged
- ThemeContractSlot (Order 12) — already gutted, stays gutted

### 3.4 Slot Placement Validation

The exact division — specifically whether Pacing, POV, Immersion, and Word Target stay in Zone C or move to Zone B — needs trial-and-error validation with actual model output. The initial plan places them in Zone B. If the model under-prioritizes pacing or word target, individual directives can move back to Zone C incrementally.

---

## Phase 4: Implementation Steps

### Step 1: DB Schema + Data
- ALTER TABLE ToneProfiles ADD 5 columns
- UPDATE all 6 ToneProfiles with proposed text
- StyleProfile Description/RuleOfThumb: stop reading, drop columns later

### Step 2: Data Model (C#)
- Add 5 properties to `IntensityProfile` domain class
- Add 5 fields to `ResolvedIntensityData`
- Remove `Description`, `ProfileDefaultRuleOfThumb`, `ProfileName` from `ResolvedWritingStyleData`
- Update `ResolveIntensityAsync` to swap all 5 directives on phase offset
- Update `IIntensityProfileService.CreateAsync` signature + implementation
- Update `SqlitePersistence.SaveToneProfileAsync` + `LoadToneProfileAsync` for 5 new columns
- Add DB migration following existing `pragma_table_info` pattern
- `[Obsolete]` on NarrativeSettings.Tone/Focus/Register/NarrativeTone
- Remove NarrativeSettings.Tone/Focus/Register from prompt builder resolution only (other usages like scoring/token counting stay)

### Step 3: Prompt Slots
- Restore `WritingStyleSlot.WriteAsync()` — emit 9-part Style Guide
- Strip `FinalInstructionSlot.WriteAsync()` — context + Action only
- Route Prose/Voice/Tone/Focus/Heat from `ResolvedIntensityData` in both slots

### Step 4: Tests
- Update `SlotContractTests.cs:96` and `:1084` — construct `ResolvedIntensityData` with 5 new required fields
- Update `PromptBuilderTests.cs:94` — same
- Update `SlotContractTests` for new expected strings in WritingStyleSlot and FinalInstructionSlot
- Update `PromptBuilderTests` for new context field requirements
- Add fail-fast test for empty IntensityProfile directive columns
- Add test for `ResolveIntensityAsync` directive swapping on phase offset

### Step 5: UI (separate task, bundled for specific agent)
- Add 5 editable textareas to ToneProfile CRUD
- Wire to new ToneProfile columns

### Step 6: Verification
- Clean build
- Create new RP session with each ToneProfile
- Run 5+ AutoComplete turns
- Validate: no atmospheric repetition, scene advances, Action is last line

---

## Relevant Files

| File | Change |
|---|---|
| DB: `ToneProfiles` table | 5 new columns + UPDATE all 6 rows |
| `StyleProfiles` table | Stop reading Description/RuleOfThumb (drop later) |
| `NarrativeSettings.cs` | `[Obsolete]` on Tone/Focus/Register/NarrativeTone |
| `PromptBuildContext.cs` | 5 fields on ResolvedIntensityData |
| `WritingStyleSlot.cs` | Restore to 9-part Style Guide |
| `FinalInstructionSlot.cs` | Strip to context + Action |
| `SlotContractTests.cs` | Update assertions |
| `PromptBuilderTests.cs` | Update assertions |
| ToneProfile UI (`.razor`) | 5 new textareas (separate task) |
