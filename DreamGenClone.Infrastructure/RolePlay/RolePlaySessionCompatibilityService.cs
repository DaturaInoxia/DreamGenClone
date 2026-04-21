using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RolePlaySessionCompatibilityService
{
    private static readonly IReadOnlyList<string> RequiredStats = AdaptiveStatCatalog.CanonicalStatNames;

    private readonly IRolePlayStateRepository _stateRepository;
    private readonly ILogger<RolePlaySessionCompatibilityService> _logger;

    public RolePlaySessionCompatibilityService(
        IRolePlayStateRepository stateRepository,
        ILogger<RolePlaySessionCompatibilityService> logger)
    {
        _stateRepository = stateRepository;
        _logger = logger;
    }

    public async Task<UnsupportedSessionError?> ValidateSessionPayloadAsync(
        string sessionId,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        _logger.LogInformation(RolePlayV2LogEvents.CompatibilityCheckStarted, sessionId, correlationId);

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return await PersistUnsupportedAsync(sessionId, null, ["PayloadJson"], cancellationToken);
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var schemaVersion = root.TryGetProperty("RolePlayV2SchemaVersion", out var schemaProp)
                ? schemaProp.GetString()
                : null;

            var missing = new List<string>();
            if (!string.Equals(schemaVersion, "2", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(schemaVersion, "v2", StringComparison.OrdinalIgnoreCase))
            {
                missing.Add("RolePlayV2SchemaVersion");
            }

            if (root.TryGetProperty("AdaptiveState", out var adaptiveState)
                && adaptiveState.TryGetProperty("CharacterStats", out var characterStats)
                && characterStats.ValueKind == JsonValueKind.Object
                && characterStats.EnumerateObject().Any())
            {
                var firstCharacter = characterStats.EnumerateObject().First().Value;
                if (firstCharacter.TryGetProperty("Stats", out var statsElement)
                    && statsElement.ValueKind == JsonValueKind.Object)
                {
                    var available = statsElement.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    missing.AddRange(RequiredStats.Where(stat => !available.Contains(stat)));
                }
                else
                {
                    missing.AddRange(RequiredStats);
                }
            }
            else
            {
                missing.AddRange(RequiredStats);
            }

            if (missing.Count == 0)
            {
                _logger.LogInformation(RolePlayV2LogEvents.CompatibilityCheckPassed, sessionId, correlationId);
                return null;
            }

            return await PersistUnsupportedAsync(sessionId, schemaVersion, missing, cancellationToken);
        }
        catch (JsonException)
        {
            return await PersistUnsupportedAsync(sessionId, null, ["InvalidJson"], cancellationToken);
        }
    }

    private async Task<UnsupportedSessionError> PersistUnsupportedAsync(
        string sessionId,
        string? detectedVersion,
        IReadOnlyList<string> missing,
        CancellationToken cancellationToken)
    {
        var error = new UnsupportedSessionError
        {
            ErrorCode = "RPV2_UNSUPPORTED_SCHEMA",
            SessionId = sessionId,
            DetectedSchemaVersion = detectedVersion,
            MissingCanonicalStats = [.. missing],
            RecoveryGuidance = "Open a v2-compatible session or recreate base stats to continue role-play v2 flows.",
            EmittedUtc = DateTime.UtcNow
        };

        await _stateRepository.SaveUnsupportedSessionErrorAsync(error, cancellationToken);

        _logger.LogInformation(
            RolePlayV2LogEvents.UnsupportedSessionRejected,
            error.SessionId,
            error.ErrorCode,
            string.Join(",", error.MissingCanonicalStats));

        return error;
    }
}
