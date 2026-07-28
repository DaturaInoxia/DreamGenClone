# B-065: Prompt Result Tester Page — Debug UI for Prompt Experimentation

**State**: `implemented` (v1), `designed` (v2 persistence)
**Priority**: `medium`
**Scope**: `small` → `medium` (v2 adds DB persistence)
**Backlog**: `specs/Planning/backlog.md#B-065`

---

## TL;DR

A new debug/diagnostic page at `/prompt-tester` in the **System** nav group. Allows selecting any enabled model from the model manager, typing a system message + user prompt, executing it against the LLM, and viewing the raw result — without running a full RP session. Purpose: tweak prompt phrasing, injection wording, and test model behaviour in isolation.

---

## Design

### 1. Page: `PromptTester.razor`

- **Route**: `@page "/prompt-tester"`
- **Render mode**: `InteractiveServer` (same as ModelManager, Administration)
- **Location**: `DreamGenClone.Web/Components/Pages/PromptTester.razor`
- **Code-behind**: `PromptTester.razor.cs` (standard pattern for this repo)

### 2. UI Layout

```
┌──────────────────────────────────────────────────────────┐
│  Prompt Tester                                           │
│  ┌────────────────────────────────────────────────────┐  │
│  │  Model                                              │  │
│  │  [Provider: Model ▼]                          [▼]  │  │
│  │  Temperature: [0.7]  TopP: [0.9]  MaxTokens: [500] │  │
│  ├────────────────────────────────────────────────────┤  │
│  │  System Message                                     │  │
│  │  ┌──────────────────────────────────────────────┐  │  │
│  │  │ (textarea, 4 rows)                           │  │  │
│  │  └──────────────────────────────────────────────┘  │  │
│  ├────────────────────────────────────────────────────┤  │
│  │  User Prompt                                        │  │
│  │  ┌──────────────────────────────────────────────┐  │  │
│  │  │ (textarea, 12 rows, monospace)               │  │  │
│  │  └──────────────────────────────────────────────┘  │  │
│  ├────────────────────────────────────────────────────┤  │
│  │  [Execute]  (spinner during request)                │  │
│  │  Elapsed: 1.23s   Tokens: N/A (if available)       │  │
│  ├────────────────────────────────────────────────────┤  │
│  │  Result                                             │  │
│  │  ┌──────────────────────────────────────────────┐  │  │
│  │  │ (scrollable <pre>, raw response text)        │  │  │
│  │  │                                              │  │  │
│  │  └──────────────────────────────────────────────┘  │  │
│  └────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────┘
```

### 3. Nav Menu Addition

Add a new nav item under the **System** group in `NavMenu.razor`:

```razor
<div class="nav-item px-3">
    <NavLink class="nav-link" href="prompt-tester">
        <span class="bi bi-terminal-nav-menu" aria-hidden="true"></span> Prompt Tester
    </NavLink>
</div>
```

Place it after the Administration link (or before — last in the System group).

### 4. Model Selection

- **Data source**: `ModelManagerFacade.GetAllModelsGroupedByProviderAsync()` — returns `Dictionary<Provider, List<RegisteredModel>>`
- **Filter**: Only show enabled providers and enabled models
- **UI**: A single `<select>` (or grouped dropdown) showing `{ProviderName} / {ModelDisplayName}`
- **Default**: First enabled model, or a "Select a model..." placeholder

### 5. Parameter Overrides

Three numeric inputs with sensible defaults:

| Parameter | Default | Min | Max | Step |
|-----------|---------|-----|-----|------|
| Temperature | 0.7 | 0.0 | 2.0 | 0.05 |
| TopP | 0.9 | 0.0 | 1.0 | 0.05 |
| MaxTokens | 500 | 1 | 8192 | 50 |

These override the selected model's function defaults since we're not resolving via `AppFunction`.

### 6. Execution

#### Dependencies (injected into the page)

- `ModelManagerFacade` — for listing models/providers
- `ICompletionClient` — for sending the prompt
- `IApiKeyEncryptionService` — for decrypting the provider's API key (part of building ResolvedModel)

#### Flow

1. User selects a model from the dropdown
2. User types system message (optional) and user prompt (required)
3. User optionally adjusts Temperature / TopP / MaxTokens
4. User clicks **Execute**
5. Page builds a `ResolvedModel` from the selected `RegisteredModel` + its `Provider`:
   ```
   ResolvedModel(
       ProviderBaseUrl: provider.BaseUrl,
       ChatCompletionsPath: provider.ChatCompletionsPath,
       ProviderTimeoutSeconds: provider.TimeoutSeconds,
       ApiKeyEncrypted: provider.ApiKeyEncrypted,
       ModelIdentifier: model.ModelIdentifier,
       Temperature: userOverride ?? 0.7,
       TopP: userOverride ?? 0.9,
       MaxTokens: userOverride ?? 500,
       ProviderName: provider.Name,
       IsSessionOverride: false)
   ```
6. Call `ICompletionClient.GenerateAsync(systemMessage, userMessage, resolvedModel, cancellationToken)`
7. If system message is empty/whitespace, call `ICompletionClient.GenerateAsync(userMessage, resolvedModel, cancellationToken)` (single-message overload)
8. Display the raw response text in a scrollable `<pre>` block
9. Show elapsed time

### 7. Error Handling

| Scenario | Behaviour |
|----------|-----------|
| No model selected | Disable Execute button, show hint |
| No prompt text | Disable Execute button, show hint |
| Provider disabled | Show error alert |
| API key decryption fails | Show error alert with "re-enter API key" message |
| HTTP/network error | Show error alert with message |
| Timeout | Show error alert with elapsed time |

### 8. What This Is NOT

- **NOT** a streaming display (B-026 already handles streaming for RP). Full response returned synchronously.
- **NOT** a prompt builder — no injection pipeline, no RP context. Raw prompt only.
- **NOT** persisted — no prompt history, no saved results. Fire-and-forget.
- **NOT** exposed to end users — System nav group is developer/diagnostic territory.

---

## V2: Persist & Recall Test Runs (planned)

Save prompt test runs to the DB so users can track how prompt/injection changes affect model output over time. Save is a manual button action — never automatic.

### Domain Model: `PromptTestRun`

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `string` | GUID primary key |
| `Comment` | `string?` | User-provided label/note (e.g. "added pacing directive, removed scene deepening") |
| `ModelIdentifier` | `string` | The model ID sent to the API (e.g. "dolphin-3.0-llama-3.1-8b") |
| `ModelDisplayName` | `string` | Registered model display name |
| `ProviderName` | `string` | Provider display name |
| `SystemMessage` | `string?` | System prompt text (null if unused) |
| `UserPrompt` | `string` | User prompt text |
| `Temperature` | `double` | |
| `TopP` | `double` | |
| `MaxTokens` | `int` | |
| `ResultText` | `string?` | Response from the model (null if error) |
| `ResultError` | `string?` | Error message (null if success) |
| `ElapsedSeconds` | `double` | Execution duration |
| `PromptCharCount` | `int` | Total character count of prompt sent (system + user) |
| `ResultWordCount` | `int` | Word count of model response (0 if error) |
| `ResultCharCount` | `int` | Character count of model response (0 if error) |
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

Added in `SqlitePersistence.InitializeAsync()` alongside other CREATE TABLE statements.

### Repository: `IPromptTestRunRepository` / `PromptTestRunRepository`

```csharp
// Interface (DreamGenClone.Application/PromptTester/IPromptTestRunRepository.cs)
public interface IPromptTestRunRepository
{
    Task SaveAsync(PromptTestRun run, CancellationToken cancellationToken = default);
    Task<List<PromptTestRun>> GetAllAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<PromptTestRun?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

// Implementation (DreamGenClone.Infrastructure/PromptTester/PromptTestRunRepository.cs)
// Follows HealthCheckRepository pattern: SqliteConnection from PersistenceOptions
```

### DI Registration

```csharp
// DreamGenClone.Web/Program.cs
builder.Services.AddScoped<IPromptTestRunRepository, PromptTestRunRepository>();
```

### Domain class location

`DreamGenClone.Domain/PromptTester/PromptTestRun.cs` — plain POCO with get/set.

### UI Updates to `PromptTester.razor`

#### After Execute

When a result is present (success or error), show these below the result card:

```
┌──────────────────────────────────────────────────────┐
│  Comment (optional)                                   │
│  ┌──────────────────────────────────────────────┐    │
│  │ (text input, single line)                     │    │
│  └──────────────────────────────────────────────┘    │
│                                                       │
│  [Save Run]   (saves current model + prompt + result) │
│  Saved ✓ (flash confirmation)                         │
└──────────────────────────────────────────────────────┘
```

- **Comment** input appears after result, single-line text
- **Save Run** button calls `IPromptTestRunRepository.SaveAsync()`
- On success: brief "Saved" flash, button disabled until next execution
- On failure: error alert

#### Previous Runs sidebar/panel

A collapsible section on the right side (or below the main form on narrow screens):

```
┌────────────────────────────────────────────┐
│  Previous Runs            [↻ refresh]      │
│  ┌──────────────────────────────────────┐  │
│  │ 7/26 22:30  Dolphin3.0-8B            │  │
│  │ "added pacing directive"             │  │
│  │ [Load]  [Delete]                     │  │
│  ├──────────────────────────────────────┤  │
│  │ 7/26 22:15  qwen2.5-14b              │  │
│  │ "baseline test"                      │  │
│  │ [Load]  [Delete]                     │  │
│  └──────────────────────────────────────┘  │
└────────────────────────────────────────────┘
```

- Listed newest-first, last 50 runs
- Each row shows: date/time, model name, comment preview
- **Load**: populates model selector, system message, user prompt, temperature/TopP/maxTokens from the saved run. Also shows the saved result below.
- **Delete**: removes the run with confirmation

#### Load behavior

When a previous run is loaded:
- All form fields populate from the saved run
- Result area shows the saved result text (or error)
- "Loaded from run {date}: {comment}" indicator shown
- Comment field is NOT pre-filled (user can add a new comment when re-saving)

### Scope change: `small` → `medium`

V2 adds proper domain model, DB table, repository interface + impl, DI wiring, and significant UI additions. Estimated ~6 files changed/created vs. 3 in v1.

---

## Files Changed (V2)

| # | File | Change |
|---|------|--------|
| 1 | `DreamGenClone.Domain/PromptTester/PromptTestRun.cs` | **NEW** — domain model |
| 2 | `DreamGenClone.Application/PromptTester/IPromptTestRunRepository.cs` | **NEW** — repository interface |
| 3 | `DreamGenClone.Infrastructure/PromptTester/PromptTestRunRepository.cs` | **NEW** — SQLite repository |
| 4 | `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | Add CREATE TABLE |
| 5 | `DreamGenClone.Web/Components/Pages/PromptTester.razor` | Add save, comment, previous runs list |
| 6 | `DreamGenClone.Web/Program.cs` | Add DI registration |

---

## Task Breakdown (V2)

| # | Task | Description |
|---|------|-------------|
| T4 | Create `PromptTestRun` domain model | POCO in `DreamGenClone.Domain/PromptTester/` |
| T5 | Create `IPromptTestRunRepository` interface | In `DreamGenClone.Application/PromptTester/` |
| T6 | Create `PromptTestRunRepository` implementation | SQLite CRUD in `DreamGenClone.Infrastructure/PromptTester/` |
| T7 | Add `PromptTestRuns` table | CREATE TABLE in `SqlitePersistence.InitializeAsync()` |
| T8 | Add DI registration | `AddScoped<IPromptTestRunRepository, PromptTestRunRepository>` in Program.cs |
| T9 | Add comment input + Save button to UI | After result card |
| T10 | Add Previous Runs list to UI | Sidebar/panel with Load + Delete |
| T11 | Build verification | `dotnet build` — 0 errors |

---

## Verification (V2)

1. Execute a prompt → result appears
2. Type a comment, click Save Run → "Saved" confirmation shown
3. Previous Runs list shows the saved run with correct model/comment/date
4. Click Load on a previous run → form populates, saved result is shown
5. Click Delete → confirmation prompt → run removed from list
6. Build: 0 errors, 0 new warnings

---

## Files Changed

| # | File | Change |
|---|------|--------|
| 1 | `DreamGenClone.Web/Components/Pages/PromptTester.razor` | **NEW** — page markup |
| 2 | `DreamGenClone.Web/Components/Pages/PromptTester.razor.cs` | **NEW** — code-behind |
| 3 | `DreamGenClone.Web/Components/Layout/NavMenu.razor` | Add nav link under System group |

No domain model changes. No DB changes. No new services. Pure UI + existing DI.

---

## Task Breakdown

| # | Task | Description |
|---|------|-------------|
| T1 | Create `PromptTester.razor` + `.razor.cs` | Page with model selector, prompt textareas, parameter inputs, execute button, result display |
| T2 | Add nav link in `NavMenu.razor` | One line: `<NavLink href="prompt-tester">Prompt Tester</NavLink>` under System group |
| T3 | Build verification | `dotnet build DreamGenClone.Web/DreamGenClone.csproj` — 0 errors |

---

## Verification

1. Navigate to `/prompt-tester` — page loads with model dropdown populated
2. Select a model, type a prompt, click Execute — raw response appears
3. Empty prompt → Execute disabled
4. No model selected → Execute disabled
5. System nav group shows "Prompt Tester" link, navigates correctly
6. Build: 0 errors, 0 new warnings
