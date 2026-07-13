# Data Model: Prompt Viewer Tab on Interaction Info Modal

**Branch**: `001-prompt-viewer-tab`  
**Date**: 2026-07-13

## Entities

### RolePlayInteraction (modified)

**File**: `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs`

**New property**:

| Property | Type | Nullable | Default | Description |
|---|---|---|---|---|
| `PromptText` | `string?` | Yes | `null` | The full LLM prompt text sent for this interaction, with the prior interactions block trimmed to first N + last N characters. Null means "not captured" (pre-deployment interactions or best-effort failure). |

**Existing properties** (unchanged): `Id`, `InteractionType`, `ActorName`, `Content`, `CreatedAt`, `NarrativePhaseAtCreation`, `IsExcluded`, `IsHidden`, `IsPinned`, `ParentInteractionId`, `AlternativeIndex`, `ActiveAlternativeIndex`, `GeneratedByModelId`, `GeneratedByModelName`, `GeneratedByCommand`, `GeneratedByProvider`, `GeneratedTemperature`, `GeneratedTopP`, `GeneratedMaxTokens`, `WasInSexScene`, `WasEncounterStart`, `WasEncounterBoundaryDetected`, `ReasoningContent`, `SessionInteractionIndex`, `EncounterNumberAtCreation`, `InteractionIndexInEncounter`, `ExplicitnessLevelAtCreation`.

**Relationships**: Unchanged — `RolePlayInteraction` remains a nested element of `RolePlaySession.Interactions`.

## Persistence

**No schema migration required.**

`RolePlayInteraction` is serialized as part of `RolePlaySession` into the `PayloadJson` TEXT column on the `Sessions` table via `System.Text.Json`. The new `PromptText` property auto-serializes as a new JSON field. Existing rows deserialize with `PromptText = null`.

**Serialization options**: `JsonSerializerOptions(JsonSerializerDefaults.Web)` — camelCase property naming, case-insensitive. The JSON field will appear as `"promptText": "..."` or be omitted when null (depending on `DefaultIgnoreCondition` — verify during implementation; if null values are serialized explicitly, no change needed; if ignored, deserialization still yields null which is correct).

## Truncation Logic

### PromptTextTruncator (new helper)

**Purpose**: Pure function that takes the full prompt string and returns a storage-efficient version with the prior interactions block trimmed.

**Location**: `DreamGenClone.Web/Application/RolePlay/PromptTextTruncation.cs` (or as a static method on an existing helper class).

**Signature**:
```csharp
public static class PromptTextTruncation
{
    /// <summary>
    /// Trims the prior interactions block within the prompt to the first N and last N characters.
    /// All other prompt sections are preserved in full.
    /// </summary>
    /// <param name="fullPrompt">The complete prompt string sent to the LLM.</param>
    /// <param name="edgeSize">N — number of characters to keep from the start and end of the history block. Default: 200.</param>
    /// <returns>The prompt with the history block trimmed, or the original prompt if the block is not found or is shorter than 2*N.</returns>
    public static string TrimInteractionHistoryBlock(string fullPrompt, int edgeSize = 200)
}
```

**Algorithm**:
1. Locate the history block header marker: `"Recent interaction history — exact scene continuity."` (stable string from `BuildPromptAsync` line 805).
2. Find the start of the block content (end of the header line).
3. Find the end of the block (next section header or end of string — the block ends when the next prompt section begins, identifiable by the next known section marker or end of prompt).
4. If the block content length ≤ 2×N, return the prompt unchanged.
5. Otherwise, replace the block content with: `first N characters + "\n...\n" + last N characters`.
6. If the header marker is not found, return the prompt unchanged (best-effort — no truncation applied).

**State transitions**: None — this is a pure function with no side effects.

**Validation rules**:
- Input `fullPrompt` null or empty → return input unchanged.
- `edgeSize` ≤ 0 → return input unchanged.
- History block not found → return input unchanged.
- History block shorter than 2×N → return input unchanged.

## State Transitions

No state transitions affected. `PromptText` is a passive data field set once at interaction creation time and never modified thereafter.
