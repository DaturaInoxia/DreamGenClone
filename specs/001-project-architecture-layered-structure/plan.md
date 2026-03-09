# Implementation Plan: DreamGenClone Architecture Foundation

## Context
- Feature: `001-project-architecture-layered-structure`
- Spec: `specs/001-project-architecture-layered-structure/spec.md`
- Tasks: `specs/001-project-architecture-layered-structure/tasks.md`

## Technical Summary
- Platform: .NET 9, Blazor Server
- Architecture: Layered (UI -> Application -> Domain), Infrastructure integrated through DI
- Persistence: SQLite default for persisted records
- Binary storage: Local disk for template images, with metadata/path references in SQLite
- Logging: Serilog with structured logging and externally configurable levels
- AI integration: LM Studio local endpoint (`http://127.0.0.1:1234`)

## Constitution Alignment
- SQLite is the default persistence provider.
- Serilog is required for logging and configured from app settings.
- Major execution paths log at Information or above as appropriate.
- Layer boundaries are represented with separate projects and enforced dependency direction.

## Phase 2 Execution Scope
This plan covers foundational tasks `T006` through `T014`:
- DI composition root and service registration
- LM Studio client abstraction and implementation
- SQLite bootstrap and schema initialization
- Template image storage service
- Strict JSON import validation
- Serilog setup and startup wiring
- Domain-level contracts for session/story/role-play operations
- Debounced autosave coordinator

## File Plan
- `DreamGenClone.Web/Program.cs`
- `DreamGenClone.Infrastructure/Configuration/LmStudioOptions.cs`
- `DreamGenClone.Infrastructure/Configuration/PersistenceOptions.cs`
- `DreamGenClone.Infrastructure/Models/ILmStudioClient.cs`
- `DreamGenClone.Infrastructure/Models/LmStudioClient.cs`
- `DreamGenClone.Infrastructure/Persistence/ISqlitePersistence.cs`
- `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
- `DreamGenClone.Infrastructure/Storage/ITemplateImageStorageService.cs`
- `DreamGenClone.Infrastructure/Storage/TemplateImageStorageService.cs`
- `DreamGenClone.Application/Validation/SessionImportValidator.cs`
- `DreamGenClone.Infrastructure/Logging/LoggingSetup.cs`
- `DreamGenClone.Domain/Contracts/ISessionService.cs`
- `DreamGenClone.Domain/Contracts/IStoryWorkflowService.cs`
- `DreamGenClone.Domain/Contracts/IRolePlayWorkflowService.cs`
- `DreamGenClone.Application/Sessions/IAutoSaveCoordinator.cs`
- `DreamGenClone.Application/Sessions/AutoSaveCoordinator.cs`

## Validation Plan
- Build command: `dotnet build DreamGenClone.sln`
- Runtime checks:
  - SQLite file path is created on startup
  - Required schema tables are initialized
  - Serilog pipeline loads from configuration
  - LM Studio health probe path is callable through the client abstraction
