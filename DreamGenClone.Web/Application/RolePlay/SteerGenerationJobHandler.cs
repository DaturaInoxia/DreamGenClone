using System.Text;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Web.Application.Assistants;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SteerGenerationJobHandler : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRolePlayAssistantService _assistant;
    private readonly ISqlitePersistence _persistence;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly ILogger<SteerGenerationJobHandler> _logger;
    private readonly RolePlayDecisionOptions _decisionOptions;

    public SteerGenerationJobHandler(
        IRolePlayAssistantService assistant,
        ISqlitePersistence persistence,
        IRolePlayDebugEventSink debugEventSink,
        IOptions<RolePlayDecisionOptions> decisionOptions,
        ILogger<SteerGenerationJobHandler> logger)
    {
        _assistant = assistant;
        _persistence = persistence;
        _debugEventSink = debugEventSink;
        _decisionOptions = decisionOptions?.Value ?? new RolePlayDecisionOptions();
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.SteerGeneration;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
    {
        if (!_decisionOptions.EnableAutoSteer)
        {
            _logger.LogInformation(
                "Skipping steer generation job {JobId}; RolePlayDecision:EnableAutoSteer is false.",
                job.JobId);
            return;
        }

        var payload = JsonSerializer.Deserialize<SteerGenerationJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Steer generation job payload is missing or invalid.");

        if (string.IsNullOrWhiteSpace(payload.SessionId))
            throw new InvalidOperationException("Steer generation job payload is missing SessionId.");

        if (payload.CharacterSnapshots.Count == 0)
        {
            _logger.LogInformation("Skipping steer generation for session {SessionId}; no characters.", payload.SessionId);
            return;
        }

        var genPrompt = BuildGenerationPrompt(payload);

        string genResponse;
        try
        {
            var context = new RolePlayAssistantContext
            {
                SessionId = payload.SessionId,
                ScenarioSummary = payload.ScenarioSummary,
                RecentInteractions = payload.RecentInteractionTexts,
                CharacterSummaries = payload.CharacterSummaries,
                CurrentNarrativePhase = payload.Phase
            };

            genResponse = await _assistant.GenerateSuggestionAsync(
                context, genPrompt,
                assistantModelId: string.IsNullOrWhiteSpace(payload.SessionModelId) ? null : payload.SessionModelId,
                assistantTemperature: payload.SessionTemperature,
                assistantTopP: payload.SessionTopP,
                assistantMaxTokens: payload.SessionMaxTokens,
                appFunction: AppFunction.RolePlaySteering);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SteerGeneration job failed SessionId={SessionId} Error={Error}",
                payload.SessionId, ex.Message);

            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = payload.SessionId,
                EventKind = "SteerGenerationFailed",
                Severity = "Warning",
                Summary = $"Steer generation threw: {ex.GetType().Name}: {ex.Message}",
                MetadataJson = JsonSerializer.Serialize(new { error = ex.Message, errorType = ex.GetType().Name })
            }, cancellationToken);

            // Persist a failed record so the UI doesn't spin forever.
            var failRecord = new SteeringGenerationRecord
            {
                SessionId = payload.SessionId,
                GenerationPrompt = genPrompt,
                GenerationResponse = string.Empty,
                Succeeded = false,
                ErrorMessage = ex.Message,
                ActiveThemeId = payload.PrimaryThemeId,
                Phase = payload.Phase,
                ModelIdentifier = payload.SessionModelId,
                CharacterSnapshotJson = BuildSnapshotJson(payload),
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            await _persistence.SaveSteeringGenerationRecordAsync(failRecord, cancellationToken);
            return;
        }

        var parsed = SteerGenerationParser.TryParse(genResponse);
        var record = new SteeringGenerationRecord
        {
            SessionId = payload.SessionId,
            GenerationPrompt = genPrompt,
            GenerationResponse = genResponse,
            ParsedOptionsJson = parsed is not null ? JsonSerializer.Serialize(parsed) : null,
            CharacterSnapshotJson = BuildSnapshotJson(payload),
            ActiveThemeId = payload.PrimaryThemeId,
            Phase = payload.Phase,
            ModelIdentifier = payload.SessionModelId,
            Succeeded = parsed is not null && parsed.Characters.Count > 0,
            ErrorMessage = parsed is null ? "Failed to parse generation response" : null,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        await _persistence.SaveSteeringGenerationRecordAsync(record, cancellationToken);

        _logger.LogInformation(
            "SteerGeneration completed SessionId={SessionId} Succeeded={Succeeded} Characters={CharCount}",
            payload.SessionId, record.Succeeded, parsed?.Characters.Count ?? 0);

        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = payload.SessionId,
            EventKind = "SteerGenerationCompleted",
            Severity = "Information",
            Summary = record.Succeeded
                ? $"Steer options generated for {parsed!.Characters.Count} characters"
                : "Steer generation failed to parse",
            MetadataJson = JsonSerializer.Serialize(new
            {
                succeeded = record.Succeeded,
                characterCount = parsed?.Characters.Count ?? 0,
                error = record.ErrorMessage
            })
        }, cancellationToken);
    }

    private static string BuildSnapshotJson(SteerGenerationJobPayload payload)
    {
        var chars = payload.CharacterSnapshots.Select(c => new
        {
            characterId = c.CharacterId,
            characterName = c.CharacterName,
            role = c.Role,
            stats = new { c.Desire, c.Restraint, c.Dominance, c.Loyalty, c.SelfRespect },
            encounterDimensions = c.EncounterDimensions
        }).ToList();
        return JsonSerializer.Serialize(new { characters = chars });
    }

    private string BuildGenerationPrompt(SteerGenerationJobPayload payload)
    {
        var sb = new StringBuilder();
        var phase = payload.Phase ?? "Setup";

        sb.AppendLine("You are generating steering options for EVERY active steerable character in the current scene.");
        sb.AppendLine();

        sb.AppendLine($"Current phase: {phase}");

        // Theme context
        if (!string.IsNullOrWhiteSpace(payload.ThemeLabel))
        {
            sb.AppendLine($"Active theme: {payload.ThemeLabel} ({payload.PrimaryThemeId}).");
            if (!string.IsNullOrWhiteSpace(payload.ThemeDescription))
            {
                sb.AppendLine($"Theme description: {payload.ThemeDescription.Trim()}");
            }
            if (payload.ThemePhaseGuidanceLines.Count > 0)
            {
                sb.AppendLine($"Theme phase guidance for {phase}:");
                foreach (var line in payload.ThemePhaseGuidanceLines)
                {
                    sb.AppendLine($"- {line}");
                }
            }
            sb.AppendLine("Every option must clearly align with the active theme and this phase guidance.");
        }
        else if (!string.IsNullOrWhiteSpace(payload.PrimaryThemeId))
        {
            sb.AppendLine($"Active theme ID: {payload.PrimaryThemeId}");
        }
        sb.AppendLine();

        foreach (var c in payload.CharacterSnapshots)
        {
            sb.AppendLine($"--- Character: {c.CharacterName} ---");
            sb.AppendLine($"  ID: {c.CharacterId}");
            sb.AppendLine($"  Role: {c.Role ?? "unassigned"}");

            if (!string.IsNullOrWhiteSpace(c.Role))
            {
                sb.AppendLine($"  Role context: {SteerRoleIntentCatalog.GetRoleContext(c.Role)}");
            }

            // Stats
            sb.AppendLine("Target character's current state:");

            // Behavioral dimension texts from EncounterDimensions.
            if (c.EncounterDimensions is { Count: > 0 }
                && !string.IsNullOrWhiteSpace(c.Role)
                && (string.Equals(c.Role, "Wife", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Role, "Husband", StringComparison.OrdinalIgnoreCase)))
            {
                var dimensions = BehavioralDimensionCatalog.GetDimensions(c.Role);
                if (dimensions.Count > 0)
                {
                    sb.AppendLine("  Behavioral dimensions:");
                    foreach (var dim in dimensions)
                    {
                        var value = c.EncounterDimensions.TryGetValue(dim.Name, out var dv) ? (int)dv : 50;
                        var tierText = BehavioralDimensionCatalog.ResolveTierText(c.Role, dim.Name, value);
                        if (!string.IsNullOrWhiteSpace(tierText))
                        {
                            sb.AppendLine($"    {dim.Name} ({value})");
                            sb.AppendLine($"    {tierText}");
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(c.Role))
            {
                sb.AppendLine("  State text:");
                try
                {
                    sb.AppendLine($"    Desire: {CharacterStatTextCatalog.ResolveText("Desire", c.Role, c.Desire)}");
                    sb.AppendLine($"    Restraint: {CharacterStatTextCatalog.ResolveText("Restraint", c.Role, c.Restraint)}");
                    sb.AppendLine($"    Dominance: {CharacterStatTextCatalog.ResolveText("Dominance", c.Role, c.Dominance)}");
                    sb.AppendLine($"    Loyalty: {CharacterStatTextCatalog.ResolveText("Loyalty", c.Role, c.Loyalty)}");
                    sb.AppendLine($"    SelfRespect: {CharacterStatTextCatalog.ResolveText("SelfRespect", c.Role, c.SelfRespect)}");
                }
                catch { /* Band text may fail for unknown roles. */ }
            }

            if (!string.IsNullOrWhiteSpace(c.Role))
            {
                sb.AppendLine("  Direction intents:");
                sb.AppendLine($"    AWAY   — {SteerRoleIntentCatalog.GetIntent(c.Role, SteerDirection.Away)}");
                sb.AppendLine($"    NEUTRAL — {SteerRoleIntentCatalog.GetIntent(c.Role, SteerDirection.Neutral)}");
                sb.AppendLine($"    TOWARDS — {SteerRoleIntentCatalog.GetIntent(c.Role, SteerDirection.Towards)}");
                sb.AppendLine($"    HARD   — {SteerRoleIntentCatalog.GetIntent(c.Role, SteerDirection.Hard)}");
            }
            sb.AppendLine();
        }

        // Recent interactions
        if (payload.RecentInteractionTexts.Count > 0)
        {
            sb.AppendLine("Recent scene context (abbreviated):");
            foreach (var text in payload.RecentInteractionTexts)
            {
                sb.AppendLine(text);
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(payload.CurrentLocation))
        {
            sb.AppendLine($"Current location: {payload.CurrentLocation}");
            sb.AppendLine();
        }

        sb.AppendLine("Generate exactly 4 steering directives for EACH character above, in this fixed order:");
        sb.AppendLine("  1. AWAY");
        sb.AppendLine("  2. NEUTRAL");
        sb.AppendLine("  3. TOWARDS");
        sb.AppendLine("  4. HARD");
        sb.AppendLine();
        sb.AppendLine("Constraints:");
        sb.AppendLine("- Each option MUST be one sentence, concrete and actionable, grounded in the recent scene.");
        sb.AppendLine("- Use the character's name, never a pronoun. Say 'Ken does X' not 'He does X'.");
        sb.AppendLine("- Each character's options must reflect that character's role intent, current state, and the way the other active characters affect the situation.");
        sb.AppendLine($"- Stay in the {phase} phase; do not advance the phase.");
        sb.AppendLine("- Do not merge characters into one directive. Return a distinct four-option set for every character.");
        sb.AppendLine();
        sb.AppendLine("Return ONLY JSON matching this shape:");
        sb.AppendLine("{ \"characters\": [");
        sb.AppendLine("  { \"characterId\": \"...\", \"characterName\": \"...\", \"role\": \"...\",");
        sb.AppendLine("    \"options\": { \"away\": \"...\", \"neutral\": \"...\", \"towards\": \"...\", \"hard\": \"...\" } }");
        sb.AppendLine("] }");
        sb.AppendLine("No markdown, no labels, no extra text.");

        return sb.ToString();
    }
}
