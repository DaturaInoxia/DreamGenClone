# Contract: Seduction Archetype Catalog & Prompt Injection

**Branch**: `066-otherman-seduction` | **Frozen per spec contract**

This contract defines the `SeductionArchetypeCatalog` public surface and the `CharacterDataSlot` injection behavior. The catalog is the single source of truth for archetype definitions. The injection behavior specifies exactly how archetype guidance appears in continuation prompts.

---

## `SeductionArchetype` Record

```csharp
namespace DreamGenClone.Domain.StoryAnalysis;

public sealed record SeductionArchetype(
    string Id,           // Unique identifier, e.g. "Charmer"
    string DisplayName,  // Human-readable label, e.g. "The Charmer / Smooth Talker"
    string Description   // Prose behavioral directive, genre-grounded
);
```

**Contract invariants**:
- `Id` is non-empty, unique within the catalog, and serves as the lookup key.
- `DisplayName` is non-empty and unique.
- `Description` is non-empty, 50-300 characters, and contains concrete example behaviors.

---

## `SeductionArchetypeCatalog` Static Class

```csharp
namespace DreamGenClone.Domain.StoryAnalysis;

public static class SeductionArchetypeCatalog
{
    /// <summary>All 8 archetype definitions. Read-only, fixed at compile time.</summary>
    public static readonly IReadOnlyList<SeductionArchetype> All;

    /// <summary>
    /// Looks up an archetype by Id (case-insensitive).
    /// Returns null if the id is not recognized.
    /// </summary>
    public static SeductionArchetype? Get(string id);

    /// <summary>
    /// Builds combined prose guidance for the given archetype IDs.
    /// Format: "{DisplayName}: {Description}" per archetype, joined with " " (space).
    /// Returns null if archetypeIds is null or empty.
    /// Silently skips unrecognized IDs.
    /// </summary>
    public static string? BuildGuidance(IReadOnlyList<string> archetypeIds);
}
```

**Contract invariants**:
- `All.Count == 8` at all times (startup assert).
- `Get(null)` returns `null`.
- `Get("")` returns `null`.
- `Get("nonexistent")` returns `null`.
- `BuildGuidance(null)` returns `null`.
- `BuildGuidance([])` returns `null`.
- `BuildGuidance(["Charmer"])` returns non-null string containing "Charmer" display name and description.
- `BuildGuidance` output is deterministic — same input always produces same output.
- `BuildGuidance` output does NOT contain leading/trailing newlines.

---

## Character Entity Contract

### `Character.SeductionArchetypes`

```csharp
// In DreamGenClone.Web.Domain.Scenarios.Character:

/// <summary>
/// Seduction archetype identifiers. Values should match SeductionArchetypeCatalog entry Ids.
/// Empty = no archetype configured → role-level catalog fallback applies.
/// </summary>
public List<string> SeductionArchetypes { get; set; } = [];
```

**Contract invariants**:
- Initialized as empty `List<string>` (never null).
- Case-insensitive matching against catalog Ids.
- Only injected into prompts when `Character.Role == "OtherMan"` AND list is non-empty.
- Persisted as JSON array within scenario character blob. Round-trip fidelity guaranteed by `System.Text.Json`.

---

## `CharacterDataSlot` Injection Contract

### Modified `AppendCharacterRoleIntents`

When building the Character Role Intents section, for each character where:
1. `Role == "OtherMan"` (case-insensitive), AND
2. `SeductionArchetypes` is non-empty

Append after the role intent line:

```text
  Seduction style: {BuildGuidance(SeductionArchetypes)}
```

**Contract invariants**:
- The "Seduction style:" prefix is fixed English text; it does NOT vary by archetype.
- The guidance text comes exclusively from `SeductionArchetypeCatalog.BuildGuidance()` — no other source.
- When `SeductionArchetypes` is empty, the "Seduction style:" line is NOT emitted — only the role-level `SteerRoleIntentCatalog.GetRoleContext("OtherMan")` text appears.
- The guidance text is appended within the existing "Character Role Intents" section, NOT as a separate section.
- The full section including archetype guidance is trimmable (Slot 5 is `IsTrimEligible = true`, priority 2).

### Example Output

**With archetypes configured** (`["Competent", "Confidante"]`):

```text
Character Role Intents:
  Mark (OtherMan): His narrative job: pursue the Wife with singular focus, adapting his seduction approach...
  Seduction style: The Competent / Capable Man: Display physical competence and reliability — fix broken things, perform manual labor, display strength and skill. Create debt through acts of service. The Confidante / Emotional Connection: Build emotional intimacy through attentive listening, understanding her frustrations. Be the "shoulder to cry on." Create the "he actually understands me" realization.

  John (Husband): His narrative job: enable or block the encounter...
```

**Without archetypes** (fallback):

```text
Character Role Intents:
  Mark (OtherMan): His narrative job: pursue the Wife with singular focus, adapting his seduction approach...

  John (Husband): His narrative job: enable or block the encounter...
```

---

## `SteerRoleIntentCatalog` Update Contract

### OtherMan TOWARDS intent

The `("OtherMan", "Towards")` entry is replaced. The new text MUST:
- Reference the archetype framework's behavioral modes.
- Include concrete genre-appropriate behavior examples (not generic courtship advice).
- NOT duplicate the full archetype descriptions (the catalog is authoritative for those).
- Serve as a self-contained fallback that works without per-character archetype configuration.

### OtherMan role context (`GetRoleContext`)

The `GetRoleContext("OtherMan")` return value is updated to reference the archetype framework. MUST:
- Describe the OtherMan's narrative job in terms that encompass all 8 archetypes.
- NOT prescribe a specific archetype (that's the per-character config's job).
