# Tasks: DreamGenClone Specification

**Input**: Design artifacts from `specs/001-project-architecture-layered-structure/`
**Prerequisites**: `spec.md` (present), `plan.md` (present)

**Tests**: No explicit TDD/test-first requirement was requested in the specification, so test tasks are not mandated in this list.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish solution-level project setup, dependencies, and configuration foundations.

- [x] T001 Validate and normalize solution structure in `DreamGenClone.sln`
- [x] T002 Create separate layer projects (`DreamGenClone.Application`, `DreamGenClone.Domain`, `DreamGenClone.Infrastructure`) and retain layer folders in `DreamGenClone.Web/`
- [x] T003 [P] Add required packages for SQLite, Serilog, and configuration in `DreamGenClone.Web/DreamGenClone.csproj`
- [x] T004 [P] Add LM Studio, persistence, and logging configuration sections in `DreamGenClone.Web/appsettings.json`
- [x] T005 [P] Add development overrides for log levels and local endpoints in `DreamGenClone.Web/appsettings.Development.json`

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core architecture, persistence, import validation, and logging policy required before feature workflows.

- [x] T006 Configure DI composition root and service registrations in `DreamGenClone.Web/Program.cs`
- [x] T007 Implement LM Studio client abstraction and HTTP client in `DreamGenClone.Infrastructure/Models/LmStudioClient.cs`
- [x] T008 Implement SQLite session/template persistence bootstrap in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
- [x] T009 Implement template image local disk storage service and path metadata handling in `DreamGenClone.Infrastructure/Storage/TemplateImageStorageService.cs`
- [x] T010 Implement strict JSON import validator (schema/version checks) in `DreamGenClone.Application/Validation/SessionImportValidator.cs`
- [x] T011 Configure Serilog pipeline, enrichers, sinks, and level overrides in `DreamGenClone.Infrastructure/Logging/LoggingSetup.cs`
- [x] T012 Wire Serilog startup and configurable log levels in `DreamGenClone.Web/Program.cs`
- [x] T013 Create shared domain contracts for session/story/role-play operations in `DreamGenClone.Domain/Contracts/`
- [x] T014 Implement autosave coordinator with meaningful-change debounce behavior in `DreamGenClone.Application/Sessions/AutoSaveCoordinator.cs`

**Checkpoint**: Foundation complete - feature workflows can be implemented incrementally.

## Phase 3: User Story 1 - Template Library (Priority: P1)

**Goal**: Users can manage reusable persona, character, location, and object templates with persistence and preview support.

**Independent Test**: Create, edit, delete, and preview each template type; restart app; verify template data and image references persist.

- [x] T015 [P] [US1] Implement template domain models in `DreamGenClone.Domain/Templates/`
- [x] T016 [P] [US1] Implement template repository/service methods in `DreamGenClone.Application/Templates/TemplateService.cs`
- [x] T017 [US1] Implement template CRUD UI in `DreamGenClone.Web/Components/Pages/Templates.razor`
- [x] T018 [US1] Implement template preview components in `DreamGenClone.Web/Components/Templates/TemplatePreview.razor`
- [x] T019 [US1] Integrate template image upload/select flow with local storage service in `DreamGenClone.Web/Components/Templates/TemplateImageEditor.razor`
- [x] T020 [US1] Add Information-level operational logs for template workflows in `DreamGenClone.Application/Templates/TemplateService.cs`

## Phase 4: User Story 2 - Scenario Editor (Priority: P2)

**Goal**: Users can author and edit scenarios with plot/setting/style, entities, openings/examples, and live token count.

**Independent Test**: Create and save scenario, reload and edit it, verify template-backed entities and token count updates.

- [x] T021 [P] [US2] Implement scenario domain models in `DreamGenClone.Web/Domain/Scenarios/`
- [x] T022 [P] [US2] Implement scenario orchestration service in `DreamGenClone.Web/Application/Scenarios/ScenarioService.cs`
- [x] T023 [US2] Build scenario editor page and sections in `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor`
- [x] T024 [US2] Implement token counting service and UI binding in `DreamGenClone.Web/Application/Scenarios/ScenarioTokenCounter.cs`
- [x] T025 [US2] Add template selection integration for scenario entities in `DreamGenClone.Web/Components/Scenarios/ScenarioEntityPicker.razor`
- [x] T026 [US2] Add Information-level logs for scenario save/load/edit operations in `DreamGenClone.Web/Application/Scenarios/ScenarioService.cs`

## Phase 5: User Story 3 - Story Mode Engine (Priority: P3)

**Goal**: Users can create/edit story blocks, issue instructions, continue generation, and use rewind/undo safely.

**Independent Test**: Build a story session with multiple blocks, continue via AI, execute rewind/undo, and validate expected restored state.

- [x] T027 [P] [US3] Implement story block/session domain structures in `DreamGenClone.Web/Domain/Story/`
- [x] T028 [P] [US3] Implement story orchestration service in `DreamGenClone.Web/Application/Story/StoryEngineService.cs`
- [x] T029 [US3] Build Story Mode page and block editor UI in `DreamGenClone.Web/Components/Pages/StoryMode.razor`
- [x] T030 [US3] Implement continue, rewind, and undo commands in `DreamGenClone.Web/Application/Story/StoryCommandService.cs`
- [x] T031 [US3] Integrate writing assistant context feed with scenario and recent story in `DreamGenClone.Web/Application/Assistants/WritingAssistantService.cs`
- [x] T032 [US3] Add Information-level logs for major story generation and command paths in `DreamGenClone.Web/Application/Story/`

## Phase 6: User Story 4 - Role-Play Mode Engine (Priority: P4)

**Goal**: Users can run interaction-based role-play with behavior modes, continuation controls, and branch/fork paths.

**Independent Test**: Create interaction sequences, switch behavior modes, continue as selected actor, fork branch, and verify independent paths.

- [x] T033 [P] [US4] Implement role-play interaction and behavior domain models in `DreamGenClone.Web/Domain/RolePlay/`
- [x] T034 [P] [US4] Implement role-play orchestration service in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [x] T035 [US4] Build role-play interaction UI and composer in `DreamGenClone.Web/Components/Pages/RolePlayMode.razor`
- [x] T036 [US4] Implement continue-as behavior (You/NPC/Custom) in `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`
- [x] T037 [US4] Implement behavior modes (Take-Turns/Spectate/NPC-Only) in `DreamGenClone.Web/Application/RolePlay/BehaviorModeService.cs`
- [x] T038 [US4] Implement branch/fork session pathing in `DreamGenClone.Web/Application/RolePlay/RolePlayBranchService.cs`
- [x] T039 [US4] Add Information-level logs for major role-play transitions and branch actions in `DreamGenClone.Web/Application/RolePlay/`

## Phase 7: User Story 5 - Session Manager (Priority: P5)

**Goal**: Users can manage saved sessions with autosave, clone/fork, and strict import/export workflows.

**Independent Test**: Persist sessions across restart, clone/fork independently, export markdown/json, and verify strict import rejects invalid payloads.

- [x] T040 [P] [US5] Implement session metadata/list service in `DreamGenClone.Web/Application/Sessions/SessionService.cs`
- [x] T041 [P] [US5] Implement clone/fork operations in `DreamGenClone.Web/Application/Sessions/SessionCloneForkService.cs`
- [x] T042 [P] [US5] Implement markdown/json export pipeline in `DreamGenClone.Web/Application/Export/ExportService.cs`
- [x] T043 [US5] Implement strict JSON import with validator integration in `DreamGenClone.Web/Application/Import/SessionImportService.cs`
- [x] T044 [US5] Build session manager UI in `DreamGenClone.Web/Components/Pages/Sessions.razor`
- [x] T045 [US5] Integrate autosave debounce policy into session lifecycle in `DreamGenClone.Web/Application/Sessions/AutoSaveCoordinator.cs`
- [x] T046 [US5] Add Information-level logs for save/import/export/clone/fork operations in `DreamGenClone.Web/Application/Sessions/`

## Phase 8: User Story 6 - Model Settings (Priority: P6)

**Goal**: Users can configure per-session model settings and retry generation using selected parameters.

**Independent Test**: Update model settings for session, perform retry with model, and verify payload values sent to LM Studio.

- [X] T047 [P] [US6] Implement model settings state and persistence model in `DreamGenClone.Web/Domain/Models/ModelSettings.cs`
- [X] T048 [P] [US6] Implement model settings service in `DreamGenClone.Web/Application/Models/ModelSettingsService.cs`
- [X] T049 [US6] Build model settings UI panel in `DreamGenClone.Web/Components/Shared/ModelSettingsPanel.razor`
- [X] T050 [US6] Implement retry-with-model flow using current settings in `DreamGenClone.Web/Application/Models/ModelRetryService.cs`
- [X] T051 [US6] Add Information-level logs for model settings changes and retry invocations in `DreamGenClone.Web/Application/Models/`

## Phase 9: User Story 7 - Assistants (Priority: P7)

**Goal**: Writing and role-play assistants provide context-aware outputs with deterministic truncation and clear-chat behavior.

**Independent Test**: Trigger assistant responses in both modes, exceed context limits, verify deterministic recency truncation with pinned retention, then clear chat.

- [X] T052 [P] [US7] Implement shared assistant context manager with deterministic truncation rules in `DreamGenClone.Web/Application/Assistants/AssistantContextManager.cs`
- [X] T053 [P] [US7] Implement writing assistant service in `DreamGenClone.Web/Application/Assistants/WritingAssistantService.cs`
- [X] T054 [P] [US7] Implement role-play assistant service in `DreamGenClone.Web/Application/Assistants/RolePlayAssistantService.cs`
- [X] T055 [US7] Build assistant UI shell with clear-chat actions in `DreamGenClone.Web/Components/Shared/AssistantPanel.razor`
- [X] T056 [US7] Enforce pinned-critical context retention policy in `DreamGenClone.Web/Application/Assistants/AssistantContextManager.cs`
- [X] T057 [US7] Add Information-level logs for assistant requests, truncation, and clear-chat operations in `DreamGenClone.Web/Application/Assistants/`

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Final consistency, diagnostics, and readiness checks across all features.

- [ ] T058 [P] Document architecture, runtime setup, and operational constraints in `specs/001-project-architecture-layered-structure/quickstart.md`
- [ ] T059 Verify structured logging coverage across major code paths and tune level overrides in `DreamGenClone.Web/appsettings.Development.json`
- [ ] T060 Validate strict import error messaging and user-facing clarity in `DreamGenClone.Web/Components/Pages/Sessions.razor`
- [ ] T061 Perform end-to-end manual validation for Story Mode and Role-Play Mode workflows in `specs/001-project-architecture-layered-structure/quickstart.md`

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1): starts immediately
- Foundational (Phase 2): depends on Setup
- User stories (Phases 3-9): depend on Foundational
- Polish (Phase 10): depends on completion of selected user stories

### User Story Dependencies

- US1 (Template Library): starts after Foundational
- US2 (Scenario Editor): depends on US1 template integration availability
- US3 (Story Mode): depends on US2 scenario availability
- US4 (Role-Play Mode): depends on US2 scenario availability
- US5 (Session Manager): depends on US3/US4 session schemas
- US6 (Model Settings): depends on Foundational LM Studio client
- US7 (Assistants): depends on US2 plus either US3 or US4 context sources

## Parallel Opportunities

- Setup tasks marked [P] can run in parallel
- Foundational tasks T007-T011 can run in parallel after T006
- Within each user story, model/service/domain tasks marked [P] can run in parallel before UI wiring
- US3 and US4 can be developed in parallel after US2
- US6 can progress in parallel with US3/US4 after foundation readiness

## Parallel Example: User Story 1

- Task: `T015 [P] [US1] Implement template domain models in DreamGenClone.Web/Domain/Templates/`
- Task: `T016 [P] [US1] Implement template repository/service methods in DreamGenClone.Web/Application/Templates/TemplateService.cs`

## Parallel Example: User Story 4

- Task: `T033 [P] [US4] Implement role-play interaction and behavior domain models in DreamGenClone.Web/Domain/RolePlay/`
- Task: `T034 [P] [US4] Implement role-play orchestration service in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2
2. Deliver US1 (Template Library) and US2 (Scenario Editor)
3. Deliver US3 (Story Mode) as first end-user playable workflow
4. Validate before expanding to remaining stories

### Incremental Delivery

1. Add US4 (Role-Play Mode)
2. Add US5 (Session Manager strict import/export)
3. Add US6 (Model Settings)
4. Add US7 (Assistants)
5. Complete Polish tasks

### Notes

- All tasks use explicit file paths to reduce ambiguity.
- [P] marks tasks that can run concurrently when dependency conditions are met.
- Logging, persistence, and import validation constraints are enforced throughout implementation.
