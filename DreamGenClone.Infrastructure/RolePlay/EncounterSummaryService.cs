using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class EncounterSummaryService : IEncounterSummaryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRolePlayStateRepository _repository;
    private readonly ILogger<EncounterSummaryService> _logger;

    public EncounterSummaryService(IRolePlayStateRepository repository, ILogger<EncounterSummaryService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task<IReadOnlyList<EncounterSummaryRecord>> GenerateTemplatesAsync(
        NarrativePhaseTransitionEvent transitionEvent,
        AdaptiveScenarioState v2State,
        IReadOnlySet<string>? allowedCharacterIds = null,
        CancellationToken cancellationToken = default)
    {
        // Build the working character list.
        // When allowedCharacterIds is provided, use it as the authoritative list — this ensures
        // records are written even when CharacterSnapshots is empty (e.g. early-arc transitions
        // before the async semantic analysis job has populated snapshots). If a snapshot exists
        // for the character use it for the stats JSON; otherwise fall back to a zero-value stub.
        // When no allowedCharacterIds is provided, fall back to whatever snapshots exist.
        List<(string CharacterId, CharacterStatProfileV2 Snapshot)> characters;
        if (allowedCharacterIds is { Count: > 0 })
        {
            var snapshotMap = v2State.CharacterSnapshots
                .ToDictionary(s => s.CharacterId, StringComparer.OrdinalIgnoreCase);
            characters = allowedCharacterIds
                .Select(id => (id, snapshotMap.TryGetValue(id, out var snap)
                    ? snap
                    : new CharacterStatProfileV2 { CharacterId = id }))
                .ToList();
        }
        else
        {
            if (v2State.CharacterSnapshots.Count == 0)
            {
                _logger.LogDebug(
                    "GenerateTemplatesAsync: no character snapshots for session {SessionId} — returning empty list.",
                    transitionEvent.SessionId);
                return Task.FromResult<IReadOnlyList<EncounterSummaryRecord>>([]);
            }
            characters = v2State.CharacterSnapshots
                .Select(s => (s.CharacterId, s))
                .ToList();
        }

        var isArcCompletion = transitionEvent.ToPhase == NarrativePhase.Reset;
        var summaryType = isArcCompletion ? EncounterSummaryType.ArcCompletion : EncounterSummaryType.PhaseMilestone;
        var records = new List<EncounterSummaryRecord>(characters.Count);

        foreach (var (charId, snapshot) in characters)
        {
            var statsJson = JsonSerializer.Serialize(snapshot, JsonOptions);
            var templateSummary = isArcCompletion
                ? BuildArcCompletionTemplate(snapshot, v2State)
                : BuildPhaseMilestoneTemplate(snapshot, transitionEvent, v2State);

            records.Add(new EncounterSummaryRecord
            {
                Id                         = Guid.NewGuid().ToString("N"),
                SessionId                  = transitionEvent.SessionId,
                CharacterId                = charId,
                SummaryType                = summaryType,
                CycleIndex                 = v2State.CycleIndex,
                FromPhase                  = transitionEvent.FromPhase,
                ToPhase                    = transitionEvent.ToPhase,
                OccurredUtc                = transitionEvent.OccurredUtc,
                InteractionCountInPhase    = v2State.InteractionCountInPhase,
                SceneLocation              = v2State.CurrentSceneLocation,
                ActiveThemeId              = v2State.PrimaryThemeId,
                FinishingMoveId            = null,
                PositionIdsJson            = "[]",
                CharacterStatsSnapshotJson = statsJson,
                TemplateSummary            = templateSummary,
                LlmSummary                 = null,
                LlmEnhancedUtc             = null
            });
        }

        _logger.LogInformation(
            "GenerateTemplatesAsync: generated {Count} {SummaryType} records for session {SessionId} cycle {CycleIndex}",
            records.Count, summaryType, transitionEvent.SessionId, v2State.CycleIndex);
        return Task.FromResult<IReadOnlyList<EncounterSummaryRecord>>(records);
    }

    public async Task SaveAsync(EncounterSummaryRecord record, CancellationToken cancellationToken = default)
    {
        await _repository.SaveEncounterSummaryAsync(record, cancellationToken);
        _logger.LogInformation(
            "Encounter summary saved: {RecordId} type={SummaryType} charId={CharacterId} session={SessionId} cycle={CycleIndex}",
            record.Id, record.SummaryType, record.CharacterId, record.SessionId, record.CycleIndex);
    }

    public Task UpdateLlmSummaryAsync(string summaryId, string llmSummary, DateTime llmEnhancedUtc, CancellationToken cancellationToken = default)
        => _repository.UpdateEncounterSummaryLlmAsync(summaryId, llmSummary, llmEnhancedUtc, cancellationToken);

    public async Task<IReadOnlyList<EncounterSummaryRecord>> LoadForSessionAsync(
        string sessionId,
        int maxMilestones,
        int currentCycleIndex,
        CancellationToken cancellationToken = default)
    {
        var all = await _repository.LoadEncounterSummariesForSessionAsync(sessionId, cancellationToken);

        // All arc completions (across all arcs)
        var arcCompletions = all
            .Where(s => s.SummaryType == EncounterSummaryType.ArcCompletion)
            .ToList();

        // Most recent N milestones for the current arc only
        var milestones = all
            .Where(s => s.SummaryType == EncounterSummaryType.PhaseMilestone && s.CycleIndex == currentCycleIndex)
            .OrderByDescending(s => s.OccurredUtc)
            .Take(maxMilestones)
            .OrderBy(s => s.OccurredUtc)
            .ToList();

        var result = new List<EncounterSummaryRecord>(arcCompletions.Count + milestones.Count);
        result.AddRange(arcCompletions);
        result.AddRange(milestones);
        return result;
    }

    private static string BuildPhaseMilestoneTemplate(
        CharacterStatProfileV2 snapshot,
        NarrativePhaseTransitionEvent transitionEvent,
        AdaptiveScenarioState v2State)
    {
        var location = string.IsNullOrWhiteSpace(v2State.CurrentSceneLocation) ? "unknown" : v2State.CurrentSceneLocation;
        return $"{snapshot.CharacterId} — phase moved from {transitionEvent.FromPhase} to {transitionEvent.ToPhase}. " +
               $"Scene: {location}. Arc {v2State.CycleIndex + 1}, interaction {v2State.InteractionCountInPhase} in phase.";
    }

    private static string BuildArcCompletionTemplate(
        CharacterStatProfileV2 snapshot,
        AdaptiveScenarioState v2State)
    {
        var beatCode = v2State.CurrentBeatCode ?? "unknown";
        var themeName = v2State.PrimaryThemeId ?? "none";
        return $"{snapshot.CharacterId} completed arc {v2State.CycleIndex + 1}. Peak phase: Climax. " +
               $"Beat reached: {beatCode}. " +
               $"Theme: {themeName}. Finishing move: unknown.";
    }
}
