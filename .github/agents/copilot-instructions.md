# DreamGenClone Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-03-17

## Active Technologies
- C# / .NET 9 (ASP.NET Core Blazor Server) + ASP.NET Core Blazor components, Microsoft.Data.Sqlite, Serilog.AspNetCore + Serilog enrichers/sinks, Microsoft.Extensions.* abstractions (001-roleplay-continue-workspace)
- SQLite-backed session persistence via existing session abstractions; in-memory caches remain runtime optimization only (001-roleplay-continue-workspace)
- C# / .NET 9 (ASP.NET Core Blazor Server) + ASP.NET Core Blazor components, Microsoft.Data.Sqlite, Serilog.AspNetCore + Serilog enrichers/sinks, Microsoft.Extensions logging/configuration abstractions (001-roleplay-command-actions)
- SQLite persistence for sessions/interactions; no non-SQLite exception required for this feature (001-roleplay-command-actions)
- C# 13 on .NET 9 (`net9.0`) + ASP.NET Core Blazor Server, Microsoft.Data.Sqlite, Serilog (AspNetCore + sinks), Microsoft.Extensions.Options, xUnit (001-storyparser-fetch-catalog)
- SQLite (default required by constitution and spec) (001-storyparser-fetch-catalog)
- C# / .NET 9 + Microsoft.Data.Sqlite (existing), Blazor Server (existing), LM Studio via OpenAI-compatible HTTP client (existing ILmStudioClient) (003-story-summarize-analyze)
- SQLite (existing `data/dreamgenclone.db`) — three new tables: StorySummaries, StoryAnalyses, RankingCriteria, StoryRankings (003-story-summarize-analyze)
- C# / .NET 9.0 + ASP.NET Core Blazor Server, Microsoft.Data.Sqlite, Serilog, System.Security.Cryptography.ProtectedData (004-model-manager)
- SQLite (existing `data/dreamgenclone.db` via `ISqlitePersistence`) (004-model-manager)
- C# / .NET 9.0 + Blazor Server (interactive SSR), Microsoft.Data.Sqlite, Serilog.AspNetCore, System.Text.Json (001-roleplay-interaction-commands)
- SQLite (sessions stored as JSON payloads via `SqlitePersistence`) (001-roleplay-interaction-commands)
- C# / .NET 9 / Blazor Server + Microsoft.Data.Sqlite, Serilog, System.Text.Json (005-adaptive-engine-redesign)
- SQLite (via SqlitePersistence.cs — single file, direct ADO.NET, no ORM) (005-adaptive-engine-redesign)
- C# / .NET 9 (net9.0) + Microsoft.Data.Sqlite 9.0.0, Microsoft.Extensions.Logging.Abstractions 9.0.0, Serilog.AspNetCore 9.0.0, Serilog.Settings.Configuration 9.0.0, Serilog.Sinks.Console 6.0.0, Serilog.Sinks.File 6.0.0 (002-adaptive-scenario-redesign2)
- SQLite via existing persistence abstractions and JSON-serialized session state payloads (002-adaptive-scenario-redesign2)
- C# / .NET 9 / Blazor Server + Microsoft.Data.Sqlite 9.x, Serilog.AspNetCore 9.x, System.Text.Json, ASP.NET Core DI/logging abstractions (001-roleplay-v2-unification)
- SQLite for persisted feature data; JSON payload fields for complex nested structures in existing persistence patterns (001-roleplay-v2-unification)
- SQLite via raw ADO.NET (`SqlitePersistence.cs`) — single file, no ORM (006-explicit-scene-writing)
- C# 13 / .NET 9 + Microsoft.Data.Sqlite, Blazor Server, Serilog (007-finishing-move-catalog)
- SQLite (project default; no exception) (007-finishing-move-catalog)
- C# / .NET 9 / Blazor Server + Microsoft.Data.Sqlite, System.Text.Json, Serilog, ASP.NET Core DI/logging abstractions (007-theme-state-machine)
- SQLite (existing persistence stack) with new theme-machine definition and diagnostic persistence; adaptive-state row remains the per-session runtime anchor (007-theme-state-machine)
- [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION] + [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION] (001-physical-attributes)
- [if applicable, default SQLite unless explicitly overridden in spec; e.g., SQLite, session storage, local storage, PostgreSQL] (001-physical-attributes)
- C# 13 / .NET 9 + Blazor Server (InteractiveServer render mode), Serilog, System.Text.Json (001-physical-attributes)
- SQLite — existing `Templates.PayloadJson` and `Sessions.PayloadJson` columns; no new schema (001-physical-attributes)
- C# 13 / .NET 9 + ASP.NET Core Blazor, Entity Framework Core (SQLite), Serilog, xUnit (024-narrative-prompt-fix)
- SQLite (unchanged — no schema changes for this feature) (024-narrative-prompt-fix)
- C# / .NET 9 / Blazor Server + ASP.NET Core DI/logging abstractions, Serilog, Microsoft.Data.Sqlite, System.Text.Json (001-semantic-telemetry-tests)
- SQLite via existing RolePlay persistence repositories and diagnostics tables (001-semantic-telemetry-tests)
- C# on .NET 9 + ASP.NET Core/Blazor host, existing RolePlay services, Serilog, SQLite persistence layer (001-semantic-telemetry-tests)
- SQLite (default policy, no new store) (001-semantic-telemetry-tests)
- C# / .NET 9 + Blazor Server, Microsoft.Data.Sqlite, Serilog, xUnit (development)
- SQLite (`DreamGenClone.Web/data/dreamgenclone.dev.db`) — all new tables and migrations in `SqlitePersistence.cs` (development)
- C# / .NET 9 / Blazor Server + System.Threading.Channels (existing), SemaphoreSlim (BCL), Serilog, Microsoft.Data.Sqlite (existing ADO.NET, no EF Core) (001-semantic-dedicated-model)
- SQLite via `FunctionModelDefaults` table — one `ALTER TABLE ... ADD COLUMN MaxConcurrentJobs INTEGER NULL` migration (001-semantic-dedicated-model)
- C# 13 / .NET 9 + Blazor Server (ASP.NET Core 9), `System.Collections.Concurrent`, `System.Threading`, Serilog (027-prompt-queue-continue)
- In-memory only (singleton `ConcurrentDictionary`); no SQLite for tracker state — exception documented in FR-015 with rationale (027-prompt-queue-continue)
- C# 12 / .NET 9 + Blazor Server (DreamGenClone.Web), SQLite via Microsoft.Data.Sqlite, Serilog, IOptions<T> configuration pattern, IBackgroundJobHandler infrastructure (001-session-memory-context)
- SQLite — new `RolePlayV2EncounterSummaries` table + additive `Sessions` column (001-session-memory-context)
- C# / .NET 9 + Blazor Server, SQLite (raw ADO.NET), xUnit, FluentAssertions, Serilog (001-stat-char-text-drift)
- SQLite — `DreamGenClone.Web/data/dreamgenclone.dev.db`; no schema change (new data serialises in existing `CharacterSnapshotsJson` JSON column) (001-stat-char-text-drift)
- C# / .NET 9 + ASP.NET Core, Microsoft.Data.Sqlite, Serilog, System.Text.Json (001-opening-period)
- SQLite (via `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`) (001-opening-period)

- C# / .NET 9 (`net9.0`) + ASP.NET Core Blazor Server, `Microsoft.Data.Sqlite`, `Microsoft.Extensions.*`, Serilog (`Serilog.AspNetCore`, `Serilog.Settings.Configuration`, sinks/enrichers) (001-roleplay-session-screens)

## Project Structure

```text
backend/
frontend/
tests/
```

## Commands

# Add commands for C# / .NET 9 (`net9.0`)

## Code Style

C# / .NET 9 (`net9.0`): Follow standard conventions

## Recent Changes
- 001-opening-period: Added C# / .NET 9 + ASP.NET Core, Microsoft.Data.Sqlite, Serilog, System.Text.Json
- 001-fix-climax-timeskip: Added C# / .NET 9 + Blazor Server, Microsoft.Data.Sqlite, Serilog
- 001-stat-char-text-drift: Added C# / .NET 9 + Blazor Server, SQLite (raw ADO.NET), xUnit, FluentAssertions, Serilog


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
