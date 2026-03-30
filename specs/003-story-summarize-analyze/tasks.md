# Tasks: Story Summarize & Analyze

**Input**: Design documents from `/specs/003-story-summarize-analyze/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create folder structure and configuration options for the Story Summarize & Analyze feature

- [x] T001 Create StoryAnalysis folder structure: `DreamGenClone.Domain/StoryAnalysis/`, `DreamGenClone.Application/StoryAnalysis/`, `DreamGenClone.Application/StoryAnalysis/Models/`, `DreamGenClone.Infrastructure/StoryAnalysis/`, `DreamGenClone.Web/Application/StoryAnalysis/`, `DreamGenClone.Tests/StoryAnalysis/`
- [x] T002 [P] Create StoryAnalysisOptions configuration class in `DreamGenClone.Infrastructure/Configuration/StoryAnalysisOptions.cs` with properties: SummarizeTemperature (default 0.3), SummarizeMaxTokens (default 500), AnalyzeTemperature (default 0.3), AnalyzeMaxTokens (default 800), RankTemperature (default 0.1), RankMaxTokens (default 200), MaxStoryTextLength (default 12000)
- [x] T003 [P] Add StoryAnalysis configuration section to `DreamGenClone.Web/appsettings.json` and `DreamGenClone.Web/appsettings.Development.json` with default values from StoryAnalysisOptions

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain entities, application interfaces, persistence schema, and DI registration that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T004 [P] Create AnalysisDimension enum in `DreamGenClone.Domain/StoryAnalysis/AnalysisDimension.cs` with values: Characters, Themes, PlotStructure, WritingStyle
- [x] T005 [P] Create StorySummary domain entity in `DreamGenClone.Domain/StoryAnalysis/StorySummary.cs` with fields: Id (GUID string), ParsedStoryId, SummaryText, GeneratedUtc, UpdatedUtc
- [x] T006 [P] Create StoryAnalysisResult domain entity in `DreamGenClone.Domain/StoryAnalysis/StoryAnalysisResult.cs` with fields: Id (GUID string), ParsedStoryId, CharactersJson, ThemesJson, PlotStructureJson, WritingStyleJson, GeneratedUtc, UpdatedUtc
- [x] T007 [P] Create RankingCriterion domain entity in `DreamGenClone.Domain/StoryAnalysis/RankingCriterion.cs` with fields: Id (GUID string), Name, Weight (int 1–5), CreatedUtc, UpdatedUtc
- [x] T008 [P] Create StoryRankingResult domain entity in `DreamGenClone.Domain/StoryAnalysis/StoryRankingResult.cs` with fields: Id (GUID string), ParsedStoryId, CriteriaSnapshotJson, ScoresJson, WeightedAggregate (double), GeneratedUtc, UpdatedUtc
- [x] T009 [P] Create SummarizeResult DTO in `DreamGenClone.Application/StoryAnalysis/Models/SummarizeResult.cs` with properties: Success (bool), SummaryText, ErrorMessage
- [x] T010 [P] Create AnalyzeResult DTO in `DreamGenClone.Application/StoryAnalysis/Models/AnalyzeResult.cs` with properties: Success (bool), CharactersJson, ThemesJson, PlotStructureJson, WritingStyleJson, DimensionErrors (dictionary of AnalysisDimension → error string)
- [x] T011 [P] Create RankResult DTO in `DreamGenClone.Application/StoryAnalysis/Models/RankResult.cs` with properties: Success (bool), ScoresJson, CriteriaSnapshotJson, WeightedAggregate (double?), CriterionErrors (dictionary of criterionId → error string)
- [x] T012 [P] Create IStorySummaryService interface in `DreamGenClone.Application/StoryAnalysis/IStorySummaryService.cs` with methods: SummarizeAsync(parsedStoryId, cancellationToken), GetSummaryAsync(parsedStoryId, cancellationToken)
- [x] T013 [P] Create IStoryAnalysisService interface in `DreamGenClone.Application/StoryAnalysis/IStoryAnalysisService.cs` with methods: AnalyzeAsync(parsedStoryId, cancellationToken), GetAnalysisAsync(parsedStoryId, cancellationToken)
- [x] T014 [P] Create IRankingCriteriaService interface in `DreamGenClone.Application/StoryAnalysis/IRankingCriteriaService.cs` with methods: CreateAsync(name, weight, cancellationToken), ListAsync(cancellationToken), UpdateAsync(id, name, weight, cancellationToken), DeleteAsync(id, cancellationToken)
- [x] T015 [P] Create IStoryRankingService interface in `DreamGenClone.Application/StoryAnalysis/IStoryRankingService.cs` with methods: RankAsync(parsedStoryId, cancellationToken), GetRankingAsync(parsedStoryId, cancellationToken)
- [x] T016 Add StorySummaries, StoryAnalyses, RankingCriteria, and StoryRankings table creation SQL to `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` InitializeAsync method, including indexes per data-model.md
- [x] T017 Add summary persistence methods to `DreamGenClone.Infrastructure/Persistence/ISqlitePersistence.cs`: SaveStorySummaryAsync, LoadStorySummaryAsync (by ParsedStoryId)
- [x] T018 Add analysis persistence methods to `DreamGenClone.Infrastructure/Persistence/ISqlitePersistence.cs`: SaveStoryAnalysisAsync, LoadStoryAnalysisAsync (by ParsedStoryId)
- [x] T019 Add ranking criteria persistence methods to `DreamGenClone.Infrastructure/Persistence/ISqlitePersistence.cs`: SaveRankingCriterionAsync, LoadRankingCriterionAsync, LoadAllRankingCriteriaAsync, DeleteRankingCriterionAsync
- [x] T020 Add ranking result persistence methods to `DreamGenClone.Infrastructure/Persistence/ISqlitePersistence.cs`: SaveStoryRankingAsync, LoadStoryRankingAsync (by ParsedStoryId)
- [x] T021 Implement SaveStorySummaryAsync and LoadStorySummaryAsync in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` using upsert (ON CONFLICT(ParsedStoryId) DO UPDATE) pattern per existing conventions
- [x] T022 Implement SaveStoryAnalysisAsync and LoadStoryAnalysisAsync in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` using upsert pattern
- [x] T023 Implement SaveRankingCriterionAsync, LoadRankingCriterionAsync, LoadAllRankingCriteriaAsync, DeleteRankingCriterionAsync in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
- [x] T024 Implement SaveStoryRankingAsync and LoadStoryRankingAsync in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` using upsert pattern
- [x] T025 Register StoryAnalysisOptions via Options binding in `DreamGenClone.Web/Program.cs`: `builder.Services.Configure<StoryAnalysisOptions>(builder.Configuration.GetSection(StoryAnalysisOptions.SectionName))`

**Checkpoint**: Foundation ready — domain entities, interfaces, persistence, and configuration are in place. User story implementation can begin.

---

## Phase 3: User Story 1 — Summarize a Persisted Story (Priority: P1) 🎯 MVP

**Goal**: Users can invoke Summarize on a persisted story from the detail view. The LLM generates a synopsis that is displayed and persisted.

**Independent Test**: Open a persisted story → click Summarize → verify summary appears and is persisted → navigate away and back → verify summary still displayed.

### Implementation for User Story 1

- [x] T026 [US1] Implement StorySummaryService in `DreamGenClone.Infrastructure/StoryAnalysis/StorySummaryService.cs`: inject ILmStudioClient, ISqlitePersistence, IOptions\<StoryAnalysisOptions\>, ILogger. Implement SummarizeAsync per summarize-prompt-contract.md — build system/user messages, apply text truncation (MaxStoryTextLength), call GenerateAsync with configured temperature/maxTokens, validate response (non-empty, ≥50 chars), persist via SaveStorySummaryAsync on success, preserve existing on failure. Implement GetSummaryAsync via LoadStorySummaryAsync.
- [x] T027 [US1] Create StoryAnalysisFacade in `DreamGenClone.Web/Application/StoryAnalysis/StoryAnalysisFacade.cs`: inject IStorySummaryService, IStoryAnalysisService, IRankingCriteriaService, IStoryRankingService. Expose facade methods for all four services. (For now, only summary methods are implemented; analysis/ranking methods can throw NotImplementedException until their phases.)
- [x] T028 [US1] Register StorySummaryService and StoryAnalysisFacade in `DreamGenClone.Web/Program.cs`: `AddScoped<IStorySummaryService, StorySummaryService>()` and `AddScoped<StoryAnalysisFacade>()`
- [x] T029 [US1] Extend `DreamGenClone.Web/Components/Pages/StoryParserDetail.razor` to add a Summary card section: inject StoryAnalysisFacade, load existing summary on page load via GetSummaryAsync, display summary text if available, add "Summarize" button that calls SummarizeAsync, show progress spinner during LLM call, show error alert on failure, disable button while operation is in progress
- [x] T030 [US1] Add Information-level Serilog logs to StorySummaryService for: summarize invoked (story ID), text truncation applied (original length, truncated length), LLM call completed (duration), summary persisted, validation failure, LLM error

**Checkpoint**: User Story 1 complete — summaries can be generated, displayed, persisted, and regenerated from the story detail view.

---

## Phase 4: User Story 2 — Analyze a Persisted Story (Priority: P2)

**Goal**: Users can invoke Analyze on a persisted story to extract characters, themes, plot structure, and writing style via four separate LLM calls. Supports partial-success.

**Independent Test**: Open a persisted story → click Analyze → verify four dimension sections appear → navigate away and back → verify analysis persisted.

### Implementation for User Story 2

- [x] T031 [US2] Implement StoryAnalysisService in `DreamGenClone.Infrastructure/StoryAnalysis/StoryAnalysisService.cs`: inject ILmStudioClient, ISqlitePersistence, IOptions\<StoryAnalysisOptions\>, ILogger. Implement AnalyzeAsync per analyze-prompt-contract.md — iterate over four AnalysisDimension values, build dimension-specific system prompts, apply text truncation, call GenerateAsync with configured temperature/maxTokens for each dimension, strip markdown code fences from responses, validate JSON against per-dimension schema, persist all successful dimensions in a single upsert (null for failed), preserve existing analysis if all four fail. Implement GetAnalysisAsync via LoadStoryAnalysisAsync.
- [x] T032 [US2] Register StoryAnalysisService in `DreamGenClone.Web/Program.cs`: `AddScoped<IStoryAnalysisService, StoryAnalysisService>()`
- [x] T033 [US2] Update StoryAnalysisFacade in `DreamGenClone.Web/Application/StoryAnalysis/StoryAnalysisFacade.cs` to wire through IStoryAnalysisService methods (replace NotImplementedException)
- [x] T034 [US2] Extend `DreamGenClone.Web/Components/Pages/StoryParserDetail.razor` to add an Analysis section: load existing analysis on page load via GetAnalysisAsync, display four collapsible cards (Characters, Themes, Plot Structure, Writing Style) with structured content rendering, add "Analyze" button that calls AnalyzeAsync, show progress spinner during LLM calls, show per-dimension error alerts for failed dimensions, disable button while operation is in progress
- [x] T035 [US2] Add Information-level Serilog logs to StoryAnalysisService for: analyze invoked (story ID), per-dimension LLM call start/complete/fail (dimension name, duration), JSON validation pass/fail per dimension, analysis persisted (success count), text truncation applied

**Checkpoint**: User Story 2 complete — analysis with four dimensions can be generated, displayed with partial-success support, and persisted.

---

## Phase 5: User Story 3 — Configure and Manage Ranking Criteria (Priority: P2)

**Goal**: Users can create, edit, and delete reusable ranking criteria with weighted values 1–5. Criteria are persisted independently from stories.

**Independent Test**: Navigate to criteria management → create/edit/delete criteria → verify persistence — no parsed story needed.

### Implementation for User Story 3

- [x] T036 [US3] Implement RankingCriteriaService in `DreamGenClone.Infrastructure/StoryAnalysis/RankingCriteriaService.cs`: inject ISqlitePersistence, ILogger. Implement CreateAsync (validate name non-empty, weight 1–5, generate GUID, persist), ListAsync (load all ordered by UpdatedUtc DESC), UpdateAsync (load existing, validate, update fields, persist), DeleteAsync (delete by ID, return bool). All methods include structured logging.
- [x] T037 [US3] Register RankingCriteriaService in `DreamGenClone.Web/Program.cs`: `AddScoped<IRankingCriteriaService, RankingCriteriaService>()`
- [x] T038 [US3] Update StoryAnalysisFacade in `DreamGenClone.Web/Application/StoryAnalysis/StoryAnalysisFacade.cs` to wire through IRankingCriteriaService methods
- [x] T039 [US3] Extend `DreamGenClone.Web/Components/Pages/StoryParserDetail.razor` to add inline Ranking Criteria management section: display existing criteria list with name and weight, add form to create new criterion (name input + weight 1–5 dropdown/input), add edit button per criterion (inline edit name and weight), add delete button per criterion with confirmation, validate weight range client-side and show validation messages, disable form controls during save operations
- [x] T040 [US3] Add Information-level Serilog logs to RankingCriteriaService for: criterion created (ID, name, weight), criterion updated (ID, changes), criterion deleted (ID), list loaded (count)

**Checkpoint**: User Story 3 complete — ranking criteria can be managed independently, ready for ranking operations.

---

## Phase 6: User Story 4 — Rank a Persisted Story Against Criteria (Priority: P3)

**Goal**: Users can rank a persisted story against their configured criteria. The LLM scores each criterion, a weighted aggregate is computed, and results are persisted.

**Independent Test**: Configure criteria → open a persisted story → click Rank → verify per-criterion scores and weighted aggregate displayed and persisted.

### Implementation for User Story 4

- [x] T041 [US4] Implement StoryRankingService in `DreamGenClone.Infrastructure/StoryAnalysis/StoryRankingService.cs`: inject ILmStudioClient, ISqlitePersistence, IRankingCriteriaService, IOptions\<StoryAnalysisOptions\>, ILogger. Implement RankAsync per rank-prompt-contract.md — validate at least one criterion exists (return error if none), load story text, take criteria snapshot, apply text truncation, iterate criteria and call GenerateAsync per criterion with configured temperature/maxTokens, strip markdown code fences, validate JSON (score 1–10 integer, reasoning non-empty), compute weightedAggregate = sum(score×weight)/sum(weights) for successful criteria, persist snapshot + scores + aggregate via SaveStoryRankingAsync on success (at least one criterion scored), preserve existing ranking if all fail. Implement GetRankingAsync via LoadStoryRankingAsync.
- [x] T042 [US4] Register StoryRankingService in `DreamGenClone.Web/Program.cs`: `AddScoped<IStoryRankingService, StoryRankingService>()`
- [x] T043 [US4] Update StoryAnalysisFacade in `DreamGenClone.Web/Application/StoryAnalysis/StoryAnalysisFacade.cs` to wire through IStoryRankingService methods
- [x] T044 [US4] Extend `DreamGenClone.Web/Components/Pages/StoryParserDetail.razor` to add a Ranking section: load existing ranking on page load via GetRankingAsync, display per-criterion scores table (criterion name, weight, score 1–10, reasoning), display weighted aggregate score prominently, add "Rank" button that calls RankAsync, show progress spinner during LLM calls, show "No criteria configured" message with link to criteria management when no criteria exist (FR-022), show per-criterion error alerts for failed criteria, disable button while operation is in progress
- [x] T045 [US4] Add Information-level Serilog logs to StoryRankingService for: rank invoked (story ID, criteria count), per-criterion LLM call start/complete/fail (criterion name, duration), score validation pass/fail per criterion, weighted aggregate computed (value), ranking persisted, no-criteria guard triggered, text truncation applied

**Checkpoint**: User Story 4 complete — stories can be ranked against user-configured criteria with weighted scoring.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements and validations that affect multiple user stories

- [x] T046 [P] Add edge-case handling for empty/whitespace-only story text across all three services (StorySummaryService, StoryAnalysisService, StoryRankingService) — return descriptive error result without calling LLM
- [x] T047 [P] Add edge-case handling for story-not-found (ParsedStoryId doesn't exist in DB) across all three services — return descriptive error result
- [x] T048 Verify all StoryParserDetail.razor action buttons (Summarize, Analyze, Rank) are disabled while any LLM operation is in progress for concurrency guard per research.md Decision 7
- [x] T049 Run quickstart.md scenarios 1–4 end-to-end against running application with LM Studio and verify all acceptance criteria pass
- [x] T050 Run quickstart.md scenario 5 (edge cases) and verify truncation warning logging, empty-story errors, concurrent-click guard, and deleted-story errors

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (T001 for folders, T002–T003 for config)
- **User Story 1 (Phase 3)**: Depends on Phase 2 completion — no other story dependencies
- **User Story 2 (Phase 4)**: Depends on Phase 2 completion — independent of US1
- **User Story 3 (Phase 5)**: Depends on Phase 2 completion — independent of US1 and US2
- **User Story 4 (Phase 6)**: Depends on Phase 2 AND Phase 5 (US3 — needs ranking criteria CRUD)
- **Polish (Phase 7)**: Depends on Phases 3–6 being complete

### User Story Dependencies

- **US1 (P1 — Summarize)**: Phase 2 only → can be first MVP delivery
- **US2 (P2 — Analyze)**: Phase 2 only → can run in parallel with US1 and US3
- **US3 (P2 — Ranking Criteria)**: Phase 2 only → can run in parallel with US1 and US2
- **US4 (P3 — Rank Story)**: Phase 2 + US3 completion → must wait for ranking criteria CRUD

### Within Each User Story

1. Service implementation first (core logic + LLM integration + persistence)
2. DI registration second (Program.cs + Facade wiring)
3. UI extension third (StoryParserDetail.razor sections)
4. Logging last (can be added incrementally with service implementation)

### Parallel Opportunities

**Phase 2 (Foundational)**: T004–T015 are all [P] — domain entities, DTOs, and interfaces can be created in parallel since they are independent files. T016–T024 (persistence) depend on T004–T008 entities but can be parallelized among themselves.

**Phase 3–5 (US1, US2, US3)**: Can be executed in parallel after Phase 2 since they touch different service files. Only StoryParserDetail.razor and Program.cs require sequential integration, but these are incremental additions.

**Phase 6 (US4)**: Must wait for US3 (Phase 5) because RankAsync calls IRankingCriteriaService.ListAsync.

## Implementation Strategy

**MVP (Minimum Viable Product)**: Phase 1 + Phase 2 + Phase 3 (User Story 1 — Summarize). Delivers the simplest high-value capability with a single LLM call.

**Incremental delivery**:
1. MVP: Summarize (T001–T030) — single LLM call, immediate value
2. +Analyze (T031–T035) — structured insights, 4 LLM calls
3. +Ranking Criteria (T036–T040) — user preference configuration, no LLM
4. +Rank Story (T041–T045) — personalized scoring, N LLM calls
5. +Polish (T046–T050) — edge cases and validation
