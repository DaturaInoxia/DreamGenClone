# 013 — GLM Disabled Thinking Mode Resolver Failure

## Report

- Session: `4f2eec18-b190-4beb-ad35-8d520ae5c800`
- Interaction: `dad139a5-8e98-469b-ab74-8e55404f1732`
- Symptom: Beat Production failed before provider submission with `Function 'RolePlaySceneBeatAnalyzer' configures thinking control, but model 'OP-glm-4.7' does not support it.`
- Configuration: `OP-glm-4.7` was assigned to `RolePlaySceneBeatAnalyzer`, with structured output set to strict JSON schema and thinking set to disabled.

## Analysis

`SceneBeatAnalyzerResolver.ResolveAsync` required an explicit thinking mode, then rejected every non-default mode when `SupportsThinkingControl` was false. That made the valid configuration `ThinkingMode.Disabled` impossible for models that do not expose a thinking-control parameter. The canonical persisted row contained exactly one analyzer default with `ThinkingMode=2` (`Disabled`) and GLM reported `SupportsThinkingControl=0`.

The resolver is the single active source-resolution path. No fallback or substitute value was added. The Model Manager UI remains the persisted configuration surface.

## Plan

- Update the resolver check so only `ThinkingMode.Enabled` requires model thinking-control support.
- Add regression coverage for disabled thinking without model support and enabled thinking with model support absent.
- Build and run the affected RolePlay tests.

## Resolution

- Updated `DreamGenClone.Web/Application/RolePlay/SceneBeatAnalyzerResolver.cs` to reject unsupported `Enabled` mode only.
- Updated `DreamGenClone.Tests/RolePlay/SceneBeatAnalyzerResolverTests.cs` with the two regression cases.
- Persisted configuration remains GLM, strict JSON schema, and disabled thinking.

## Validated

- [x] Focused resolver tests: 7 passed, 0 failed on 2026-09-04.
- [ ] Fresh Beat Production run pending user confirmation/result.
