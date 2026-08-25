# Phase 1B Contracts - Vision-Aware Image Editing

## Multimodal Transport

```csharp
public interface IMultimodalCompletionClient
{
    Task<MultimodalCompletionResult> GenerateAsync(
        ResolvedMultimodalModel model,
        MultimodalCompletionRequest request,
        CancellationToken cancellationToken);
}
```

The request contains ordered text/image parts, a response schema, and explicit generation values.
The client validates configured media limits before transmission. It does not resize silently,
change models, strip images, or retry against another endpoint. Any normalization creates a
declared derived input with dimensions and checksum in provenance.

## Compiler

```csharp
public interface ISceneImageEditPromptCompiler
{
    SceneImageEditCompilerMessages BuildMessages(SceneImageEditCompilerContext context);
    SceneImageEditCompilationResult Parse(string rawResponse);
}
```

`BuildMessages` supplies the official Qwen-derived edit rules, source image, raw intent, optional
clarification history, and schema version. `Parse` is strict and deterministic. The compiler does
not call Qwen Image Edit or mutate persistence.

## Compilation Schema

Known root fields are `schemaVersion`, `status`, `sourceSummary`, `targets`, `requestedChanges`,
`preserve`, `clarificationQuestion`, `invalidReason`, and `compiledPrompt`.

Status rules:

- `ready`: executable fields are complete; clarification and invalid reason are null.
- `clarification_required`: exactly one question is present; compiled prompt is null.
- `invalid`: explicit reason is present; compiled prompt is null.

Targets use visible descriptions and optional normalized regions. They cannot rely solely on story
names unless the visible source or trusted source metadata establishes that mapping.

## Resolution

`IMultimodalModelResolutionService.ResolveAsync(AppFunction, ...)` resolves exactly one configured
model with image-input capability and compatible content policy. Missing capability, endpoint,
model, credentials, image limits, or generation settings fails before attempt creation. The
compiler and future validator use different `AppFunction` values even if they point to one model.

## Orchestration

`SceneImageEditService.EnqueueCompilationAsync`:

1. Loads and validates the source image and checksum.
2. Resolves compiler configuration fail-fast.
3. Creates one pending attempt with immutable input snapshots.
4. Enqueues `SceneImageEditPromptCompilation` using attempt ID as the dedupe key.

The handler transitions monotonically, invokes the multimodal client once, persists raw response,
strictly parses it, and writes the terminal result. Provider errors and malformed output are
`Failed`; semantic impossibility is `Invalid`.

`SceneImageEditService.EnqueueEditAsync` requires source image ID, ready attempt ID, exact prompt
revision ID, and their hashes. It creates the existing Qwen edit child and copies the accepted
prompt into `PromptSnapshot`. There is one active decision path; raw intent cannot reach the editor.

## Staleness

An attempt is stale when source checksum, raw intent, clarification history, compiler schema,
model/config snapshot, or selected revision no longer matches the execution request. Stale work is
visible but cannot execute. Recompilation creates a new attempt.

## Runtime Contract

The application sees configured HTTP providers on one pod, not SSH or RunPod credentials. The
Juggernaut, Qwen Edit, and Qwen VL artifacts remain installed on that pod's persistent volume.
Separate runtime directories isolate dependencies only; they are not separate hosts.

GPU residency is an operator/deployment concern with explicit health checks. If all models cannot
remain loaded simultaneously, a pinned same-pod coordinator or explicit procedure loads/unloads the
required model and proves its endpoint ready before the application job runs. Endpoint absence
fails; the app does not start another pod or select a substitute model.

## Logging and Privacy

Structured debug events record attempt/session/source IDs, source checksum, model/config/schema
versions, prompt hashes, timings, statuses, and errors. Source bytes and base64/data URLs are never
written to logs, SQLite debug metadata, or exception messages.