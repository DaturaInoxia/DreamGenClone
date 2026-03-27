# DreamGenClone Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-03-17

## Active Technologies
- C# / .NET 9 (ASP.NET Core Blazor Server) + ASP.NET Core Blazor components, Microsoft.Data.Sqlite, Serilog.AspNetCore + Serilog enrichers/sinks, Microsoft.Extensions.* abstractions (001-roleplay-continue-workspace)
- SQLite-backed session persistence via existing session abstractions; in-memory caches remain runtime optimization only (001-roleplay-continue-workspace)
- C# / .NET 9 (ASP.NET Core Blazor Server) + ASP.NET Core Blazor components, Microsoft.Data.Sqlite, Serilog.AspNetCore + Serilog enrichers/sinks, Microsoft.Extensions logging/configuration abstractions (001-roleplay-command-actions)
- SQLite persistence for sessions/interactions; no non-SQLite exception required for this feature (001-roleplay-command-actions)

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
- 001-roleplay-command-actions: Added C# / .NET 9 (ASP.NET Core Blazor Server) + ASP.NET Core Blazor components, Microsoft.Data.Sqlite, Serilog.AspNetCore + Serilog enrichers/sinks, Microsoft.Extensions logging/configuration abstractions
- 001-roleplay-continue-workspace: Added C# / .NET 9 (ASP.NET Core Blazor Server) + ASP.NET Core Blazor components, Microsoft.Data.Sqlite, Serilog.AspNetCore + Serilog enrichers/sinks, Microsoft.Extensions.* abstractions

- 001-roleplay-session-screens: Added C# / .NET 9 (`net9.0`) + ASP.NET Core Blazor Server, `Microsoft.Data.Sqlite`, `Microsoft.Extensions.*`, Serilog (`Serilog.AspNetCore`, `Serilog.Settings.Configuration`, sinks/enrichers)

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
