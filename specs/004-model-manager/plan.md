# Implementation Plan: Model Manager

**Branch**: `004-model-manager` | **Date**: 2026-04-03 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/004-model-manager/spec.md`

## Summary

Replace hardcoded appsettings.json model configuration with a runtime-configurable Model Manager supporting three OpenAI-compatible providers (LM Studio local, Together AI, OpenRouter), encrypted API key storage via DPAPI, per-provider timeout, and per-function default model assignments managed through a dedicated UI page. All existing services that perform generation calls will be refactored to use a model resolution service that resolves model + parameters from SQLite at call time with per-session override support.

## Technical Context

**Language/Version**: C# / .NET 9.0
**Primary Dependencies**: ASP.NET Core Blazor Server, Microsoft.Data.Sqlite, Serilog, System.Security.Cryptography.ProtectedData
**Storage**: SQLite (existing `data/dreamgenclone.db` via `ISqlitePersistence`)
**Testing**: xUnit + Coverlet
**Target Platform**: Windows (local single-user desktop)
**Project Type**: Web application (Blazor Server with interactive server rendering)
**Performance Goals**: Model resolution < 5ms (SQLite lookup), generation call timeout per provider (default 120s local, 30s cloud)
**Constraints**: DPAPI is Windows-only; API keys must never appear in git-tracked files; model changes must take effect without app restart
**Scale/Scope**: Single user, ~3 providers, ~10-20 registered models, 9 application function assignments

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
  - LM Studio local provider is seeded by default; cloud providers are optional additions
- [x] Module boundaries and adapter seams are explicit and swappable
  - New ICompletionClient replaces ILmStudioClient with provider-agnostic interface; IModelResolutionService and IApiKeyEncryptionService are explicit seams
- [x] .NET layered architecture uses separate projects with enforced dependency direction
  - Domain entities in Domain project, interfaces in Application project, implementations in Infrastructure project, UI/facades in Web project
- [x] Deterministic state transitions and JSON contract validation are test-covered
  - Model resolution fallback chain (session override → function default → error) is deterministic and testable
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
  - All Model Manager data in SQLite; per-session overrides remain in-memory (spec documents rationale: ephemeral by nature)
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
  - FR-037/038/038a/039 specify Serilog structured logging with defined properties
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
  - FR-038 covers provider ops, model ops, function default changes, and model resolution; FR-038a defines structured log fields
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes
  - FR-040 requires configurable log levels via settings

**Gate result: PASS** — No violations detected.

### Post-Design Re-evaluation (Phase 1 complete)

- [x] Local-first runtime preserved — LM Studio seeded by default; cloud providers optional. `CompletionClient` works with local-only setup.
- [x] Module boundaries explicit — `ICompletionClient`, `IModelResolutionService`, `IApiKeyEncryptionService`, 3 repository interfaces are clean seams in Application layer. Infrastructure provides implementations.
- [x] Layered architecture enforced — Domain entities (`Provider`, `RegisteredModel`, `FunctionModelDefault`, enums) have no infrastructure dependencies. Application interfaces have no infrastructure dependencies. Infrastructure implements Application contracts.
- [x] Deterministic state transitions — Model resolution chain is `session override → function default → error` with no ambiguity. `ResolvedModel` value object is immutable.
- [x] SQLite persistence — All 3 new tables in existing SQLite database. Per-session overrides documented as in-memory/ephemeral (spec assumption #5).
- [x] Serilog structured logging — Contracts specify structured properties (function, model, provider, override flag, duration).
- [x] Logging coverage — All CRUD operations, model resolution, generation calls, and errors have logging requirements in FR-037 through FR-040.
- [x] Configurable log levels — No changes to existing Serilog configuration mechanism.

**Post-design gate result: PASS**

## Project Structure

### Documentation (this feature)

```text
specs/004-model-manager/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── model-manager-contract.md
└── tasks.md             # Phase 2 output (via /speckit.tasks)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/
├── ModelManager/
│   ├── Provider.cs                    # Provider entity
│   ├── ProviderType.cs                # Enum: LmStudio, TogetherAI, OpenRouter
│   ├── RegisteredModel.cs             # RegisteredModel entity
│   ├── FunctionModelDefault.cs        # FunctionModelDefault entity
│   └── AppFunction.cs                 # Enum: 9 application functions

DreamGenClone.Application/
├── Abstractions/
│   ├── ICompletionClient.cs           # Replaces ILmStudioClient (OpenAI-compatible)
│   └── ILmStudioClient.cs            # [DEPRECATED → removed after migration]
├── ModelManager/
│   ├── IModelResolutionService.cs     # Resolves model per function with fallback chain
│   ├── IProviderRepository.cs         # Provider CRUD interface
│   ├── IRegisteredModelRepository.cs  # Model CRUD interface
│   ├── IFunctionDefaultRepository.cs  # Function default CRUD interface
│   └── IApiKeyEncryptionService.cs    # Encrypt/decrypt API keys

DreamGenClone.Infrastructure/
├── Models/
│   ├── CompletionClient.cs            # Replaces LmStudioClient (multi-provider)
│   └── LmStudioClient.cs             # [DEPRECATED → removed after migration]
├── ModelManager/
│   ├── ProviderRepository.cs          # SQLite CRUD for Providers table
│   ├── RegisteredModelRepository.cs   # SQLite CRUD for RegisteredModels table
│   ├── FunctionDefaultRepository.cs   # SQLite CRUD for FunctionModelDefaults table
│   ├── ApiKeyEncryptionService.cs     # DPAPI encryption implementation
│   └── ModelManagerMigration.cs       # Table creation + seed from appsettings
├── Configuration/
│   ├── LmStudioOptions.cs            # [Model field becomes seed-only]
│   ├── StoryAnalysisOptions.cs        # [Model field becomes seed-only]
│   └── ScenarioAdaptationOptions.cs   # [Model field becomes seed-only]
├── Persistence/
│   └── SqlitePersistence.cs           # [Add Model Manager table creation to InitializeAsync]

DreamGenClone.Web/
├── Application/
│   ├── ModelManager/
│   │   ├── ModelResolutionService.cs  # Resolves model for AppFunction with caching
│   │   ├── ModelManagerFacade.cs      # UI-facing orchestration for Model Manager page
│   │   └── ProviderTestService.cs     # Connection testing
│   ├── RolePlay/
│   │   └── RolePlayContinuationService.cs  # [Refactor: use IModelResolutionService]
│   ├── Story/
│   │   └── StoryEngineService.cs            # [Refactor: use IModelResolutionService]
│   ├── Assistants/
│   │   ├── WritingAssistantService.cs       # [Refactor: use IModelResolutionService]
│   │   └── RolePlayAssistantService.cs      # [Refactor: use IModelResolutionService]
│   ├── Scenarios/
│   │   └── ScenarioAdaptationService.cs     # [Refactor: use IModelResolutionService]
│   └── Models/
│       └── ModelSettingsService.cs          # [Refactor: source dropdown from DB]
├── Components/
│   ├── Pages/
│   │   └── ModelManager.razor               # New: dedicated Model Manager page
│   ├── Shared/
│   │   └── ModelSettingsPanel.razor          # [Refactor: dropdown from registered models]
│   └── Layout/
│       └── NavMenu.razor                    # [Add Model Manager nav entry]

DreamGenClone.Infrastructure/
├── StoryAnalysis/
│   ├── StoryAnalysisService.cs        # [Refactor: use IModelResolutionService]
│   ├── StorySummaryService.cs         # [Refactor: use IModelResolutionService]
│   └── StoryRankingService.cs         # [Refactor: use IModelResolutionService]

DreamGenClone.Tests/
├── ModelManager/
│   ├── ModelResolutionServiceTests.cs
│   ├── CompletionClientTests.cs
│   ├── ApiKeyEncryptionServiceTests.cs
│   ├── ProviderRepositoryTests.cs
│   ├── ModelManagerMigrationTests.cs
│   └── ProviderTestServiceTests.cs
```

**Structure Decision**: Follows existing layered architecture pattern. Domain entities in `DreamGenClone.Domain/ModelManager/`, application interfaces in `DreamGenClone.Application/ModelManager/`, infrastructure implementations in `DreamGenClone.Infrastructure/ModelManager/`, and UI facade + page in `DreamGenClone.Web/`. No new projects needed — uses existing project structure with new subdirectories.

## Complexity Tracking

No constitution violations requiring justification.
