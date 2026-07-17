using System.Text.Json;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class LocationDetectionJobHandler : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISessionService _sessionService;
    private readonly IScenarioService _scenarioService;
    private readonly ILocationDetectionService _detectionService;
    private readonly IRolePlayStateRepository _stateRepository;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly ILogger<LocationDetectionJobHandler> _logger;
    private readonly RolePlayDecisionOptions _decisionOptions;

    public LocationDetectionJobHandler(
        ISessionService sessionService,
        IScenarioService scenarioService,
        ILocationDetectionService detectionService,
        IRolePlayStateRepository stateRepository,
        IRolePlayDebugEventSink debugEventSink,
        IOptions<RolePlayDecisionOptions> decisionOptions,
        ILogger<LocationDetectionJobHandler> logger)
    {
        _sessionService = sessionService;
        _scenarioService = scenarioService;
        _detectionService = detectionService;
        _stateRepository = stateRepository;
        _debugEventSink = debugEventSink;
        _decisionOptions = decisionOptions?.Value ?? new RolePlayDecisionOptions();
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.LocationDetection;

    public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
    {
        if (!_decisionOptions.EnableLocationServices)
        {
            _logger.LogInformation(
                "Skipping location detection job {JobId}; RolePlayDecision:EnableLocationServices is false.",
                job.JobId);
            return;
        }

        var payload = JsonSerializer.Deserialize<LocationDetectionJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Location detection job payload is missing or invalid.");

        if (string.IsNullOrWhiteSpace(payload.SessionId))
            throw new InvalidOperationException("Location detection job payload is missing SessionId.");

        if (payload.ScenarioLocationNames.Count == 0)
        {
            _logger.LogInformation("Skipping location detection for session {SessionId}; payload has no location names.", payload.SessionId);
            return;
        }

        if (payload.CharacterNames.Count == 0)
        {
            _logger.LogInformation("Skipping location detection for session {SessionId}; payload has no character names.", payload.SessionId);
            return;
        }

        // Load adaptive state from DB to apply changes (this table is always up-to-date).
        // Interactions, location names, and character names come from the inline payload
        // to avoid the race condition where PayloadJson hasn't been saved yet.
        var adaptiveState = await _stateRepository.LoadAdaptiveStateAsync(payload.SessionId, cancellationToken);
        if (adaptiveState is null)
        {
            _logger.LogInformation("Skipping location detection for session {SessionId}; no adaptive state loaded.", payload.SessionId);
            return;
        }

        // Build character-location affinity context from scenario so the LLM
        // knows which character lives/works at which location.
        string? characterAffinityContext = null;
        var session = await _sessionService.LoadRolePlaySessionAsync(payload.SessionId, cancellationToken);
        if (session is not null && !string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null)
            {
                var affinityLines = new List<string>();
                foreach (var character in scenario.Characters)
                {
                    if (string.IsNullOrWhiteSpace(character.Name)) continue;
                    var required = character.LocationAffinities
                        .Where(a => a.AffinityType == DreamGenClone.Web.Domain.Scenarios.AffinityType.Required)
                        .Select(a => a.LocationName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                    if (required.Count > 0)
                        affinityLines.Add($"{character.Name.Trim()} belongs to: {string.Join(", ", required)}");
                }
                if (affinityLines.Count > 0)
                    characterAffinityContext = string.Join("; ", affinityLines);
            }
        }

        var request = new LocationDetectionRequest
        {
            SessionId = payload.SessionId,
            RecentInteractions = payload.RecentInteractionSummaries,
            ScenarioLocationNames = payload.ScenarioLocationNames,
            PreviousLocation = adaptiveState.CurrentSceneLocation ?? payload.PreviousLocation,
            CharacterNames = payload.CharacterNames,
            CharacterLocationAffinityContext = characterAffinityContext
        };

        LocationDetectionResult result;
        try
        {
            result = await _detectionService.DetectAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LocationDetection job failed SessionId={SessionId} Error={Error}",
                payload.SessionId, ex.Message);

            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = payload.SessionId,
                EventKind = "LocationDetectionFailed",
                Severity = "Warning",
                Summary = $"Location detection job threw: {ex.GetType().Name}: {ex.Message}",
                MetadataJson = JsonSerializer.Serialize(new { error = ex.Message, errorType = ex.GetType().Name })
            }, cancellationToken);
            return;
        }

        if (!result.Success)
        {
            _logger.LogWarning(
                "LocationDetection returned failure SessionId={SessionId} Error={ErrorMessage}",
                payload.SessionId, result.ErrorMessage ?? "(none)");

            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = payload.SessionId,
                EventKind = "LocationDetectionSkipped",
                Severity = "Warning",
                Summary = $"Location detection failed: {result.ErrorMessage ?? "unknown error"}",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    error = result.ErrorMessage,
                    previousLocation = request.PreviousLocation
                })
            }, cancellationToken);
            return;
        }

        var previousLocation = adaptiveState.CurrentSceneLocation;

        // Apply per-character locations from the LLM result
        // BUG-005 fix: skip entries where the LLM returned null — leave those characters
        // at their existing TrueLocation instead of applying previousLocation uniformly.
        // Also skip persona ("You") entries — the persona is not a tracked character.
        if (result.PerCharacterLocations is { Count: > 0 })
        {
            RolePlayCharacterStateMutator.EnsureCharacterLocationRows(adaptiveState);
            foreach (var kvp in result.PerCharacterLocations)
            {
                if (string.IsNullOrWhiteSpace(kvp.Value))
                {
                    // LLM didn't place this character — leave existing truth unchanged
                    continue;
                }
                // Never apply location for the persona. IsPersonaActor intentionally
                // returns false for "You" to avoid false positives elsewhere, so also
                // guard against the literal string "You" (LLM hallucinated pronoun) and
                // exact match against the session's configured PersonaName.
                if (session is not null
                    && (session.IsPersonaActor(kvp.Key)
                        || string.Equals(kvp.Key, "You", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(kvp.Key, session.PersonaName, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogDebug("Skipping persona actor {CharacterName} in per-character locations", kvp.Key);
                    continue;
                }
                RolePlayCharacterStateMutator.UpsertTrueLocation(
                    adaptiveState, kvp.Key, kvp.Value,
                    sourceIsHidden: false);
            }
            RolePlayCharacterStateMutator.UpdatePerceivedLocationsFromTruth(adaptiveState);
        }

        // Update the current scene location
        adaptiveState.CurrentSceneLocation = result.DetectedLocation ?? previousLocation;

        // Apply time-of-day from LLM detection (respect manual override)
        if (!adaptiveState.TimeOfDayManuallySet
            && !string.IsNullOrWhiteSpace(result.DetectedTimeOfDay)
            && Enum.TryParse<DreamGenClone.Domain.RolePlay.TimeOfDay>(result.DetectedTimeOfDay, ignoreCase: true, out var tod))
        {
            adaptiveState.CurrentTimeOfDay = tod;
        }

        await _stateRepository.SaveAdaptiveStateAsync(adaptiveState, cancellationToken);

        _logger.LogInformation(
            "LocationDetection completed SessionId={SessionId} PreviousLocation={PreviousLocation} DetectedLocation={DetectedLocation} Confidence={Confidence} LocationChanged={LocationChanged}",
            payload.SessionId,
            previousLocation ?? "(null)",
            result.DetectedLocation ?? "(null)",
            result.LocationConfidence?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(null)",
            result.LocationChanged);

        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = payload.SessionId,
            EventKind = "LocationDetectionCompleted",
            Severity = "Information",
            Summary = result.LocationChanged
                ? $"Location changed: {previousLocation ?? "(none)"} → {result.DetectedLocation} (confidence: {result.LocationConfidence?.ToString(System.Globalization.CultureInfo.InvariantCulture)})"
                : $"Location unchanged: {result.DetectedLocation ?? previousLocation ?? "(none)"}",
            MetadataJson = JsonSerializer.Serialize(new
            {
                previousLocation,
                detectedLocation = result.DetectedLocation,
                confidence = result.LocationConfidence,
                locationChanged = result.LocationChanged,
                reasoning = result.Reasoning,
                detectedTimeOfDay = result.DetectedTimeOfDay,
                perCharacterLocations = result.PerCharacterLocations
            })
        }, cancellationToken);
    }

    private static string BuildInteractionSummary(RolePlayInteraction interaction)
    {
        var content = interaction.Content ?? string.Empty;
        // Take first 2-3 sentences (~300 chars max)
        var maxLen = Math.Min(content.Length, 300);
        var truncated = content.Length > 300 ? content[..maxLen] + "..." : content;
        return $"{interaction.ActorName}: {truncated}";
    }
}
