# Scene Image Beat JSON Control Character

> Superseded by `024-scene-image-beat-reasoning-response-path.md`. The original parser-level diagnosis was incomplete; runtime evidence showed that beat generation was using the plain completion path instead of the established reasoning-aware RP path.

## Report

The Scene Image Studio displayed `Beat analysis failed: '0x0A' is invalid within a JSON string. The string should be correctly escaped.` The failure occurred while parsing a model-generated beat-analysis response containing a literal newline inside a JSON string.

## Analysis

`SceneImageBeatGenerationJobHandler` calls `SceneImageBeatAnalysisService.ParseOutput`. The parser extracted the model response's JSON object and passed it directly to `JsonDocument.Parse`. System.Text.Json correctly rejected the unescaped line-feed in a model-generated description value.

The model-analysis contract remains authoritative and strict for beat count, fields, interaction membership, characters, clothing, location, and time. The defect was transport formatting, not beat interpretation.

## Plan

Update `SceneImageBeatAnalysisService.ParseOutput` to escape literal JSON control characters only while inside quoted strings, then continue using `JsonDocument` for validation. Add parser regression tests and preserve rejection of control characters outside strings.

## Resolution

The beat-specific control-character sanitizer was removed. Beat generation now follows the established reasoning-aware completion pattern documented in issue 024.

## Validated

- [x] Web build succeeded: `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore`.
- [x] Beat parser tests passed: 2/2.
- [x] Full test suite passed: 1188/1188.
- [ ] Pending user confirmation in a fresh Scene Image Studio run.