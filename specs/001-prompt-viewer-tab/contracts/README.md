# Contracts: Prompt Viewer Tab on Interaction Info Modal

**Branch**: `001-prompt-viewer-tab`  
**Date**: 2026-07-13

## 1. Domain Contract: RolePlayInteraction.PromptText

**Type**: Data field contract (no external API).

```csharp
// DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs
public sealed class RolePlayInteraction
{
    // ... existing properties ...

    /// <summary>
    /// The full LLM prompt text sent for this interaction, with the prior
    /// interactions block trimmed to first N + last N characters for storage
    /// efficiency. Null means "not captured" (pre-deployment interactions or
    /// best-effort capture failure).
    /// </summary>
    public string? PromptText { get; set; }
}
```

**Contract rules**:
- `PromptText` is set once at interaction creation time and MUST NOT be modified thereafter.
- `PromptText` is null for interactions created before this feature was deployed.
- `PromptText` is null when best-effort capture fails (per FR-007a).
- When non-null, `PromptText` contains the full prompt with only the prior interactions block trimmed; all other sections are in full.

---

## 2. Truncation Function Contract

**Type**: Pure function contract.

```csharp
// DreamGenClone.Web/Application/RolePlay/PromptTextTruncation.cs
public static class PromptTextTruncation
{
    public const int DefaultEdgeSize = 200;

    public static string TrimInteractionHistoryBlock(string fullPrompt, int edgeSize = DefaultEdgeSize);
}
```

**Contract rules**:
- **Input**: `fullPrompt` — the complete prompt string sent to the LLM.
- **Output**: the prompt with the prior interactions block content trimmed to first N + last N characters, separated by `"\n...\n"`.
- **Idempotent**: calling twice produces the same result (the trimmed block is not re-trimmed because the marker is no longer present in its original form).
- **Pure**: no side effects, no I/O, no logging.
- **Safe on missing block**: if the history block header marker is not found, returns the input unchanged.
- **Safe on short block**: if the block content is shorter than 2×N, returns the input unchanged.
- **Safe on null/empty**: if `fullPrompt` is null or empty, returns the input unchanged.

---

## 3. UI Contract: LLM Prompt Tab

**Type**: UI component contract (Razor).

**Component**: `RolePlayWorkspace.razor` — Interaction Info modal.

**Tab identifier**: `"prompt"` (value of `_infoPopupTab` when the LLM Prompt tab is active).

**Tab visibility**: Always shown (the tab button is always rendered, regardless of whether `PromptText` is null). This ensures discoverability — users can open the tab and see the "No prompt data available" message for old interactions.

**Tab content contract**:
- When `PromptText` is null or empty: display the message `"No prompt data available for this interaction."` in a styled info block.
- When `PromptText` is non-empty: display the prompt text in a monospace-styled, vertically scrollable `<pre>` container with CSS class `rw-prompt-viewer` (or similar, following existing `rw-` prefix convention).
- A copy-to-clipboard button is rendered above or beside the prompt text container, allowing the user to copy the full `PromptText` to the clipboard.

**Tab methods**:
```csharp
private void SetPromptTab() => _infoPopupTab = "prompt";
```

---

## 4. Capture Contract: Prompt Capture at Interaction Creation

**Type**: Service behavior contract.

**Service**: `RolePlayContinuationService.ContinueAsync` (and equivalent paths in `ContinueNarrativeAsync`, `RolePlayEngineService` multi-actor paths, `InteractionRetryService`).

**Contract rules**:
- After `BuildPromptAsync` returns the prompt string, the truncation function is applied: `var storedPrompt = PromptTextTruncation.TrimInteractionHistoryBlock(prompt);`
- The `RolePlayInteraction` object initializer sets `PromptText = storedPrompt`.
- If `BuildPromptAsync` or `TrimInteractionHistoryBlock` throws, the exception is caught, a warning is logged via Serilog, and `PromptText` is left null. The interaction is still created and persisted. The error does NOT propagate (best-effort per FR-007a).
- The prompt capture does NOT modify the `prompt` variable sent to the LLM — truncation is storage-only; the LLM receives the full untruncated prompt.
