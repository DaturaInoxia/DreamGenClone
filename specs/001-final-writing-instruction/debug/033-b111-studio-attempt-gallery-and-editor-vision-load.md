# B-111 Studio Attempt Gallery and Editor Vision Load

## Report

On 2026-09-06, the user reported that the Production Studio attempt list was constrained to a narrow area, did not show every image created, and lacked readily accessible actions including editing. The user also requested that opening the scene-image editor immediately make the configured vision-edit model reachable and enable the same Qwen native multi-reference identity workflow used by the proof path.

No single session or interaction ID was supplied with the report. The visible symptoms were a horizontally constrained attempt strip that omitted Composition-stage images and an editor that did not automatically begin source-image description generation.

## Analysis

The attempt deck in `SceneImageStudio.razor.css` was restricted to grid column 2, leaving it constrained to the central canvas column. The stage loop in `SceneImageStudio.razor` enumerated Identity and Finish only, which omitted Composition attempts from the rendered gallery. Completed cards had command handling, but the constrained deck made access poor and did not expose a direct route to edit a specific image.

`SceneImageEditor.razor` already resolves exact configured editor models, passes the selected `EditorModelId` on dispatch, and exposes approved identity references through `ReferenceApplyPanel` with the `NativeMultiReference` capability. Unlike the established asset editor, however, it did not enqueue source description generation when loaded. The configured vision model could therefore remain unused until another interaction.

The B-111 native Qwen mechanism remains correctly named `NativeMultiReference`, not IP-Adapter. Its sole resolution path remains `ReferenceStrategyResolver`, which requires the exact model/provider declaration and a real qualification record. The Qwen model is declared but has no formal qualification, so submission remains honestly unavailable until an endpoint proof completes.

Relevant feature and parent specifications were consulted: `specs/001-final-writing-instruction/spec.md` and `specs/001-rp-prompt-redesign/spec.md`.

## Plan

1. Make Studio actions and the attempt deck span the full production workspace grid.
2. Render Composition, Identity, and Finish attempt stages and provide a direct editor route for each completed attempt.
3. On scene-image editor load, enqueue source description generation and enable normal polling, matching the established asset-image editor behavior.
4. Add focused source-contract coverage for the gallery, editor initialization, selected model dispatch, and native reference surface; restore stale durable-job test fakes required to compile the test project.

## Resolution

- `SceneImageStudio.razor.css`: made production canvas actions and the attempt gallery span `grid-column: 1 / -1`.
- `SceneImageStudio.razor`: added Composition to the rendered stage sequence and added a direct `/roleplay/image-editor/{sessionId}/{interactionId}/{attemptId}` edit link to complete attempt cards.
- `SceneImageEditor.razor`: enqueues `CompilationService.EnqueueDescriptionAsync(_editSession.Id)`, records the pending state, refreshes, and begins polling after successful initialization.
- `SceneImageEditingJobHandler.cs`: corrected reference-processing diagnostics to identify the scene-image edit path rather than the Finish path.
- `SceneImageStudioUiContractTests.cs`: added Studio/editor contract coverage and updated stale assertions for the current inline Run Tray, Composition Composer, and cancel-confirmation architecture.
- `FrozenAdapters.cs` and eight existing test fakes: implemented the added durable-job `TryActivateAsync` interface member with their existing recording semantics.

## Validated

- [x] `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore --nologo -c Release` completed with 0 errors (119 existing warnings).
- [x] `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore --nologo -c Release --filter "FullyQualifiedName~SceneImageStudioUiContractTests"` passed: 19 total, 19 passed, 0 failed, 0 skipped.
- [ ] Pending user confirmation in a live Production Studio browser session that full-width gallery layout, all stages, direct edit links, and editor auto-analysis behave as intended.
- [ ] Pending formal Qwen endpoint proof and capability qualification before Native Multi-Reference Edit submission may be enabled.