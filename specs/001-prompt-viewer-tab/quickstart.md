# Quickstart: Prompt Viewer Tab on Interaction Info Modal

**Branch**: `001-prompt-viewer-tab`  
**Date**: 2026-07-13

## What This Feature Does

Adds a scrollable "LLM Prompt" tab to the Interaction Info modal in the RP workspace, showing the exact prompt text that was sent to the LLM for each interaction. The prior session interactions block within the stored prompt is trimmed to first 200 + last 200 characters for storage efficiency; all other prompt sections (system preamble, scenario context, character descriptions, injected directives, current turn instruction) are stored in full.

## How to Verify

### Prerequisites

- Build the solution: `dotnet build DreamGenClone.sln`
- The dev database at `DreamGenClone.Web/data/dreamgenclone.dev.db` should exist with at least one RP session.

### Step 1: Verify new interactions capture the prompt

1. Start the web app.
2. Open an existing RP session (or create a new one).
3. Trigger a continuation (Continue button).
4. After the AI response appears, click the info (ℹ) button on the new interaction to open the Interaction Info modal.
5. Click the "LLM Prompt" tab.
6. **Expected**: The tab shows the full prompt text in a scrollable monospace container. The prior interactions block shows first ~200 chars, `...`, then last ~200 chars. All other prompt sections are visible in full.
7. Click the copy-to-clipboard button.
8. **Expected**: The full prompt text is copied to the clipboard.

### Step 2: Verify old interactions show the "no data" message

1. Open an RP session that was created before this feature was deployed.
2. Click the info button on any interaction.
3. Click the "LLM Prompt" tab.
4. **Expected**: The tab displays "No prompt data available for this interaction." in a styled info block.

### Step 3: Verify best-effort capture (failure does not block)

1. This is verified via unit tests (see Step 4) — the truncation function and capture path are tested in isolation.
2. At runtime, if prompt capture fails, the interaction is still created with `PromptText = null` and the "LLM Prompt" tab shows the "No prompt data available" message.

### Step 4: Run unit tests

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "PromptText"
```

**Expected**: All tests pass, covering:
- Truncation with long history block → first N + last N preserved
- Truncation with short history block → unchanged
- Truncation with missing history block marker → unchanged
- Truncation with null/empty input → unchanged
- Interaction creation sets `PromptText` to truncated prompt

## Key Files Changed

| File | Change |
|---|---|
| `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs` | Add `PromptText` property |
| `DreamGenClone.Web/Application/RolePlay/PromptTextTruncation.cs` | New — truncation helper |
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | Set `PromptText` at interaction creation sites |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Set `PromptText` on multi-actor paths |
| `DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs` | Set `PromptText` on retry paths |
| `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` | Add "LLM Prompt" tab + `SetPromptTab` + content block + copy button |
| `DreamGenClone.Tests/RolePlay/PromptTextTruncationTests.cs` | New — unit tests for truncation logic |
