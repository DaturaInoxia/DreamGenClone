# Data Model: Physical Attributes

**Branch**: `001-physical-attributes` | **Date**: 2026-05-13

---

## New Entities

### PhysicalAttributes
`DreamGenClone.Domain/Templates/PhysicalAttributes.cs`

A value object carrying optional appearance fields for a character or persona. Stored as JSON nested inside existing payload columns; no standalone database table.

| Field | Type | Notes |
|-------|------|-------|
| `Age` | `string?` | Free-text (e.g. "32", "mid-thirties") |
| `Height` | `string?` | Free-text (e.g. "5'7\"", "170 cm") |
| `Weight` | `string?` | Free-text (e.g. "130 lbs", "58 kg") |
| `HairColour` | `string?` | Preset or custom; see catalog |
| `HairStyle` | `string?` | Preset or custom; see catalog |
| `EyeColour` | `string?` | Preset or custom; see catalog |
| `BodyType` | `string?` | Preset or custom; see catalog |
| `SkinTone` | `string?` | Preset or custom; see catalog |
| `Ethnicity` | `string?` | Preset or custom; see catalog |
| `BustMeasurement` | `string?` | Free-text (e.g. "36") |
| `WaistMeasurement` | `string?` | Free-text (e.g. "26") |
| `HipMeasurement` | `string?` | Free-text (e.g. "36") |
| `EndowmentLength` | `string?` | Free-text; rendered only for Male/Unknown gender |
| `EndowmentGirth` | `string?` | Free-text; rendered only for Male/Unknown gender |
| `FemaleGenitalia` | `string?` | Preset or custom; rendered only for Female/Unknown gender |
| `DistinguishingMarks` | `string?` | Free-text (e.g. "scar above left eyebrow") |
| `Piercings` | `string?` | Free-text |
| `Tattoos` | `string?` | Free-text |
| `ClothingStyle` | `string?` | Free-text |
| `AttractivenessRating` | `int?` | Integer 1–10 |

**Constraints**:
- All string fields are nullable; absent fields are omitted from prompt injection.
- `AttractivenessRating` must be 1–10 (enforced by UI min/max; stored as-is if valid).
- No enums. All preset values are plain strings from `PhysicalAttributesCatalog`.

---

### PhysicalAttributesCatalog
`DreamGenClone.Domain/Templates/PhysicalAttributesCatalog.cs`

Static readonly string arrays. Used only by the UI editor to populate preset dropdowns.

| Field | Preset values |
|-------|--------------|
| `HairColours` | Black, Dark Brown, Brown, Auburn, Dirty Blonde, Blonde, Platinum Blonde, Red, Strawberry Blonde, Silver, Grey, White, Mixed/Highlighted |
| `HairStyles` | Short, Pixie Cut, Bob, Shoulder-Length, Long, Wavy, Curly, Straight, Braided, Ponytail, Bun, Shaved |
| `EyeColours` | Brown, Dark Brown, Hazel, Green, Blue, Grey, Amber |
| `BodyTypes` | Slim, Petite, Athletic, Toned, Average, Curvy, Full-Figured, Muscular, Stocky, Plus-Size |
| `SkinTones` | Fair, Light, Light Olive, Olive, Medium Brown, Brown, Dark Brown, Deep Brown, Ebony |
| `Ethnicities` | Caucasian, Hispanic/Latina, African/Black, East Asian, South Asian, Middle Eastern, Mixed, Other |
| `FemaleGenitaliaOptions` | Pristine, Tight, Normal, Relaxed, Well-Used |

**Note**: These are ordered for display; the UI adds a leading `(none)` empty option and a trailing `(Custom…)` sentinel.

---

## Extended Entities

### TemplateDefinition (extended)
`DreamGenClone.Domain/Templates/TemplateDefinition.cs`

**New property added**:
```
PhysicalAttributes? PhysicalAttributes
```
Existing properties are unchanged.

**Storage path**: Serialised as `physicalAttributes` JSON node inside `Templates.PayloadJson` via `CharacterTemplatePayload`. Only present for Character and Persona template types (guarded by `IsCharacterLikeTemplate` in `TemplateService`).

---

### Character (extended)
`DreamGenClone.Web/Domain/Scenarios/Character.cs`

**New property added**:
```
PhysicalAttributes? PhysicalAttributes
```
Existing properties are unchanged.

**Storage path**: The `Character` list is embedded in the Scenario entity which is serialised to JSON. The new property participates automatically; no schema change needed.

**Copy-on-add rule**: When `ScenarioEditor` adds a character from a template that has `PhysicalAttributes` set, a shallow clone is copied into the new `Character` instance. Subsequent template edits do not affect the scenario copy (snapshot isolation).

---

### RolePlaySession (extended)
`DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs`

**New property added**:
```
PhysicalAttributes? PersonaPhysicalAttributes
```
Placed after the existing `PersonaRelationTargetId` property (last persona property in the current entity).

**Storage path**: `RolePlaySession` is serialised as JSON into `Sessions.PayloadJson`. The new property participates automatically; no schema change needed.

**Inheritance rule**: `RolePlayCreate` copies `template.PhysicalAttributes` onto `_personaPhysicalAttributes` (local state) inside `OnPersonaTemplateChanged`. On session create, this value is written to the new `RolePlaySession.PersonaPhysicalAttributes`.

---

### CharacterTemplatePayload (extended, internal)
`DreamGenClone.Application/Templates/TemplateService.cs` — private sealed class

**New property added**:
```
PhysicalAttributes? PhysicalAttributes
```

**SerializePayload changes**: Add `PhysicalAttributes = template.PhysicalAttributes` to the payload initialiser.

**TryDeserializeCharacterPayload changes**: After the existing `relationTargetTemplateId` block, add a block that reads the `physicalAttributes` JSON object node and deserialises it using `JsonSerializer.Deserialize<PhysicalAttributes>(...)`. If the node is absent or null, `payload.PhysicalAttributes` remains null (safe default — no fallback values).

---

## State Transitions

### Auto-initialise on first edit
When `PhysicalAttributesEditor` raises `AttributesChanged` for the first time and the parent's `Attributes` parameter is null, the parent's binding initialises a new `PhysicalAttributes()` instance. The editor component itself does not allocate; it signals via the callback. This is consistent with how Blazor two-way binding works — the parent owns the state.

### Snapshot isolation
- Template → Session: copy on RolePlayCreate template select; no live link.
- Template → Scenario character: copy on add; no live link.
- Both copies are plain reference-broken JSON-serialisable objects; no shared identity.

---

## Formatter Output

`PhysicalAttributesFormatter.FormatBlock(PhysicalAttributes? attrs)` returns a string of the form:

```
Appearance — Age: 32; Hair colour: auburn; Hair style: shoulder-length; Eyes: green; Body type: athletic; Skin: light olive; Ethnicity: Hispanic; Bust: 36; Waist: 26; Hip: 36; Attractiveness: 8/10
```

Rules:
- Returns `string.Empty` when `attrs` is null or all fields are null/empty.
- Fields are emitted in a fixed, consistent order (see contracts).
- Each field is its own labelled entry; no compound merging.
- `AttractivenessRating` is formatted as `n/10`.
- `EndowmentLength` and `EndowmentGirth` are included regardless of gender (the formatter has no gender context; gender-gating is UI-only).
