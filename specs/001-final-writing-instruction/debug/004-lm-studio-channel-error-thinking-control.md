# 004 — LM Studio Channel Error from Thinking Control

## Report
- **Symptom:** Local model `qwen2.5-14b-instruct-1m` succeeds in Prompt Tester but RP semantic analysis fails with LM Studio `Channel Error`.
- **Session:** RP session details were investigated in the active debug session; no new production database mutation was performed.
- **Affected path:** `RolePlayEngineService` → semantic background job → `SemanticEventInferenceService` → model resolution → `CompletionClient`.

## Analysis
- Consulted the 001-final-writing-instruction and 001-rp-prompt-redesign specification artifacts, including their plans, tasks, research, data model, and contracts.
- RP semantic analysis previously forced `DisableThinking = true`, causing `CompletionClient` to send `chat_template_kwargs: { "thinking": false }`.
- Prompt Tester omitted that field. The request endpoint, authentication, and timeout were not the differentiators.
- A single function-level boolean could not represent both model capability and per-function DeepSeek behavior.

## Plan
- Add model-level `SupportsThinkingControl` capability.
- Add function-level `ThinkingMode`: `Default` (omit), `Enabled`, or `Disabled`.
- Send `chat_template_kwargs.thinking` only when the model supports the capability and the function mode is explicit.
- Persist both settings and expose them in Model Manager UI.
- Validate with project builds and relevant tests.

## Resolution
- Added the capability and tri-state mode to domain models and runtime `ResolvedModel`.
- Added SQLite schema columns and repository persistence/mapping.
- Removed the hardcoded RP semantic-analysis suppression from model resolution.
- Updated `CompletionClient` payload construction and diagnostics.
- Added model capability and function mode controls to `ModelManager.razor` and `ModelDetailsEditor.razor`.

## Validated
- [x] Web project build passed with existing warnings.
- [x] Tests project build passed with existing warnings.
- [ ] Fresh RP session with Qwen confirmed by user.
- [ ] DeepSeek per-function behavior confirmed by user.
