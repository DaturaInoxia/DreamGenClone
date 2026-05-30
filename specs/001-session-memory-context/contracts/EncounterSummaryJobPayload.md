# Contract: `EncounterSummaryJobPayload`

**Location**: `DreamGenClone.Application/RolePlay/EncounterSummaryJobPayload.cs`
**Used by**: `EncounterSummaryJobHandler` (Infrastructure), `RolePlayEngineService` (Web/Application)

---

## Type Definition

```csharp
/// <summary>
/// Payload for the EncounterSummaryEnhancement background job.
/// One job is enqueued per arc transition (Climax→Reset). The handler
/// reads all arc interactions once and generates per-character LLM prose
/// in a single LLM call, writing one LlmSummary update per character row.
/// </summary>
public sealed class EncounterSummaryJobPayload
{
    public string SessionId { get; set; } = string.Empty;

    /// <summary>The CycleIndex of the completed arc to summarize.</summary>
    public int CycleIndex { get; set; }
}
```

---

## Enqueue Pattern

```csharp
var payload = JsonSerializer.Serialize(new EncounterSummaryJobPayload
{
    SessionId  = session.SessionId,
    CycleIndex = lifecycle.CycleIndex
});

_backgroundJobQueue!.Enqueue(
    BackgroundJobTypes.EncounterSummaryEnhancement,
    payload,
    dedupeKey: $"enc-summary:{session.SessionId}:{lifecycle.CycleIndex}");
```

---

## Handler Entry Point

```csharp
public sealed class EncounterSummaryJobHandler : IBackgroundJobHandler
{
    public string JobType => BackgroundJobTypes.EncounterSummaryEnhancement;

    public async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<EncounterSummaryJobPayload>(payloadJson)
            ?? throw new InvalidOperationException("Null payload for EncounterSummaryEnhancement job");

        // Load summary, load arc interactions, call LLM, write LlmSummary
        // On any exception: log Warning, return normally
    }
}
```

---

## Notes

- `SummaryId` uniquely identifies the row in `RolePlayV2EncounterSummaries`. The handler loads the record before processing to verify it is an `ArcCompletion` type.
- The deduplication key `$"enc-summary:{SummaryId}:{CharacterId}"` prevents double-processing if the engine hook fires more than once for the same transition (engine re-evaluation edge case).
- `SessionId` is included in the payload for efficient session-scoped queries (load arc interactions by `SessionId + CycleIndex`).
