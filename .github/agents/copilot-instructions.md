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
- 004-model-manager: Added C# / .NET 9.0 + ASP.NET Core Blazor Server, Microsoft.Data.Sqlite, Serilog, System.Security.Cryptography.ProtectedData
- 003-story-summarize-analyze: Added C# / .NET 9 + Microsoft.Data.Sqlite (existing), Blazor Server (existing), LM Studio via OpenAI-compatible HTTP client (existing ILmStudioClient)
- 001-storyparser-fetch-catalog: Added C# 13 on .NET 9 (`net9.0`) + ASP.NET Core Blazor Server, Microsoft.Data.Sqlite, Serilog (AspNetCore + sinks), Microsoft.Extensions.Options, xUnit


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
