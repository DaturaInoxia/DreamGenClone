# Feature Specification: Physical Attributes

**Feature Branch**: `001-physical-attributes`  
**Created**: 2026-05-13  
**Status**: Draft  

## Clarifications

### Session 2026-05-13

- Q: When a character is added to a scenario from a template that already has `PhysicalAttributes` defined, what should happen to those attributes in the scenario? → A: Copy the template's `PhysicalAttributes` into the scenario character at add-time; the scenario copy is independent from then on.
- Q: Should the appearance formatter consolidate related fields (e.g. Hair colour + style merged, measurements merged) or treat each field as its own labelled entry? → A: Each field is its own labelled entry — no compound merging. Null/empty fields are individually omitted.
- Q: In RolePlayWorkspace, what save trigger should persist persona physical attribute changes? → A: Attributes are saved as part of the existing persona-panel save action (same button/trigger already used for other persona fields).
- Q: Where in the prompt should the appearance block be injected for characters and the persona? → A: Immediately after the description text for both — inline within the same section, no separate system note.
- Q: Where is `PersonaPhysicalAttributes` persisted on `RolePlaySession`? → A: Stored in the existing session payload JSON column (same one carrying other session-level persona overrides); no new column or table needed.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Author Defines Character Appearance (Priority: P1)

As a story author, I want to define detailed physical appearance for characters and personas so that the AI generates narratively consistent descriptions in roleplay sessions.

**Why this priority**: Physical appearance is the core deliverable of this feature. Without it, no other story can be told. Every other phase depends on this data existing.

**Independent Test**: Create a character template, fill in hair colour, eye colour, and body type using the preset dropdowns, save it, reload the page, and confirm all values are restored.

**Acceptance Scenarios**:

1. **Given** a character template is open in the Templates editor, **When** the author picks "Auburn" from the Hair Colour dropdown and saves, **Then** reloading the template shows "Auburn" pre-selected.
2. **Given** a character template with gender "Male", **When** the author views the physical attributes panel, **Then** the Female Genitalia field is hidden and the Endowment fields are visible.
3. **Given** a character template with gender "Female", **When** the author views the physical attributes panel, **Then** the Endowment fields are hidden and the Female Genitalia field is visible.
4. **Given** a preset dropdown for any field, **When** the author selects "(Custom…)", **Then** a free-text input appears and the authored value is saved verbatim.
5. **Given** no physical attributes have been edited yet, **When** the author edits any field for the first time, **Then** a new PhysicalAttributes record is auto-initialised and linked to the template.

---

### User Story 2 — Scenario Characters Carry Physical Attributes (Priority: P2)

As a story author, I want characters in a scenario to each have their own physical attribute data so that different characters can have distinct appearances within the same story.

**Why this priority**: Without per-character attributes in scenarios, appearance data is only available at the template level and cannot be overridden per scenario cast.

**Independent Test**: Open an existing scenario in ScenarioEditor, expand a character card, fill in body type and skin tone, save, reopen, and confirm values persisted.

**Acceptance Scenarios**:

1. **Given** a scenario is open in ScenarioEditor, **When** the author expands a character card and fills physical attributes, **Then** those values are saved with the scenario character and survive page navigation.
2. **Given** a character card in ScenarioEditor with gender "Female", **When** the author opens the attributes panel, **Then** the Endowment fields are not displayed.
3. **Given** a character card with a free-text custom value in Hair Style, **When** the scenario is saved and reopened, **Then** the custom value is preserved and the custom free-text input is shown instead of the select.

---

### User Story 3 — Persona Inherits Appearance from Template (Priority: P3)

As a player starting a roleplay session, I want the persona's physical attributes to be pre-populated from the chosen persona template so I don't have to re-enter appearance data manually.

**Why this priority**: Template-to-session inheritance is the expected quality-of-life flow; players set up persona templates precisely to avoid re-entry.

**Independent Test**: Select a persona template that has hair colour and body type defined, start session creation, and confirm the physical attributes section is pre-filled on the create form.

**Acceptance Scenarios**:

1. **Given** a persona template with PhysicalAttributes defined, **When** the player selects that template on RolePlayCreate, **Then** the physical attributes fields are pre-populated with the template values.
2. **Given** a session is created from a template with attributes, **When** the session is opened in RolePlayWorkspace, **Then** the persona panel shows the inherited attributes.
3. **Given** a player edits persona attributes in RolePlayWorkspace, **When** they save, **Then** the updated attributes are stored on the session and survive browser refresh.

---

### User Story 4 — Appearance Appears in AI Prompts (Priority: P4)

As a storyteller, I want physical appearance data to be injected into the AI continuation prompt so that generated narration and dialogue are visually accurate.

**Why this priority**: Prompt injection is what makes the feature valuable to the AI. Without it, storing appearance is useful only for author record-keeping, not for generation quality.

**Independent Test**: Set hair colour and body type on a character, trigger a continue or retry in a session featuring that character, capture the prompt sent to the model, and confirm an "Appearance —" block is present with the correct values.

**Acceptance Scenarios**:

1. **Given** a character with Hair colour: auburn, Eyes: green, Body type: athletic, **When** a continuation prompt is built, **Then** the prompt contains `Appearance — Hair colour: auburn; Eyes: green; Body type: athletic` with each field as a separate labelled entry.
2. **Given** a character with no physical attributes set, **When** a continuation prompt is built, **Then** no Appearance block is injected (no empty or placeholder block).
3. **Given** a character with only some fields filled, **When** a continuation prompt is built, **Then** only non-null/non-empty fields appear in the Appearance block.
4. **Given** an interaction retry is triggered, **When** the retry prompt is assembled, **Then** the Appearance block is included in the same way as for normal continuation.

---

### Edge Cases

- What happens when a character has a mix of preset and custom values? Custom values must be stored and restored verbatim; the select reverts to "(Custom…)" on reload.
- What happens when AttractivenessRating is outside 1–10? The input must enforce min/max; invalid values must not be saved.
- What happens when PhysicalAttributes is null for a character? The formatter returns empty string; no appearance block is injected into the prompt.
- What happens when Gender is null/unknown? Both Endowment and FemaleGenitalia fields are visible (defensive UX).
- What happens when a template with attributes is updated after a session was created from it? Session retains its own snapshot; template changes do not retroactively update sessions.
- What happens when a character is added to a scenario from a template that has `PhysicalAttributes`? The attributes are copied into the scenario character at add-time and become an independent snapshot; the scenario character is never affected by later template edits.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a `PhysicalAttributes` data type with nullable string fields: Age, Height, Weight, HairColour, HairStyle, EyeColour, BodyType, SkinTone, Ethnicity, BustMeasurement, WaistMeasurement, HipMeasurement, EndowmentLength, EndowmentGirth, FemaleGenitalia, DistinguishingMarks, Piercings, Tattoos, ClothingStyle, and a nullable integer AttractivenessRating.
- **FR-002**: System MUST provide a `PhysicalAttributesCatalog` with preset string arrays for each of the following fields: HairColour, HairStyle, EyeColour, BodyType, SkinTone, Ethnicity, FemaleGenitalia.
- **FR-003**: `TemplateDefinition` MUST carry an optional `PhysicalAttributes` property.
- **FR-004**: `Character` (domain entity) MUST carry an optional `PhysicalAttributes` property.
- **FR-005**: `RolePlaySession` MUST carry an optional `PersonaPhysicalAttributes` property, serialised into the existing session payload JSON column alongside other session-level persona overrides. No new database column or table is required.
- **FR-006**: `CharacterTemplatePayload` MUST include `PhysicalAttributes`; serialization and deserialization MUST round-trip all fields without data loss. The equivalent session payload type MUST include `PersonaPhysicalAttributes` with the same round-trip guarantee.
- **FR-007**: The `PhysicalAttributesEditor` UI component MUST render preset fields as `<select>` dropdowns backed by the catalog, with a "(Custom…)" sentinel option that reveals a free-text input when selected.
- **FR-008**: Free-text fields (Age, Height, Weight, BustMeasurement, WaistMeasurement, HipMeasurement, DistinguishingMarks, Piercings, Tattoos, ClothingStyle) MUST render as plain text inputs.
- **FR-009**: `EndowmentLength` and `EndowmentGirth` fields MUST be visible only when Gender is "Male" or null/unknown; they MUST be hidden when Gender is "Female".
- **FR-010**: `FemaleGenitalia` field MUST be visible only when Gender is "Female" or null/unknown; it MUST be hidden when Gender is "Male".
- **FR-011**: `AttractivenessRating` MUST render as a numeric input with minimum 1 and maximum 10.
- **FR-012**: The editor MUST auto-initialise `PhysicalAttributes` to a new instance on the first field edit when the property is currently null.
- **FR-013**: `PhysicalAttributesEditor` MUST accept a `Gender` parameter to drive conditional field visibility.
- **FR-014**: The `Templates` page MUST embed `PhysicalAttributesEditor` for Character and Persona template types, binding `Gender` from the template's gender field.
- **FR-015**: `ScenarioEditor` MUST embed `PhysicalAttributesEditor` inside each character card, after the Description textarea, binding `Gender` from the character's gender field. When a character is added to a scenario from a template that already carries `PhysicalAttributes`, those attributes MUST be copied into the scenario character as an independent snapshot at add-time; subsequent edits to the source template MUST NOT affect the scenario copy.
- **FR-016**: `RolePlayCreate` MUST copy `PhysicalAttributes` from the selected persona template onto the session payload at creation time.
- **FR-017**: `RolePlayWorkspace` MUST embed `PhysicalAttributesEditor` in the persona panel. Attribute changes MUST be persisted using the same save action already used for other persona-panel fields; no separate save button or auto-save on field change is introduced.
- **FR-018**: A `PhysicalAttributesFormatter` service MUST produce a compact labelled single-line string from a `PhysicalAttributes` instance, omitting null and empty fields, in the format: `Appearance — Field label: value; Field label: value; …`. Each field MUST appear as its own labelled entry; related fields (e.g. HairColour and HairStyle, or BustMeasurement / WaistMeasurement / HipMeasurement) MUST NOT be merged into compound entries. Example output: `Appearance — Age: 32; Hair colour: auburn; Hair style: shoulder-length; Eyes: green; Body type: athletic; Skin: light olive; Ethnicity: Hispanic; Bust: 36; Waist: 26; Hip: 36; Attractiveness: 8/10`.
- **FR-019**: `RolePlayContinuationService` MUST inject the formatted appearance block immediately after each character's description text and immediately after the persona's description text, inline within the same prompt section. No separate system note is used.
- **FR-020**: `InteractionRetryService` MUST inject the formatted appearance block immediately after each character's description text, inline within the same prompt section, consistent with the injection approach used in `RolePlayContinuationService`.
- **FR-021**: Persisted feature data MUST use the existing SQLite store; no new database schema or migration is required (payload serialised inside existing JSON columns).
- **FR-022**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-023**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-024**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities

- **PhysicalAttributes**: Value object carrying all optional appearance fields for a character or persona. Stored as JSON within existing payload columns; no standalone database table.
- **PhysicalAttributesCatalog**: Static lookup providing ordered preset string arrays per field. Used exclusively by the UI editor to populate dropdowns.
- **PhysicalAttributesFormatter**: Stateless service that converts a `PhysicalAttributes` instance into a prompt-ready string, omitting absent fields.
- **TemplateDefinition** *(extended)*: Domain entity representing a character or persona template; gains optional `PhysicalAttributes`.
- **Character** *(extended)*: Domain entity representing a cast member in a scenario; gains optional `PhysicalAttributes`.
- **RolePlaySession** *(extended)*: Session entity representing an active roleplay; gains optional `PersonaPhysicalAttributes`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A character template with all physical attribute fields filled can be saved and reloaded with every field intact — zero data loss across the round-trip.
- **SC-002**: The Templates, ScenarioEditor, RolePlayCreate, and RolePlayWorkspace pages each correctly show or hide gender-conditional fields (Endowment / FemaleGenitalia) based on the character or persona's gender value.
- **SC-003**: A continuation or retry prompt for a character with attributes includes a correctly formatted Appearance block; a character with no attributes produces no Appearance block.
- **SC-004**: The solution builds with zero errors (`dotnet build DreamGenClone.sln -v minimal` → 0 errors) after all phases are implemented.
- **SC-005**: Persona template selection on RolePlayCreate auto-populates physical attributes in under 1 second with no additional user action required.
- **SC-006**: Custom free-text values survive save/reload without being replaced by a preset value.

## Assumptions

- No dedicated database migration is needed because `PhysicalAttributes` is serialised as JSON inside the existing `CharacterTemplatePayload` JSON column, and `PersonaPhysicalAttributes` is serialised inside the existing session payload JSON column alongside other persona overrides.
- The `Gender` property referenced in the editor is already present on `TemplateDefinition` and `Character` entities; this spec does not add it.
- When a character is first added to a scenario, the system already has access to the source template's full payload (including `PhysicalAttributes`) so a copy-on-add approach requires no additional service calls.
- `PhysicalAttributesCatalog` presets are static and do not require user customisation or admin configuration.
- All fields are stored as plain strings; no enums are introduced so that preset catalog updates do not require entity migrations.
