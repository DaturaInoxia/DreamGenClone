# Research: Physical Attributes

**Branch**: `001-physical-attributes` | **Date**: 2026-05-13

## Summary

All NEEDS CLARIFICATION items from the spec have been resolved through codebase exploration. No unknowns remain. This document records the decisions and the evidence that supports each one.

---

## RD-001: Domain model placement for PhysicalAttributes

**Decision**: New `PhysicalAttributes` sealed class lives in `DreamGenClone.Domain/Templates/`, alongside the existing `TemplateDefinition.cs`.

**Rationale**: The `DreamGenClone.Domain` project is the correct home for cross-cutting data types shared between entities. `TemplateDefinition`, `Character`, and `RolePlaySession` all reference this type; placing it in `Domain` avoids circular references and aligns with the existing pattern for shared domain value objects (e.g. catalog types, stat maps).

**Alternatives considered**: Placing it in `DreamGenClone.Web/Domain/` — rejected because `DreamGenClone.Domain` is already depended on by the Web layer, and placing a shared type in `Web/Domain` would prevent `DreamGenClone.Application` or `DreamGenClone.Infrastructure` from referencing it without a circular dependency.

---

## RD-002: Persistence approach — no migration needed

**Decision**: `PhysicalAttributes` is stored as JSON nested inside existing payload columns. No new database schema, migration, or table is needed.

**Evidence**:
- `TemplateDefinition` → persisted in `Templates.PayloadJson` via `CharacterTemplatePayload` (private sealed class in `TemplateService.cs` line 268). Adding `PhysicalAttributes?` to `CharacterTemplatePayload` and updating `SerializePayload` / `TryDeserializeCharacterPayload` is sufficient.
- `Character` → embedded in the Scenario entity as a list; the entire Scenario is serialized to JSON. Adding `PhysicalAttributes?` to `Character.cs` automatically participates in that serialization.
- `RolePlaySession` → stored in `Sessions.PayloadJson`. The session object is serialized as a whole. Adding `PersonaPhysicalAttributes?` to `RolePlaySession.cs` is sufficient.

**Alternatives considered**: Dedicated `PhysicalAttributes` table with foreign keys — rejected because all existing persona/character state uses JSON payload columns, no new schema is warranted for an optional nullable value object, and the spec explicitly forbids a schema change.

---

## RD-003: CharacterTemplatePayload — internal private class

**Decision**: `CharacterTemplatePayload` remains a private sealed class inside `TemplateService.cs`. The new `PhysicalAttributes?` property is added directly to it.

**Evidence**: `CharacterTemplatePayload` is defined at line 268 of `TemplateService.cs` and is used only within that file. It currently has 5 properties. `TryDeserializeCharacterPayload` (line 191) manually reads JSON properties; it must be extended to read a `physicalAttributes` JSON object node and deserialise it.

**Rationale**: Keeping it private and extending it in-place avoids surfacing an internal DTO as a public API type. The deserialiser extension follows the same `TryGetProperty` pattern already used for every other field.

---

## RD-004: Prompt injection points — exact locations confirmed

**Decision**: Injection is inline immediately after the description text for both characters and the persona, within the `StringBuilder` that builds the final prompt string.

**Evidence**:

| Service | Section | Line range | Exact variable | Injection point |
|---------|---------|------------|---------------|----------------|
| `RolePlayContinuationService` | Persona | ~364 | `session.PersonaDescription` | After `sb.AppendLine(session.PersonaDescription.Trim())` |
| `RolePlayContinuationService` | Characters | ~580 | `character.Description` | After `sb.AppendLine($"  {character.Name}…: {description}")` |
| `InteractionRetryService` | Characters | ~357 | `character.Description` | After `sb.AppendLine($"  {character.Name}…: {description}")` |

The persona section is inside a `if (!string.IsNullOrWhiteSpace(session.PersonaDescription))` guard. The appearance block should only be appended when the formatter returns a non-empty string — the formatter already handles null/empty PhysicalAttributes by returning empty string.

---

## RD-005: UI component save trigger in RolePlayWorkspace

**Decision**: The persona attributes editor binds to `_session.PersonaPhysicalAttributes` and the `AttributesChanged` callback fires `SaveSessionSettingsAsync()` — the same method already called for every other persona field change in the workspace panel.

**Evidence**: Every field in the persona panel (Name, Description, Role, Gender, Perspective, Relation) uses the pattern:
```razor
@onchange="@(e => { _session.FieldName = ...; _ = SaveSessionSettingsAsync(); })"
```
The `PhysicalAttributesEditor` `AttributesChanged` callback should follow the same pattern: `@(attrs => { _session.PersonaPhysicalAttributes = attrs; _ = SaveSessionSettingsAsync(); })`.

---

## RD-006: Scenario character copy-on-add path

**Decision**: When a character is added to a scenario from a template, `PhysicalAttributes` is copied from the template to the new `Character` instance at add-time, so the scenario owns an independent snapshot.

**Evidence**: `ScenarioEditor.razor` already copies `Name`, `Description`, `Role`, `Gender`, `BaseStats`, `PerspectiveMode`, and `TemplateId` from the source template when a character is added. Adding `PhysicalAttributes = template.PhysicalAttributes is not null ? Clone(template.PhysicalAttributes) : null` follows the same copy pattern.

---

## RD-007: Shared component naming convention

**Decision**: New component is named `PhysicalAttributesEditor.razor`, placed in `DreamGenClone.Web/Components/Shared/`.

**Evidence**: Existing shared components follow the naming pattern `<Domain><Function>.razor` — e.g. `ModelDetailsEditor.razor`, `ModelSettingsPanel.razor`, `TemplatesPanel.razor`. The spec explicitly names it `PhysicalAttributesEditor`, which matches the convention.

---

## RD-008: Gender conditional logic — string comparison

**Decision**: Visibility conditions use case-insensitive string comparison against the `CharacterGenderCatalog` string constants already defined in the project (`"Male"`, `"Female"`, `"Unknown"`). No enums are introduced.

**Rationale**: The existing codebase uses `CharacterGenderCatalog.NormalizeForCharacter(...)` throughout; all gender values stored in entities are normalised strings from this catalog. Comparing against `"Male"` / `"Female"` in the editor component is safe and consistent.

---

## RD-009: PhysicalAttributesFormatter placement

**Decision**: `PhysicalAttributesFormatter.cs` is a static class (or a simple non-injected class) placed in `DreamGenClone.Web/Application/RolePlay/`, alongside `RolePlayContinuationService` and `InteractionRetryService`.

**Rationale**: The formatter is consumed only by the two Web-layer services. Making it static or a simple sealed class (rather than an injected service) keeps the call sites clean — `PhysicalAttributesFormatter.FormatBlock(attrs)` — and avoids adding a new DI registration for a pure-function utility. This matches the pattern used by `RolePlayRelationFormatter` which is already a static utility class used in the same services.

**Alternatives considered**: Registering as `IPhysicalAttributesFormatter` service — rejected because the formatter has no dependencies, requires no configuration, and is pure functional; a static class is simpler and sufficient.
