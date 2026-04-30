# Data Model: Explicit Scene Writing Directives

**Branch**: `006-explicit-scene-writing` | **Date**: 2026-04-27

## Overview

Phase 1 of this feature involves no schema changes — it is purely prompt-assembly logic. Phase 2 adds one column to the `ToneProfiles` table.

---

## Entity: IntensityProfile (modified)

**File**: `DreamGenClone.Domain/StoryAnalysis/IntensityProfile.cs`

### Current State

```csharp
public sealed class IntensityProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IntensityLevel Intensity { get; set; } = IntensityLevel.SensualMature;
    public int BuildUpPhaseOffset { get; set; }
    public int CommittedPhaseOffset { get; set; }
    public int ApproachingPhaseOffset { get; set; } = 1;
    public int ClimaxPhaseOffset { get; set; } = 2;
    public int ResetPhaseOffset { get; set; } = -1;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    // ...
}
```

### Change (Phase 2)

Add one property:

```csharp
/// <summary>
/// Optional operator-authored instruction block for how to write explicit scenes
/// at this intensity level. Injected into the Scene Writing Directive block in the
/// system prompt when non-empty. When empty, the built-in static directive applies.
/// Max 2000 characters after sanitization.
/// </summary>
public string SceneDirective { get; set; } = string.Empty;
```

### Validation Rules

| Field | Constraint |
|-------|-----------|
| `SceneDirective` | Optional, nullable-tolerant (treated as empty string if null). Max 2000 chars (enforced pre-persistence and at injection). Sanitized before prompt use. |

### State Transitions

`SceneDirective` is set/updated through the UI Intensity Profile edit form. It does not affect phase transitions, adaptive state, or scoring. It is purely a prompt-assembly input.

---

## Database: ToneProfiles Table (modified, Phase 2)

**Database file**: `DreamGenClone.Web/data/dreamgenclone.dev.db` (SQLite)  
**Table**: `ToneProfiles`

### Current Schema

| Column | Type | Constraint | Notes |
|--------|------|-----------|-------|
| Id | TEXT | PRIMARY KEY | GUID string, N format |
| Name | TEXT | NOT NULL | Display name |
| Description | TEXT | NOT NULL DEFAULT '' | Prose style label shown in Intensity Writing Contract |
| Intensity | TEXT | NOT NULL | Enum name string |
| BuildUpPhaseOffset | INTEGER | NOT NULL DEFAULT 0 | Added via prior migration |
| CommittedPhaseOffset | INTEGER | NOT NULL DEFAULT 0 | Added via prior migration |
| ApproachingPhaseOffset | INTEGER | NOT NULL DEFAULT 1 | Added via prior migration |
| ClimaxPhaseOffset | INTEGER | NOT NULL DEFAULT 2 | Added via prior migration |
| ResetPhaseOffset | INTEGER | NOT NULL DEFAULT -1 | Added via prior migration |
| CreatedUtc | TEXT | NOT NULL | ISO 8601 |
| UpdatedUtc | TEXT | NOT NULL | ISO 8601 |

### Change (Phase 2) — Add `SceneDirective` Column

```sql
ALTER TABLE ToneProfiles ADD COLUMN SceneDirective TEXT NOT NULL DEFAULT '';
```

| Column | Type | Constraint | Notes |
|--------|------|-----------|-------|
| SceneDirective | TEXT | NOT NULL DEFAULT '' | Operator-authored scene-writing instruction. Empty = use static system default. |

### Migration

Applied inline in `SqlitePersistence.InitializeSchemaAsync` using the `pragma_table_info` pattern:

```csharp
var checkSceneDirective = connection.CreateCommand();
checkSceneDirective.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ToneProfiles') WHERE name='SceneDirective'";
var hasSceneDirective = Convert.ToInt64(await checkSceneDirective.ExecuteScalarAsync(cancellationToken)) > 0;
if (!hasSceneDirective)
{
    var alterSceneDirective = connection.CreateCommand();
    alterSceneDirective.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN SceneDirective TEXT NOT NULL DEFAULT ''";
    await alterSceneDirective.ExecuteNonQueryAsync(cancellationToken);
    _logger.LogInformation("Migrated ToneProfiles table: added SceneDirective column");
}
```

Existing rows will have `SceneDirective = ''`, which correctly triggers the static fallback in prompt assembly.

---

## New Utility: PromptSanitizer

**File**: `DreamGenClone.Web/Application/RolePlay/PromptSanitizer.cs` (new)

Not a domain entity, but documented here as the data-transformation boundary for `SceneDirective` before prompt injection.

### Responsibility

Accepts raw `SceneDirective` string from persistence. Returns a sanitized version safe for injection into the LLM system prompt.

### Transformation Rules (in order)

1. If null or empty → return `string.Empty`
2. Truncate to 2000 characters (hard limit)
3. Remove null bytes (`\0`) and ASCII control characters (`< \x20`) except `\n`, `\r`, `\t`
4. Split into lines; remove any line whose trimmed content starts with a known LLM role/injection token:
   - `SYSTEM:` / `system:`
   - `USER:` / `user:`
   - `ASSISTANT:` / `assistant:`
   - `[INST]`
   - `</s>`
   - `###`
   - `<|`
5. Re-join remaining lines; trim trailing whitespace

### Input/Output Contract

```
Input:  raw string from DB (may be empty, may contain control chars, may contain injection tokens)
Output: sanitized string ≤ 2000 chars with no injection tokens
```

### Not the Responsibility of PromptSanitizer

- Authorization (that's the UI/service layer)  
- HTML encoding (the LLM receives raw text)  
- Length validation beyond truncation (the service layer enforces the 2000-char limit before save; the sanitizer truncates defensively at injection)

---

## Prompt Assembly: Scene Writing Directive Block

**Not a database entity** — documented here as the output contract of the prompt assembly logic.

### Block Injection Condition

- `resolvedScale >= (int)IntensityLevel.Explicit` AND
- `currentPhase != "BuildUp"` AND
- `intent != PromptIntent.Instruction`

### Block Content Decision

1. If `resolvedProfile.SceneDirective` is non-empty after sanitization → inject sanitized profile value
2. Else → inject static default directive text

Exactly one source is used. No fallback to a secondary profile.

### Static Default Directive Text

The static default text (used when `SceneDirective` is empty):

```
Scene Writing Directive:
Core Scene-Writing Principles:
- You are writing an adult erotic scene. Write with physical and sensory specificity.
- Describe body positioning, physical sensations, movement, and pacing explicitly.
- Do not rush to climax. Sustain tension and escalation across multiple paragraphs AND multiple turns.
- Each AI response advances physical intimacy by one measured increment. Major transitions—moving from kissing to oral stimulation, from oral to penetration, changing positions—warrant their own turns. Do not cram multiple major acts into a single response.
Act Variety and Attention:
- Explore the full spectrum of physical intimacy: kissing (mouth, neck, body), licking and sucking (nipples, body, genitals), manual stimulation (hands on body, groin, genitals), oral sex, and penetrative sex in varied positions.
- Not every scene requires every act—adapt to context, character dynamics, and established continuity. But each act you DO include receives dedicated, detailed attention.
- Vary positions and approaches based on setting, character preferences, and emergent dynamics. Avoid formulaic sequences.
Pacing and Urgency:
- Narrative urgency (characters in a hurry, limited time, spontaneous encounter) is expressed through action intensity, breathless dialogue, and emotional tone. It does NOT abbreviate the writing.
- Even a "quickie" scene spans multiple full beats. The characters may be rushed; the prose remains detailed.
Continuity Awareness:
- Foreplay, teasing, and escalating physical contact must be written in detail before penetration occurs, unless the scene has already passed that point.
- Use direct, explicit language appropriate to the resolved intensity level.
```
