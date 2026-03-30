# Implementation Plan: Story Summarize & Analyze

**Branch**: `003-story-summarize-analyze` | **Date**: 2026-03-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/003-story-summarize-analyze/spec.md`

## Summary

Add Summarize, Analyze, and Rank capabilities to persisted stories in the catalog. Each operation invokes the existing local LLM (LM Studio / OpenAI-compatible) through focused, separate calls for accuracy with smaller models. Results are persisted in SQLite alongside existing ParsedStoryRecord data. Ranking uses user-configured weighted criteria stored independently.

## Technical Context

**Language/Version**: C# / .NET 9  
**Primary Dependencies**: Microsoft.Data.Sqlite (existing), Blazor Server (existing), LM Studio via OpenAI-compatible HTTP client (existing ILmStudioClient)  
**Storage**: SQLite (existing `data/dreamgenclone.db`) — three new tables: StorySummaries, StoryAnalyses, RankingCriteria, StoryRankings  
**Testing**: xUnit (existing DreamGenClone.Tests project)  
**Target Platform**: Windows desktop (local-first)  
**Project Type**: Blazor Server web application (existing)  
**Performance Goals**: Accuracy over speed; each LLM call is sequential and focused; no parallelism requirement for v1  
**Constraints**: Offline-capable (local LLM only), context window limits of local models (~4K–8K tokens typical)  
**Scale/Scope**: Single user, dozens of stories, dozens of ranking criteria

## Constitution Check (Pre-Phase 0)

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved — all LLM calls go to local LM Studio at 127.0.0.1:1234; no cloud dependency
- [x] Module boundaries and adapter seams are explicit and swappable — LLM access through ILmStudioClient abstraction; persistence through ISqlitePersistence
- [x] .NET layered architecture uses separate projects with enforced dependency direction — Domain (entities) → Application (services/interfaces) → Infrastructure (persistence/LLM client) → Web (UI/DI)
- [x] Deterministic state transitions and JSON contract validation are test-covered — LLM prompt/response contracts use JSON schemas; persistence operations are upsert-based with explicit timestamps
- [x] Persistence uses SQLite by default — three new tables in existing SQLite database; no exceptions requested
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices — follows existing pattern in SqlitePersistence and LmStudioClient
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths — all service methods will log entry/exit/errors
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes — existing appsettings.json override pattern

## Project Structure

### Documentation (this feature)

```text
specs/003-story-summarize-analyze/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── summarize-prompt-contract.md
│   ├── analyze-prompt-contract.md
│   └── rank-prompt-contract.md
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/
├── StoryParser/
│   └── ParsedStoryRecord.cs         # Existing — referenced by new entities
└── StoryAnalysis/
    ├── StorySummary.cs               # New domain entity
    ├── StoryAnalysisResult.cs        # New domain entity (4 dimensions)
    ├── AnalysisDimension.cs          # New enum: Characters, Themes, PlotStructure, WritingStyle
    ├── RankingCriterion.cs           # New domain entity
    └── StoryRankingResult.cs         # New domain entity (per-criterion scores + aggregate)

DreamGenClone.Application/
└── StoryAnalysis/
    ├── IStorySummaryService.cs       # New interface
    ├── IStoryAnalysisService.cs      # New interface
    ├── IRankingCriteriaService.cs    # New interface
    ├── IStoryRankingService.cs       # New interface
    └── Models/
        ├── SummarizeRequest.cs       # New DTO
        ├── SummarizeResult.cs        # New DTO
        ├── AnalyzeRequest.cs         # New DTO
        ├── AnalyzeResult.cs          # New DTO
        ├── RankRequest.cs            # New DTO
        └── RankResult.cs             # New DTO

DreamGenClone.Infrastructure/
├── Persistence/
│   ├── ISqlitePersistence.cs         # Extended with new methods
│   └── SqlitePersistence.cs          # Extended with new tables/queries
└── StoryAnalysis/
    ├── StorySummaryService.cs        # New — implements IStorySummaryService
    ├── StoryAnalysisService.cs       # New — implements IStoryAnalysisService
    ├── RankingCriteriaService.cs     # New — implements IRankingCriteriaService
    └── StoryRankingService.cs        # New — implements IStoryRankingService

DreamGenClone.Web/
├── Application/
│   └── StoryAnalysis/
│       └── StoryAnalysisFacade.cs    # New — Web-layer facade for DI
├── Components/
│   └── Pages/
│       └── StoryParserDetail.razor   # Extended — add Summarize/Analyze/Rank sections
└── Program.cs                        # Extended — register new services

DreamGenClone.Tests/
└── StoryAnalysis/
    ├── StorySummaryServiceTests.cs   # New
    ├── StoryAnalysisServiceTests.cs  # New
    ├── RankingCriteriaServiceTests.cs # New
    └── StoryRankingServiceTests.cs   # New
```

**Structure Decision**: Follows established layered architecture. New `StoryAnalysis/` folders in Domain, Application, Infrastructure, and Tests mirror the existing `StoryParser/` pattern. No new projects needed — features are added within existing project boundaries.

## Complexity Tracking

No constitution violations. All design decisions follow existing patterns.

## Constitution Check (Post-Phase 1 Design)

*Re-evaluated after data model, contracts, and quickstart are finalized.*

- [x] Local-first runtime preserved — all LLM calls to local LM Studio; no cloud services in any contract; story text never leaves the machine
- [x] Module boundaries and adapter seams are explicit and swappable — new services (IStorySummaryService, IStoryAnalysisService, IRankingCriteriaService, IStoryRankingService) follow existing interface/implementation pattern; LLM access through ILmStudioClient
- [x] .NET layered architecture uses separate projects with enforced dependency direction — Domain entities in DreamGenClone.Domain/StoryAnalysis, interfaces in Application, implementations in Infrastructure, DI in Web
- [x] Deterministic state transitions and JSON contract validation are test-covered — all LLM responses validated against JSON schemas before persistence; prompt contracts define exact expected shapes; scoring uses deterministic weighted average formula
- [x] Persistence uses SQLite by default — four new tables (StorySummaries, StoryAnalyses, RankingCriteria, StoryRankings) in existing SQLite database; no exceptions
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices — all contracts specify logging for entry/exit/error paths
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths — each LLM call, persistence operation, and validation step will log at Information level
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes — temperature, max_tokens, and truncation settings configurable via StoryAnalysisOptions in appsettings.json
