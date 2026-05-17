# Contracts: Physical Attributes

**Branch**: `001-physical-attributes` | **Date**: 2026-05-13

This document defines the public-surface contracts introduced by this feature: the formatter utility, the UI component, and the prompt-injection extension points.

---

## Contract 1 — PhysicalAttributesFormatter

**File**: `DreamGenClone.Web/Application/RolePlay/PhysicalAttributesFormatter.cs`  
**Type**: `internal static class PhysicalAttributesFormatter`

### Method: FormatBlock

```csharp
/// <summary>
/// Returns a compact, single-line labelled appearance string for prompt injection,
/// or <see cref="string.Empty"/> when <paramref name="attrs"/> is null or all fields are absent.
/// </summary>
/// <param name="attrs">The physical attributes to format. May be null.</param>
/// <returns>
/// Empty string when attrs is null or has no non-empty fields.
/// Otherwise: "Appearance — Label: value; Label: value; …"
/// Each field is its own entry; no compound merging.
/// AttractivenessRating is formatted as "n/10".
/// </returns>
internal static string FormatBlock(PhysicalAttributes? attrs)
```

**Output field order** (fields with null/empty values are omitted):

| Order | Label in output | Source property |
|-------|----------------|-----------------|
| 1 | `Age` | `Age` |
| 2 | `Height` | `Height` |
| 3 | `Weight` | `Weight` |
| 4 | `Hair colour` | `HairColour` |
| 5 | `Hair style` | `HairStyle` |
| 6 | `Eyes` | `EyeColour` |
| 7 | `Body type` | `BodyType` |
| 8 | `Skin` | `SkinTone` |
| 9 | `Ethnicity` | `Ethnicity` |
| 10 | `Bust` | `BustMeasurement` |
| 11 | `Waist` | `WaistMeasurement` |
| 12 | `Hip` | `HipMeasurement` |
| 13 | `Endowment length` | `EndowmentLength` |
| 14 | `Endowment girth` | `EndowmentGirth` |
| 15 | `Female genitalia` | `FemaleGenitalia` |
| 16 | `Marks` | `DistinguishingMarks` |
| 17 | `Piercings` | `Piercings` |
| 18 | `Tattoos` | `Tattoos` |
| 19 | `Clothing style` | `ClothingStyle` |
| 20 | `Attractiveness` | `AttractivenessRating` → `"n/10"` |

**Example outputs**:
```
Appearance — Hair colour: auburn; Hair style: shoulder-length; Eyes: green; Body type: athletic
Appearance — Age: 32; Hair colour: black; Eyes: brown; Body type: slim; Skin: fair; Bust: 34; Waist: 24; Hip: 35; Attractiveness: 9/10
```

---

## Contract 2 — PhysicalAttributesEditor Component

**File**: `DreamGenClone.Web/Components/Shared/PhysicalAttributesEditor.razor`

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `Attributes` | `PhysicalAttributes?` | No | Current value. Null means not yet initialised. |
| `AttributesChanged` | `EventCallback<PhysicalAttributes>` | Yes | Raised on every field change. Parent must update its binding and persist. |
| `Gender` | `string?` | No | Drives conditional field visibility. Values from `CharacterGenderCatalog`. |

### Behaviour contract

- When `Attributes` is null and the user edits any field: a new `PhysicalAttributes()` is created, the field is set, and `AttributesChanged` is invoked with the new instance.
- When `Attributes` is non-null: a shallow copy is mutated, then `AttributesChanged` is invoked with the modified copy.
- `EndowmentLength` and `EndowmentGirth` are **hidden** when `Gender` is `"Female"` (case-insensitive); visible otherwise (including null/unknown).
- `FemaleGenitalia` is **hidden** when `Gender` is `"Male"` (case-insensitive); visible otherwise (including null/unknown).
- Preset fields (`HairColour`, `HairStyle`, `EyeColour`, `BodyType`, `SkinTone`, `Ethnicity`, `FemaleGenitalia`) render as `<select>` with a leading empty/none option, catalog options, and a trailing `(Custom…)` sentinel. Selecting `(Custom…)` reveals a `<input type="text">` override.
- If a saved value does not match any catalog entry, the select shows `(Custom…)` and the text input shows the saved value.
- Free-text fields (`Age`, `Height`, `Weight`, `BustMeasurement`, `WaistMeasurement`, `HipMeasurement`, `DistinguishingMarks`, `Piercings`, `Tattoos`, `ClothingStyle`) render as `<input type="text">`.
- `AttractivenessRating` renders as `<input type="number" min="1" max="10">`.
- The component does not persist; all persistence is the parent's responsibility via the `AttributesChanged` callback.

### Usage example

```razor
<PhysicalAttributesEditor
    Attributes="@_session.PersonaPhysicalAttributes"
    AttributesChanged="@(attrs => { _session.PersonaPhysicalAttributes = attrs; _ = SaveSessionSettingsAsync(); })"
    Gender="@_session.PersonaGender" />
```

---

## Contract 3 — RolePlayContinuationService injection points

**File**: `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`

### Persona appearance injection (~line 364)

After `sb.AppendLine(session.PersonaDescription.Trim())`, inside the `if (!string.IsNullOrWhiteSpace(session.PersonaDescription))` block:

```csharp
var personaAppearance = PhysicalAttributesFormatter.FormatBlock(session.PersonaPhysicalAttributes);
if (!string.IsNullOrEmpty(personaAppearance))
{
    sb.AppendLine(personaAppearance);
}
```

### Character appearance injection (~line 581)

After `sb.AppendLine($"  {character.Name!.Trim()}{roleText}{relationSuffix}: {description}")`, inside the `foreach (var character in scenario.Characters...)` loop:

```csharp
var charAppearance = PhysicalAttributesFormatter.FormatBlock(character.PhysicalAttributes);
if (!string.IsNullOrEmpty(charAppearance))
{
    sb.AppendLine($"    {charAppearance}");
}
```

---

## Contract 4 — InteractionRetryService injection point

**File**: `DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs`

### Character appearance injection (~line 357)

After `sb.AppendLine($"  {character.Name!.Trim()}{roleText}{relationSuffix}: {description}")`, inside the `foreach (var character in scenario.Characters...)` loop:

```csharp
var charAppearance = PhysicalAttributesFormatter.FormatBlock(character.PhysicalAttributes);
if (!string.IsNullOrEmpty(charAppearance))
{
    sb.AppendLine($"    {charAppearance}");
}
```

---

## Contract 5 — RolePlayCreate persona template copy

**File**: `DreamGenClone.Web/Components/Pages/RolePlayCreate.razor`

### Local state field

```csharp
private PhysicalAttributes? _personaPhysicalAttributes;
```

### OnPersonaTemplateChanged extension

After the existing `_personaRole = CharacterRoleCatalog.Normalize(persona.Role);` line:

```csharp
_personaPhysicalAttributes = persona.PhysicalAttributes is not null
    ? ClonePhysicalAttributes(persona.PhysicalAttributes)
    : null;
```

On reset (empty template id), also set `_personaPhysicalAttributes = null;`.

### Session create extension

When constructing the new `RolePlaySession`:

```csharp
PersonaPhysicalAttributes = _personaPhysicalAttributes
```

---

## Contract 6 — ScenarioEditor copy-on-add

**File**: `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor`

When building a `Character` from a template, add:

```csharp
PhysicalAttributes = template.PhysicalAttributes is not null
    ? ClonePhysicalAttributes(template.PhysicalAttributes)
    : null
```

The `ClonePhysicalAttributes` helper creates a new `PhysicalAttributes` with all properties copied (memberwise copy is sufficient since all fields are strings or nullable int).
