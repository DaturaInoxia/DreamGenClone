# Quickstart: OtherMan Seduction Archetype

**Branch**: `066-otherman-seduction` | **Date**: 2026-08-11

How to build, run, test, and verify the OtherMan Seduction Archetype feature.

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

**Database**: No migration needed. The `SeductionArchetypes` property is a JSON array within the existing scenario character blob — existing scenarios deserialize with an empty list automatically.

---

## Test

```powershell
# Run the full RP test suite
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~RolePlay"

# Run only the new archetype catalog tests
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~SeductionArchetypeCatalog"

# Run character data slot tests (includes archetype injection)
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~CharacterDataSlot"
```

**Test files**:
- `DreamGenClone.Tests/StoryAnalysis/SeductionArchetypeCatalogTests.cs` — all 8 entries present, Get() lookup, BuildGuidance() composition, empty/null inputs, unrecognized IDs.
- `DreamGenClone.Tests/RolePlay/Prompts/CharacterDataSlotTests.cs` — existing tests extended with: OtherMan + archetypes emits guidance, OtherMan + empty archetypes falls back, non-OtherMan + archetypes does NOT emit guidance.

**Known issue**: `dotnet test` fails while the web app is running because `DreamGenClone.Web/bin/Debug/net9.0` DLLs are locked. Stop the app before running tests.

---

## Verify

### 1. Verify catalog has exactly 8 entries

```powershell
# Run a quick inline check via dbquery
dotnet run --project artifacts/tmp/dbquery -- sql - <<EOF
-- This verifies the catalog at build time; run the unit test instead
SELECT 'Run: dotnet test --filter SeductionArchetypeCatalog';
EOF
```

Better:

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~SeductionArchetypeCatalogTests.AllEightEntries"
```

### 2. Verify SteerRoleIntentCatalog OtherMan TOWARDS is updated

```powershell
# Use dbquery to read the catalog text (if exposed) or inspect the source:
findstr /C:"OtherMan" /C:"Towards" DreamGenClone.Domain/StoryAnalysis/SteerRoleIntentCatalog.cs
```

### 3. Verify CharacterDataSlot emits archetype guidance

Create a scenario with an OtherMan character, assign archetypes via the scenario editor (P3) or directly in the database JSON, then start a session. Check the built prompt via the Prompt Viewer tab or debug events:

```powershell
# Use the rp-session-debug skill to inspect a session's prompt
# Follow the skill instructions at .github/skills/rp-session-debug/SKILL.md
```

### 4. Verify no archetype guidance for non-OtherMan characters

Configure archetypes on a Husband or Wife character. Start a session. Verify the prompt does NOT contain "Seduction style:" for those characters. Only OtherMan characters receive archetype injection.

### 5. Verify empty archetypes = fallback only

Create a scenario with an OtherMan character and no archetypes configured. Start a session. Verify the prompt contains the role-level intent from `SteerRoleIntentCatalog` but NO "Seduction style:" line.

---

## Implementation Order (Priority Tiers)

### P1: Catalog + Character Data Model + Updated Fallback

Files to create/modify:
1. **NEW** `DreamGenClone.Domain/StoryAnalysis/SeductionArchetypeCatalog.cs` — 8 archetype records + static class
2. **MODIFY** `DreamGenClone.Web/Domain/Scenarios/Character.cs` — add `SeductionArchetypes` property
3. **MODIFY** `DreamGenClone.Domain/StoryAnalysis/SteerRoleIntentCatalog.cs` — update OtherMan TOWARDS intent + GetRoleContext

### P2: Continuation Prompt Injection

Files to modify:
4. **MODIFY** `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/CharacterDataSlot.cs` — extend `AppendCharacterRoleIntents()`

### P3: Scenario Editor UI

Files to modify:
5. **MODIFY** `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor` — add archetype multi-select in character settings

### Tests (alongside each tier)

6. **NEW** `DreamGenClone.Tests/StoryAnalysis/SeductionArchetypeCatalogTests.cs`
7. **MODIFY** `DreamGenClone.Tests/RolePlay/Prompts/CharacterDataSlotTests.cs`
