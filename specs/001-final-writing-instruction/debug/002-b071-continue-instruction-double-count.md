# 002 — B-071 Continue-path instruction persistence

## Report
A user instruction submitted from the role-play workspace could appear only after a later Continue action. Session `2586da8f-1c19-41c1-9984-b29d392d9ef3` showed a `SubmitPrompt | SendButton | Becky` turn and two output interactions, indicating the affected path was classified as a normal message rather than an instruction-only submission.

## Analysis
Consulted the B-071 planning artifact, the final-writing-instruction specification artifacts, `RolePlayWorkspace.razor`, `ContinueAsRequest.cs`, `RolePlayEngineService.cs`, and canonical session DB diagnostics. `SubmitPromptAsync` already suppresses turn creation and counters for `PromptIntent.Instruction`, but its autosave is queued asynchronously. The instruction interaction can therefore remain absent from persisted session retrieval until another operation flushes the autosave queue. The ContinueAs path is intentionally a real narrative turn and must not be changed into an instruction path.

## Plan
Keep ordinary Continue/ContinueAs actions as real turns. Ensure instruction-only submissions are flushed immediately after queuing their session save. Add focused validation for instruction persistence and counters, then build and run the focused RolePlay tests.

## Resolution
Updated `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` to flush the autosave coordinator immediately for `PromptIntent.Instruction` submissions. No ContinueAs turn behavior was changed.

## Validated
[ ] pending — build, focused tests, and fresh-session verification.
