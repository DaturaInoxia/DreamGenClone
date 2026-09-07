# Scene Image Editor Prepare Binding Propagation

## Report

On 2026-09-06, `/roleplay/image-editor` appeared to discard the text entered for an edit and no compiled edit prompt returned after `Prepare edit` was selected. The affected generic editor uses the configured Qwen multimodal compiler to create the edit prompt.

## Analysis

`SceneImageEditor.PrepareAsync` correctly rejects blank `_rawIntent` before it can enqueue a compilation attempt. The durable compilation service and job handler correctly persist an ordinal-zero prompt revision after a `Ready` compilation.

The shared `EditIterateWorkbench` exposed `IntentChanged`, `ClarificationChanged`, and `EditablePromptChanged` parameters, but its inputs bound directly to its parameter properties. That updated the child component's local parameter value without invoking the parent callbacks. Consequently, the visible textarea held the user input while the parent `_rawIntent` remained blank and `PrepareAsync` returned without calling the service.

The read-only diagnostic query `DreamGenClone.DbQuery/queries/recent-scene-image-editor-compilations.sql` confirmed the boundary failure: recent active `SceneImageEditSessions` had no matching compilation attempts, raw intents, revisions, or prompts. This rules out the Qwen compiler, durable queue, and prompt-result persistence as the first failing path.

The applicable 001 feature spec, task breakdown, plan, research, data model, Slot 17 contract, terminology mapping, and parent RP prompt redesign specification were reviewed. This repair does not alter the frozen prompt architecture or any configured RP behavior.

## Plan

1. Retain the shared workbench and its existing `@bind-Intent`, `@bind-Clarification`, and `@bind-EditablePrompt` public API.
2. Forward each input value explicitly to its matching `EventCallback<string>`.
3. Add a focused source-contract assertion for the forwarding behavior.
4. Build and run the focused editor contract suite, then retain browser acceptance as the final configured-compiler validation.

## Resolution

- `DreamGenClone.Web/Components/Shared/EditIterateWorkbench.razor` now uses explicit bind getter/setter methods for the intent, clarification, and editable compiled prompt fields. Each setter invokes the matching parent callback after updating the child state.
- `DreamGenClone.Tests/RolePlay/SceneImageStudioUiContractTests.cs` now asserts the intent setter binding and all three callback invocations.

## Validated

- [x] `SceneImageStudioUiContractTests` passed: 19 total, 19 passed, 0 failed, 0 skipped.
- [x] Razor diagnostics reported no errors for `EditIterateWorkbench.razor` or `SceneImageEditor.razor`.
- [x] `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore --nologo -c Release` passed with 0 errors after stopping the local process that held its own Release executable lock.
- [x] The rebuilt Release application started against `data/dreamgenclone.dev.db` at `http://localhost:5178`.
- [ ] Pending user/browser acceptance: enter an intent, select `Prepare edit`, confirm a `SceneImageEditCompilationAttempt` persists with that raw intent, then confirm a returned prompt revision or a real compiler clarification/error.