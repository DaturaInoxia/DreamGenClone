# Contract: `ILocationDetectionService`

**Location**: `DreamGenClone.Application/RolePlay/Abstractions/ILocationDetectionService.cs`
**Implementation**: `DreamGenClone.Web/Application/RolePlay/LocationDetectionService.cs`

---

## Interface Definition

```csharp
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.RolePlay.Abstractions;

/// <summary>
/// Detects the current scene location from recent interaction narrative via an LLM call.
/// Used as a fire-and-forget background job in the adaptive pipeline; never blocks
/// foreground turn generation.
///
/// No-fallback contract: <see cref="DetectAsync"/> returns <c>Success = false</c>
/// (without mutating prior state) when no model is configured for
/// <see cref="AppFunction.RolePlayLocationDetection"/>, the LLM call fails, the
/// response times out, or the JSON output fails to parse. The caller MUST preserve the
/// previous <c>CurrentSceneLocation</c> value in those cases — NEVER fall back to regex
/// detection or guessed values.
/// </summary>
public interface ILocationDetectionService
{
    /// <summary>
    /// Synchronously detects the current scene location. Resolves the model via
    /// <see cref="IModelResolutionService.ResolveAsync"/>, builds the LLM prompt, calls
    /// the completion client, and parses / validates the JSON response.
    ///
    /// Timeout: the model's configured <c>ProviderTimeoutSeconds</c> applies — no
    /// additional hard timeout at this service layer by default.
    /// </summary>
    /// <param name="request">Detection input — recent interactions, known scenario
    /// location names, previous location, character names.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="LocationDetectionResult"/> with <see cref="LocationDetectionResult.Success"/>
    /// set to <c>false</c> and <see cref="LocationDetectionResult.ErrorMessage"/> populated
    /// on failure; otherwise with <see cref="LocationDetectionResult.DetectedLocation"/>
    /// populated from one of <paramref name="request"/>'s <c>ScenarioLocationNames</c>.
    /// </returns>
    Task<LocationDetectionResult> DetectAsync(
        LocationDetectionRequest request,
        CancellationToken cancellationToken = default);
}
```

---

## DTOs (in the implementation file)

Defined in `DreamGenClone.Web/Application/RolePlay/Models/LocationDetectionModels.cs` (full schema documented in [data-model.md](../data-model.md)):

- `LocationDetectionRequest` — `SessionId`, `RecentInteractions`, `ScenarioLocationNames`, `PreviousLocation`, `CharacterNames`
- `LocationDetectionResult` — `Success`, `ErrorMessage`, `DetectedLocation`, `LocationConfidence`, `PerCharacterLocations`, `LocationChanged`, `Reasoning`, `RawModelOutput`, `PromptSystem`, `PromptUser`

---

## LLM Prompt Schema

### System Message

```
You detect the current scene location from roleplay narrative text.
Output ONLY strict JSON. Never include markdown.
Schema: {"detectedLocation": "<name or null>", "confidence": 0.0, "perCharacterLocations": {"<characterName>": "<locationName or null>"}, "reasoning": "<one sentence>"}
Rules:
- detectedLocation MUST be one of the provided scenarioLocationNames, or null if no location is clearly identifiable.
- confidence is a decimal in [0,1]; values below 0.5 should return null for detectedLocation.
- perCharacterLocations is optional; when present, each value MUST also be one of scenarioLocationNames or null.
- If recentInteractions consistently reference the previousLocation with no transition language, return detectedLocation = previousLocation.
- If recentInteractions describe a transition (e.g., "we drive to"), set detectedLocation to the destination.
- Never invent a location name; only use scenarioLocationNames.
```

### User Message

```
sessionId=<sessionId>
previousLocation=<previousLocation or "(none)">
scenarioLocationNames=[<names joined>]
characterNames=[<names joined>]
recentInteractions=<condensed text>
```

---

## Behavior Contract (Non-negotiable)

| Scenario | Result | Side Effect |
|---|---|---|
| Model resolved + LLM returns valid JSON with one of `scenarioLocationNames` | `Success=true`, `DetectedLocation=<name>` | Caller writes `CurrentSceneLocation` to DB |
| Model not configured → `ModelResolutionException` caught | `Success=false`, `ErrorMessage=<exception.Message>` | Caller logs warning, leaves `CurrentSceneLocation` unchanged |
| LLM call throws (timeout, network, auth) | Service rethrows — caller (the `LocationDetectionJobHandler` inside `SemanticBackgroundJobWorker`) catches and marks the job failed | Caller logs failure, `CurrentSceneLocation` unchanged |
| JSON parses but violates schema (e.g., unknown location name) | `Success=false`, `ErrorMessage="parse error..."` | Caller leaves `CurrentSceneLocation` unchanged |

**Forbidden patterns**: No regex fallback path. No silent detection of "Living Room", "Bedroom", or other generic words. No "best-guess" mode.

---

## DI Registration

`DreamGenClone.Web/Program.cs`:

```csharp
builder.Services.AddScoped<ILocationDetectionService, LocationDetectionService>();
builder.Services.AddScoped<IBackgroundJobHandler, LocationDetectionJobHandler>(); // background worker dispatch
```

---

## Caller (`RolePlayEngineService`)

The adaptive pipeline (~L3891) replaces:

```csharp
var sceneLocationSignal = _enableLocationServices
    ? await DetectSceneLocationSignalAsync(session, v2State, cancellationToken)
    : null;
```

with:

```csharp
if (_enableLocationServices)
{
    EnqueueLocationDetectionJob(session);   // fire-and-forget; no await
}
```

The new helper serialises `LocationDetectionJobPayload { SessionId = session.Id }`, calls `_backgroundJobQueue.Enqueue(BackgroundJobTypes.LocationDetection, payloadJson, dedupeKey: $"location:{session.Id}")`, and returns immediately. The next turn reads `CurrentSceneLocation` from the persisted V2 state.

---

## Job Handler (`LocationDetectionJobHandler`)

`DreamGenClone.Web/Application/RolePlay/LocationDetectionJobHandler.cs`

Implements `IBackgroundJobHandler` with `JobType => BackgroundJobTypes.LocationDetection`.

Behavior:
1. Check `RolePlayDecisionOptions.EnableLocationServices`; if false, log Information and return (no work)
2. Deserialize `LocationDetectionJobPayload` from `job.PayloadJson`
3. Load fresh `AdaptiveScenarioState` + `RolePlaySession` from `RolePlayStateRepository`
4. Build `LocationDetectionRequest` from last 3 NPC/Custom interactions + scenario locations + previous location
5. Call `_locationDetectionService.DetectAsync(request, ct)`
6. On `Success=true`:
   - Apply `UpsertTrueLocation` per entry of `PerCharacterLocations`
   - Call `UpdatePerceivedLocationsFromTruth(state)`
   - Update `state.CurrentSceneLocation = result.DetectedLocation`
   - `await _stateRepository.SaveAdaptiveStateAsync(state, ct)` to persist
   - Emit `LocationDetectionCompleted` debug event with source (`LLM`), detected location, previous location, confidence
7. On `Success=false`:
   - Log warning with `ErrorMessage`
   - Leave `state.CurrentSceneLocation` unchanged
   - Emit `LocationDetectionSkipped` debug event with reason

Idempotency: handler reads fresh state and only writes if `Success=true` and the new location differs from the previous one. Reenqueue safety provided by the queue dedupe key (`$"location:{sessionId}"`).