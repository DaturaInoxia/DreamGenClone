# Data Model: Model Manager

**Feature**: 004-model-manager | **Date**: 2026-04-03

## Entity Definitions

### Provider

Represents an AI inference endpoint configuration.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | TEXT (GUID) | PRIMARY KEY | Unique identifier |
| Name | TEXT | NOT NULL, UNIQUE | User-defined display name |
| ProviderType | INTEGER | NOT NULL | Enum: 0=LmStudio, 1=TogetherAI, 2=OpenRouter |
| BaseUrl | TEXT | NOT NULL | Provider endpoint URL (e.g., http://127.0.0.1:1234) |
| ChatCompletionsPath | TEXT | NOT NULL, DEFAULT '/v1/chat/completions' | API path appended to base URL |
| TimeoutSeconds | INTEGER | NOT NULL, DEFAULT 120 | Per-provider request timeout |
| ApiKeyEncrypted | TEXT | NULL | DPAPI-encrypted API key (Base64), NULL for LM Studio |
| IsEnabled | INTEGER | NOT NULL, DEFAULT 1 | 0=disabled, 1=enabled |
| CreatedUtc | TEXT | NOT NULL | ISO 8601 timestamp |
| UpdatedUtc | TEXT | NOT NULL | ISO 8601 timestamp |

**Validation Rules**:
- `Name` must be non-empty, max 100 characters
- `BaseUrl` must be a valid URI (http:// or https://)
- `TimeoutSeconds` must be > 0 and ≤ 600
- `ProviderType` must be 0, 1, or 2
- `ApiKeyEncrypted` required (non-null) when `ProviderType` is 1 (TogetherAI) or 2 (OpenRouter) — enforced at application level, not DB constraint

---

### RegisteredModel

Represents a specific model available through a provider.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | TEXT (GUID) | PRIMARY KEY | Unique identifier |
| ProviderId | TEXT (GUID) | NOT NULL, FK → Providers(Id) | Parent provider |
| ModelIdentifier | TEXT | NOT NULL | Model name sent in API requests (e.g., "Dolphin3.0-Llama3.1-8B") |
| DisplayName | TEXT | NOT NULL | User-friendly name for UI display |
| IsEnabled | INTEGER | NOT NULL, DEFAULT 1 | 0=disabled, 1=enabled |
| CreatedUtc | TEXT | NOT NULL | ISO 8601 timestamp |

**Validation Rules**:
- `ModelIdentifier` must be non-empty, max 200 characters
- `DisplayName` must be non-empty, max 200 characters
- `(ProviderId, ModelIdentifier)` must be unique (UNIQUE constraint)
- `ProviderId` must reference an existing provider

---

### FunctionModelDefault

Represents the default model and generation parameters assigned to an application function.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | TEXT (GUID) | PRIMARY KEY | Unique identifier |
| FunctionName | TEXT | NOT NULL, UNIQUE | AppFunction enum name (e.g., "RolePlayGeneration") |
| ModelId | TEXT (GUID) | NOT NULL, FK → RegisteredModels(Id) | Assigned model |
| Temperature | REAL | NOT NULL, DEFAULT 0.7 | 0.0–2.0 |
| TopP | REAL | NOT NULL, DEFAULT 0.9 | 0.0–1.0 |
| MaxTokens | INTEGER | NOT NULL, DEFAULT 500 | 1–8000 |
| UpdatedUtc | TEXT | NOT NULL | ISO 8601 timestamp |

**Validation Rules**:
- `FunctionName` must be one of the 9 AppFunction values
- `Temperature` must be ≥ 0.0 and ≤ 2.0
- `TopP` must be ≥ 0.0 and ≤ 1.0
- `MaxTokens` must be ≥ 1 and ≤ 8000
- `ModelId` must reference an existing enabled model from an enabled provider

---

## Relationships

```text
Provider (1) ──< RegisteredModel (many)
    │                     │
    │                     │
    └── IsEnabled ──────> availability cascade
                          │
RegisteredModel (1) ──< FunctionModelDefault (many, but unique per FunctionName)
```

- **Provider → RegisteredModel**: One-to-many. Deleting a provider cascades to delete its models.
- **RegisteredModel → FunctionModelDefault**: One-to-many (a model can be assigned to multiple functions). Deleting a model requires clearing its function assignments first (application-level warning + confirmation).
- **Availability cascade**: Disabling a provider makes all its models unavailable for function default selection and session overrides (enforced at application level via filtered queries).

## SQLite DDL

```sql
CREATE TABLE IF NOT EXISTS Providers (
    Id TEXT PRIMARY KEY NOT NULL,
    Name TEXT NOT NULL UNIQUE,
    ProviderType INTEGER NOT NULL,
    BaseUrl TEXT NOT NULL,
    ChatCompletionsPath TEXT NOT NULL DEFAULT '/v1/chat/completions',
    TimeoutSeconds INTEGER NOT NULL DEFAULT 120,
    ApiKeyEncrypted TEXT,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS RegisteredModels (
    Id TEXT PRIMARY KEY NOT NULL,
    ProviderId TEXT NOT NULL,
    ModelIdentifier TEXT NOT NULL,
    DisplayName TEXT NOT NULL,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    CreatedUtc TEXT NOT NULL,
    FOREIGN KEY (ProviderId) REFERENCES Providers(Id) ON DELETE CASCADE,
    UNIQUE (ProviderId, ModelIdentifier)
);

CREATE TABLE IF NOT EXISTS FunctionModelDefaults (
    Id TEXT PRIMARY KEY NOT NULL,
    FunctionName TEXT NOT NULL UNIQUE,
    ModelId TEXT NOT NULL,
    Temperature REAL NOT NULL DEFAULT 0.7,
    TopP REAL NOT NULL DEFAULT 0.9,
    MaxTokens INTEGER NOT NULL DEFAULT 500,
    UpdatedUtc TEXT NOT NULL,
    FOREIGN KEY (ModelId) REFERENCES RegisteredModels(Id)
);
```

## Enumerations

### ProviderType

```csharp
public enum ProviderType
{
    LmStudio = 0,
    TogetherAI = 1,
    OpenRouter = 2
}
```

### AppFunction

```csharp
public enum AppFunction
{
    RolePlayGeneration,
    StoryModeGeneration,
    StorySummarize,
    StoryAnalyze,
    StoryRank,
    ScenarioPreview,
    ScenarioAdapt,
    WritingAssistant,
    RolePlayAssistant
}
```

## Value Objects

### ResolvedModel

Returned by `IModelResolutionService.ResolveAsync()`. Contains all information needed for a generation call.

| Field | Type | Description |
|-------|------|-------------|
| ProviderBaseUrl | string | Full base URL of the provider |
| ChatCompletionsPath | string | API endpoint path |
| ProviderTimeoutSeconds | int | Request timeout |
| ApiKeyEncrypted | string? | DPAPI-encrypted API key (null for LM Studio) |
| ModelIdentifier | string | Model name for the API request |
| Temperature | double | Generation temperature |
| TopP | double | Top-p sampling parameter |
| MaxTokens | int | Maximum tokens to generate |
| ProviderName | string | For logging |
| IsSessionOverride | bool | Whether this came from a session override (for logging) |

## Seed Migration Data

On first run (empty `Providers` table), seed from `appsettings.json`:

### Provider Seed

| Name | ProviderType | BaseUrl | ChatCompletionsPath | TimeoutSeconds | ApiKeyEncrypted | IsEnabled |
|------|-------------|---------|---------------------|----------------|-----------------|-----------|
| LM Studio (Local) | LmStudio (0) | http://127.0.0.1:1234 | /v1/chat/completions | 120 | NULL | 1 |

### Model Seed

| ProviderId | ModelIdentifier | DisplayName | IsEnabled |
|-----------|-----------------|-------------|-----------|
| (LM Studio provider) | Dolphin3.0-Llama3.1-8B | Dolphin 3.0 Llama 3.1 8B | 1 |
| (LM Studio provider) | qwen2.5-14b-instruct-1m | Qwen 2.5 14B Instruct 1M | 1 |

### Function Default Seed

| FunctionName | Model | Temperature | TopP | MaxTokens |
|-------------|-------|-------------|------|-----------|
| RolePlayGeneration | Dolphin3.0-Llama3.1-8B | 0.7 | 0.9 | 500 |
| StoryModeGeneration | Dolphin3.0-Llama3.1-8B | 0.7 | 0.9 | 500 |
| StorySummarize | qwen2.5-14b-instruct-1m | 0.3 | 0.9 | 500 |
| StoryAnalyze | qwen2.5-14b-instruct-1m | 0.3 | 0.9 | 800 |
| StoryRank | qwen2.5-14b-instruct-1m | 0.1 | 0.9 | 200 |
| ScenarioPreview | Dolphin3.0-Llama3.1-8B | 0.6 | 0.95 | 1200 |
| ScenarioAdapt | Dolphin3.0-Llama3.1-8B | 0.5 | 0.95 | 2000 |
| WritingAssistant | Dolphin3.0-Llama3.1-8B | 0.7 | 0.9 | 500 |
| RolePlayAssistant | Dolphin3.0-Llama3.1-8B | 0.7 | 0.9 | 500 |

*Note*: RolePlayGeneration, StoryModeGeneration, WritingAssistant, and RolePlayAssistant use LmStudioOptions defaults (temperature 0.7, topP 0.9, maxTokens 500) since they currently use the `ILmStudioClient` default overload. StorySummarize/StoryAnalyze/StoryRank use StoryAnalysisOptions values. ScenarioPreview/ScenarioAdapt use ScenarioAdaptationOptions values.
