# 003 — Semantic raw response not persisted on parse failure

## Report
The Semantic Analysis modal displayed `Prompt data is not available for this record` for a failed semantic analysis, even though the model had returned a response. The response was not necessarily valid JSON and needed to remain visible for diagnosis.

## Analysis
The inference service generated and logged the prompts and raw model output, then threw when JSON parsing or validation failed. The job handler caught the failure through the unsuccessful-result path only for model-resolution failures; parse failures reached the outer exception handler, which persisted status and error text without `ResultJson`. The UI therefore had no diagnostic payload to deserialize.

Consulted the final-writing-instruction specification artifacts, `SemanticEventInferenceService.cs`, `SemanticInteractionAnalysisJobHandler.cs`, and `RolePlayWorkspace.razor`.

## Plan
Return a failed inference result after parse failure while preserving the generated prompts and complete raw response. Serialize that diagnostic result into `ResultJson` in the handler's unsuccessful-result path. Keep successful parsing behavior unchanged.

## Resolution
Updated `SemanticEventInferenceService.cs` to return `Success = false` with the parse error, prompts, and raw output. Updated `SemanticInteractionAnalysisJobHandler.cs` to persist those fields in `ResultJson` for error records.

## Validated
[ ] pending — build and fresh-session verification.