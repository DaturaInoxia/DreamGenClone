using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
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
            // All memory enrichment (PhaseMilestone, ArcCompletion, EncounterCompletion) uses the
            // dedicated summary-enhancement slot. PhaseMilestone prompts include up to 30 recent
            // interactions and can exceed the RolePlaySemanticAnalysis model's context, so they
            // must not share that slot. Interaction-phase semantic analysis
            // (SemanticEventInferenceService) stays on RolePlaySemanticAnalysis.
            resolvedModel = await _modelResolutionService.ResolveAsync(
                AppFunction.RolePlaySummaryEnhancement,
                cancellationToken: cancellationToken);
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

        foreach (var record in recordsToEnhance)
        {
            if (record.LlmEnhancedUtc.HasValue)
            {
                _logger.LogDebug(
                    "EncounterSummaryJobHandler: skipping already-enhanced record {RecordId}.",
                    record.Id);
                continue;
            }

            var prompt = record.SummaryType switch
            {
                EncounterSummaryType.PhaseMilestone =>
                    BuildMilestonePrompt(record, recentInteractions),
                EncounterSummaryType.ArcCompletion =>
                    BuildArcCompletionPrompt(record, BuildCharacterMemorySet(allSummaries, record), recentInteractions),
                EncounterSummaryType.EncounterCompletion =>
                    BuildEncounterCompletionPrompt(record, session),
                _ => throw new InvalidOperationException($"Unsupported summary type {record.SummaryType}")
            };

            if (prompt is null)
            {
                _logger.LogInformation(
                    "EncounterSummaryJobHandler: skipping record {RecordId} for character {CharacterId} — no interactions in range [{StartIdx}-{EndIdx}]",
                    record.Id, record.CharacterId, record.StartInteractionIndex, record.EndInteractionIndex);
                continue;
            }

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
        var statsContext = GetStatsContext(record, "Character stats at transition");

        return $"""
            You are writing a durable phase memory for {record.CharacterId} in an ongoing role-play.
            This is a memory-generation task, not a scene response: you are producing a compact internal record of one phase, not continuing the story.

            Write from inside {record.CharacterId}'s mind as this phase closes, recalling what this stretch of the story meant to them. Use {record.CharacterId}'s own inner voice and emotional register. Be specific, concrete, and sensory. The memory will be injected into future prompts to maintain continuity, so it must stand alone as a self-contained record.

            Phase transition: {record.FromPhase} → {record.ToPhase} (Arc {record.CycleIndex + 1})
            Scene: {(string.IsNullOrWhiteSpace(record.SceneLocation) ? "unknown location" : record.SceneLocation)}
            {statsContext}
            Phase record — story material for this phase (for reference only; do not repeat verbatim):
            Recent story interactions:
            {interactionsText}

            ## INSTRUCTIONS

            Write 4-6 sentences from {record.CharacterId}'s perspective recalling the {record.FromPhase} phase before this transition. Capture:
            1. What happened — the key physical actions and beats across this phase.
            2. What they felt — the dominant emotional texture and how it shifted as the phase unfolded.
            3. Who was involved — which characters shaped this phase and how they interacted.
            4. What shifted — how the dynamic, the tension, or their own state moved during this phase.
            5. What stands out — the single moment or realization that defined this phase.

            Rules:
            - Write in {record.CharacterId}'s voice — first person, past-tense reflection.
            - Be specific and concrete; favor lived detail over summary.
            - Cover this phase only — do not summarize the whole story.
            - Do not mention this memory system or the act of remembering. Just be the memory.
            - Output only the memory — no headings, labels, or extra text.
            """;
    }

    private static string BuildArcCompletionPrompt(
        EncounterSummaryRecord record,
        IReadOnlyList<EncounterSummaryRecord> characterMemories,
        List<string> recentInteractions)
    {
        var statsContext = GetStatsContext(record, "Character stats at close");

        string contextBlock;
        if (characterMemories.Count > 0)
        {
            var memoryText = string.Join("\n", characterMemories.Select(s =>
                $"[{GetMemoryLabel(s)}] {s.LlmSummary}"));
            contextBlock = $"""
                {record.CharacterId}'s memory records so far (oldest to newest) — for reference only; do not repeat verbatim:
                {memoryText}
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
            You are writing a durable arc memory for {record.CharacterId} in an ongoing role-play.
            This is a memory-generation task, not a scene response: you are producing a permanent internal record of a complete arc, not continuing the story.

            Write from inside {record.CharacterId}'s mind as the arc closes, looking back on everything that happened from beginning to end. Use {record.CharacterId}'s own inner voice and emotional register. Be specific, concrete, and sensory. The memory will be injected into future prompts and carried into future sessions to maintain continuity, so it must stand alone as a self-contained record.

            Character: {record.CharacterId}
            Arc {record.CycleIndex + 1} — full arc summary
            Scene: {(string.IsNullOrWhiteSpace(record.SceneLocation) ? "unknown location" : record.SceneLocation)}
            {statsContext}
            Source material — arc record (for reference only; do not repeat verbatim):
            {contextBlock}

            ## INSTRUCTIONS

            Write 5-7 sentences from {record.CharacterId}'s perspective recalling the complete arc. Capture:
            1. What happened — how the arc began and how the encounter unfolded across all phases.
            2. What they felt — the dominant emotional texture and how it evolved from start to finish.
            3. Who was involved — which characters shaped the arc and how they interacted.
            4. Physical specifics — positions, partners, sequence, and where his release occurred.
            5. What changed — how the relationship dynamic or their self-image shifted by the end.
            6. The aftermath — the feeling left behind as the arc concluded.

            Rules:
            - Write in {record.CharacterId}'s voice — first person, past-tense reflection.
            - Be specific and explicit; favor concrete memory over summary.
            - This is a permanent memory — it will be referenced in future sessions to ensure continuity.
            - Do not mention this memory system or the act of remembering. Just be the memory.
            - Output only the memory — no headings, labels, or extra text.
            """;
    }

    /// <summary>
    /// B-058: Gather every enriched memory the character has accumulated so far (phase
    /// milestones, encounter completions, and prior arc completions) oldest → newest, so the
    /// arc consolidation prompt has the full picture of this character's remembered history.
    /// Excludes the record currently being written.
    /// </summary>
    private static List<EncounterSummaryRecord> BuildCharacterMemorySet(
        IReadOnlyList<EncounterSummaryRecord> allSummaries,
        EncounterSummaryRecord current)
    {
        return allSummaries
            .Where(s => string.Equals(s.CharacterId, current.CharacterId, StringComparison.OrdinalIgnoreCase)
                     && s.LlmSummary is not null
                     && s.Id != current.Id)
            .OrderBy(s => s.OccurredUtc)
            .ToList();
    }

    private static string GetMemoryLabel(EncounterSummaryRecord s) => s.SummaryType switch
    {
        EncounterSummaryType.PhaseMilestone => $"Phase {s.FromPhase} → {s.ToPhase}, Arc {s.CycleIndex + 1}",
        EncounterSummaryType.EncounterCompletion => $"Encounter {s.EncounterNumber}, Arc {s.CycleIndex + 1}",
        EncounterSummaryType.ArcCompletion => $"Arc {s.CycleIndex + 1} completion",
        _ => "Memory"
    };

    private static string GetStatsContext(EncounterSummaryRecord record, string header)
    {
        var statsLine = GetStatsLine(record);
        return string.IsNullOrWhiteSpace(statsLine)
            ? ""
            : $"{header}: {statsLine}\n";
    }

    private static string GetStatsLine(EncounterSummaryRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.CharacterStatsSnapshotJson))
            return "";
        try
        {
            var snapshot = JsonSerializer.Deserialize<CharacterStatProfileV2>(
                record.CharacterStatsSnapshotJson, JsonOptions);
            return snapshot is null
                ? ""
                : $"Desire {snapshot.Desire}, Restraint {snapshot.Restraint}";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// Rewritten enrichment prompt per contracts/encounter-enrichment-contract.md.
    /// Captures 8 dimensions (FR-033) using Narrative response as primary source (FR-035).
    /// </summary>
    /// <returns>Null when character has zero interactions in the encounter range — no memory to generate.</returns>
    private static string? BuildEncounterCompletionPrompt(
        EncounterSummaryRecord record,
        RolePlaySession session)
    {
        var rangeCount = Math.Max(0, record.EndInteractionIndex - record.StartInteractionIndex + 1);
        var allEncounterInteractions = session.Interactions
            .Skip(record.StartInteractionIndex)
            .Take(rangeCount)
            .Where(x => !x.IsExcluded)
            .ToList();

        // All interactions in the encounter range (in order) — used as context for enrichment.
        var allContextText = string.Join("\n", allEncounterInteractions
            .Where(x => !x.IsExcluded)
            .Select(x => $"[{x.InteractionType}] {x.ActorName}: {x.Content}"));

        // Use all encounter context as the narrative account (FR-035: omniscient view of the encounter).
        var narrativeResponseText = allContextText.Length > 0
            ? allContextText
            : "(no interactions recorded for this encounter)";

        // Character responses for this character's perspective.
        var characterResponses = allEncounterInteractions
            .Where(x => string.Equals(x.ActorName, record.CharacterId, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Content?.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();

        if (characterResponses.Count == 0 && string.IsNullOrWhiteSpace(allContextText))
            return null; // No content = no memory to generate

        var characterResponseTexts = characterResponses.Count > 0
            ? string.Join("\n", characterResponses.Select(r => $"[{record.CharacterId}]: {r}"))
            : $"(no direct responses recorded for {record.CharacterId})";

        // Previous encounter summaries for comparison (dimension 6).
        var previousSummaries = session.AdaptiveState?.EncounterSummaries
            ?.Where(s => s.EncounterNumber < record.EncounterNumber
                      && s.LlmSummary is not null)
            .OrderBy(s => s.EncounterNumber)
            .ToList();

        string previousEncounterContext;
        if (previousSummaries is { Count: > 0 })
        {
            var previousLines = previousSummaries.Select(s =>
                $"Encounter {s.EncounterNumber} ({s.CharacterId}): {s.LlmSummary!.Trim()}");
            previousEncounterContext = "Previous encounters:\n" + string.Join("\n", previousLines) + "\n";
        }
        else
        {
            previousEncounterContext = "";
        }

        // ── Build 8-dimension enrichment prompt per contract ──
        return $"""
            You are writing a private, first-person memory for {record.CharacterId} in an ongoing role-play.
            This is a memory-generation task, not a scene response: you are producing a durable internal record, not continuing the story.

            Write from inside {record.CharacterId}'s mind after the encounter has ended — {record.CharacterId} looking back on what just happened. Use {record.CharacterId}'s own inner voice, vocabulary, and emotional register. Be specific, concrete, and sensory; think and feel from the inside, not narrate from the outside. The finished memory will be injected into future prompts to maintain continuity across encounters, so it must stand alone as one self-contained paragraph.

            Encounter {record.EncounterNumber} at {(string.IsNullOrWhiteSpace(record.SceneLocation) ? "unknown location" : record.SceneLocation)}.

            Source material — encounter record (for reference only; do not repeat verbatim):
            Narrative account (omniscient):
            {narrativeResponseText}

            {record.CharacterId}'s responses during this encounter:
            {characterResponseTexts}

            {previousEncounterContext}
            ## INSTRUCTIONS

            Write a 3-5 sentence first-person memory from {record.CharacterId}'s perspective that captures:
            1. What happened — the key physical and emotional beats of this encounter.
            2. What they felt — the dominant emotional texture (guilt, thrill, shame, desire, satisfaction).
            3. What they learned — any sexual self-knowledge gained: what felt good, what surprised them, what they want again.
            4. What changed — how this encounter shifted the relationship dynamic or their self-image.
            5. What risk was taken — any near-miss, discovery risk, or boundary crossed.
            6. Sexual comparison — if this is not the first encounter, how it compared to previous ones (more confident? more guilty? more physically intense?).
            7. Comparison to husband and past experiences — how this encounter measured up against her marriage and her broader sexual history.
            8. Physical specifics — name the actual positions and movements from the encounter (e.g., bent over the table, on hands and knees, legs stretched wide), capture her climax as it truly happened, and record where his release occurred (e.g., inside her, across her skin, in her mouth). These belong in the memory itself as concrete, lived detail — not as descriptive writing direction.

            Rules:
            - Write in {record.CharacterId}'s voice — first person, past-tense reflection.
            - Be specific and sensory; favor concrete memory over summary.
            - Weave the dimensions into one flowing 3-5 sentence paragraph — do not number them or write a checklist.
            - Do not mention this memory system, this prompt, or the act of remembering. Just be the memory.
            - Output only the memory paragraph — no headings, labels, or extra text.
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
                _logger.LogInformation(
                    "EncounterSummaryJobHandler: enrichment request record={RecordId} type={SummaryType} charId={CharacterId} promptChars={PromptLen}",
                    record.Id, record.SummaryType, record.CharacterId, prompt.Length);
                _logger.LogInformation(
                    "EncounterSummaryJobHandler: enrichment prompt record={RecordId}{NewLine}--- PROMPT ---{NewLine}{Prompt}{NewLine}--- END PROMPT ---",
                    record.Id, Environment.NewLine, prompt, Environment.NewLine);

                // Reasoning-aware path — mirrors SemanticEventInferenceService and
                // RolePlayContinuationService. Separates reasoning_content from the final
                // content (a plain GenerateAsync would fall back to persisting the model's
                // chain-of-thought as the summary when it spends its whole budget on
                // reasoning and emits empty content). If only reasoning comes back, the
                // client issues a focused force-answer follow-up.
                var (content, reasoning) = await _completionClient.StreamGenerateWithReasoningAsync(
                    prompt, resolvedModel, _ => Task.CompletedTask, cancellationToken);
                var llmSummary = content ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(llmSummary)
                    && llmSummary.Length > _memoryOptions.MaxLlmSummaryChars)
                {
                    _logger.LogWarning(
                        "EncounterSummaryJobHandler: enrichment response rejected (exceeds MaxLlmSummaryChars={MaxChars}) record={RecordId} type={SummaryType} charId={CharacterId} responseChars={ResponseLen}; keeping template summary.",
                        _memoryOptions.MaxLlmSummaryChars, record.Id, record.SummaryType, record.CharacterId, llmSummary.Length);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(llmSummary))
                {
                    _logger.LogInformation(
                        "EncounterSummaryJobHandler: enrichment response record={RecordId} responseChars={ResponseLen} reasoningChars={ReasoningLen}",
                        record.Id, llmSummary.Length, reasoning?.Length ?? 0);
                    _logger.LogInformation(
                        "EncounterSummaryJobHandler: enrichment response record={RecordId}{NewLine}--- RESPONSE ---{NewLine}{Response}{NewLine}--- END RESPONSE ---",
                        record.Id, Environment.NewLine, llmSummary, Environment.NewLine);
                    await _encounterSummaryService.UpdateLlmSummaryAsync(record.Id, llmSummary.Trim(), DateTime.UtcNow, prompt, cancellationToken);
                    _logger.LogInformation(
                        "Encounter summary LLM enhancement complete: {RecordId} type={SummaryType} charId={CharacterId} session={SessionId} cycle={CycleIndex}",
                        record.Id, record.SummaryType, record.CharacterId, payload.SessionId, payload.CycleIndex);
                }
                else
                {
                    _logger.LogWarning(
                        "EncounterSummaryJobHandler: enrichment returned empty response record={RecordId} type={SummaryType} charId={CharacterId}",
                        record.Id, record.SummaryType, record.CharacterId);
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
