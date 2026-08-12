# Phase 1 Data Model: OtherMan Seduction Archetype

**Branch**: `066-otherman-seduction` | **Date**: 2026-08-11

This document defines the entities, fields, validation rules, and state transitions introduced by the OtherMan Seduction Archetype feature. All persisted config is UI-backed (repo Hard Rule) with a single fallback path through `SteerRoleIntentCatalog`.

---

## Domain Layer (`DreamGenClone.Domain/StoryAnalysis/`)

### `SeductionArchetype` (record)

```csharp
namespace DreamGenClone.Domain.StoryAnalysis;

/// <summary>
/// A named behavioral mode for OtherMan seduction, grounded in erotic fiction genre analysis.
/// Each entry has an identifier, display name, and a prose description of the behavioral pattern.
/// </summary>
public sealed record SeductionArchetype(string Id, string DisplayName, string Description);
```

**Fields**:

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | `string` | Unique identifier, e.g. `"Charmer"`. Used for catalog lookup and persisted on `Character.SeductionArchetypes`. |
| `DisplayName` | `string` | Human-readable label, e.g. `"The Charmer / Smooth Talker"`. Used in UI. |
| `Description` | `string` | Prose behavioral directive grounded in erotic fiction tropes, e.g. "Use calibrated compliments, witty banter, and verbal seduction. Make her feel uniquely seen and desired. Create intimacy through words before any physical move." Injected directly into prompts. |

### `SeductionArchetypeCatalog` (static class)

```csharp
namespace DreamGenClone.Domain.StoryAnalysis;

/// <summary>
/// Code-defined catalog of 8 seduction archetypes for the OtherMan role.
/// Single source of truth for archetype definitions and prose guidance.
/// Analogous to <see cref="SteerRoleIntentCatalog"/>.
/// </summary>
public static class SeductionArchetypeCatalog
{
    public static readonly IReadOnlyList<SeductionArchetype> All = [ /* 8 entries */ ];

    /// <summary>Case-insensitive lookup by Id.</summary>
    public static SeductionArchetype? Get(string id);

    /// <summary>Builds combined prose guidance for the given archetype IDs.
    /// Returns null if the list is empty.</summary>
    public static string? BuildGuidance(IReadOnlyList<string> archetypeIds);
}
```

**Validation**:
- `All` contains exactly 8 entries (startup self-check).
- All `Id` values are unique and non-empty.
- `BuildGuidance` returns `null` for empty input (so callers can distinguish "no archetypes configured" from "archetypes produce empty text").
- `BuildGuidance` silently skips unrecognized IDs (graceful degradation for data migration edge cases).

**Archetype Entries** (exact Id values):

| Id | DisplayName |
|----|-------------|
| `Charmer` | The Charmer / Smooth Talker |
| `Competent` | The Competent / Capable Man |
| `Confidante` | The Confidante / Emotional Connection |
| `Tease` | The Tease / Playful Provocateur |
| `Protector` | The Protector / Rescuer |
| `Dominant` | The Dominant / Assertive |
| `Mysterious` | The Mysterious / Dangerous Stranger |
| `Situational` | The Situational / Opportunist |

---

## Web Domain Layer (`DreamGenClone.Web/Domain/Scenarios/`)

### `Character.SeductionArchetypes` (new property)

```csharp
// In DreamGenClone.Web.Domain.Scenarios.Character:

/// <summary>
/// Seduction archetype identifiers for this character when cast as OtherMan.
/// Empty list = no archetype configured → falls back to SteerRoleIntentCatalog role-level intent.
/// Values must match <see cref="SeductionArchetypeCatalog"/> entry Ids.
/// </summary>
public List<string> SeductionArchetypes { get; set; } = [];
```

**Validation**:
- Values should match `SeductionArchetypeCatalog.All[*].Id` (case-insensitive). Unknown values are silently ignored at prompt-build time (graceful degradation).
- Only meaningful when `Character.Role == "OtherMan"`. Non-OtherMan characters may carry archetypes (for future role-switching scenarios) but they are not injected into prompts.
- Empty list is valid and means "use role-level catalog fallback."

**Persistence**: Serialized as a JSON string array within the scenario's character JSON blob in the `Scenarios` table (`PayloadJson` column). No new table or column. The existing `System.Text.Json` serializer handles `List<string>` natively.

---

## Application Layer (`DreamGenClone.Web/Application/RolePlay/Prompts/Slots/`)

### `CharacterDataSlot` modifications

The `AppendCharacterRoleIntents` method is extended to append archetype guidance for OtherMan characters:

```csharp
private static void AppendCharacterRoleIntents(StringBuilder sb, IReadOnlyList<ScenarioCharacter> characters)
{
    // ... existing role intent emission ...

    // NEW: Append seduction archetype guidance for OtherMan characters
    foreach (var character in characters)
    {
        if (!string.Equals(character.Role?.Trim(), "OtherMan", StringComparison.OrdinalIgnoreCase))
            continue;
        if (character.SeductionArchetypes is not { Count: > 0 })
            continue;

        var guidance = SeductionArchetypeCatalog.BuildGuidance(character.SeductionArchetypes);
        if (guidance is not null)
        {
            sb.AppendLine();
            sb.Append("  Seduction style: ");
            sb.AppendLine(guidance);
        }
    }
}
```

**Output format** (appended to the Character Role Intents section):

```text
Character Role Intents:
  Mark (OtherMan): His narrative job: pursue the Wife with singular focus...
  Seduction style: [archetype guidance text]

  John (Husband): His narrative job: enable or block...
```

**Injection rules** (FR-007):
- Only applies when `character.Role == "OtherMan"`.
- Only emits archetype guidance when `SeductionArchetypes` is non-empty.
- When archetypes are empty, only the role-level `SteerRoleIntentCatalog.GetRoleContext("OtherMan")` text is emitted (fallback behavior — unchanged from today).

---

## SteerRoleIntentCatalog Update

### OtherMan TOWARDS intent (FR-005)

The existing `SteerRoleIntentCatalog` entry for `("OtherMan", "Towards")` is replaced with research-backed seduction behavioral directives that reference the archetype framework:

```csharp
new("OtherMan", "Towards",
    "He should actively seduce her using proven seduction patterns. Draw from these behavioral modes as the situation demands: " +
    "display physical competence and reliability (fix things, show strength), " +
    "build emotional intimacy through attentive listening and understanding her frustrations, " +
    "use calibrated verbal seduction — compliments and witty banter that make her feel uniquely desired, " +
    "create playful tension through teasing and 'accidental' physical contact, " +
    "leverage protector/rescuer dynamics when opportunity arises, " +
    "project confident physical presence and direct intent, " +
    "cultivate intrigue through controlled mystery, " +
    "or exploit situational proximity and heightened emotional states. " +
    "Adapt the approach to the Wife's responses — the seduction should feel earned and natural, not mechanical.")
```

### OtherMan role context (`GetRoleContext`)

The existing role context text is preserved but lightly updated to reference the archetype framework:

```csharp
"OtherMan" => "His narrative job: pursue the Wife with singular focus, adapting his seduction approach to succeed. " +
    "Core conflict: find the method that works right now — whether through displays of competence, emotional connection, " +
    "verbal charm, playful provocation, protective rescue, confident assertion, mysterious intrigue, or situational exploitation."
```

---

## Relationship Summary

```mermaid
erDiagram
    SeductionArchetypeCatalog ||--o{ Character : "references (by Id)"
    Character ||--o| SteerRoleIntentCatalog : "falls back to (when SeductionArchetypes empty)"
    CharacterDataSlot ||--|| SeductionArchetypeCatalog : "calls BuildGuidance()"
    CharacterDataSlot ||--|| SteerRoleIntentCatalog : "calls GetRoleContext()"
    CharacterDataSlot ||--|| Character : "reads SeductionArchetypes"

    SeductionArchetypeCatalog {
        IReadOnlyList SeductionArchetype All
        Get string id
        BuildGuidance IReadOnlyList archetypeIds
    }

    Character {
        string Role
        List string SeductionArchetypes
    }

    SteerRoleIntentCatalog {
        GetRoleContext string role
        GetIntent string role SteerDirection
    }
```
