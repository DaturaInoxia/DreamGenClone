using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SemanticEventInferenceService : ISemanticEventInferenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly ILogger<SemanticEventInferenceService>? _logger;

    public SemanticEventInferenceService(
        ICompletionClient completionClient,
        IModelResolutionService modelResolutionService,
        ILogger<SemanticEventInferenceService>? logger = null)
    {
        _completionClient = completionClient;
        _modelResolutionService = modelResolutionService;
        _logger = logger;
    }

    public async Task<SemanticEventInferenceResult> InferAsync(
        SemanticEventInferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var resolved = await _modelResolutionService.ResolveAsync(
            AppFunction.RolePlayGeneration,
            cancellationToken: cancellationToken);

        var systemMessage =
            "You extract canonical semantic event IDs from roleplay text. " +
            "Output ONLY strict JSON. Never include markdown. " +
            "Schema: {\"events\":[{\"eventId\":\"id\",\"confidence\":0.0,\"actorName\":\"name\",\"targetCharacterName\":\"name\",\"evidenceSpan\":\"text\"}]}. " +
            "Rules: Use ONLY event IDs from allowedEventIds. If no event applies, return {\"events\":[]}. " +
            "confidence must be decimal in [0,1].";

        var allowedJson = JsonSerializer.Serialize(request.AllowedEventIds, JsonOptions);
        var contextJson = JsonSerializer.Serialize(request.ContextTurns, JsonOptions);
        var userMessage =
            $"sessionId={request.SessionId}\n" +
            $"interactionId={request.InteractionId}\n" +
            $"actorName={request.ActorName}\n" +
            $"allowedEventIds={allowedJson}\n" +
            $"contextTurns={contextJson}\n" +
            $"interactionText={request.InteractionText}";

        _logger?.LogInformation(
            "SemanticInference REQUEST SessionId={SessionId} InteractionId={InteractionId} Actor={Actor} Model={Provider}/{ModelId} AllowedEventIdsCount={AllowedCount} ContextTurns={ContextTurnsCount} InteractionTextLen={TextLen}\n--- SYSTEM ---\n{System}\n--- USER ---\n{User}",
            request.SessionId,
            request.InteractionId,
            request.ActorName,
            resolved.ProviderName,
            resolved.ModelIdentifier,
            request.AllowedEventIds.Count,
            request.ContextTurns.Count,
            (request.InteractionText ?? string.Empty).Length,
            systemMessage,
            userMessage);

        var stopwatch = Stopwatch.StartNew();
        string output;
        try
        {
            output = await _completionClient.GenerateAsync(systemMessage, userMessage, resolved, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(
                ex,
                "SemanticInference FAILED SessionId={SessionId} InteractionId={InteractionId} Model={Provider}/{ModelId} ElapsedMs={ElapsedMs} ErrorType={ErrorType} Error={Error}",
                request.SessionId,
                request.InteractionId,
                resolved.ProviderName,
                resolved.ModelIdentifier,
                stopwatch.ElapsedMilliseconds,
                ex.GetType().Name,
                ex.Message);
            throw;
        }
        stopwatch.Stop();

        _logger?.LogInformation(
            "SemanticInference RESPONSE SessionId={SessionId} InteractionId={InteractionId} Model={Provider}/{ModelId} ElapsedMs={ElapsedMs} OutputLen={OutputLen}\n--- RAW OUTPUT ---\n{Output}",
            request.SessionId,
            request.InteractionId,
            resolved.ProviderName,
            resolved.ModelIdentifier,
            stopwatch.ElapsedMilliseconds,
            (output ?? string.Empty).Length,
            output ?? string.Empty);

        IReadOnlyList<SemanticInferredEvent> parsed;
        try
        {
            parsed = ParseAndValidate(output ?? string.Empty, request.AllowedEventIds);
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "SemanticInference PARSE-FAILED SessionId={SessionId} InteractionId={InteractionId} Error={Error}\n--- RAW OUTPUT ---\n{Output}",
                request.SessionId,
                request.InteractionId,
                ex.Message,
                output ?? string.Empty);
            throw;
        }

        _logger?.LogInformation(
            "SemanticInference PARSED SessionId={SessionId} InteractionId={InteractionId} EventCount={EventCount} Events={Events}",
            request.SessionId,
            request.InteractionId,
            parsed.Count,
            string.Join(", ", parsed.Select(e => $"{e.EventId}@{e.Confidence.ToString(CultureInfo.InvariantCulture)}")));

        return new SemanticEventInferenceResult
        {
            Events = parsed,
            RawModelOutput = output ?? string.Empty,
            PromptSystem = systemMessage,
            PromptUser = userMessage
        };
    }

    private static IReadOnlyList<SemanticInferredEvent> ParseAndValidate(string modelOutput, IReadOnlyList<string> allowedEventIds)
    {
        InferenceEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<InferenceEnvelope>(modelOutput, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Semantic inference returned invalid JSON.", ex);
        }

        if (envelope?.Events is null)
        {
            throw new InvalidOperationException("Semantic inference JSON did not include required 'events' array.");
        }

        var allowed = new HashSet<string>(allowedEventIds, StringComparer.OrdinalIgnoreCase);
        var results = new List<SemanticInferredEvent>(envelope.Events.Count);

        foreach (var candidate in envelope.Events)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.EventId))
            {
                throw new InvalidOperationException("Semantic inference event is missing eventId.");
            }

            var eventId = candidate.EventId.Trim();
            if (!allowed.Contains(eventId))
            {
                throw new InvalidOperationException($"Semantic inference produced unknown event id '{eventId}'.");
            }

            var confidence = candidate.Confidence;
            if (confidence < 0m || confidence > 1m)
            {
                throw new InvalidOperationException(
                    $"Semantic inference confidence for event '{eventId}' is out of range [0,1]: {confidence.ToString(CultureInfo.InvariantCulture)}.");
            }

            results.Add(new SemanticInferredEvent
            {
                EventId = eventId,
                Confidence = confidence,
                ActorName = string.IsNullOrWhiteSpace(candidate.ActorName) ? null : candidate.ActorName.Trim(),
                TargetCharacterName = string.IsNullOrWhiteSpace(candidate.TargetCharacterName) ? null : candidate.TargetCharacterName.Trim(),
                EvidenceSpan = string.IsNullOrWhiteSpace(candidate.EvidenceSpan) ? null : candidate.EvidenceSpan.Trim()
            });
        }

        return results;
    }

    private static void ValidateRequest(SemanticEventInferenceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new InvalidOperationException("Semantic inference requires SessionId.");
        }

        if (string.IsNullOrWhiteSpace(request.InteractionId))
        {
            throw new InvalidOperationException("Semantic inference requires InteractionId.");
        }

        if (string.IsNullOrWhiteSpace(request.ActorName))
        {
            throw new InvalidOperationException("Semantic inference requires ActorName.");
        }

        if (string.IsNullOrWhiteSpace(request.InteractionText))
        {
            throw new InvalidOperationException("Semantic inference requires interaction text.");
        }

        if (request.AllowedEventIds.Count == 0)
        {
            throw new InvalidOperationException("Semantic inference requires at least one allowed event id.");
        }
    }

    private sealed class InferenceEnvelope
    {
        public List<InferenceEvent?> Events { get; set; } = [];
    }

    private sealed class InferenceEvent
    {
        public string? EventId { get; set; }

        public decimal Confidence { get; set; }

        public string? ActorName { get; set; }

        public string? TargetCharacterName { get; set; }

        public string? EvidenceSpan { get; set; }
    }
}
