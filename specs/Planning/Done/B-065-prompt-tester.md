# B-065: Prompt Result Tester Page — Debug UI for Prompt Experimentation

**State**: `implemented` (v1), `designed` (v2 persistence)
**Priority**: `medium`
**Scope**: `small` → `medium` (v2 adds DB persistence)
**Backlog**: `specs/Planning/backlog.md#B-065`

---

## TL;DR

A new debug/diagnostic page at `/prompt-tester` in the **System** nav group. V1 implemented: model selector, prompt textareas, execute, raw result. V2 planned: persist runs to DB with comment, list/load/delete previous runs.

---

## V2: Persist & Recall Test Runs

Save prompt test runs to the DB so users can track how prompt/injection changes affect model output over time. Save is a manual button action — never automatic.

### Domain Model: `PromptTestRun`

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `string` | GUID primary key |
| `Comment` | `string?` | User-provided label/note |
| `ModelIdentifier` | `string` | Model ID sent to API |
| `ModelDisplayName` | `string` | Registered model display name |
| `ProviderName` | `string` | Provider display name |
| `SystemMessage` | `string?` | System prompt (null if unused) |
| `UserPrompt` | `string` | User prompt text |
| `Temperature` | `double` | |
| `TopP` | `double` | |
| `MaxTokens` | `int` | |
| `ResultText` | `string?` | Response (null if error) |
| `ResultError` | `string?` | Error message (null if success) |
| `ElapsedSeconds` | `double` | Execution duration |
| `PromptCharCount` | `int` | Total character count of prompt sent |
| `ResultWordCount` | `int` | Word count of model response |
| `ResultCharCount` | `int` | Character count of model response |
| `CreatedUtc` | `string` | ISO 8601 timestamp |

### DB Table

```sql
CREATE TABLE IF NOT EXISTS PromptTestRuns (
    Id TEXT PRIMARY KEY NOT NULL,
    Comment TEXT NULL,
    ModelIdentifier TEXT NOT NULL,
    ModelDisplayName TEXT NOT NULL,
    ProviderName TEXT NOT NULL,
    SystemMessage TEXT NULL,
    UserPrompt TEXT NOT NULL,
    Temperature REAL NOT NULL DEFAULT 0.7,
    TopP REAL NOT NULL DEFAULT 0.9,
    MaxTokens INTEGER NOT NULL DEFAULT 500,
    ResultText TEXT NULL,
    ResultError TEXT NULL,
    PromptCharCount INTEGER NOT NULL DEFAULT 0,
    ResultWordCount INTEGER NOT NULL DEFAULT 0,
    ResultCharCount INTEGER NOT NULL DEFAULT 0,
    ElapsedSeconds REAL NOT NULL DEFAULT 0,
    CreatedUtc TEXT NOT NULL
);
```

### Repository: `IPromptTestRunRepository` / `PromptTestRunRepository`

```csharp
public interface IPromptTestRunRepository
{
    Task SaveAsync(PromptTestRun run, CancellationToken cancellationToken = default);
    Task<List<PromptTestRun>> GetAllAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<PromptTestRun?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
```

### UI: Save after result

```
┌──────────────────────────────────────────────────────┐
│  Comment (optional)                                   │
│  ┌──────────────────────────────────────────────┐    │
│  │ (text input, single line)                     │    │
│  └──────────────────────────────────────────────┘    │
│  [Save Run]                                           │
└──────────────────────────────────────────────────────┘
```

### UI: Previous Runs list

- Collapsible panel, newest-first, last 50 runs
- Each row: date/time, model name, comment preview, [Load] [Delete]
- Load: populates form + shows saved result. Comment NOT pre-filled.
- Delete: with confirmation

### Files Changed (V2)

| # | File | Change |
|---|------|--------|
| 1 | `DreamGenClone.Domain/PromptTester/PromptTestRun.cs` | **NEW** — domain model |
| 2 | `DreamGenClone.Application/PromptTester/IPromptTestRunRepository.cs` | **NEW** — interface |
| 3 | `DreamGenClone.Infrastructure/PromptTester/PromptTestRunRepository.cs` | **NEW** — SQLite impl |
| 4 | `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | Add CREATE TABLE |
| 5 | `DreamGenClone.Web/Components/Pages/PromptTester.razor` | Save + previous runs UI |
| 6 | `DreamGenClone.Web/Program.cs` | DI registration |

### Task Breakdown (V2)

| # | Task | Description |
|---|------|-------------|
| T4 | Domain model | `PromptTestRun.cs` POCO |
| T5 | Repository interface | `IPromptTestRunRepository.cs` |
| T6 | Repository impl | `PromptTestRunRepository.cs` SQLite CRUD |
| T7 | DB table | CREATE TABLE in `SqlitePersistence` |
| T8 | DI wiring | AddScoped in Program.cs |
| T9 | Save UI | Comment input + Save button |
| T10 | List UI | Previous Runs panel with Load/Delete |
| T11 | Build verify | 0 errors, 0 new warnings |
