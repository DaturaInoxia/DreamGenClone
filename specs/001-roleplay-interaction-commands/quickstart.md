# Quickstart: RolePlay Interaction Commands

**Feature**: `001-roleplay-interaction-commands`  
**Branch**: `001-roleplay-interaction-commands`

---

## Prerequisites

- .NET 9 SDK
- Local LLM backend running (LM Studio or Ollama) for retry/rewrite operations
- SQLite (included, no separate install)

## Build & Run

```powershell
cd D:\src\DreamGenClone
dotnet build
dotnet run --project DreamGenClone.Web
```

## Run Tests

```powershell
dotnet test DreamGenClone.Tests
```

## Feature-Specific Test Scope

```powershell
# Run only interaction command tests
dotnet test DreamGenClone.Tests --filter "FullyQualifiedName~InteractionCommand"

# Run only retry service tests
dotnet test DreamGenClone.Tests --filter "FullyQualifiedName~InteractionRetry"

# Run only fork tests
dotnet test DreamGenClone.Tests --filter "FullyQualifiedName~BranchServiceFork"

# Run only context filter tests
dotnet test DreamGenClone.Tests --filter "FullyQualifiedName~ContinuationContextFilter"
```

## Key Files to Change

| File | Change Type | Description |
|------|-------------|-------------|
| `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs` | Modify | Add IsExcluded, IsHidden, IsPinned, ParentInteractionId, AlternativeIndex, ActiveAlternativeIndex |
| `DreamGenClone.Web/Domain/RolePlay/InteractionCommand.cs` | New | Command enum |
| `DreamGenClone.Web/Application/RolePlay/IInteractionCommandService.cs` | New | Flag/edit/delete interface |
| `DreamGenClone.Web/Application/RolePlay/InteractionCommandService.cs` | New | Implementation |
| `DreamGenClone.Web/Application/RolePlay/IInteractionRetryService.cs` | New | Retry/rewrite interface |
| `DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs` | New | Implementation |
| `DreamGenClone.Web/Application/RolePlay/IRolePlayBranchService.cs` | Modify | Add ForkAboveAsync, ForkBelowAsync |
| `DreamGenClone.Web/Application/RolePlay/RolePlayBranchService.cs` | Modify | Implement fork variants with active-alternative-only cloning |
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | Modify | Flag-aware context filtering in BuildPromptAsync |
| `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` | Modify | Toolbar, menus, dialogs, alternatives carousel |
| `DreamGenClone.Web/Program.cs` | Modify | Register new services in DI |

## Backward Compatibility

Existing sessions stored in SQLite will deserialize correctly. New fields default to safe values (`false`, `null`, `0`), so existing interactions appear unchanged. No data migration required.

## Architecture Notes

- All new services are in `DreamGenClone.Web/Application/RolePlay/` following the existing pattern
- Domain models are in `DreamGenClone.Web/Domain/RolePlay/`
- No new projects — fits within existing layered structure
- Session persistence remains JSON-in-SQLite via `SqlitePersistence`
- See [data-model.md](data-model.md) for entity schema and [contracts/](contracts/) for service contracts
