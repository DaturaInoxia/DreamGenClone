using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.Sessions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class EncounterSummaryJobHandler : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISessionService _sessionService;
    private readonly IRolePlayStateRepository _stateRepository;
    private readonly IEncounterSummaryService _encounterSummaryService;
    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly RolePlayMemoryOptions _memoryOptions;
    private readonly ILogger<EncounterSummaryJobHandler> _logger;

    public EncounterSummaryJobHandler(
        ISessionService sessionService,
        IRolePlayStateRepository stateRepository,
        IEncounterSummaryService encounterSummaryService,
        ICompletionClient completionClient,
        IModelResolutionService modelResolutionService,
        IOptions<RolePlayMemoryOptions> memoryOptions,
        ILogger<EncounterSummaryJobHandler> logger)
    {
        _sessionService = sessionService;
        _stateRepository = stateRepository;
        _encounterSummaryService = encounterSummaryService;
        _completionClient = completionClient;
        _modelResolutionService = modelResolutionService;
        _memoryOptions = memoryOptions.Value;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.EncounterSummaryEnhancement;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
    {
        if (!_memoryOptions.EnableLlmSummaryEnhancement)
        {
            _logger.LogInformation(
                "Skipping encounter summary enhancement job {JobId}; RolePlayMemory:EnableLlmSummaryEnhancement is false.",
                job.JobId);
            return;
        }

        var payload = JsonSerializer.Deserialize<EncounterSummaryJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Encounter summary job payload is missing or invalid.");

        if (string.IsNullOrWhiteSpace(payload.SessionId))
            throw new InvalidOperationException("Encounter summary job payload is missing SessionId.");

        _logger.LogInformation(
            "EncounterSummaryJobHandler started for session {SessionId} cycle {CycleIndex} type {SummaryType} recordId {SummaryId}",
            payload.SessionId, payload.CycleIndex, payload.SummaryType, payload.SummaryId ?? "(batch-legacy)");

        var allSummaries = await _stateRepository.LoadEncounterSummariesForSessionAsync(payload.SessionId, cancellationToken);

        // Resolve the specific record(s) to enhance
        List<EncounterSummaryRecord> recordsToEnhance;
        if (!string.IsNullOrWhiteSpace(payload.SummaryId))
        {
            var target = allSummaries.FirstOrDefault(s => s.Id == payload.SummaryId);
            if (target is null)
            {
                _logger.LogDebug(
                    "EncounterSummaryJobHandler: record {SummaryId} not found for session {SessionId}; skipping.",
                    payload.SummaryId, payload.SessionId);
                return;
            }
            recordsToEnhance = [target];
        }
        else
        {
            // Legacy fallback: process all ArcCompletion records for the cycle
            recordsToEnhance = allSummaries
                .Where(s => s.SummaryType == EncounterSummaryType.ArcCompletion && s.CycleIndex == payload.CycleIndex)
                .ToList();
        }

        if (recordsToEnhance.Count == 0)
        {
            _logger.LogDebug(
                "EncounterSummaryJobHandler: no records to enhance for session {SessionId} cycle {CycleIndex}.",
                payload.SessionId, payload.CycleIndex);
            return;
        }

        ResolvedModel resolvedModel;
        try
        {
            resolvedModel = await _modelResolutionService.ResolveAsync(AppFunction.RolePlaySemanticAnalysis, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "EncounterSummaryJobHandler: model resolution failed for session {SessionId} cycle {CycleIndex}.",
                payload.SessionId, payload.CycleIndex);
            return;
        }

        // Build context shared across records in this batch
        var session = await _sessionService.LoadRolePlaySessionAsync(payload.SessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{payload.SessionId}' was not found for encounter summary enhancement.");

        var recentInteractions = session.Interactions
            .Where(x => !x.IsExcluded)
            .TakeLast(30)
            .Select(x => $"[{x.InteractionType}] {x.ActorName}: {x.Content}")
            .ToList();

        // Phase summaries for this arc (used as structured input for arc completion consolidation)
        var arcPhaseSummaries = allSummaries
            .Where(s => s.SummaryType == EncounterSummaryType.PhaseMilestone
                     && s.CycleIndex == payload.CycleIndex
                     && s.LlmSummary is not null)
            .OrderBy(s => s.OccurredUtc)
            .ToList();

        foreach (var record in recordsToEnhance)
        {
            if (record.LlmEnhancedUtc.HasValue)
            {
                _logger.LogDebug(
                    "EncounterSummaryJobHandler: skipping already-enhanced record {RecordId}.",
                    record.Id);
                continue;
            }

            var prompt = record.SummaryType == EncounterSummaryType.PhaseMilestone
                ? BuildMilestonePrompt(record, recentInteractions)
                : BuildArcCompletionPrompt(record, arcPhaseSummaries, recentInteractions);

            await EnhanceRecordAsync(record, prompt, resolvedModel, payload, cancellationToken);
        }
    }

    private static string BuildMilestonePrompt(
        EncounterSummaryRecord record,
        List<string> recentInteractions)
    {
        var interactionsText = recentInteractions.Count > 0
            ? string.Join("\n", recentInteractions)
            : "(no interactions available)";

        return $"""
            You are writing a short memory record for a roleplay session.

            Character: {record.CharacterId}
            Phase transition: {record.FromPhase} → {record.ToPhase} (Arc {record.CycleIndex + 1})
            Scene: {(string.IsNullOrWhiteSpace(record.SceneLocation) ? "unknown location" : record.SceneLocation)}

            Recent story interactions:
            {interactionsText}

            Write 1-2 sentences describing specifically what {record.CharacterId} did or experienced during the {record.FromPhase} phase before this transition. Focus on physical actions, emotional state, and who was involved. Be specific and concrete. Write in third person past tense. Do not summarize the whole story — only what happened in this phase.
            """;
    }

    private static string BuildArcCompletionPrompt(
        EncounterSummaryRecord record,
        List<EncounterSummaryRecord> arcPhaseSummaries,
        List<string> recentInteractions)
    {
        string contextBlock;
        if (arcPhaseSummaries.Count > 0)
        {
            var phaseSummaryText = string.Join("\n\n", arcPhaseSummaries.Select(s =>
                $"[{s.FromPhase} phase — {s.CharacterId}]\n{s.LlmSummary}"));
            contextBlock = $"""
                Phase-by-phase memories for this arc:
                {phaseSummaryText}
                """;
        }
        else
        {
            var interactionsText = recentInteractions.Count > 0
                ? string.Join("\n", recentInteractions)
                : "(no interactions available)";
            contextBlock = $"""
                Arc interactions (in order):
                {interactionsText}
                """;
        }

        return $"""
            You are writing a consolidated memory record for a roleplay session.

            Character: {record.CharacterId}
            Arc {record.CycleIndex + 1} — full arc summary

            {contextBlock}

            Write 3-4 sentences from {record.CharacterId}'s perspective summarizing the complete arc: how the encounter began, what physical acts took place across all phases (note positions, partners, sequence), and how it concluded. This is a permanent memory that will be referenced in future sessions to ensure continuity. Be specific and explicit. Write in third person past tense.
            """;
    }

    private async Task EnhanceRecordAsync(
        EncounterSummaryRecord record,
        string prompt,
        ResolvedModel resolvedModel,
        EncounterSummaryJobPayload payload,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var llmSummary = await _completionClient.GenerateAsync(prompt, resolvedModel, cancellationToken);
                if (!string.IsNullOrWhiteSpace(llmSummary))
                {
                    await _encounterSummaryService.UpdateLlmSummaryAsync(record.Id, llmSummary.Trim(), DateTime.UtcNow, cancellationToken);
                    _logger.LogInformation(
                        "Encounter summary LLM enhancement complete: {RecordId} type={SummaryType} charId={CharacterId} session={SessionId} cycle={CycleIndex}",
                        record.Id, record.SummaryType, record.CharacterId, payload.SessionId, payload.CycleIndex);
                }
                return;
            }
            catch (Exception ex) when (attempt == 0)
            {
                _logger.LogWarning(ex,
                    "EncounterSummaryJobHandler: LLM call failed on first attempt for record {RecordId}; retrying after 5s.",
                    record.Id);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "EncounterSummaryJobHandler: LLM call failed on second attempt for record {RecordId} session {SessionId} cycle {CycleIndex}. Abandoning.",
                    record.Id, payload.SessionId, payload.CycleIndex);
                return;
            }
        }
    }
}
