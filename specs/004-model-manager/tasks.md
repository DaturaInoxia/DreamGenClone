# Tasks: Model Manager

**Input**: Design documents from `/specs/004-model-manager/`
**Prerequisites**: plan.md, spec.md (7 user stories), research.md, data-model.md, contracts/model-manager-contract.md, quickstart.md

**Tests**: Not explicitly requested in the feature specification. Test tasks are omitted.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Domain entities, enums, value objects, and application interface definitions shared across all user stories

- [x] T001 [P] Create ProviderType enum in DreamGenClone.Domain/ModelManager/ProviderType.cs with values LmStudio=0, TogetherAI=1, OpenRouter=2
- [x] T002 [P] Create AppFunction enum in DreamGenClone.Domain/ModelManager/AppFunction.cs with 9 values: RolePlayGeneration, StoryModeGeneration, StorySummarize, StoryAnalyze, StoryRank, ScenarioPreview, ScenarioAdapt, WritingAssistant, RolePlayAssistant
- [x] T003 [P] Create Provider entity in DreamGenClone.Domain/ModelManager/Provider.cs with fields per data-model.md (Id, Name, ProviderType, BaseUrl, ChatCompletionsPath, TimeoutSeconds, ApiKeyEncrypted, IsEnabled, CreatedUtc, UpdatedUtc)
- [x] T004 [P] Create RegisteredModel entity in DreamGenClone.Domain/ModelManager/RegisteredModel.cs with fields per data-model.md (Id, ProviderId, ModelIdentifier, DisplayName, IsEnabled, CreatedUtc)
- [x] T005 [P] Create FunctionModelDefault entity in DreamGenClone.Domain/ModelManager/FunctionModelDefault.cs with fields per data-model.md (Id, FunctionName, ModelId, Temperature, TopP, MaxTokens, UpdatedUtc)
- [x] T006 [P] Create ResolvedModel value object in DreamGenClone.Domain/ModelManager/ResolvedModel.cs with fields per data-model.md (ProviderBaseUrl, ChatCompletionsPath, ProviderTimeoutSeconds, ApiKeyEncrypted, ModelIdentifier, Temperature, TopP, MaxTokens, ProviderName, IsSessionOverride)
- [x] T007 [P] Create ModelResolutionException in DreamGenClone.Domain/ModelManager/ModelResolutionException.cs extending Exception with actionable message property
- [x] T008 [P] Create IProviderRepository interface in DreamGenClone.Application/ModelManager/IProviderRepository.cs per contracts (SaveAsync, GetByIdAsync, GetAllAsync, DeleteAsync, ExistsByNameAsync)
- [x] T009 [P] Create IRegisteredModelRepository interface in DreamGenClone.Application/ModelManager/IRegisteredModelRepository.cs per contracts (SaveAsync, GetByIdAsync, GetByProviderIdAsync, GetAllEnabledAsync, DeleteAsync, ExistsByProviderAndIdentifierAsync)
- [x] T010 [P] Create IFunctionDefaultRepository interface in DreamGenClone.Application/ModelManager/IFunctionDefaultRepository.cs per contracts (SaveAsync, GetByFunctionAsync, GetAllAsync, GetByModelIdAsync, DeleteByFunctionAsync)
- [x] T011 [P] Create IApiKeyEncryptionService interface in DreamGenClone.Application/ModelManager/IApiKeyEncryptionService.cs per contracts (Encrypt, Decrypt)
- [x] T012 [P] Create IModelResolutionService interface in DreamGenClone.Application/ModelManager/IModelResolutionService.cs per contracts (ResolveAsync with AppFunction, optional session override params)
- [x] T013 [P] Create ICompletionClient interface in DreamGenClone.Application/Abstractions/ICompletionClient.cs per contracts (GenerateAsync with ResolvedModel, CheckHealthAsync with explicit params)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: SQLite table creation, seed migration, repository implementations, encryption service, and unified completion client — MUST be complete before any user story work

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T014 Add Providers, RegisteredModels, and FunctionModelDefaults table creation DDL to DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs InitializeAsync method using CREATE TABLE IF NOT EXISTS per data-model.md
- [x] T015 Implement seed migration logic in DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs InitializeAsync: check if Providers table is empty, if so seed 1 LM Studio provider, 2 models, 9 function defaults from LmStudioOptions/StoryAnalysisOptions/ScenarioAdaptationOptions values per data-model.md seed tables
- [x] T016 [P] Implement ApiKeyEncryptionService in DreamGenClone.Infrastructure/ModelManager/ApiKeyEncryptionService.cs using System.Security.Cryptography.ProtectedData with DataProtectionScope.CurrentUser, Base64 encode/decode
- [x] T017 [P] Implement ProviderRepository in DreamGenClone.Infrastructure/ModelManager/ProviderRepository.cs with SQLite CRUD using ISqlitePersistence connection pattern (INSERT ON CONFLICT DO UPDATE for Save, parameterized queries)
- [x] T018 [P] Implement RegisteredModelRepository in DreamGenClone.Infrastructure/ModelManager/RegisteredModelRepository.cs with SQLite CRUD, UNIQUE(ProviderId, ModelIdentifier) enforcement
- [x] T019 [P] Implement FunctionDefaultRepository in DreamGenClone.Infrastructure/ModelManager/FunctionDefaultRepository.cs with SQLite CRUD, UNIQUE(FunctionName) enforcement
- [x] T020 Implement CompletionClient in DreamGenClone.Infrastructure/Models/CompletionClient.cs using IHttpClientFactory("CompletionClient"), dynamic base URL + Authorization Bearer header from ResolvedModel, HTTP error mapping per FR-018a (401→invalid key, 429→rate limit, 5xx→server error), structured Serilog logging per FR-038a
- [x] T021 Implement ModelResolutionService in DreamGenClone.Web/Application/ModelManager/ModelResolutionService.cs with fallback chain: session override params → IFunctionDefaultRepository.GetByFunctionAsync → ModelResolutionException, joining RegisteredModel + Provider to build ResolvedModel, logging per FR-038

**Checkpoint**: Foundation ready — all repositories, encryption, completion client, and model resolution operational. User story implementation can begin.

---

## Phase 3: User Story 1 — Register and Configure AI Providers (Priority: P1) 🎯 MVP

**Goal**: Users can add/edit/delete AI providers (LM Studio, Together AI, OpenRouter) with connection details, API keys, and per-provider timeout through the Model Manager page.

**Independent Test**: Navigate to Model Manager page, add a provider with base URL, click "Test Connection", confirm provider persists after page refresh.

### Implementation for User Story 1

- [x] T022 [US1] Implement ProviderTestService in DreamGenClone.Web/Application/ModelManager/ProviderTestService.cs that uses ICompletionClient.CheckHealthAsync with decrypted API key via IApiKeyEncryptionService, handles CryptographicException for bad keys
- [x] T023 [US1] Implement ModelManagerFacade provider methods in DreamGenClone.Web/Application/ModelManager/ModelManagerFacade.cs: GetAllProvidersAsync, SaveProviderAsync (with encryption for API keys), DeleteProviderAsync (with dependent model/function warning data), TestProviderConnectionAsync, EnableDisableProviderAsync
- [x] T024 [US1] Create ModelManager.razor page in DreamGenClone.Web/Components/Pages/ModelManager.razor with @page "/model-manager" route, Bootstrap 5 layout with Providers section: provider list/cards, add/edit inline form (name, type dropdown, base URL, timeout, API key masked input), save/delete/enable-disable buttons, "Test Connection" button with status indicator
- [x] T025 [US1] Add Model Manager nav entry in DreamGenClone.Web/Components/Layout/NavMenu.razor with Bootstrap Icons icon linking to /model-manager
- [x] T026 [US1] Register ProviderTestService and ModelManagerFacade in DreamGenClone.Web/Program.cs as Scoped services; register IProviderRepository, IRegisteredModelRepository, IFunctionDefaultRepository, IApiKeyEncryptionService as Singleton; register ICompletionClient/CompletionClient as Singleton with AddHttpClient("CompletionClient"); replace ILmStudioClient typed client registration
- [x] T027 [US1] Add inline validation in ModelManager.razor Providers section: required name, valid URL format, timeout 1-600, API key required warning for TogetherAI/OpenRouter types, confirmation dialog before deleting a provider with dependent models per FR-006

**Checkpoint**: Provider CRUD + Test Connection fully functional. Model Manager page accessible from nav.

---

## Phase 4: User Story 6 — Secure API Key Storage (Priority: P1)

**Goal**: API keys for cloud providers are encrypted via DPAPI before SQLite storage and never exposed as plaintext in UI or git-tracked files.

**Independent Test**: Add a Together AI provider with API key, inspect SQLite database to verify encrypted storage, confirm UI shows masked key after save.

*Note: Most encryption infrastructure was built in Phase 2 (T016) and Phase 3 (T023). This phase validates and hardens the security path.*

### Implementation for User Story 6

- [x] T028 [US6] Add API key masking logic in ModelManager.razor: after save, display "sk-****...****" instead of plaintext, never send decrypted key back to browser; on edit, show "unchanged" placeholder unless user enters a new key
- [x] T029 [US6] Add Serilog Error-level logging in CompletionClient and ModelResolutionService for CryptographicException during API key decryption per FR-039, with actionable message directing user to re-enter key in Model Manager
- [x] T030 [US6] Verify .gitignore excludes data/ directory (SQLite database with encrypted keys) and that no appsettings file contains API key values per FR-032

**Checkpoint**: API keys encrypted at rest, masked in UI, decrypted only at HTTP call time. No keys in git.

---

## Phase 5: User Story 7 — First-Run Migration from appsettings.json (Priority: P1)

**Goal**: On first start, automatically migrate existing appsettings.json model configuration to the Model Manager database tables with zero manual setup.

**Independent Test**: Start application with no Model Manager tables, verify migration creates LM Studio provider, 2 models, 9 function defaults matching current appsettings behavior.

*Note: Core migration logic was implemented in Phase 2 (T014, T015). This phase validates correctness and adds the ISqlitePersistence dependency injection for options.*

### Implementation for User Story 7

- [x] T031 [US7] Update SqlitePersistence constructor in DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs to inject IOptions\<LmStudioOptions\>, IOptions\<StoryAnalysisOptions\>, IOptions\<ScenarioAdaptationOptions\> for seed migration data access
- [x] T032 [US7] Validate seed migration produces correct function default parameter values: RolePlayGeneration (temp=0.7, topP=0.9, max=500), StoryModeGeneration (temp=0.7, topP=0.9, max=500), StorySummarize (temp=0.3, topP=0.9, max=500), StoryAnalyze (temp=0.3, topP=0.9, max=800), StoryRank (temp=0.1, topP=0.9, max=200), ScenarioPreview (temp=0.6, topP=0.95, max=1200), ScenarioAdapt (temp=0.5, topP=0.95, max=2000), WritingAssistant (temp=0.7, topP=0.9, max=500), RolePlayAssistant (temp=0.7, topP=0.9, max=500)
- [x] T033 [US7] Add idempotency guard in seed migration: only seed when Providers table has zero rows, log Information when seeding occurs, log Information when skipping (data already exists) per FR-035

**Checkpoint**: First-run migration seeds all data from appsettings; subsequent starts skip migration. Application behaves identically to pre-migration.

---

## Phase 6: User Story 2 — Register Models Under Providers (Priority: P1)

**Goal**: Users can register models under providers with identifier and display name, view models grouped by provider, enable/disable models.

**Independent Test**: Add a provider, register a model with identifier "Dolphin3.0-Llama3.1-8B" and display name, confirm it appears grouped under the provider.

### Implementation for User Story 2

- [x] T034 [US2] Add ModelManagerFacade model methods in DreamGenClone.Web/Application/ModelManager/ModelManagerFacade.cs: GetModelsByProviderAsync, GetAllModelsGroupedByProviderAsync, SaveModelAsync (with duplicate identifier check), DeleteModelAsync (with dependent function default warning data), EnableDisableModelAsync
- [x] T035 [US2] Add Models section to DreamGenClone.Web/Components/Pages/ModelManager.razor: models grouped by provider header, add model inline form (provider dropdown, model identifier input, display name input), save/delete/enable-disable buttons, inline "Add a provider first" message when no providers exist per FR-002a
- [x] T036 [US2] Add validation in ModelManager.razor Models section: duplicate identifier check per provider per FR-008, confirmation dialog before disabling/deleting a model that is assigned as function default per FR-011, provider must be selected

**Checkpoint**: Model registration, grouping by provider, enable/disable, and dependency warnings all functional.

---

## Phase 7: User Story 3 — Assign Default Models to Application Functions (Priority: P1)

**Goal**: Users can assign a registered model with temperature/topP/maxTokens to each of the 9 application functions, changes persist to SQLite.

**Independent Test**: Register a provider and model, assign it to "RolePlay Generation" with specific parameters, verify the assignment persists after page refresh.

### Implementation for User Story 3

- [x] T037 [US3] Add ModelManagerFacade function default methods in DreamGenClone.Web/Application/ModelManager/ModelManagerFacade.cs: GetAllFunctionDefaultsAsync (returns all 9 functions with current assignments), SaveFunctionDefaultAsync, ClearFunctionDefaultAsync, GetEnabledModelsForDropdownAsync (enabled models from enabled providers grouped by provider)
- [x] T038 [US3] Add Function Defaults section to DreamGenClone.Web/Components/Pages/ModelManager.razor: grid/table of all 9 AppFunction values with display names, model dropdown per function (enabled models from enabled providers grouped by provider per FR-014), temperature slider (0.0–2.0), topP slider (0.0–1.0), maxTokens input (1–8000), save button per row, inline "Register a model first" message when no models exist per FR-002a
- [x] T039 [US3] Add validation in ModelManager.razor Function Defaults section: temperature range 0.0–2.0, topP range 0.0–1.0, maxTokens range 1–8000 per FR-013, model must be from enabled provider

**Checkpoint**: All 9 functions can be assigned models with parameters. Function defaults section fully operational.

---

## Phase 8: User Story 4 — Runtime Model Switching Without Restart (Priority: P1)

**Goal**: Changes to providers, models, or function defaults take effect immediately for subsequent generation calls without restarting the application.

**Independent Test**: Assign Model A to RolePlay Generation, trigger a generation, switch default to Model B, trigger another generation, verify second uses Model B.

*Note: Runtime switching is inherent in the architecture — ModelResolutionService reads from SQLite on each call (T021). This phase refactors all existing services to use the new resolution path.*

### Implementation for User Story 4

- [x] T040 [P] [US4] Refactor RolePlayContinuationService in DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs: replace ILmStudioClient with ICompletionClient, replace IOptions\<LmStudioOptions\> model usage with IModelResolutionService.ResolveAsync(AppFunction.RolePlayGeneration), pass session override if set
- [x] T041 [P] [US4] Refactor StoryEngineService in DreamGenClone.Web/Application/Story/StoryEngineService.cs: replace ILmStudioClient with ICompletionClient, replace IOptions\<LmStudioOptions\> model usage with IModelResolutionService.ResolveAsync(AppFunction.StoryModeGeneration)
- [x] T042 [P] [US4] Refactor StorySummaryService in DreamGenClone.Infrastructure/StoryAnalysis/StorySummaryService.cs: replace ILmStudioClient with ICompletionClient, replace IOptions\<StoryAnalysisOptions\>/IOptions\<LmStudioOptions\> model usage with IModelResolutionService.ResolveAsync(AppFunction.StorySummarize), keep IOptions\<StoryAnalysisOptions\> for MaxStoryTextLength
- [x] T043 [P] [US4] Refactor StoryAnalysisService in DreamGenClone.Infrastructure/StoryAnalysis/StoryAnalysisService.cs: replace ILmStudioClient with ICompletionClient, replace model/param options with IModelResolutionService.ResolveAsync(AppFunction.StoryAnalyze)
- [x] T044 [P] [US4] Refactor StoryRankingService in DreamGenClone.Infrastructure/StoryAnalysis/StoryRankingService.cs: replace ILmStudioClient with ICompletionClient, replace model/param options with IModelResolutionService.ResolveAsync(AppFunction.StoryRank), keep IOptions\<StoryAnalysisOptions\> for RankConfidenceThreshold
- [x] T045 [P] [US4] Refactor ScenarioAdaptationService in DreamGenClone.Web/Application/Scenarios/ScenarioAdaptationService.cs: replace ILmStudioClient with ICompletionClient, replace IOptions\<ScenarioAdaptationOptions\>/IOptions\<LmStudioOptions\> model usage with IModelResolutionService.ResolveAsync(AppFunction.ScenarioPreview) and ResolveAsync(AppFunction.ScenarioAdapt) for respective methods
- [x] T046 [P] [US4] Refactor WritingAssistantService in DreamGenClone.Web/Application/Assistants/WritingAssistantService.cs: replace ILmStudioClient with ICompletionClient, replace model options with IModelResolutionService.ResolveAsync(AppFunction.WritingAssistant)
- [x] T047 [P] [US4] Refactor RolePlayAssistantService in DreamGenClone.Web/Application/Assistants/RolePlayAssistantService.cs: replace ILmStudioClient with ICompletionClient, replace model options with IModelResolutionService.ResolveAsync(AppFunction.RolePlayAssistant)
- [x] T048 [US4] Update ModelProcessingWorker in DreamGenClone.Infrastructure/Processing/ModelProcessingWorker.cs to use ICompletionClient instead of ILmStudioClient if it makes direct generation calls
- [x] T049 [US4] Remove old ILmStudioClient interface from DreamGenClone.Application/Abstractions/ILmStudioClient.cs and LmStudioClient class from DreamGenClone.Infrastructure/Models/LmStudioClient.cs after all consumers are migrated; remove typed HttpClient registration from Program.cs
- [x] T050 [US4] Update DI registrations in DreamGenClone.Web/Program.cs: remove IOptions\<LmStudioOptions\> model-dependent service configurations that are no longer needed for model resolution (keep options registrations for non-model config like TimeoutSeconds seed, MaxStoryTextLength, RankConfidenceThreshold)

**Checkpoint**: All 8 generation services use ICompletionClient + IModelResolutionService. Model changes take effect immediately without restart. Old ILmStudioClient removed.

---

## Phase 9: User Story 5 — Per-Session Model Override in RolePlay (Priority: P2)

**Goal**: Users can select a registered model from a dropdown in the RolePlay workspace settings panel to override the function default for that session only.

**Independent Test**: Open a RolePlay session, select a different model from settings panel dropdown, generate a continuation, verify it uses session model instead of function default.

### Implementation for User Story 5

- [x] T051 [US5] Refactor ModelSettingsService in DreamGenClone.Web/Application/Models/ModelSettingsService.cs: change model field from free-text string to RegisteredModel ID (string GUID), add GetSessionModelId/SetSessionModelId methods, integrate with IRegisteredModelRepository to validate model exists and is enabled
- [x] T052 [US5] Refactor ModelSettingsPanel.razor in DreamGenClone.Web/Components/Shared/ModelSettingsPanel.razor: replace free-text model input with dropdown populated from IRegisteredModelRepository.GetAllEnabledAsync grouped by provider per FR-025/FR-026, retain temperature/topP/maxTokens sliders, add "Use Default" option to clear override per FR-027
- [x] T053 [US5] Wire per-session override through RolePlayContinuationService: pass session model ID, temperature, topP, maxTokens from ModelSettingsService to IModelResolutionService.ResolveAsync session override parameters when generating continuations
- [x] T054 [US5] Verify session override isolation: per-session overrides scoped to individual session per FR-027, changes in one session do not affect other sessions

**Checkpoint**: RolePlay settings panel shows model dropdown, session override correctly overrides function default, overrides are session-scoped.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Logging, error handling, and cleanup across all user stories

- [x] T055 [P] Add structured Serilog logging across all ModelManagerFacade operations (provider create/update/delete, model register/enable-disable/delete, function default changes) at Information level per FR-038 in DreamGenClone.Web/Application/ModelManager/ModelManagerFacade.cs
- [x] T056 [P] Add structured Serilog logging to CompletionClient in DreamGenClone.Infrastructure/Models/CompletionClient.cs: log function name, resolved model identifier, provider name, session override flag, and request duration at Information level per FR-038a; log errors at Error level per FR-039
- [x] T057 [P] Add user-facing error handling in ModelManager.razor for provider test failures, save validation errors, deletion confirmation, and CryptographicException (re-enter API key) per edge cases in spec.md
- [x] T058 [P] Add user-facing error handling in generation services for ModelResolutionException: display clear "No model configured for [function] — configure in Model Manager" message per FR-022 fallback
- [x] T059 Verify Serilog log levels are configurable via appsettings.json for new DreamGenClone.Infrastructure.ModelManager and DreamGenClone.Web.Application.ModelManager namespaces per FR-040
- [x] T060 Run quickstart.md validation: build solution, start application, verify first-run migration completes, navigate to Model Manager page, verify seeded provider/models/function defaults are correct

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately. All 13 tasks are parallelizable.
- **Foundational (Phase 2)**: Depends on Phase 1 completion. T014-T015 depend on domain entities. T017-T019 depend on interfaces. T020 depends on ICompletionClient + ResolvedModel. T021 depends on repositories + IModelResolutionService.
- **US1 — Providers (Phase 3)**: Depends on Phase 2. First UI phase.
- **US6 — Security (Phase 4)**: Depends on Phase 3 (encryption wired through provider save).
- **US7 — Migration (Phase 5)**: Depends on Phase 2 (tables + seed logic). Can parallel with Phase 3-4 but validation needs Phase 3 UI.
- **US2 — Models (Phase 6)**: Depends on Phase 3 (providers must exist to register models).
- **US3 — Function Defaults (Phase 7)**: Depends on Phase 6 (models must exist to assign defaults).
- **US4 — Runtime Switching (Phase 8)**: Depends on Phase 7 (function defaults must work for resolution). All 8 service refactors (T040-T047) are parallelizable.
- **US5 — Session Override (Phase 9)**: Depends on Phase 8 (services must use IModelResolutionService).
- **Polish (Phase 10)**: Depends on all previous phases. All logging/error tasks are parallelizable.

### User Story Dependencies

```text
Phase 1 (Setup) → Phase 2 (Foundation)
                       ↓
                  Phase 3 (US1: Providers) ← Phase 5 (US7: Migration) can parallel
                       ↓
                  Phase 4 (US6: Security)
                       ↓
                  Phase 6 (US2: Models)
                       ↓
                  Phase 7 (US3: Function Defaults)
                       ↓
                  Phase 8 (US4: Runtime Switching) — 8 parallel service refactors
                       ↓
                  Phase 9 (US5: Session Override)
                       ↓
                  Phase 10 (Polish)
```

### Parallel Opportunities

**Phase 1**: All 13 tasks (T001-T013) can run in parallel — different files with no dependencies.

**Phase 2**: T016, T017, T018, T019 can run in parallel (different repository files). T020 and T021 depend on repositories.

**Phase 8**: All 8 service refactors (T040-T047) can run in parallel — each touches a different service file with no cross-dependencies.

**Phase 10**: All logging/error tasks (T055-T058) can run in parallel.

---

## Implementation Strategy

### MVP First (US1 + US6 + US7 Only)

1. Complete Phase 1: Setup (domain + interfaces)
2. Complete Phase 2: Foundational (repositories, encryption, client, resolution)
3. Complete Phase 3: US1 — Providers (CRUD + UI)
4. Complete Phase 4: US6 — Security (encryption hardening)
5. Complete Phase 5: US7 — Migration (seed validation)
6. **STOP and VALIDATE**: Provider management works, migration seeds correctly, keys encrypted

### Full Delivery

7. Complete Phase 6: US2 — Models (registration under providers)
8. Complete Phase 7: US3 — Function Defaults (model-to-function assignment)
9. Complete Phase 8: US4 — Runtime Switching (refactor all 8 services)
10. Complete Phase 9: US5 — Session Override (RolePlay dropdown)
11. Complete Phase 10: Polish (logging, error handling, validation)

### Incremental Delivery

Each phase adds testable value without breaking previous phases:
- After Phase 3: Can manage providers
- After Phase 7: Can assign models to functions
- After Phase 8: All generation calls use Model Manager (full feature)
- After Phase 9: Per-session flexibility added

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Options classes (LmStudioOptions, StoryAnalysisOptions, ScenarioAdaptationOptions) are retained for non-model config fields (MaxStoryTextLength, RankConfidenceThreshold, seed values) — only model/temperature/topP/maxTokens fields become DB-driven
- ScenarioAdaptationService uses TWO AppFunctions (ScenarioPreview + ScenarioAdapt) with different parameters — each call resolves independently
- Commit after each task or logical group
- Stop at any checkpoint to validate the current story independently
