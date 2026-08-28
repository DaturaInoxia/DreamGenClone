# Contract: `IEncounterSummaryService`

**Location**: `DreamGenClone.Application/RolePlay/Abstractions/IEncounterSummaryService.cs`
**Implementation**: `DreamGenClone.Infrastructure/RolePlay/EncounterSummaryService.cs`

---

## Interface Definition

```csharp
/// <summary>
/// Generates, persists, and retrieves per-character encounter summaries.
/// Summaries are written at phase transitions (PhaseMilestone) and at arc
/// completion (ArcCompletion). ArcCompletion entries are later enriched by the
/// EncounterSummaryJobHandler with LLM-generated intimate act prose.
/// </summary>
public interface IEncounterSummaryService
{
    /// <summary>
    /// Generates template-based EncounterSummaryRecord instances for every character
    /// in the current session state at the given phase transition. Does NOT persist.
    /// Returns one record per character in v2State.CharacterSnapshots.
    /// Returns empty list if CharacterSnapshots is empty (no throw).
    /// SummaryType is ArcCompletion when transitionEvent.ToPhase == NarrativePhase.Reset,
    /// otherwise PhaseMilestone.
    /// </summary>
    Task<IReadOnlyList<EncounterSummaryRecord>> GenerateTemplatesAsync(
        NarrativePhaseTransitionEvent transitionEvent,
        AdaptiveScenarioState v2State,
        RolePlaySession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a single EncounterSummaryRecord to RolePlayV2EncounterSummaries.
    /// TemplateSummary must not be null or empty.
    /// </summary>
    Task SaveAsync(
        EncounterSummaryRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the LLM-generated summary to an existing record.
    /// Called only by EncounterSummaryJobHandler.
    /// </summary>
    Task UpdateLlmSummaryAsync(
        string summaryId,
        string llmSummary,
        DateTime llmEnhancedUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all ArcCompletion entries for the session plus the last
    /// maxMilestones PhaseMilestone entries for the given currentCycleIndex.
    /// Ordered: arc completions (ascending by OccurredUtc) first, then
    /// milestones (ascending by OccurredUtc).
    /// Returns empty list if none exist.
    /// </summary>
    Task<IReadOnlyList<EncounterSummaryRecord>> LoadForSessionAsync(
        string sessionId,
        int maxMilestones,
        int currentCycleIndex,
        CancellationToken cancellationToken = default);
}
```

---

## Notes

- `GenerateTemplatesAsync` is synchronous in nature (no I/O); `Task` return is for interface consistency with the rest of the service layer. Implementation may return `ValueTask.FromResult(...)` internally.
- `SaveAsync` is called once per character per transition — the caller iterates and calls individually (not bulk insert).
- `UpdateLlmSummaryAsync` is only ever called from `EncounterSummaryJobHandler`. No other code path writes `LlmSummary`.
- `LoadForSessionAsync` is called from `RolePlayStateRepository` during session load, and optionally from `RolePlayContinuationService` if the in-memory `AdaptiveScenarioState.EncounterSummaries` needs a refresh.
