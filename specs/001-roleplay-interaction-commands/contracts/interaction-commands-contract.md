# Contract: Interaction Command Services

**Feature**: `001-roleplay-interaction-commands`  
**Date**: 2026-04-04  
**Data Model**: [data-model.md](../data-model.md)

---

## Overview

This contract defines the public service interfaces exposed by the Interaction Commands feature. All services are registered in DI and consumed by the `RolePlayWorkspace.razor` Blazor component. No external APIs or HTTP endpoints are introduced — these are internal application-layer contracts.

---

## IInteractionCommandService

**Namespace**: `DreamGenClone.Web.Application.RolePlay`  
**Implementation**: `InteractionCommandService`  
**Dependencies**: `IRolePlayEngineService` (session save), `ILogger<InteractionCommandService>`

### Operations

#### ToggleFlagAsync

```
Input:  RolePlaySession session, string interactionId, InteractionFlag flag, CancellationToken ct
Output: bool (new flag value)
Errors: ArgumentException if interactionId not found in session
```

Behavior:
- Locates interaction by ID in `session.Interactions` (includes alternatives)
- Toggles the specified flag (`IsExcluded`, `IsHidden`, or `IsPinned`)
- Saves session via `IRolePlayEngineService.SaveSessionAsync`
- Logs: `Information` — "Toggled {Flag} to {Value} on interaction {InteractionId} in session {SessionId}"

#### UpdateContentAsync

```
Input:  RolePlaySession session, string interactionId, string newContent, CancellationToken ct
Output: void
Errors: ArgumentException if interactionId not found or newContent is empty/whitespace
```

Behavior:
- Locates interaction by ID
- Replaces `Content` with `newContent.Trim()`
- Saves session
- Logs: `Information` — "Updated content of interaction {InteractionId} in session {SessionId}"

#### DeleteInteractionAsync

```
Input:  RolePlaySession session, string interactionId, bool deleteBelow, CancellationToken ct
Output: void
Errors: ArgumentException if interactionId not found
```

Behavior:
- Finds target interaction index in the session list (original interactions only, ignoring alternatives)
- If `deleteBelow == false`: removes the target interaction and all its alternatives
- If `deleteBelow == true`: removes the target interaction, all its alternatives, and all interactions (with their alternatives) at higher indices
- Saves session
- Logs: `Information` — "Deleted interaction {InteractionId} (deleteBelow={DeleteBelow}) from session {SessionId}, removed {Count} entries"

#### NavigateAlternativeAsync

```
Input:  RolePlaySession session, string interactionId, int direction, CancellationToken ct
Output: int (new ActiveAlternativeIndex)
Errors: ArgumentException if interactionId not found or has no alternatives
```

Behavior:
- Finds the original interaction (follows `ParentInteractionId` if needed)
- Computes max alternative index among siblings
- Clamps `ActiveAlternativeIndex + direction` to `[0, maxIndex]`
- Updates `ActiveAlternativeIndex` on the original
- Saves session
- Logs: `Debug` — "Navigated to alternative {Index} for interaction {InteractionId}"

---

## IInteractionRetryService

**Namespace**: `DreamGenClone.Web.Application.RolePlay`  
**Implementation**: `InteractionRetryService`  
**Dependencies**: `ICompletionClient`, `IModelResolutionService`, `IModelSettingsService`, `IScenarioService`, `IRolePlayEngineService`, `ILogger<InteractionRetryService>`

### Common Behavior (all retry/rewrite methods)

1. Locate target interaction (resolve to original via `ParentInteractionId` if needed)
2. Build context: `session.GetContextView()` to get flag-filtered, active-alternative-only list
3. Assemble prompt using scenario context + filtered history + actor info + instruction
4. Call `ICompletionClient.GenerateAsync` with resolved model
5. Create new `RolePlayInteraction` as alternative (set `ParentInteractionId`, increment `AlternativeIndex`)
6. Update parent's `ActiveAlternativeIndex` to point to new alternative
7. Add to `session.Interactions` and save
8. Log: `Information` — "Created alternative {AlternativeIndex} for interaction {InteractionId} via {Command}"
9. On failure: log `Warning`, throw (caller shows inline error, no alternative created per spec clarification)

### Operations

#### RetryAsync

```
Input:  RolePlaySession session, string interactionId, CancellationToken ct
Output: RolePlayInteraction (new alternative)
```

Instruction: "Regenerate this {ActorName} response in the same style."

#### RetryWithModelAsync

```
Input:  RolePlaySession session, string interactionId, string modelId, CancellationToken ct
Output: RolePlayInteraction (new alternative)
```

Same as RetryAsync but resolves model from `modelId` instead of session/default model.

#### RetryAsAsync

```
Input:  RolePlaySession session, string interactionId, ContinueAsActor actorType, string? customName, CancellationToken ct
Output: RolePlayInteraction (new alternative)
```

Instruction: "Regenerate this response as {actorName}."  
The new alternative has `InteractionType` and `ActorName` matching the specified actor.

#### MakeLongerAsync

```
Input:  RolePlaySession session, string interactionId, CancellationToken ct
Output: RolePlayInteraction (new alternative)
```

Instruction: "Rewrite this response to be significantly longer and more detailed, maintaining the same tone and character."

#### MakeShorterAsync

```
Input:  RolePlaySession session, string interactionId, CancellationToken ct
Output: RolePlayInteraction (new alternative)
```

Instruction: "Rewrite this response to be more concise and shorter, maintaining the same tone and character."

#### AskToRewriteAsync

```
Input:  RolePlaySession session, string interactionId, string instruction, CancellationToken ct
Output: RolePlayInteraction (new alternative)
Errors: ArgumentException if instruction is empty/whitespace
```

Instruction: User-provided instruction text, passed directly to the prompt.

---

## IRolePlayBranchService (Extended)

**Namespace**: `DreamGenClone.Web.Application.RolePlay`  
**Existing methods unchanged.**

### New Operations

#### ForkAboveAsync

```
Input:  string sourceSessionId, string interactionId, string branchTitle, CancellationToken ct
Output: RolePlaySession? (null if source not found)
```

Behavior:
- Loads source session
- Finds target interaction index
- Clones session with interactions from index 0 through target index (inclusive)
- For each position, copies only the active alternative (flattens to `AlternativeIndex 0`, `ParentInteractionId null`)
- Flags (`IsExcluded`, `IsHidden`, `IsPinned`) are copied as-is
- Sets `ParentSessionId` on new session
- Logs: `Information` — "Forked above from session {SourceId} at interaction {InteractionId}, new session {BranchId} with {Count} interactions"

#### ForkBelowAsync

```
Input:  string sourceSessionId, string interactionId, string branchTitle, CancellationToken ct
Output: RolePlaySession? (null if source not found)
```

Behavior:
- Same as ForkAboveAsync but takes interactions from target index through end
- Logs: `Information` — "Forked below from session {SourceId} at interaction {InteractionId}, new session {BranchId} with {Count} interactions"

---

## InteractionFlag Enum

```csharp
namespace DreamGenClone.Web.Application.RolePlay;

public enum InteractionFlag
{
    Excluded,
    Hidden,
    Pinned
}
```

---

## Error Contract

All services throw standard .NET exceptions:
- `ArgumentException`: Invalid input (missing interaction, empty content)
- `InvalidOperationException`: Illegal state transition
- `OperationCanceledException`: Cancellation token triggered
- `HttpRequestException` (from `ICompletionClient`): AI generation failure — caller catches and displays inline error

No custom exception types are introduced.
