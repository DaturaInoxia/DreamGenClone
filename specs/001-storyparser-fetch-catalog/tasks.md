# Tasks: StoryParser URL Fetch, Parse, and Catalog

**Input**: Design documents from `/specs/001-storyparser-fetch-catalog/`
**Prerequisites**: `plan.md` (required), `spec.md` (required), `research.md`, `data-model.md`, `contracts/`

**Tests**: Tests are required by this feature spec (sample parity, determinism, error handling, and catalog workflows).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (`[US1]`, `[US2]`, `[US3]`)

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish project scaffolding and feature-level configuration surfaces.

- [X] T001 Add HTML parser dependency AngleSharp in DreamGenClone.Infrastructure/DreamGenClone.Infrastructure.csproj
- [X] T002 [P] Add StoryParser options model in DreamGenClone.Application/StoryParser/StoryParserOptions.cs
- [X] T003 [P] Add StoryParser config section defaults in DreamGenClone.Web/appsettings.json
- [X] T004 Wire StoryParser options binding and service registrations in DreamGenClone.Web/Program.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core contracts, models, and persistence plumbing required before any user story implementation.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 Create StoryParser domain status/sort enums in DreamGenClone.Domain/StoryParser/CatalogSortMode.cs
- [X] T006 [P] Create parse diagnostics domain model in DreamGenClone.Domain/StoryParser/ParseDiagnostics.cs
- [X] T007 [P] Create parsed page domain model in DreamGenClone.Domain/StoryParser/ParsedStoryPage.cs
- [X] T008 [P] Create parsed story aggregate model in DreamGenClone.Domain/StoryParser/ParsedStoryRecord.cs
- [X] T009 Create parser service contract in DreamGenClone.Application/StoryParser/IStoryParserService.cs
- [X] T010 [P] Create catalog service contract in DreamGenClone.Application/StoryParser/IStoryCatalogService.cs
- [X] T011 Extend SQLite persistence interface for parsed story operations in DreamGenClone.Infrastructure/Persistence/ISqlitePersistence.cs
- [X] T012 Extend SQLite schema and indexes for parsed stories in DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs
- [X] T013 Add Information/error logging checkpoints for StoryParser persistence operations in DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs

**Checkpoint**: Foundation ready. User story implementation can proceed.

---

## Phase 3: User Story 1 - Parse Full Story From URL (Priority: P1) 🎯 MVP

**Goal**: Parse supported URL pages into deterministic cleaned content and return combined output.

**Independent Test**: Provide a supported URL fixture and verify multi-page discovery, deterministic normalization, and parse output contracts.

### Tests for User Story 1 (write first, ensure failing before implementation)

- [X] T014 [P] [US1] Add single-page parity tests against Sample1 fixtures in DreamGenClone.Tests/StoryParser/ParsingParityTests.cs
- [X] T015 [P] [US1] Add multi-page parity tests against Sample2 fixtures in DreamGenClone.Tests/StoryParser/ParsingParityTests.cs
- [X] T016 [P] [US1] Add deterministic repeat-run tests in DreamGenClone.Tests/StoryParser/DeterminismTests.cs
- [X] T017 [P] [US1] Add parse error-policy tests (fail-fast/partial-success, no-retry) in DreamGenClone.Tests/StoryParser/ErrorHandlingTests.cs

### Implementation for User Story 1

- [X] T018 [P] [US1] Implement URL validation and HTML fetch client with timeout/content-type/max-size guards in DreamGenClone.Infrastructure/StoryParser/HtmlFetchClient.cs
- [X] T019 [P] [US1] Implement pagination discovery using query progression and terminal conditions in DreamGenClone.Infrastructure/StoryParser/PaginationDiscoveryService.cs
- [X] T020 [P] [US1] Implement domain-specific content extraction and deterministic normalization in DreamGenClone.Infrastructure/StoryParser/DomainStoryExtractor.cs
- [X] T021 [US1] Implement parse orchestration and error-mode behavior in DreamGenClone.Infrastructure/StoryParser/StoryParserService.cs
- [X] T022 [US1] Implement StoryParser application facade for parse workflow in DreamGenClone.Web/Application/StoryParser/StoryParserFacade.cs
- [X] T023 [US1] Add StoryParser parse page and input workflow in DreamGenClone.Web/Components/Pages/StoryParser.razor
- [X] T024 [US1] Add Information-level logs for major parse execution paths in DreamGenClone.Infrastructure/StoryParser/StoryParserService.cs

**Checkpoint**: User Story 1 is independently functional and testable (MVP parse flow).

---

## Phase 4: User Story 2 - Persist Parsed Story Results (Priority: P2)

**Goal**: Persist parsed outputs and diagnostics in SQLite and retrieve parsed story details by ID.

**Independent Test**: Run parse, verify persisted record shape, then retrieve by ID and validate required fields.

### Tests for User Story 2 (write first, ensure failing before implementation)

- [X] T025 [P] [US2] Add parsed story persistence create/read tests in DreamGenClone.Tests/StoryParser/CatalogPersistenceTests.cs
- [X] T026 [P] [US2] Add diagnostics persistence tests in DreamGenClone.Tests/StoryParser/CatalogPersistenceTests.cs
- [X] T027 [P] [US2] Add configured-limit persistence behavior tests in DreamGenClone.Tests/StoryParser/ErrorHandlingTests.cs

### Implementation for User Story 2

- [X] T028 [US2] Implement parsed story save/load operations in DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs
- [X] T029 [US2] Persist parse results and diagnostics from parser service in DreamGenClone.Infrastructure/StoryParser/StoryParserService.cs
- [X] T030 [US2] Implement get-by-id parsed story detail contract in DreamGenClone.Web/Application/StoryParser/StoryParserFacade.cs
- [X] T031 [US2] Add detail retrieval endpoint flow for parsed story page in DreamGenClone.Web/Components/Pages/StoryParserDetail.razor

**Checkpoint**: User Story 2 is independently functional and testable with durable persistence.

---

## Phase 5: User Story 3 - List, Search, and View Parsed Stories (Priority: P3)

**Goal**: Provide catalog list/search/view with required sorting and metadata-only filtering.

**Independent Test**: Parse multiple stories, validate default and alternate sorting, metadata search, and required detail fields in UI.

### Tests for User Story 3 (write first, ensure failing before implementation)

- [X] T032 [P] [US3] Add catalog list sorting tests (newest-first and URL/title ascending) in DreamGenClone.Tests/StoryParser/CatalogPersistenceTests.cs
- [X] T033 [P] [US3] Add metadata-only search tests (exclude combined-text search) in DreamGenClone.Tests/StoryParser/CatalogPersistenceTests.cs
- [X] T034 [P] [US3] Add catalog integration tests for list/search/view flows in DreamGenClone.Tests/StoryParser/CatalogIntegrationTests.cs

### Implementation for User Story 3

- [X] T035 [US3] Implement catalog list/search query operations in DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs
- [X] T036 [US3] Implement catalog service facade and sort mode switching in DreamGenClone.Web/Application/StoryParser/StoryCatalogFacade.cs
- [X] T037 [US3] Implement catalog list/search UI and navigation to detail in DreamGenClone.Web/Components/Pages/StoryParser.razor
- [X] T038 [US3] Ensure detail view displays required fields (combined text, URL, parsed date, page count, diagnostics) in DreamGenClone.Web/Components/Pages/StoryParserDetail.razor
- [X] T039 [US3] Add dedicated StoryParser navigation entry to shared shell in DreamGenClone.Web/Components/Layout/NavMenu.razor

**Checkpoint**: User Story 3 is independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, consistency, and readiness checks across stories.

- [X] T040 [P] Add StoryParser-specific Serilog level overrides and Verbose support in DreamGenClone.Web/appsettings.json
- [X] T041 [P] Add end-to-end quickstart validation notes in specs/001-storyparser-fetch-catalog/quickstart.md
- [X] T042 Execute full regression and StoryParser test suites via DreamGenClone.Tests/DreamGenClone.Tests.csproj
- [X] T043 [P] Update feature implementation traceability notes in specs/001-storyparser-fetch-catalog/checklists/requirements.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies.
- **Phase 2 (Foundational)**: Depends on Phase 1 and blocks all user story work.
- **Phase 3 (US1)**: Depends on Phase 2.
- **Phase 4 (US2)**: Depends on Phase 2 and integrates with US1 parse outputs.
- **Phase 5 (US3)**: Depends on Phase 2 and requires persisted records from US2 for full workflow validation.
- **Phase 6 (Polish)**: Depends on completion of selected user stories.

### User Story Dependencies

- **US1 (P1)**: No dependency on other user stories after foundational completion.
- **US2 (P2)**: Depends on parser outputs from US1 for end-to-end persistence verification.
- **US3 (P3)**: Depends on persisted records from US2 for catalog list/search/view behavior.

### Within Each User Story

- Tests must be authored and failing before implementation tasks.
- Core service logic before UI integration.
- Persistence and contract completion before list/search/detail UX verification.

---

## Parallel Opportunities

- Setup: T002 and T003 can run in parallel after T001.
- Foundational: T006, T007, T008, and T010 can run in parallel.
- US1 tests: T014-T017 can run in parallel.
- US1 implementation: T018, T019, and T020 can run in parallel before T021.
- US2 tests: T025-T027 can run in parallel.
- US3 tests: T032-T034 can run in parallel.

### Parallel Example: User Story 1

```bash
# Parallel tests
T014 + T015 + T016 + T017

# Parallel core implementation tasks
T018 + T019 + T020
```

### Parallel Example: User Story 2

```bash
# Parallel tests
T025 + T026 + T027
```

### Parallel Example: User Story 3

```bash
# Parallel tests
T032 + T033 + T034
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate parse flow independently using parity + determinism tests.
4. Demo parse MVP before moving to persistence/catalog expansions.

### Incremental Delivery

1. Deliver US1 parse flow (MVP).
2. Deliver US2 persistence and detail retrieval.
3. Deliver US3 catalog list/search/view and shared-shell navigation.
4. Finish with cross-cutting polish and regression.

### Parallel Team Strategy

1. Team completes Setup + Foundational together.
2. Then parallelize by stream:
   - Stream A: US1 parser engine and tests.
   - Stream B: US2 persistence and detail retrieval.
   - Stream C: US3 catalog UI/queries after US2 persistence APIs are stable.
