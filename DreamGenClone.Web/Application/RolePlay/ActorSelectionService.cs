using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay.Models;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class ActorSelectionService : IActorSelectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly ILogger<ActorSelectionService>? _logger;

    public ActorSelectionService(
        ICompletionClient completionClient,
        IModelResolutionService modelResolutionService,
        ILogger<ActorSelectionService>? logger = null)
    {
        _completionClient = completionClient;
        _modelResolutionService = modelResolutionService;
        _logger = logger;
    }

    public async Task<ActorSelectionResponse> SelectActorsAsync(
        ActorSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        ResolvedModel resolved;
        try
        {
            resolved = await _modelResolutionService.ResolveAsync(
                AppFunction.RolePlayActorSelection,
                cancellationToken: cancellationToken);
        }
        catch (ModelResolutionException ex)
        {
            _logger?.LogWarning(ex,
                "ActorSelection: model unavailable for {Function}, SessionId={SessionId} — using scoring base path",
                AppFunction.RolePlayActorSelection, request.SessionId);
            return BuildScoringResponse(request);
        }

        var systemMessage =
            "You are a narrative director selecting which characters speak next in a roleplay story. " +
            "Output ONLY strict JSON. Never include markdown. " +
            "Schema: {\"characters\":[\"Name1\",\"Name2\",...],\"reasoning\":\"<one or two sentences>\"}. " +
            "Rules: Characters MUST be a subset of the provided candidates (case-sensitive names). " +
            "Order characters by dramatic importance to the current scene. " +
            "Select at most the requested batch size. " +
            "Prefer characters who are in-scene and who have not spoken recently. " +
            "Honor affinity hints: Required characters should ALWAYS be included; Excluded candidates are not in the list (filtered upstream); Preferred is a hint. " +
            "Honor time-of-day match: prefer characters whose affinity time-of-day matches the current time. " +
            "Use the baseScore as a hint, NOT as the sole determinant. " +
            "All characters including the persona are listed as candidates. " +
            "If no character is a good fit, return an empty 'characters' array and explain.";

        var candidatesText = BuildCandidatesText(request.Candidates);
        var themesText = request.ActiveThemes.Count > 0
            ? string.Join(", ", request.ActiveThemes)
            : "(none)";
        var eventsText = request.RecentSemanticEvents.Count > 0
            ? string.Join(", ", request.RecentSemanticEvents)
            : "(none)";

        var userMessage =
            $"sessionId={request.SessionId}\n" +
            $"currentPhase={request.CurrentPhase}\n" +
            $"currentLocation={request.CurrentLocation ?? "(unknown)"}\n" +
            $"currentTimeOfDay={request.CurrentTimeOfDay ?? "(unknown)"}\n" +
            $"narrativeSummary={request.NarrativeSummary}\n" +
            $"activeThemes={themesText}\n" +
            $"recentSemanticEvents={eventsText}\n" +
            $"batchSize={request.BatchSize}\n" +
            $"candidates (in score-desc order):\n{candidatesText}";

        _logger?.LogInformation(
            "ActorSelection REQUEST SessionId={SessionId} Model={Provider}/{ModelId} CandidateCount={CandidateCount} BatchSize={BatchSize} CacheKey={CacheKey}\n--- SYSTEM ---\n{System}\n--- USER ---\n{User}",
            request.SessionId,
            resolved.ProviderName,
            resolved.ModelIdentifier,
            request.Candidates.Count,
            request.BatchSize,
            request.CacheKey ?? "(none)",
            systemMessage,
            userMessage);

        var stopwatch = Stopwatch.StartNew();
        string output;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
            output = await _completionClient.GenerateAsync(systemMessage, userMessage, resolved, linked.Token);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogWarning(ex,
                "ActorSelection FAILED SessionId={SessionId} Model={Provider}/{ModelId} ElapsedMs={ElapsedMs} ErrorType={ErrorType} Error={Error} — falling back to scoring",
                request.SessionId,
                resolved.ProviderName,
                resolved.ModelIdentifier,
                stopwatch.ElapsedMilliseconds,
                ex.GetType().Name,
                ex.Message);
            return BuildFallbackResponse(request, ex.Message);
        }
        stopwatch.Stop();

        _logger?.LogInformation(
            "ActorSelection RESPONSE SessionId={SessionId} Model={Provider}/{ModelId} ElapsedMs={ElapsedMs} OutputLen={OutputLen}\n--- RAW OUTPUT ---\n{Output}",
            request.SessionId,
            resolved.ProviderName,
            resolved.ModelIdentifier,
            stopwatch.ElapsedMilliseconds,
            (output ?? string.Empty).Length,
            output ?? string.Empty);

        try
        {
            var parsed = ParseAndValidate(output ?? string.Empty, request);
            _logger?.LogInformation(
                "ActorSelection PARSED SessionId={SessionId} OrderedCount={OrderedCount} Reasoning={Reasoning}",
                request.SessionId, parsed.OrderedNames.Count, parsed.Reasoning ?? "(none)");
            return new ActorSelectionResponse
            {
                Success = true,
                OrderedNames = parsed.OrderedNames,
                Reasoning = parsed.Reasoning,
                Source = ActorSelectionSource.LLM,
                RawModelOutput = output ?? string.Empty,
                PromptSystem = systemMessage,
                PromptUser = userMessage
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "ActorSelection PARSE-FAILED SessionId={SessionId} Error={Error} — falling back to scoring",
                request.SessionId, ex.Message);
            return BuildFallbackResponse(request, $"Parse error: {ex.Message}");
        }
    }

    private static ActorSelectionResponse BuildScoringResponse(ActorSelectionRequest request)
    {
        var ordered = request.Candidates
            .OrderByDescending(c => c.BaseScore)
            .Take(request.BatchSize)
            .Select(c => c.Name)
            .ToList();
        return new ActorSelectionResponse
        {
            Success = true,
            OrderedNames = ordered,
            Source = ActorSelectionSource.Scoring,
            RawModelOutput = string.Empty,
            PromptSystem = string.Empty,
            PromptUser = string.Empty
        };
    }

    private static ActorSelectionResponse BuildFallbackResponse(ActorSelectionRequest request, string errorMessage)
    {
        var ordered = request.Candidates
            .OrderByDescending(c => c.BaseScore)
            .Take(request.BatchSize)
            .Select(c => c.Name)
            .ToList();
        return new ActorSelectionResponse
        {
            Success = false,
            ErrorMessage = errorMessage,
            OrderedNames = ordered,
            Source = ActorSelectionSource.Fallback,
            RawModelOutput = string.Empty,
            PromptSystem = string.Empty,
            PromptUser = string.Empty
        };
    }

    private static void ValidateRequest(ActorSelectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new InvalidOperationException("ActorSelection requires SessionId.");
        if (request.Candidates.Count == 0)
            throw new InvalidOperationException("ActorSelection requires at least one candidate.");
        if (request.BatchSize < 1)
            throw new InvalidOperationException("ActorSelection BatchSize must be >= 1.");
    }

    private static string BuildCandidatesText(IReadOnlyList<ActorCandidateInfo> candidates)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in candidates)
        {
            sb.AppendLine($"- Name: {c.Name}, Role: {c.Role ?? "(unknown)"}, " +
                          $"InScene: {c.IsInScene}, Affinity: {c.AffinityStatus}, " +
                          $"TimeMatch: {c.TimeOfDayMatch?.ToString() ?? "null"}, " +
                          $"LastSpokeTurnsAgo: {c.LastSpokeTurnsAgo?.ToString() ?? "never"}, " +
                          $"BaseScore: {c.BaseScore:F0}, " +
                          $"Details: {c.AffinityDetails ?? "(none)"}");
        }
        return sb.ToString().TrimEnd();
    }

    internal static string ExtractJsonObject(string modelOutput)
    {
        if (string.IsNullOrWhiteSpace(modelOutput)) return modelOutput ?? string.Empty;
        var start = modelOutput.IndexOf('{');
        var end = modelOutput.LastIndexOf('}');
        if (start >= 0 && end > start)
            return modelOutput.Substring(start, end - start + 1);
        return modelOutput;
    }

    private static (List<string> OrderedNames, string? Reasoning) ParseAndValidate(
        string modelOutput, ActorSelectionRequest request)
    {
        var json = ExtractJsonObject(modelOutput);
        ActorSelectionEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ActorSelectionEnvelope>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Actor selection returned invalid JSON.", ex);
        }

        if (envelope?.Characters is null)
            throw new InvalidOperationException("Actor selection JSON did not include required 'characters' array.");

        var candidateNames = new HashSet<string>(request.Candidates.Select(c => c.Name), StringComparer.Ordinal);

        var ordered = new List<string>();
        foreach (var name in envelope.Characters)
        {
            var trimmed = name?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
            if (!candidateNames.Contains(trimmed))
                throw new InvalidOperationException($"Actor selection returned unknown character '{trimmed}'.");
            ordered.Add(trimmed);
        }

        // Cap to batch size
        if (ordered.Count > request.BatchSize)
            ordered = ordered.Take(request.BatchSize).ToList();

        return (ordered, envelope.Reasoning?.Trim());
    }

    private sealed class ActorSelectionEnvelope
    {
        public List<string>? Characters { get; set; }
        public string? Reasoning { get; set; }
    }
}
