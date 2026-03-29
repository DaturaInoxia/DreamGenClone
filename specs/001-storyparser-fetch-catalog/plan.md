# Implementation Plan: StoryParser URL Fetch, Parse, and Catalog

**Branch**: `001-storyparser-fetch-catalog` | **Date**: 2026-03-28 | **Spec**: `D:\src\DreamGenClone\specs\001-storyparser-fetch-catalog\spec.md`
**Input**: Feature specification from `D:\src\DreamGenClone\specs\001-storyparser-fetch-catalog\spec.md`

## Summary

Implement StoryParser as a separate feature scope that reuses the existing DreamGenClone UI shell via a dedicated navigation entry. The feature fetches HTML from a supported URL, discovers paginated pages, extracts deterministic cleaned text via domain-specific selectors, combines pages into one story, persists outputs and diagnostics to SQLite, and provides list/search/view catalog workflows with metadata-only search in v1.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (`net9.0`)  
**Primary Dependencies**: ASP.NET Core Blazor Server, Microsoft.Data.Sqlite, Serilog (AspNetCore + sinks), Microsoft.Extensions.Options, xUnit  
**Additional Dependency Decision**: Add a DOM parser library for robust selector-based HTML extraction (AngleSharp preferred)  
**Storage**: SQLite (default required by constitution and spec)  
**Testing**: xUnit + Microsoft.NET.Test.Sdk + fixture parity tests under DreamGenClone.Tests  
**Target Platform**: Windows local runtime (Phase 1 local-first)  
**Project Type**: Layered .NET web application (Web/UI host + Application + Domain + Infrastructure + Tests)  
**Performance Goals**: Parse and persist a typical 1-20 page story within configured limits; deterministic byte-equivalent output across repeated runs with unchanged source HTML  
**Constraints**: Configurable timeout default 10s, max HTML/page default 5MB, max pages default 20, no automatic retries, one supported source domain in v1  
**Scale/Scope**: Single-user local workflow, catalog operations over locally persisted parsed-story records

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [x] Module boundaries and adapter seams are explicit and swappable
- [x] .NET layered architecture uses separate projects with enforced dependency direction
- [x] Deterministic state transitions and JSON contract validation are test-covered
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes

## Project Structure

### Documentation (this feature)

```text
specs/001-storyparser-fetch-catalog/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── storyparser-service-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
DreamGenClone.Domain/
└── StoryParser/
  ├── ParsedStoryRecord.cs
  ├── ParsedStoryPage.cs
  ├── ParseDiagnostics.cs
  └── CatalogSortMode.cs

DreamGenClone.Application/
└── StoryParser/
  ├── IStoryParserService.cs
  ├── IStoryCatalogService.cs
  ├── StoryParserOptions.cs
  └── Models/

DreamGenClone.Infrastructure/
├── StoryParser/
│   ├── HtmlFetchClient.cs
│   ├── PaginationDiscoveryService.cs
│   ├── DomainStoryExtractor.cs
│   └── StoryParserService.cs
└── Persistence/
  ├── ISqlitePersistence.cs          # extend with parsed story operations
  └── SqlitePersistence.cs           # extend schema + parsed story CRUD/query

DreamGenClone.Web/
├── Components/
│   ├── Layout/
│   │   └── NavMenu.razor             # add StoryParser navigation entry
│   └── Pages/
│      ├── StoryParser.razor          # parse + catalog landing
│      └── StoryParserDetail.razor    # selected story view
├── Application/
│   └── StoryParser/
│      ├── StoryParserFacade.cs
│      └── StoryCatalogFacade.cs
└── appsettings.json                  # StoryParser config section

DreamGenClone.Tests/
└── StoryParser/
  ├── ParsingParityTests.cs
  ├── DeterminismTests.cs
  ├── ErrorHandlingTests.cs
  └── CatalogPersistenceTests.cs
```

**Structure Decision**: Preserve existing layered architecture and place StoryParser contracts in Domain/Application, implementation in Infrastructure/Web, and verification in DreamGenClone.Tests. This satisfies modular boundary and dependency-direction requirements in the constitution.

## Phase 0: Research and Decisions

Research output is documented in `research.md` and resolves all technical unknowns for parser library, pagination strategy, persistence shape, and catalog query strategy.

## Phase 1: Design and Contracts

Design output is documented in:
- `data-model.md` (entities, relationships, validation, state flow)
- `contracts/storyparser-service-contract.md` (service contract and DTO rules)
- `quickstart.md` (developer workflow for running, validating, and troubleshooting)

## Post-Design Constitution Check

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
- [x] Module boundaries and adapter seams are explicit and swappable
- [x] .NET layered architecture uses separate projects with enforced dependency direction
- [x] Deterministic state transitions and JSON contract validation are test-covered
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes

Post-design constitution re-check result: PASS (no violations introduced).

## Complexity Tracking

No constitution violations or exceptional complexity justifications required.
