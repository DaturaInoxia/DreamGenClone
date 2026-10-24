# Qwen-2.1 edit compiler: withholds the instruction (echo) and can leak the response envelope into it

## Report

Reported 2026-10-13 from the Scene Image Editor (`/roleplay/image-editor`, edit turn
`5c29e747-6614-4aa9-8c49-1980fd884f01`, source `1a6e2769-50f2-40e0-b451-05ced234314d`, interaction
`460f8394-26ad-4e9c-867d-761b0a55578d`).

The operator typed the intent *"the woman is now standing facing to the right, her body and face fully
turned to the right"*. The job completed in 19 s on the configured Qwen-Image-2.1 native graph
(25 steps / CFG 1 / euler+simple / no LoRA) and returned an image in which the pose had not moved. Handing
the editor a hand-written, image-anchored, whole-body version of the same intent rendered correctly.

Two separate defects were reported in the compiled prompt:

1. **Echo.** The compiled prompt was a near-verbatim restatement of the operator's own sentence.
2. **JSON leak.** On other runs the compiled prompt came back as JSON.

Measured from the dev DB (`SceneImageEditPromptRevisions` joined to `SceneImageEditCompilationAttempts`,
`RevisionKind = 'CompilerOutput'`):

| Intent | Compiled prompt | Δ chars |
|---|---|---|
| "the woman is now standing facing to the right her body and face fully turned to the right" | "The woman is now standing facing to the right, her body and face fully turned to the right." | +2 |
| "the women is bending over at the hips placing the basket on the ground" | "The woman should be bending over at her hips and placing a laundry basket on the ground." | +18 |
| "Have the woman stand still with both feet planted. Rotate her entire body 90 degrees…" | `{"schemaVersion": "scenе-imаgе-edit-cоmріlеr-v1", …}` (783 chars) | +482 |

## Analysis

Both defects are in the compiler contract, not the render graph. Confirmed by reading
`ComfyUIImageEditingClient.BuildQwenImage21EditWorkflow` against the graph the proof harnesses submit for
the same model row (`helpers/local-comfyui-host/run-qwen21-native-edit.ps1`) — identical node set and
settings. The App and the slideshow renders differ only in the instruction text.

**Defect 1 — the instruction never required a self-sufficient prompt.** `QwenImage21EditPromptCompiler`'s
system message asked for "one **concise** edit instruction", required it to be "direct and feasible" and to
"describe only the requested change", and rewarded brevity. Nothing required the target state after the
edit, image-relative direction (the operator's bare "right" is ambiguous between viewer-right and
subject-right), or whole-body coverage for a pose/orientation change. The observed outputs are exactly what
that instruction asks for.

**Defect 2 — no validation that the compiled prompt is an instruction.** Attempt
`0f09b525-526d-40e3-b6bb-0c767d1c451e` (`Ready`) returned a field-valid envelope whose `compiledPrompt`
value was that envelope serialized as a string. `SceneImageEditPromptCompilerBase.NormalizeTerminalState`
required only that the field be non-blank, so it passed; `MediaEditCompilationJobHandler` persisted it as
the `CompilerOutput` revision and the render sent the JSON to the editor verbatim. There was no gate — and
therefore exactly one wrong path — between the model's answer and what the image editor executes.

**Contributing factor (configuration, not code).** The compiler resolves to a local 7B VL
(`qwen2.5-vl-7b-instruct-abliterated-local`, temperature 0.2, 512 max tokens) through
`AppFunction.RolePlaySceneImageEditPromptCompiler`. LM Studio does not enforce the response schema, so
re-emitting the envelope is a known shape for a model this size. Recorded here; no code change.

## Plan

1. Gate the compiled prompt in the shared compiler base: a `Ready` result whose `compiledPrompt` is
   serialized JSON or carries envelope field names fails fast, naming the cause.
2. Require a self-sufficient, image-anchored, whole-body instruction in the Qwen-2.1 compiler's system
   message, and state that an echo of the operator's sentence is a failed compilation.
3. Bump the 2.1 system-prompt version so attempts name the contract they were compiled under, and keep the
   superseded version resolving so a stale attempt fails with the explicit "contract changed" diagnostic.
4. Cover the gate and the new contract with tests; the 2511 instruction text must stay unchanged.

## Resolution

- `SceneImageEditPromptCompilerBase.cs` — `RequireProseInstruction` refuses a `Ready` `compiledPrompt` that
  starts as a JSON object/array or contains `"schemaVersion"` / `"compiledPrompt"` / `"requestedChanges"` /
  `"invalidReason"`. It fails fast with the cause and the remedy; nothing is repaired or passed through.
- `QwenImage21EditPromptCompiler.cs` — the compiled prompt is now required to be a self-sufficient prose
  instruction carrying the post-edit target state, image-anchored directions ("toward the right edge of the
  image", "facing the viewer's left"), the whole body and resulting stance/view for pose or orientation
  changes, and what stays unchanged; an echo of the operator's sentence is named as a failed compilation.
  `SystemPromptVersion` → `qwen-edit-2.1-rules-v2`. Schema and parse contract unchanged.
- `SceneImageEditPromptCompilerResolver.cs` — `qwen-edit-2.1-rules-v1` kept as the historical version of the
  2.1 compiler, so an attempt queued before this change re-resolves and then fails on the handler's explicit
  version check rather than on "no compiler registered".
- Tests — `QwenSceneImageEditPromptCompilerTests` (serialized envelope refused; any JSON object refused;
  prose still accepted), `QwenImage21EditPromptCompilerTests` (new contract required in the 2.1 text),
  `SceneImageEditPromptCompilerResolverTests` (current and historical 2.1 versions both resolve; the 2511
  assertion that the 2.1-only rules stay out of the 2511 text still holds).

## Validated

- [x] `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore` — 0 warnings, 0 errors.
- [x] `dotnet build DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore` — 0 errors.
- [x] Focused: `QwenSceneImageEditPromptCompilerTests`, `QwenImage21EditPromptCompilerTests`,
      `SceneImageEditPromptCompilerResolverTests`, `SceneImageEditCompilationJobTests`,
      `MediaEditJobHandlerTests`, `MediaEditCompilationServiceTests` — 84 passed, 0 failed.
- [x] Full suite: 4236 passed / 27 failed. All 27 failures are in an unrelated, pre-existing workstream
      (`CharacterLoraCoveragePlanGeneratorTests` 24, `CharacterLoraCellTemplateSeedTests`,
      `CharacterLoraCoverageSchemaTests`); none of those tests or their subjects reference the compiler, the
      resolver, or the edit-compilation path. Reported, not caused by this change.
- [ ] Pending operator acceptance: re-run an intent-only pose edit and confirm the compiled prompt is a
      full instruction, and re-run the JSON-leak intent to confirm it now fails with the explicit diagnostic
      instead of sending JSON to the editor.
