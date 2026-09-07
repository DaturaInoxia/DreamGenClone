# EditIterateWorkbench

## 1. Job (U1)
Describe, compile, run, compare, iterate.

## 2. Reused by
- Roleplay image editor.
- Studio Identity stage.
- Studio Finish stage.
- Asset Manager: Edit.
- Studio Composition uses the same loop with its framing inputs.

This is the shared B-110 replacement for three duplicated edit loops.

## 3. Input contract
All types below are proposed design types, not final APIs.

- `Guid ProductionGroupId` - production group whose state is preserved across steps.
- `EditSurfaceKind Surface` - consuming surface and its storage policy.
- `RawAssetBytes RawBytes` - source image or asset bytes supplied to compilation.
- `string Intent` - user description of the requested generation or edit.
- `EditablePrompt DraftPrompt` - compiled prompt draft that the user may edit.
- `IReadOnlyList<EditTurnViewModel> Turns` - prior turns with inputs, outputs, and per-turn scores.
- `IEditWorkbenchStorageAdapter StorageAdapter` - thin per-surface adapter for raw bytes and intent persistence.
- `bool IsReadOnly` - whether the current surface permits another run.
- `WorkbenchConfiguration Configuration` - permitted provider, model, size, and change-class choices.

## 4. Output/events
- `EventCallback<DescribeSubmittedEventArgs> OnDescribe` - captures or changes intent.
- `EventCallback<CompileRequestedEventArgs> OnCompile` - requests compilation from raw bytes plus intent.
- `EventCallback<PromptChangedEventArgs> OnPromptChanged` - reports an editable prompt change.
- `EventCallback<RunRequestedEventArgs> OnRun` - submits the resolved edit/generation request.
- `EventCallback<IterationRequestedEventArgs> OnIterate` - starts the next turn from a selected before/after result.
- `EventCallback<WorkbenchNavigationEventArgs> OnStepChanged` - moves between steps without discarding state.

## 5. States
- `empty`: no intent or source bytes; describe and source selection are available.
- `loading`: compiling, loading a source, or loading a before/after result.
- `error`: compilation, storage, or run error with retry and preserved draft state.
- `populated`: editable prompt and current turn are available.
- `compiled`: raw bytes plus intent produced a draft prompt.
- `ready`: prompt is editable and the run can be reviewed/submitted.
- `running`: run has been handed to `RunTray`; the workbench remains usable.
- `comparison`: before and after are available for inspection.
- `iterating`: a prior result is selected as the next turn input.

## 6. Data-volume behaviour (U4)
- Do not render raw bytes or a full prompt in the main flow. Show size and a compact summary.
- Open the full prompt and submitted payload in an on-demand panel or modal with a size indicator.
- Keep the turn history bounded with paged older turns; render the active before/after pair only.
- Load image previews as thumbnails first and load full resolution only when opened.
- Persist every step through the thin per-surface storage adapter so navigation does not lose work.

## 7. Progressive disclosure (U3)
By default show the current step, intent, editable prompt summary, primary action, and latest before/after result. Behind expanders show the full compiled prompt, negative prompt, seed, model, provider, class, size, steps, raw submitted payload, adapter provenance, and older turns. Show the multi-turn score for each step beside its result, with metric detail behind the score expander.

## 8. Honest-state / no-black-box notes (U5/U6)
Every Run action must expose a `View what will be submitted` affordance before submission. It must show the exact resolved prompt, negative prompt, seed, model, provider, class, size, steps, and strategy using backend vocabulary. Compilation is visibly distinct from running; a cold or warming provider is not represented as a generic spinner. Each turn shows its backend run state and its multi-turn score (FR-C6-05).

## 9. Acceptance
- The complete describe -> compile -> editable-prompt -> run -> before/after -> iterate loop is available in one reusable component and replaces the three B-110 loops.
- Compilation uses raw bytes plus intent and a thin per-surface storage adapter; the adapter is not a second workflow implementation.
- Every completed step displays its multi-turn score, and the next iteration preserves prior state (FR-C6-05, U7).
- A user can inspect the exact submitted inputs before every generate/edit action (U5/U6).
- Large prompts, payloads, turns, and images remain bounded and on demand (U3/U4).
