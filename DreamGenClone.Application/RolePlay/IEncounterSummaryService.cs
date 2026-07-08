using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

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
    /// When <paramref name="allowedCharacterIds"/> is non-null, only snapshots whose
    /// CharacterId is contained in that set are included (case-insensitive).
    /// </summary>
    Task<IReadOnlyList<EncounterSummaryRecord>> GenerateTemplatesAsync(
        NarrativePhaseTransitionEvent transitionEvent,
        AdaptiveScenarioState v2State,
        IReadOnlySet<string>? allowedCharacterIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates EncounterCompletion template summaries for every allowed character at the
    /// moment of an encounter-boundary detection (universal — any phase). Stores the encounter
    /// sequence number, the raw detection evidence span, and the inclusive interaction-list
    /// index range of the encounter that just ended. Does NOT persist.
    ///
    /// When <paramref name="allowedCharacterIds"/> is non-null, only those characters are
    /// included; pass CharacterSnapshots keys for NPCs + persona. Returns empty list if
    /// <paramref name="allowedCharacterIds"/> is null or empty (no character list to write).
    /// </summary>
    Task<IReadOnlyList<EncounterSummaryRecord>> GenerateEncounterCompletionTemplatesAsync(
        AdaptiveScenarioState v2State,
        int encounterNumber,
        string detectionEvidence,
        int startInteractionIndex,
        int endInteractionIndex,
        IReadOnlyDictionary<string, string>? characterInteractionsTexts = null,
        IReadOnlySet<string>? allowedCharacterIds = null,
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

