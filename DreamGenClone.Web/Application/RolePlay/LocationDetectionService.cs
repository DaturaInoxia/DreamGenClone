using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class LocationDetectionService : ILocationDetectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly ILogger<LocationDetectionService>? _logger;

    public LocationDetectionService(
        ICompletionClient completionClient,
        IModelResolutionService modelResolutionService,
        ILogger<LocationDetectionService>? logger = null)
    {
        _completionClient = completionClient;
        _modelResolutionService = modelResolutionService;
        _logger = logger;
    }

    public async Task<Models.LocationDetectionResult> DetectAsync(
        Models.LocationDetectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        ResolvedModel resolved;
        try
        {
            resolved = await _modelResolutionService.ResolveAsync(
                AppFunction.RolePlayLocationDetection,
                cancellationToken: cancellationToken);
        }
        catch (ModelResolutionException ex)
        {
            _logger?.LogWarning(ex,
                "LocationDetection: model unavailable for {Function}, SessionId={SessionId}",
                AppFunction.RolePlayLocationDetection, request.SessionId);
            return new Models.LocationDetectionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                RawModelOutput = string.Empty,
                PromptSystem = string.Empty,
                PromptUser = string.Empty
            };
        }

        var systemMessage =
            "You detect the current scene location from roleplay narrative text. " +
            "Output ONLY strict JSON. Never include markdown. " +
            "Schema: {\"detectedLocation\":\"<name or null>\",\"confidence\":0.0,\"perCharacterLocations\":{\"<characterName>\":\"<locationName or null>\"},\"reasoning\":\"<one sentence>\"}. " +
            "Rules: " +
            "detectedLocation MUST be one of the provided scenarioLocationNames, or null if no location is clearly identifiable. " +
            "confidence is a decimal in [0,1]; values below 0.5 should return null for detectedLocation. " +
            "perCharacterLocations is optional; each value MUST also be one of scenarioLocationNames or null. " +
            "If recentInteractions consistently reference the previousLocation with no transition language, return detectedLocation=previousLocation. " +
            "If recentInteractions describe a transition (e.g., 'we drive to', 'arriving at'), set detectedLocation to the destination. " +
            "Never invent a location name; only use scenarioLocationNames.";

        var namesJson = JsonSerializer.Serialize(request.ScenarioLocationNames, JsonOptions);
        var charsJson = JsonSerializer.Serialize(request.CharacterNames, JsonOptions);
        var interactionsText = string.Join("\n", request.RecentInteractions);
        var userMessage =
            $"sessionId={request.SessionId}\n" +
            $"previousLocation={request.PreviousLocation ?? "(none)"}\n" +
            $"scenarioLocationNames={namesJson}\n" +
            $"characterNames={charsJson}\n" +
            $"recentInteractions={interactionsText}";

        _logger?.LogInformation(
            "LocationDetection REQUEST SessionId={SessionId} Model={Provider}/{ModelId} LocationNamesCount={LocCount} InteractionsLen={InteractionsLen}\n--- SYSTEM ---\n{System}\n--- USER ---\n{User}",
            request.SessionId,
            resolved.ProviderName,
            resolved.ModelIdentifier,
            request.ScenarioLocationNames.Count,
            interactionsText.Length,
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
                "LocationDetection FAILED SessionId={SessionId} Model={Provider}/{ModelId} ElapsedMs={ElapsedMs} ErrorType={ErrorType} Error={Error}",
                request.SessionId,
                resolved.ProviderName,
                resolved.ModelIdentifier,
                stopwatch.ElapsedMilliseconds,
                ex.GetType().Name,
                ex.Message);
            throw;
        }
        stopwatch.Stop();

        _logger?.LogInformation(
            "LocationDetection RESPONSE SessionId={SessionId} Model={Provider}/{ModelId} ElapsedMs={ElapsedMs} OutputLen={OutputLen}\n--- RAW OUTPUT ---\n{Output}",
            request.SessionId,
            resolved.ProviderName,
            resolved.ModelIdentifier,
            stopwatch.ElapsedMilliseconds,
            (output ?? string.Empty).Length,
            output ?? string.Empty);

        Models.LocationDetectionResult parsed;
        try
        {
            parsed = ParseAndValidate(output ?? string.Empty, request.ScenarioLocationNames, request.CharacterNames);
            parsed = parsed with
            {
                RawModelOutput = output ?? string.Empty,
                PromptSystem = systemMessage,
                PromptUser = userMessage
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "LocationDetection PARSE-FAILED SessionId={SessionId} Error={Error}\n--- RAW OUTPUT ---\n{Output}",
                request.SessionId,
                ex.Message,
                output ?? string.Empty);
            throw;
        }

        var locationChanged = parsed.DetectedLocation is not null
            && !string.Equals(parsed.DetectedLocation, request.PreviousLocation, StringComparison.OrdinalIgnoreCase);

        _logger?.LogInformation(
            "LocationDetection PARSED SessionId={SessionId} DetectedLocation={DetectedLocation} Confidence={Confidence} LocationChanged={LocationChanged}",
            request.SessionId,
            parsed.DetectedLocation ?? "(null)",
            parsed.LocationConfidence?.ToString(CultureInfo.InvariantCulture) ?? "(null)",
            locationChanged);

        return parsed with { LocationChanged = locationChanged };
    }

    private static void ValidateRequest(Models.LocationDetectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new InvalidOperationException("LocationDetection requires SessionId.");
        if (request.RecentInteractions.Count == 0)
            throw new InvalidOperationException("LocationDetection requires at least one recent interaction.");
        if (request.ScenarioLocationNames.Count == 0)
            throw new InvalidOperationException("LocationDetection requires at least one scenario location name.");
        if (request.CharacterNames.Count == 0)
            throw new InvalidOperationException("LocationDetection requires at least one character name.");
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

    private static Models.LocationDetectionResult ParseAndValidate(
        string modelOutput,
        IReadOnlyList<string> scenarioLocationNames,
        IReadOnlyList<string> characterNames)
    {
        var json = ExtractJsonObject(modelOutput);
        LocationEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<LocationEnvelope>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Location detection returned invalid JSON.", ex);
        }

        var allowedLocations = new HashSet<string>(scenarioLocationNames, StringComparer.OrdinalIgnoreCase);
        var allowedCharacters = new HashSet<string>(characterNames, StringComparer.OrdinalIgnoreCase);

        var detectedLocation = envelope?.DetectedLocation?.Trim();
        if (detectedLocation is not null)
        {
            if (!allowedLocations.Contains(detectedLocation))
            {
                throw new InvalidOperationException(
                    $"Location detection returned unknown location '{detectedLocation}' not in scenarioLocationNames.");
            }
        }

        var confidence = envelope?.Confidence;
        if (confidence is < 0m or > 1m)
        {
            throw new InvalidOperationException(
                $"Location detection confidence is out of range [0,1]: {confidence?.ToString(CultureInfo.InvariantCulture)}.");
        }

        Dictionary<string, string?>? perCharacter = null;
        if (envelope?.PerCharacterLocations is { Count: > 0 } pcl)
        {
            perCharacter = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in pcl)
            {
                var charName = kvp.Key.Trim();
                if (!allowedCharacters.Contains(charName))
                {
                    throw new InvalidOperationException(
                        $"Location detection returned per-character location for unknown character '{charName}'.");
                }

                var locName = kvp.Value?.Trim();
                if (locName is not null && !allowedLocations.Contains(locName))
                {
                    throw new InvalidOperationException(
                        $"Location detection returned unknown per-character location '{locName}' for '{charName}'.");
                }

                perCharacter[charName] = locName;
            }
        }

        return new Models.LocationDetectionResult
        {
            Success = true,
            DetectedLocation = detectedLocation,
            LocationConfidence = confidence,
            PerCharacterLocations = perCharacter,
            Reasoning = envelope?.Reasoning?.Trim(),
            RawModelOutput = modelOutput,
            PromptSystem = string.Empty,
            PromptUser = string.Empty,
            LocationChanged = false // computed by caller
        };
    }

    private sealed class LocationEnvelope
    {
        public string? DetectedLocation { get; set; }
        public decimal? Confidence { get; set; }
        public Dictionary<string, string?>? PerCharacterLocations { get; set; }
        public string? Reasoning { get; set; }
    }
}
