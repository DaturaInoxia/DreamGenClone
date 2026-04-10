# Quickstart: Model Manager

**Feature**: 004-model-manager | **Branch**: `004-model-manager`

## Prerequisites

- Windows 10/11 (required for DPAPI encryption)
- .NET 9.0 SDK
- LM Studio running locally at http://127.0.0.1:1234 (for local model testing)
- Optional: Together AI or OpenRouter API key (for cloud provider testing)

## Getting Started

### 1. Build and Run

```powershell
cd D:\src\DreamGenClone
dotnet build
cd DreamGenClone.Web
dotnet run
```

### 2. First-Run Migration

On first start after implementation, the application automatically:
1. Creates `Providers`, `RegisteredModels`, and `FunctionModelDefaults` tables in SQLite
2. Seeds a "LM Studio (Local)" provider from `appsettings.json` LmStudio section
3. Registers two models: "Dolphin3.0-Llama3.1-8B" and "qwen2.5-14b-instruct-1m"
4. Creates 9 function defaults matching current appsettings behavior

No manual configuration needed — the app behaves identically to before migration.

### 3. Access Model Manager

Navigate to the **Model Manager** page from the left navigation menu.

The page has 3 sections:
- **Providers**: View, add, edit, test, enable/disable, delete providers
- **Models**: View, register, enable/disable, delete models (grouped by provider)
- **Function Defaults**: Assign a model + parameters (temperature, topP, maxTokens) to each of the 9 application functions

### 4. Add a Cloud Provider (Optional)

1. In the Providers section, click "Add Provider"
2. Select provider type (Together AI or OpenRouter)
3. Enter a name, base URL (defaults pre-filled), timeout, and API key
4. Click "Test Connection" to verify
5. Save the provider

### 5. Register Cloud Models

1. In the Models section, click "Add Model"
2. Select the cloud provider
3. Enter the model identifier (e.g., `meta-llama/Llama-3.3-70B-Instruct-Turbo` for Together AI)
4. Enter a display name
5. Save

### 6. Assign Function Defaults

1. In the Function Defaults section, select a model from the dropdown for any function
2. Adjust temperature, topP, maxTokens as needed
3. Changes take effect immediately — no restart required

## Running Tests

```powershell
cd D:\src\DreamGenClone
dotnet test
```

Key test areas:
- `DreamGenClone.Tests/ModelManager/` — Unit tests for model resolution, encryption, repositories, migration
- Model resolution fallback chain (session override → function default → error)
- DPAPI encryption round-trip
- Seed migration correctness

## Key Files for Development

| Area | File | Purpose |
|------|------|---------|
| Domain entities | `DreamGenClone.Domain/ModelManager/` | Provider, RegisteredModel, FunctionModelDefault, enums |
| Application interfaces | `DreamGenClone.Application/ModelManager/` | Repository + service contracts |
| Completion client | `DreamGenClone.Application/Abstractions/ICompletionClient.cs` | Provider-agnostic generation interface |
| Infrastructure | `DreamGenClone.Infrastructure/ModelManager/` | SQLite repositories, DPAPI encryption |
| Client implementation | `DreamGenClone.Infrastructure/Models/CompletionClient.cs` | Multi-provider HTTP client |
| Model resolution | `DreamGenClone.Web/Application/ModelManager/ModelResolutionService.cs` | Function → model resolution |
| UI page | `DreamGenClone.Web/Components/Pages/ModelManager.razor` | Model Manager page |
| DI setup | `DreamGenClone.Web/Program.cs` | Service registration |

## Development Notes

- **No EF Core**: The project uses raw `Microsoft.Data.Sqlite` throughout. Follow the existing `SqlitePersistence` patterns for repository implementations.
- **DPAPI scope**: Uses `DataProtectionScope.CurrentUser`. Encrypted keys are tied to the Windows user profile.
- **HttpClient factory**: `CompletionClient` uses `IHttpClientFactory` (not typed client) to support dynamic base URLs per provider.
- **Options classes retained**: `LmStudioOptions`, `StoryAnalysisOptions`, `ScenarioAdaptationOptions` keep their model/parameter fields as seed sources but are no longer the authoritative runtime configuration.
