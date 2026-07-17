# Contract: `IActorSelectionService`

**Location**: `DreamGenClone.Application/RolePlay/Abstractions/IActorSelectionService.cs`
**Implementation**: `DreamGenClone.Web/Application/RolePlay/ActorSelectionService.cs`

---

## Interface Definition

```csharp
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.RolePlay.Abstractions;

/// <summary>
/// Selects which characters should speak on an overflow continue click.
/// Returns an ordered list of candidate names from the available character pool.
///
/// Decision path priority:
/// 1. If cache fingerprint matches the previous selection's fingerprint AND
///    <see cref="ActorSelectionRequest.CacheKey"/> matches the stored ordering → return
///    cached ordering rotated by recency (<see cref="ActorSelectionSource.Cache"/>).
/// 2. If a model is configured for <see cref="AppFunction.RolePlayActorSelection"/> →
///    call LLM, parse &amp; validate JSON, cache ordering → <see cref="ActorSelectionSource.LLM"/>.
/// 3. No model configured → return scoring-ordered candidates unchanged →
///    <see cref="ActorSelectionSource.Scoring"/> (base path; NOT a fallback).
/// 4. Model resolution or LLM call/parse fails → return scoring-ordered candidates
///    unchanged with <see cref="ActorSelectionSource.Fallback"/> (logged explicitly).
///
/// No-fallback compliance: scoring IS the base path, never a hidden alternate.
/// <see cref="ActorSelectionSource.Fallback"/> is reserved for "LLM call was attempted
/// and failed" cases, distinct from "no model configured" cases.
/// </summary>
public interface IActorSelectionService
{
    /// <summary>
    /// Selects up to <paramref name="request"/>'s <c>BatchSize</c> characters from
    /// <paramref name="request"/>'s <c>Candidates</c>, in the order they should speak.
    /// </summary>
    /// <param name="request">Selection input — narrative summary, candidates,
    /// themes, events, phase, location, time, batch size, and a fingerprint key
    /// for cache lookup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="ActorSelectionResponse"/> with <c>OrderedNames</c> populated
    /// from the resolved source; <c>Success = false</c> only when the LLM was attempted
    /// and failed — scoring-path returns <c>Success = true</c>.</returns>
    Task<ActorSelectionResponse> SelectActorsAsync(
        ActorSelectionRequest request,
        CancellationToken cancellationToken = default);
}
```

---

## DTOs (in `DreamGenClone.Web/Application/RolePlay/Models/ActorSelectionModels.cs`)

Full schema in [data-model.md](../data-model.md).

- `ActorSelectionRequest` — `SessionId`, `NarrativeSummary`, `CurrentPhase`, `CurrentLocation`, `CurrentTimeOfDay`, `Candidates` (`IReadOnlyList<ActorCandidateInfo>`), `ActiveThemes`, `RecentSemanticEvents`, `BatchSize`. Includes a `CacheKey` property computed by the caller (composite fingerprint per R10).

- `ActorCandidateInfo` — `Name`, `Role`, `IsInScene`, `AffinityStatus`, `TimeOfDayMatch`, `KeyStats`, `LastSpokeTurnsAgo`, `BaseScore`, `AffinityDetails`

- `ActorSelectionResponse` — `Success`, `ErrorMessage`, `OrderedNames`, `Reasoning`, `Source`, `RawModelOutput`, `PromptSystem`, `PromptUser`

- `ActorSelectionSource` enum — `LLM`, `Cache`, `Scoring`, `Fallback`

---

## LLM Prompt Schema

### System Message

```
You are a narrative director selecting which characters speak next in a roleplay story.
Output ONLY strict JSON. Never include markdown.
Schema: {"characters": ["Name1", "Name2", ...], "reasoning": "<one or two sentences>"}
Rules:
- Characters MUST be a subset of the provided candidates (case-sensitive names).
- Order characters by dramatic importance to the current scene.
- Select at most <batchSize> characters.
- Prefer characters who are in-scene and who haven't spoken recently.
- Honor affinity hints: "Required" characters should ALWAYS be included; "Excluded"
  candidates are not in the list (filtered upstream); "Preferred" is a hint.
- Honor time-of-day match: prefer characters whose affinity time-of-day matches
  the current time.
- Use the baseScore as a hint, NOT as the sole determinant.
- For characters with low baseScore but high narrative relevance, you may include
  them; explain your reasoning.
- The persona ("You" POV character) is NOT in this list — it is inserted separately
  by the engine.
- If no character is a good fit, return an empty "characters" array and explain.
```

### User Message

```
sessionId=<sessionId>
currentPhase=<phase>
currentLocation=<location or "(unknown)">
currentTimeOfDay=<timeOfDay or "(unknown)">
narrativeSummary=<last ~3 interactions condensed>

activeThemes=<theme IDs or names joined>
recentSemanticEvents=<last 3 event IDs with actor names joined>

candidates (in score-desc order):
- Name: <name>, Role: <role>, InScene: <bool>, Affinity: <status>, TimeMatch: <match>, LastSpokeTurnsAgo: <n>, BaseScore: <num>, AffinityDetails: <text>
... (one line per candidate)
```

### Response Parsing

Strict JSON via `JsonSerializer.Deserialize<ActorSelectionEnvelope>` with `JsonSerializerDefaults.Web`. Envelope shape:

```csharp
private sealed class ActorSelectionEnvelope
{
    public List<string>? Characters { get; set; }
    public string? Reasoning { get; set; }
}
```

Validation:
- Each name in `Characters` MUST exist in `request.Candidates.Select(c => c.Name)` (case-sensitive match — case mismatch produces a parse failure)
- `Characters.Count` MUST be ≤ `request.BatchSize`
- Empty array is valid (handled by the caller as "no one sensible to pick")

---

## Behavior Contract (Non-negotiable)

| Scenario | `Success` | `Source` | Side Effect |
|---|---|---|---|
| Cache hit: fingerprint matches stored `LastContextFingerprint` AND `LastActorOrdering` non-null | `true` | `Cache` | Caller rotates `LastActorOrdering` by recency and returns; no LLM call, no error log |
| Cache miss, model configured, LLM call succeeds | `true` | `LLM` | Caller persists `LastActorOrdering` + `LastContextFingerprint` on `RolePlaySession` (in memory only — transient per data-model.md) |
| Cache miss, model configured, LLM fails / times out / unknown error | `false` | `Fallback` | Caller uses scoring-ordered candidates; logs warning with `ErrorMessage`; DOES NOT persist cache (next click retries LLM) |
| Cache miss, no model configured (`ModelResolutionException` caught) | `true` | `Scoring` | Caller uses scoring-ordered candidates; logs Information-level "no model configured" hint; does NOT persist cache (deterministic path) |
| LLM returns names not in candidate set | `false` | `Fallback` | Same as LLM-failure case; logged with the parse-error message |

**Forbidden patterns**:
- Hardcoded hidden default model when none configured → use scoring only (Source=`Scoring`)
- Silent retry logic in this service layer → retries belong in callers, if anywhere (V1: no retries)
- Substituting a different `AppFunction` model slot → only `RolePlayActorSelection` is queried

---

## Caller (`RolePlayEngineService.ResolveSceneContinueActorsAsync`)

After B-056 aftermath guard + `GetAllowedActors`:

```csharp
var availableCharacters = ResolveAvailableCharacters(session, scenario, currentSceneLocation, v2State);
if (availableCharacters.Count == 0) { /* fall through to existing empty fallback at L2461 */ }

var scored = availableCharacters
    .Select(c => (Character: c, Score: ScoreActorForAutoSelection(c, v2State, recentActors, session.CharacterTurnOverrides)))
    .ToList();

var request = new ActorSelectionRequest
{
    SessionId = session.Id,
    NarrativeSummary = BuildNarrativeSummary(session, lastN: 3),
    CurrentPhase = v2State.CurrentPhase.ToString(),
    CurrentLocation = currentSceneLocation,
    CurrentTimeOfDay = v2State.CurrentTimeOfDay?.ToString(),
    Candidates = BuildCandidateInfos(scored),
    ActiveThemes = BuildActiveThemes(v2State),
    RecentSemanticEvents = BuildRecentSemanticEvents(v2State),
    BatchSize = Math.Clamp(session.SceneContinueBatchSize, 1, 6),
    CacheKey = BuildFingerprint(v2State, availableCharacters)
};

var selection = await _actorSelectionService.SelectActorsAsync(request, cancellationToken);

var ordered = MapOrderedNamesToCandidates(selection.OrderedNames, availableCharacters);
// ordered may be empty if LLM returned empty array or all candidates were filtered

if (selection.Source == ActorSelectionSource.LLM || selection.Source == ActorSelectionSource.Cache)
{
    session.LastActorOrdering = ordered;
    session.LastContextFingerprint = request.CacheKey;
}

// Persona insertion rules (preserved verbatim from existing L2435-2470):
// - totalInteractions < 6 → Insert(0)
// - else if ObservedTurnCount % 2 == 0 → Add() at end (last before narrative)
// - else skip persona
```

---

## DI Registration

`DreamGenClone.Web/Program.cs`:

```csharp
builder.Services.AddScoped<IActorSelectionService, ActorSelectionService>();
```

---

## Logging Contract

All paths emit `Information`-level structured logs with these properties for追溯:

| Event | Level | Properties |
|---|---|---|
| Request sent to LLM | `Information` | `SessionId`, `Function`, `Model`, `Provider`, `CandidateCount`, `NarrativeLength`, `BatchSize`, `CacheKey` |
| Response received from LLM | `Information` | `SessionId`, `Model`, `Provider`, `ElapsedMs`, `Source`, `OutputLength`, `Reasoning` |
| Cache hit | `Debug` | `SessionId`, `CacheKey`, `Returned` |
| Model not configured → Scoring | `Information` | `SessionId`, `Function`, `Source="Scoring"` |
| LLM failure → Fallback | `Warning` | `SessionId`, `Function`, `ErrorType`, `ErrorMessage`, `Source="Fallback"` |
| Parse failure → Fallback | `Warning` | `SessionId`, `Function`, `ParseError`, `Source="Fallback"`, `RawOutputLength` |

In addition, a `OverflowActorSelection` debug event is written via `RolePlayDebugEventSink` for the failed/incomplete paths, including per-candidate score breakdown when LLM is unavailable.