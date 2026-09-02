# Scene Image Pipeline Contract

**Feature**: 001-scene-image-generator  
**Date**: 2026-08-19  
**Spec**: [spec.md](../spec.md) | **Data model**: [data-model.md](../data-model.md)

This document defines the interface contracts the feature exposes and consumes. The feature is a web application feature (Blazor Server pages + background services), so the contracts are: (1) the public service surface the UI calls, (2) the background-job payloads, (3) the image-generation HTTP wire contract, and (4) the debug-event contract.

---

## 1. Public Service Contract — `ISceneImageService`

The UI (studio page, gallery page, workspace) interacts with image generation exclusively through this interface. Implemented in `DreamGenClone.Web/Application/RolePlay/SceneImageService.cs`; registered as a singleton in DI.

```csharp
public interface ISceneImageService
{
    /// <summary>Enqueue preprocessor prompt generation. Fails fast if the session/interaction
    /// is missing or the preprocessor function default is unset. Creates a SceneImagePromptRecord
    /// (Pending) and enqueues a SceneImagePromptGeneration job. Dedupes by record id.</summary>
    Task<SceneImagePromptRecord> EnqueuePromptAsync(
        ScenePromptRequest request, CancellationToken cancellationToken = default);

    /// <summary>Enqueue image rendering from a (possibly edited) prompt. Fails fast if the
    /// session/interaction is missing or the image function default is unset. Creates a
    /// SceneImageRecord (Pending) referencing the prompt record and enqueues a
    /// SceneImageRendering job. Dedupes by record id.</summary>
    Task<SceneImageRecord> EnqueueRenderAsync(
        SceneRenderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Load a prompt record by id.</summary>
    Task<SceneImagePromptRecord?> GetPromptAsync(
        string sessionId, string promptId, CancellationToken cancellationToken = default);

    /// <summary>Most recent prompt record for an interaction (used when reopening the studio).</summary>
    Task<SceneImagePromptRecord?> GetLatestPromptAsync(
        string sessionId, string interactionId, CancellationToken cancellationToken = default);

    /// <summary>All images for one interaction (studio results strip).</summary>
    Task<IReadOnlyList<SceneImageRecord>> ListImagesByInteractionAsync(
        string sessionId, string interactionId, CancellationToken cancellationToken = default);

    /// <summary>All images for a session (gallery page).</summary>
    Task<IReadOnlyList<SceneImageRecord>> ListImagesBySessionAsync(
        string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Interaction → image-count map for a session (workspace indicator). Only counts
    /// Complete images.</summary>
    Task<Dictionary<string, int>> CountImagesByInteractionAsync(
        string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Delete an image: removes the DB row and the file on disk. Idempotent.</summary>
    Task DeleteImageAsync(
        string sessionId, string imageId, CancellationToken cancellationToken = default);
}
```

**Error contract (no-fallback, fail-fast):**
- Missing session/interaction → `InvalidOperationException` with a clear message, before any record is created.
- No `RolePlaySceneImagePreprocessor` function default → `ModelResolutionException` with Model Manager guidance (raised by `ModelResolutionService.ResolveImagePromptModelAsync`).
- No `RolePlaySceneImage` function default / non-image model / non-image provider / `ContentPolicy == Unknown` → `ModelResolutionException` with Model Manager guidance (raised by `ModelResolutionService.ResolveImageModelAsync`).

---

## 2. Background Job Contracts

Two new job types, registered in `BackgroundJobTypes.cs` and handled by two `IBackgroundJobHandler` implementations. Both reuse the existing `GenericBackgroundJobQueue` (unbounded `Channel`, dedupe by key).

### 2.1 `BackgroundJobTypes.SceneImagePromptGeneration`

**Payload** (`SceneImagePromptGenerationJobPayload`, JSON):
```json
{ "SessionId": "<string>", "InteractionId": "<string>", "PromptRecordId": "<string>" }
```

**Handler**: `SceneImagePromptGenerationJobHandler`
1. Load the prompt record; if already `Complete`, skip (idempotent).
2. Load the session + the interaction; fail fast if missing.
3. Resolve the preprocessor text model via `ResolveImagePromptModelAsync` (fail-fast).
4. Build the preprocessor system+user prompt via `ISceneImagePromptPreprocessor`.
5. Call the model via the existing `ICompletionClient`.
6. Parse the output (plain-text-with-JSON-envelope-fallback); validate non-empty + length-capped.
7. Write `OutputPrompt` + `InputExcerpt` + `ModelIdentifier`; set `Status = Complete`; stamp `UpdatedUtc`.
8. On any failure: set `Status = Failed`, write `ErrorMessage`, stamp `UpdatedUtc`, log `LogWarning`.
9. Emit `SceneImagePromptSent` + `SceneImageResponseReceived` debug events.

### 2.2 `BackgroundJobTypes.SceneImageRendering`

**Payload** (`SceneImageRenderingJobPayload`, JSON):
```json
{ "SessionId": "<string>", "InteractionId": "<string>", "ImageRecordId": "<string>" }
```

**Handler**: `SceneImageRenderingJobHandler`
1. Load the image record; if already `Complete`, skip (idempotent).
2. Set `Status = Generating`, stamp `StartedUtc` + `UpdatedUtc`.
3. Resolve the image model via `ResolveImageModelAsync` (fail-fast: function default, `ModelKind == Image`, `ImageCapability != None`, `ContentPolicy != Unknown`).
4. Send `PromptSnapshot` to `IImageGenerationClient.GenerateAsync(resolvedModel, prompt, size, ct)`.
5. On success: save bytes via `ISceneImageStorageService.SaveAsync`; write `FileRelativePath`, `ModelIdentifier`, `ProviderName`, `ContentPolicy`, `ImageSize`, `Style`; set `Status = Complete`; stamp `CompletedUtc` + `UpdatedUtc`.
6. On failure (HTTP error / policy rejection / unusable response): set `Status = Failed`, write `ErrorMessage`, stamp `UpdatedUtc`, log `LogWarning`. Never silently skip; never fall back to another model.
7. Emit `SceneImageResponseReceived` debug event.

**Dedupe**: both enqueue calls use `dedupeKey = $"{jobType}:{recordId}"` so a double-click cannot spawn duplicate in-flight work for the same record.

---

## 3. Image Generation HTTP Wire Contract — `IImageGenerationClient`

```csharp
public interface IImageGenerationClient
{
    /// <summary>Generate an image. Returns the decoded image bytes, or null if the provider
    /// returned no image data. Throws ImageGenerationException on HTTP/policy errors.</summary>
    Task<byte[]?> GenerateAsync(
        ResolvedImageModel model, string prompt, string? size, CancellationToken cancellationToken = default);
}
```

**Request** — `POST {ProviderBaseUrl}{ImageGenerationPath}`:
- Headers: `Authorization: Bearer {decrypted key}` for cloud providers (`ProviderType != LmStudio`); no auth header for local.
- Body (JSON):
  ```json
  {
    "model": "<ModelIdentifier>",
    "prompt": "<prompt>",
    "n": 1,
    "size": "<size or omitted>",
    "response_format": "b64_json",
    "steps": <optional int>
  }
  ```

**Response** (200):
```json
{ "data": [ { "b64_json": "<base64 image bytes>" } ] }
```
The client decodes `data[0].b64_json` via `Convert.FromBase64String` and returns the bytes.

**Error mapping** (mirrors `CompletionClient`):
| HTTP status | Exception | User-facing message |
|---|---|---|
| 401 | `ImageGenerationException` | "Invalid API key for provider X. Update the key in the Model Manager." |
| 429 | `ImageGenerationException` | "Rate limit exceeded for provider X. Wait and retry." |
| 402 | `ImageGenerationException` | "Payment required for provider X. Check your account billing." |
| 5xx | `ImageGenerationException` | "Provider X server error (status Y). Retry later." |
| Body indicates policy/filter rejection | `ImageGenerationException` | "Provider X rejected the prompt (content policy). The image was not generated." |

`ImageGenerationException` carries `ProviderName`, `StatusCode`, and `ReasonCode` for structured logging.

---

## 4. Preprocessor LLM Contract — `ISceneImagePromptPreprocessor`

```csharp
public interface ISceneImagePromptPreprocessor
{
    /// <summary>Build the system+user messages for the preprocessor model.</summary>
    (string SystemPrompt, string UserPrompt) BuildMessages(
        RolePlaySession session,
        RolePlayInteraction interaction,
        AdaptiveScenarioState scenarioState,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? excerptOverride,
        string? refineInstruction);

    /// <summary>Parse the preprocessor output into the editable prompt (+ pulled excerpt).
    /// Tolerates a JSON envelope {prompt, excerpt} or plain text. Fails fast on empty/overlong.</summary>
    SceneImagePreprocessorResult ParseOutput(string rawOutput);
}

public sealed record SceneImagePreprocessorResult(string Prompt, string Excerpt);
```

**Output contract (constitution principle V — JSON-in/JSON-out with fail-fast):**
- Preferred envelope: `{ "prompt": "<image prompt>", "excerpt": "<pulled passage>" }`.
- Fallback: the entire output is treated as `Prompt`, `Excerpt` = empty.
- **Validation**: `Prompt` must be non-empty and ≤ ~2000 chars; otherwise `ParseOutput` throws with an explicit error (never returns empty/invalid output that would silently degrade).

**Content-policy clamp**: when `resolvedPolicy == SfwFiltered`, the system prompt instructs the model to produce a safe-for-work prompt regardless of the `AllowExplicitImage` setting, and the builder appends `"keep fully clothed / non-explicit"`. The clamp is logged (not silently skipped).

---

## 5. Model Resolution Contract — `ModelResolutionService` (additions)

```csharp
// Preprocessor (text): reuses the existing resolution path.
Task<ResolvedModel> ResolveImagePromptModelAsync(
    string? sessionOverrideId = null, CancellationToken cancellationToken = default);

// Renderer (image): new path with capability + policy checks.
Task<ResolvedImageModel> ResolveImageModelAsync(
    string? sessionOverrideId = null, CancellationToken cancellationToken = default);
```

**`ResolveImageModelAsync` decision path (exactly one, no fallback):**
1. Look up `FunctionModelDefault` for `RolePlaySceneImage`. None → throw `ModelResolutionException("No image model configured for RolePlaySceneImage. Add an image model + function default in the Model Manager.")`.
2. Load `RegisteredModel`. If `ModelKind != Image` → throw `ModelResolutionException("Model '<name>' is not an image model. Assign an image-kind model to RolePlaySceneImage.")`.
3. Load `Provider`. If `ImageCapability == None` → throw `ModelResolutionException("Provider '<name>' is not image-capable. Set its image capability in the Model Manager.")`.
4. If `ContentPolicy == Unknown` → throw `ModelResolutionException("Image content policy not configured for provider '<name>'. Set its content policy (SFW-filtered or adult-allowed) in the Model Manager.")`.
5. Return `ResolvedImageModel`.

`ResolveImagePromptModelAsync` fails fast when no `RolePlaySceneImagePreprocessor` function default is configured (reuses the existing `ModelResolutionException` path).

---

## 6. Debug Event Contract

Two new `RolePlayDebugEventRecord` kinds, written via the existing `IRolePlayDebugEventSink`:

| EventKind | When | MetadataJson fields |
|---|---|---|
| `SceneImagePromptSent` | Preprocessor call dispatched | `sessionId`, `interactionId`, `promptRecordId`, `modelIdentifier`, `providerName`, `resolvedPolicy`, `settingsJson`, `systemPrompt`, `userPrompt` |
| `SceneImageResponseReceived` | Preprocessor or image-model response received | `sessionId`, `interactionId`, `recordId`, `stage` (`preprocessor`\|`renderer`), `status`, `rawOutput` (preprocessor) or `bytesLen` (renderer), `errorMessage`, `durationMs` |

These flow through the existing debug-event pipeline and are inspectable in the workspace Debug View and via `QuerySessionEventsAsync` — no new debug infrastructure.

---

## 7. URL / Route Contract

| Route | Component | Purpose |
|---|---|---|
| `/roleplay/studio/{sessionId}/{interactionId}` | `SceneImageStudio.razor` | The two-stage studio for one interaction |
| `/roleplay/gallery/{sessionId}` | `SceneImageGallery.razor` | Per-session image gallery, grouped by interaction |
| `/scene-images/{sessionId}/{imageId}.png` | `UseStaticFiles` (PhysicalFileProvider) | Static image file serving (not a component) |

The workspace's per-interaction "Generate image" action navigates to the studio route. The studio and gallery link back to `/roleplay/workspace/{sessionId}`.

---

## 8. Storage Contract — `ISceneImageStorageService`

```csharp
public interface ISceneImageStorageService
{
    /// <summary>Save image bytes. Returns the relative path "{sessionId}/{imageId}.png".</summary>
    Task<string> SaveAsync(
        string sessionId, string fileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Open a stored image for reading.</summary>
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Delete a stored image. Idempotent (no-op if absent).</summary>
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}
```

Root from `PersistenceOptions.SceneImageRoot` (default `data/scene-images/`, git-ignored). Mirrors `ITemplateImageStorageService`.
