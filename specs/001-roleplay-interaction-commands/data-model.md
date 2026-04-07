# Data Model: RolePlay Interaction Commands

**Feature**: `001-roleplay-interaction-commands`  
**Date**: 2026-04-04  
**Research**: [research.md](research.md)

---

## Entity Changes

### Modified: `RolePlayInteraction`

**File**: `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs`

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Id` | `string` | `Guid.NewGuid().ToString()` | Existing. Unique interaction ID. |
| `InteractionType` | `InteractionType` | — | Existing. User/Npc/Custom/System. |
| `ActorName` | `string` | `string.Empty` | Existing. Display name of actor. |
| `Content` | `string` | `string.Empty` | Existing. Interaction text content. |
| `CreatedAt` | `DateTime` | `DateTime.UtcNow` | Existing. Timestamp. |
| **`IsExcluded`** | `bool` | `false` | **NEW**. When true, interaction is skipped in AI context building. |
| **`IsHidden`** | `bool` | `false` | **NEW**. When true, interaction is visually collapsed in UI but still sent to AI. |
| **`IsPinned`** | `bool` | `false` | **NEW**. When true, interaction has priority retention during context trimming. |
| **`ParentInteractionId`** | `string?` | `null` | **NEW**. If non-null, this is a sibling alternative of the referenced interaction. |
| **`AlternativeIndex`** | `int` | `0` | **NEW**. 0-based ordinal among siblings sharing the same parent (or self if parent is null). |
| **`ActiveAlternativeIndex`** | `int` | `0` | **NEW**. On the original (parent) interaction only. Tracks which alternative is currently displayed. Ignored on child alternatives. |

**Relationships**:
- Self-referential: `ParentInteractionId` → `RolePlayInteraction.Id` (within same session)  
- An interaction with `ParentInteractionId == null` and `AlternativeIndex == 0` is the original
- An interaction with `ParentInteractionId != null` is an alternative sibling
- The "active" alternative for display = the sibling with `AlternativeIndex == parent.ActiveAlternativeIndex`

**Validation Rules**:
- `ParentInteractionId` must reference an existing interaction in the same session, or be null
- `AlternativeIndex` must be ≥ 0
- `ActiveAlternativeIndex` must be ≥ 0 and ≤ max `AlternativeIndex` among siblings
- `AlternativeIndex` values must be unique among siblings sharing the same parent

**State Transitions**:

```text
Flag toggles (idempotent):
  IsExcluded: false ↔ true (toggle)
  IsHidden:   false ↔ true (toggle) 
  IsPinned:   false ↔ true (toggle)

Alternative creation (Retry/Rewrite operations):
  1. Find original (ParentInteractionId == null) at target position
  2. Count existing siblings: nextIndex = max(AlternativeIndex) + 1
  3. Create new RolePlayInteraction with:
     - ParentInteractionId = original.Id
     - AlternativeIndex = nextIndex
     - InteractionType = original.InteractionType (or overridden for RetryAs)
     - ActorName = original.ActorName (or overridden for RetryAs)
  4. Set original.ActiveAlternativeIndex = nextIndex
  5. New alternative becomes the displayed one

Alternative navigation:
  - Left (<): parent.ActiveAlternativeIndex = max(0, current - 1)
  - Right (>): parent.ActiveAlternativeIndex = min(maxIndex, current + 1)
```

### New Enum: `InteractionCommand`

**File**: `DreamGenClone.Web/Domain/RolePlay/InteractionCommand.cs`

```csharp
namespace DreamGenClone.Web.Domain.RolePlay;

public enum InteractionCommand
{
    ToggleExcluded = 1,
    ToggleHidden = 2,
    TogglePinned = 3,
    Delete = 4,
    DeleteAndBelow = 5,
    MakeEdit = 6,
    Retry = 10,
    RetryWithModel = 11,
    RetryAs = 12,
    MakeLonger = 13,
    MakeShorter = 14,
    AskToRewrite = 15,
    ForkAbove = 20,
    ForkBelow = 21
}
```

### Unchanged: `RolePlaySession`

No field changes. The `Interactions` list now contains both original interactions and their alternatives (linked by `ParentInteractionId`). Session serialization to JSON is transparent since `List<RolePlayInteraction>` already handles the flat list.

### Unchanged: `InteractionType`

No changes to the `User = 1, Npc = 2, Custom = 3, System = 4` enum.

---

## New Interfaces

### `IInteractionCommandService`

**File**: `DreamGenClone.Web/Application/RolePlay/IInteractionCommandService.cs`

| Method | Parameters | Returns | Description |
|--------|-----------|---------|-------------|
| `ToggleFlagAsync` | `session: RolePlaySession`, `interactionId: string`, `flag: InteractionFlag`, `ct` | `Task<bool>` | Toggle the specified flag on the interaction. Returns new flag value. |
| `UpdateContentAsync` | `session: RolePlaySession`, `interactionId: string`, `newContent: string`, `ct` | `Task` | Replace interaction content (Make Edit). |
| `DeleteInteractionAsync` | `session: RolePlaySession`, `interactionId: string`, `deleteBelow: bool`, `ct` | `Task` | Remove interaction (and optionally all below). Removes associated alternatives. |
| `NavigateAlternativeAsync` | `session: RolePlaySession`, `interactionId: string`, `direction: int`, `ct` | `Task<int>` | Move ActiveAlternativeIndex by direction (-1 or +1). Returns new index. |

Supporting enum:
```csharp
public enum InteractionFlag { Excluded, Hidden, Pinned }
```

### `IInteractionRetryService`

**File**: `DreamGenClone.Web/Application/RolePlay/IInteractionRetryService.cs`

| Method | Parameters | Returns | Description |
|--------|-----------|---------|-------------|
| `RetryAsync` | `session: RolePlaySession`, `interactionId: string`, `ct` | `Task<RolePlayInteraction>` | Regenerate using same actor/model. Creates new alternative. |
| `RetryWithModelAsync` | `session: RolePlaySession`, `interactionId: string`, `modelId: string`, `ct` | `Task<RolePlayInteraction>` | Regenerate using specified model. Creates new alternative. |
| `RetryAsAsync` | `session: RolePlaySession`, `interactionId: string`, `actorType: ContinueAsActor`, `customName: string?`, `ct` | `Task<RolePlayInteraction>` | Regenerate as different actor. Creates new alternative. |
| `MakeLongerAsync` | `session: RolePlaySession`, `interactionId: string`, `ct` | `Task<RolePlayInteraction>` | Rewrite with "make longer" instruction. Creates new alternative. |
| `MakeShorterAsync` | `session: RolePlaySession`, `interactionId: string`, `ct` | `Task<RolePlayInteraction>` | Rewrite with "make shorter" instruction. Creates new alternative. |
| `AskToRewriteAsync` | `session: RolePlaySession`, `interactionId: string`, `instruction: string`, `ct` | `Task<RolePlayInteraction>` | Rewrite with user-provided instruction. Creates new alternative. |

### Modified: `IRolePlayBranchService`

**File**: `DreamGenClone.Web/Application/RolePlay/IRolePlayBranchService.cs`

Add two methods to existing interface:

| Method | Parameters | Returns | Description |
|--------|-----------|---------|-------------|
| `ForkAboveAsync` | `sourceSessionId: string`, `interactionId: string`, `branchTitle: string`, `ct` | `Task<RolePlaySession?>` | Fork session with interactions from start through target (inclusive). Only copies active alternatives. |
| `ForkBelowAsync` | `sourceSessionId: string`, `interactionId: string`, `branchTitle: string`, `ct` | `Task<RolePlaySession?>` | Fork session with interactions from target through end (inclusive). Only copies active alternatives. |

---

## Context View Helper

A helper method on `RolePlaySession` or as an extension method to produce a filtered, display-ready list:

```csharp
public static List<RolePlayInteraction> GetContextView(this RolePlaySession session)
{
    // 1. Group: find all originals (ParentInteractionId == null)
    // 2. For each original, select the active alternative (or self if index 0)
    // 3. Exclude interactions where IsExcluded == true
    // 4. Return ordered list for prompt assembly
}
```

This is used by:
- `BuildPromptAsync` in `RolePlayContinuationService` (replaces direct `session.Interactions.TakeLast(12)`)
- `IInteractionRetryService` for building retry context
- UI for rendering the visible interaction timeline

---

## JSON Serialization

No changes to serialization configuration needed. The new fields on `RolePlayInteraction` are all primitive types (`bool`, `string?`, `int`) that `System.Text.Json` handles automatically with default options. Existing session payloads in SQLite will deserialize with defaults (`false`, `null`, `0`) for the new fields, providing seamless backward compatibility.
