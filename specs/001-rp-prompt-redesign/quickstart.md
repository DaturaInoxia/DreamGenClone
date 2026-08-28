# Quickstart: RP Prompt Redesign

**Branch**: `001-rp-prompt-redesign` | **Date**: 2026-07-17

How to build, run, test, and verify the RP Prompt Redesign feature.

---

## Prerequisites

- .NET 9 SDK
- Windows (local-first runtime per Constitution I)
- SQLite (bundled via `Microsoft.Data.Sqlite`)
- Existing dev database at `DreamGenClone.Web/data/dreamgenclone.dev.db`

---

## Build

```powershell
# From repo root — build the full solution
dotnet build DreamGenClone.sln

# Or build just the Web project (faster iteration)
dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore

# Or build just the Tests project
dotnet build DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore
```

---

## Run

```powershell
# Start the web app (Blazor Server) in development mode
dotnet run --project DreamGenClone.Web/DreamGenClone.csproj

# Or use the helper script
powershell -ExecutionPolicy RemoteSigned -File helpers/start-webapp-dev.ps1
```

The app starts on `https://localhost:5xxx` (check launchSettings.json for the exact port).

**First-run migration**: On startup, `SqlitePersistence.InitializeAsync` runs idempotent `ALTER TABLE` migrations to add the new `Sessions` columns (`MaxPromptChars`, `ContextWindowTurns`, `ScenarioCompressionTurnThreshold`, `HistoryFullDetailTurnBand`, `HistoryNarrativeOnlyTurnBand`, `SessionMemoryLongTermTurnThreshold`) and create the `PhaseRuleOfThumb` table seeded with 6 phase rows. Re-runs are no-ops.

---

## Test

```powershell
# Run the full RP test suite
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~RolePlay"

# Run only the new prompt slot tests
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~RolePlay.Prompts"

# Run a specific slot contract test
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~SceneAnchorSlot"
```

**Test files** (in `DreamGenClone.Tests/RolePlay/Prompts/`):
- `SlotContractTests.cs` — one test per slot (FR-036, SC-008)
- `PromptBuilderTests.cs` — end-to-end build, budget, dedup, ordering
- `ActorProfileResolverTests.cs` — 5 profiles × variant matrix
- `PromptBudgetEnforcerTests.cs` — trim priority, never-trim invariants, fail-fast
- `EncounterEnrichmentPromptTests.cs` — 6-dimension capture (SC-009)
- `LegacyRemovalTests.cs` — asserts no residual `BuildPromptAsync` code path (SC-010)

**Known issue**: `dotnet test` fails while the web app is running because `DreamGenClone.Web/bin/Debug/net9.0` DLLs are locked. Stop the app before running tests.

---

## Verify

### 1. Verify the 17-slot architecture is wired

```powershell
# Check that all 17 slots are registered in Program.cs
findstr /R "AddScoped<IPromptSlot" DreamGenClone.Web/Program.cs
```

Expect 17 lines (plus WorldState conditional registration).

### 2. Verify no residual legacy path

```powershell
# The old BuildPromptAsync method should be gone from RolePlayContinuationService
findstr /R "private async Task<string> BuildPromptAsync" DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs
```

Expect no matches (SC-010).

### 3. Verify fail-fast on missing MaxPromptChars

Use the dbquery tool to inspect a session row:

```powershell
dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/dbquery/queries/check_session_maxpromptchars.sql <session-id>
```

If `MaxPromptChars` is NULL for a session, continuing that session should throw an explicit diagnostic (FR-004).

### 4. Verify prompt size reduction

Start a session, advance 10+ turns, and capture the prompt via the debug event sink:

```powershell
dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/dbquery/queries/find_session_prompt.sql <session-id>
```

Verify `promptLength` is <= 35,000 (SC-006) and reduced by >= 30% vs. the ~50,000 baseline (SC-001).

### 5. Verify deduplication

Inspect the captured prompt text and search for duplicate content categories:

```powershell
# Theme contract should appear exactly once (SC-002)
Select-String -Path <prompt-file> -Pattern "Theme Contract" | Measure-Object
```

Expect count = 1 for: theme contract, behavioral frames, turn context, intensity directives, final instruction (FR-027, SC-002).

### 6. Verify Narrative variant has no POV persona

```powershell
# Narrative prompt should contain zero "POV Persona" text (SC-004)
Select-String -Path <narrative-prompt-file> -Pattern "POV Persona" | Measure-Object
```

Expect count = 0.

---

## DB Query Tool

For SQLite inspections against `DreamGenClone.Web/data/dreamgenclone.dev.db`:

```powershell
# Run a named SQL file
dotnet run --project artifacts/tmp/dbquery -- sql <query.sql> [id]

# Inspect a session
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq-session.ps1 -SessionId <session-id>
```

Full schema and commands: `.github/instructions/dbquery-reference.instructions.md`.

---

## Key Files

| File | Purpose |
|------|---------|
| `DreamGenClone.Web/Application/RolePlay/Prompts/RolePlayPromptBuilder.cs` | New builder orchestrating 17 slots |
| `DreamGenClone.Web/Application/RolePlay/Prompts/IPromptSlot.cs` | Slot contract |
| `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs` | Immutable context record |
| `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/*.cs` | 17 slot implementations |
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | Refactored — delegates to builder |
| `DreamGenClone.Web/Application/RolePlay/EncounterSummaryJobHandler.cs` | Rewritten enrichment prompt |
| `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs` | New config properties |
| `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | Migrations + PhaseRuleOfThumb table |
| `DreamGenClone.Web/Program.cs` | DI registration for slots + builder |
