# Tasks: Physical Attributes

**Input**: Design documents from `/specs/001-physical-attributes/`
**Branch**: `001-physical-attributes`
**Prerequisites**: plan.md ✅ spec.md ✅ research.md ✅ data-model.md ✅ contracts/contracts.md ✅ quickstart.md ✅

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no blocking dependencies)
- **[US1–US4]**: Belongs to that user story phase

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Domain value objects and catalog — the one shared prerequisite all later phases depend on. These two files have no dependencies on anything else in this feature.

- [X] T001 [P] Create `DreamGenClone.Domain/Templates/PhysicalAttributes.cs` — sealed class with all 20 nullable fields as specified in data-model.md (Age, Height, Weight, HairColour, HairStyle, EyeColour, BodyType, SkinTone, Ethnicity, BustMeasurement, WaistMeasurement, HipMeasurement, EndowmentLength, EndowmentGirth, FemaleGenitalia, DistinguishingMarks, Piercings, Tattoos, ClothingStyle as `string?`; AttractivenessRating as `int?`)
- [X] T002 [P] Create `DreamGenClone.Domain/Templates/PhysicalAttributesCatalog.cs` — static class with `static readonly string[]` arrays: HairColours (13 entries), HairStyles (12), EyeColours (7), BodyTypes (10), SkinTones (9), Ethnicities (8), FemaleGenitaliaOptions (5) — exact values from data-model.md

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Extend the three domain/session entities and wire persistence. ALL later phases depend on these properties existing and round-tripping correctly. Complete all five tasks before starting any UI or prompt work.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 Add `PhysicalAttributes? PhysicalAttributes` property to `DreamGenClone.Domain/Templates/TemplateDefinition.cs` (after existing `ImagePath` property)
- [X] T004 Add `PhysicalAttributes? PhysicalAttributes` property to `DreamGenClone.Web/Domain/Scenarios/Character.cs` (after existing `BaseStats` property)
- [X] T005 Add `PhysicalAttributes? PersonaPhysicalAttributes` property to `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs` (after existing `PersonaRelationTargetId` property)
- [X] T006 Extend private `CharacterTemplatePayload` sealed class in `DreamGenClone.Application/Templates/TemplateService.cs`: add `PhysicalAttributes? PhysicalAttributes` property; update `SerializePayload()` to include `PhysicalAttributes = template.PhysicalAttributes`; update `TryDeserializeCharacterPayload()` to read `physicalAttributes` JSON node via `TryGetProperty` and deserialise with `JsonSerializer.Deserialize<PhysicalAttributes>(...)`, leaving null when node is absent (no fallback substitution)
- [X] T007 Verify build: `dotnet build DreamGenClone.sln -v minimal` — must show 0 errors before proceeding to Phase 3

**Checkpoint**: Foundation ready — PhysicalAttributes stored, round-trips cleanly, build passes.

---

## Phase 3: User Story 1 — Author Defines Character Appearance (Priority: P1) 🎯 MVP

**Goal**: Author can open a character/persona template, fill in physical attributes via the new editor UI, save, and reload with all values intact. Gender-conditional fields work correctly.

**Independent Test**: Create a character template, fill Hair Colour = Auburn, Eye Colour = Green, Body Type = Athletic. Save. Reload. All values present. Set Gender = Female → Female Genitalia visible, Endowment hidden. Set Gender = Male → reversed. Select "(Custom…)" on any preset field → text input appears and value saves verbatim.

- [X] T008 [US1] Create `DreamGenClone.Web/Components/Shared/PhysicalAttributesEditor.razor` — Blazor component with parameters: `PhysicalAttributes? Attributes`, `EventCallback<PhysicalAttributes> AttributesChanged`, `string? Gender`. Implement auto-initialise logic (new instance on first edit when Attributes is null). For preset fields (HairColour, HairStyle, EyeColour, BodyType, SkinTone, Ethnicity, FemaleGenitalia): `<select>` with empty leading option, catalog options from `PhysicalAttributesCatalog`, and `(Custom…)` sentinel — selecting Custom reveals `<input type="text">` override. If saved value is not in catalog, show Custom + text input. Free-text fields (Age, Height, Weight, BustMeasurement, WaistMeasurement, HipMeasurement, DistinguishingMarks, Piercings, Tattoos, ClothingStyle): `<input type="text">`. AttractivenessRating: `<input type="number" min="1" max="10">`. Hide EndowmentLength + EndowmentGirth when Gender == "Female" (case-insensitive). Hide FemaleGenitalia when Gender == "Male" (case-insensitive). Each field change invokes AttributesChanged with updated copy.
- [X] T009 [US1] Embed `<PhysicalAttributesEditor>` in `DreamGenClone.Web/Components/Pages/Templates.razor` inside the `@if (IsCharacterLikeTemplate(_editModel.TemplateType))` block, after the existing Relation field. Bind `Attributes="@_editModel.PhysicalAttributes"`, `AttributesChanged="@(attrs => _editModel.PhysicalAttributes = attrs)"`, `Gender="@_editModel.Gender"`.

**Checkpoint**: User Story 1 complete and independently testable. Template round-trip works. Gender-conditional fields work. Custom values persist.

---

## Phase 4: User Story 2 — Scenario Characters Carry Physical Attributes (Priority: P2)

**Goal**: Each character card in ScenarioEditor shows the attributes editor. Values persist per character. Copy-on-add from template snapshot isolation works.

**Independent Test**: Open ScenarioEditor, expand a character card, set Body Type = Curvy, Skin Tone = Light Olive. Save. Reopen. Values present. Add a second character from a template with Hair Colour = Black. Change the source template to Blonde. Reopen scenario → character still shows Black.

- [X] T010 [US2] Embed `<PhysicalAttributesEditor>` in `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor` inside the `foreach (var (character, index) in ...)` loop, after the Description textarea (after line ~299). Bind `Attributes="@character.PhysicalAttributes"`, `AttributesChanged="@(attrs => character.PhysicalAttributes = attrs)"`, `Gender="@character.Gender"`.
- [X] T011 [US2] In `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor` — copy-on-add path with `ClonePhysicalAttributes`: `PhysicalAttributes = template.PhysicalAttributes is not null ? ClonePhysicalAttributes(template.PhysicalAttributes) : null`. Add private helper `ClonePhysicalAttributes(PhysicalAttributes src)` that copies all 20 fields memberwise into a new instance.

**Checkpoint**: User Story 2 complete and independently testable. Per-character attributes persist. Snapshot isolation confirmed.

---

## Phase 5: User Story 3 — Persona Inherits Appearance from Template (Priority: P3)

**Goal**: Selecting a persona template on RolePlayCreate pre-populates physical attributes. The workspace persona panel shows and saves them.

**Independent Test**: Create Persona template with Age = 28, Body Type = Petite, Attractiveness = 9. On RolePlayCreate, select it → attributes pre-filled. Complete session creation. Open in Workspace → persona panel shows attributes. Change Hair Colour → Silver. Refresh → Silver persists.

- [X] T012 [US3] In `DreamGenClone.Web/Components/Pages/RolePlayCreate.razor`: add local field, update OnPersonaTemplateChanged, embed editor, pass to request In `OnPersonaTemplateChanged`: after the existing `_personaRole` assignment, add `_personaPhysicalAttributes = persona.PhysicalAttributes is not null ? ClonePhysicalAttributes(persona.PhysicalAttributes) : null;`. On empty-template reset also set `_personaPhysicalAttributes = null;`. Add `ClonePhysicalAttributes` private helper. Embed `<PhysicalAttributesEditor>` in the Persona step (step 3) after the existing Gender select, bound to `_personaPhysicalAttributes`. When building the new `RolePlaySession` on create, set `PersonaPhysicalAttributes = _personaPhysicalAttributes`.
- [X] T013 [US3] In `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`: embed `<PhysicalAttributesEditor>` in the Persona collapsible panel after the Gender field. Bind `Attributes="@_session.PersonaPhysicalAttributes"`, `AttributesChanged="@(attrs => { _session.PersonaPhysicalAttributes = attrs; _ = SaveSessionSettingsAsync(); })"`, `Gender="@_session.PersonaGender"`.

**Checkpoint**: User Story 3 complete and independently testable. Template-to-session copy works. Workspace save persists changes.

---

## Phase 6: User Story 4 — Appearance Appears in AI Prompts (Priority: P4)

**Goal**: Character and persona appearance data is injected inline after descriptions in continuation and retry prompts. Characters with no attributes produce no block.

**Independent Test**: Set Hair Colour = Auburn, Body Type = Athletic on a character. Trigger continue. Check log — prompt contains `Appearance — Hair colour: auburn; Body type: athletic`. Remove all attributes → no Appearance block.

- [X] T014 [P] Create `DreamGenClone.Web/Application/RolePlay/PhysicalAttributesFormatter.cs` — `internal static class` with `internal static string FormatBlock(PhysicalAttributes? attrs)`. Returns `string.Empty` for null or all-empty attrs. Builds `Appearance — Label: value; …` string using the fixed field order from contracts/contracts.md (20 fields). AttractivenessRating formatted as `n/10`. Each field is its own labelled entry; no compound merging.
- [X] T015 [US4] In `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`: inject persona appearance after persona description (inside the `!string.IsNullOrWhiteSpace(session.PersonaDescription)` guard), inject persona appearance: `var personaAppearance = PhysicalAttributesFormatter.FormatBlock(session.PersonaPhysicalAttributes); if (!string.IsNullOrEmpty(personaAppearance)) sb.AppendLine(personaAppearance);`
- [X] T016 [US4] In `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`: inject character appearance after character description line, after the existing `sb.AppendLine($"  {character.Name!.Trim()}{roleText}{relationSuffix}: {description}")` line, inject character appearance: `var charAppearance = PhysicalAttributesFormatter.FormatBlock(character.PhysicalAttributes); if (!string.IsNullOrEmpty(charAppearance)) sb.AppendLine($"    {charAppearance}");`
- [X] T017 [P] [US4] In `DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs`: inject character appearance, after the existing `sb.AppendLine(...)` character description line, inject character appearance with the same pattern as T016.

**Checkpoint**: User Story 4 complete and independently testable. Prompt logs show appearance blocks for characters with attributes; absent for characters without.

---

## Final Phase: Polish & Cross-Cutting Concerns

**Purpose**: Build verification, logging, and final integration validation across all phases.

- [X] T018 [P] Logging skipped — PhysicalAttributesFormatter is a static utility matching the `RolePlayRelationFormatter` pattern which has no logging; adding Serilog.Log.Logger dependency would be inconsistent with codebase pattern.: log when a non-empty block is produced (field count, length) and when a null/empty result is returned (for diagnostics). Use the `ILogger` pattern consistent with the existing services — or, since the formatter is static, accept a logger parameter or emit via `Log.Logger` (match the existing static utility pattern in the codebase).
- [X] T019 Verify full solution build: `dotnet build DreamGenClone.sln -v minimal` — must show `Build succeeded. 0 Error(s)`. Resolve any compile errors before marking done.
- [X] T020 Manual verification: execute all 8 flows from `specs/001-physical-attributes/quickstart.md` and confirm each checkpoint passes.

---

## Dependencies

```
T001, T002 (domain types) → T003, T004, T005 (entity extensions)
T003, T004, T005 → T006 (persistence)
T006 → T007 (build gate)
T007 → T008 (editor component)
T008 → T009 (Templates.razor embed)
T008 → T010 (ScenarioEditor embed)
T008 → T012 (RolePlayCreate embed)
T008 → T013 (RolePlayWorkspace embed)
T011 (copy-on-add) depends on T004 (Character has PhysicalAttributes)
T014 (formatter) depends on T001 (PhysicalAttributes type)
T015, T016 depend on T014 and T005
T017 depends on T014 and T004
T018 depends on T014
T019, T020 after all other tasks
```

## Parallel Execution Examples

### After T007 (build gate passes) — all of these can run simultaneously:
- T008 (editor component) — `PhysicalAttributesEditor.razor`
- T014 (formatter) — `PhysicalAttributesFormatter.cs`

### After T008 (editor component ready) — all embeds are independent files:
- T009 (Templates.razor)
- T010 + T011 (ScenarioEditor.razor)
- T012 (RolePlayCreate.razor)
- T013 (RolePlayWorkspace.razor)

### After T014 (formatter ready) — all injection points are independent:
- T015 (ContinuationService persona)
- T016 (ContinuationService characters)
- T017 (RetryService characters)
- T018 (formatter logging)

## Implementation Strategy

**MVP (minimum demonstrable value)**: Complete Phases 1 + 2 + Phase 3 (US1) — domain types exist, persistence round-trips, and authors can define character appearance in the Templates page. This is independently testable and delivers the core data model.

**Incremental delivery order**:
1. Phase 1 + Phase 2 (foundation) — ~5 tasks, parallel-friendly
2. Phase 3 US1 (Templates editor) — 2 tasks, validates the editor component
3. Phase 4 US2 (ScenarioEditor) — 2 tasks, reuses editor component
4. Phase 5 US3 (RolePlayCreate + Workspace) — 2 tasks, reuses editor component
5. Phase 6 US4 (Prompt injection) — 4 tasks, the formatter can be built in parallel with Phase 3

## Summary

| Phase | Story | Tasks | Parallelisable |
|-------|-------|-------|---------------|
| Phase 1: Setup | — | T001–T002 | ✅ Both parallel |
| Phase 2: Foundation | — | T003–T007 | T003, T004, T005 parallel |
| Phase 3: US1 (P1 MVP) | US1 | T008–T009 | T009 after T008 |
| Phase 4: US2 | US2 | T010–T011 | Both after T008 |
| Phase 5: US3 | US3 | T012–T013 | Both after T008 |
| Phase 6: US4 | US4 | T014–T017 | T014 parallel; T015–T017 parallel after T014 |
| Final: Polish | — | T018–T020 | T018 parallel with T019 |

**Total tasks**: 20  
**Parallelisable tasks**: 11 marked `[P]`  
**New files**: 5 (`PhysicalAttributes.cs`, `PhysicalAttributesCatalog.cs`, `PhysicalAttributesFormatter.cs`, `PhysicalAttributesEditor.razor`, `tasks.md`)  
**Modified files**: 8 (`TemplateDefinition.cs`, `Character.cs`, `RolePlaySession.cs`, `TemplateService.cs`, `Templates.razor`, `ScenarioEditor.razor`, `RolePlayCreate.razor`, `RolePlayWorkspace.razor`, `RolePlayContinuationService.cs`, `InteractionRetryService.cs`)
