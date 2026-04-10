using DreamGenClone.Web.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Models;

/// <summary>
/// In-memory manager for per-session model settings with logging.
/// </summary>
public sealed class ModelSettingsService : IModelSettingsService
{
    private readonly Dictionary<string, ModelSettings> _sessionSettings = new();
    private readonly ILogger<ModelSettingsService> _logger;

    public ModelSettingsService(ILogger<ModelSettingsService> logger)
    {
        _logger = logger;
    }

    public ModelSettings GetSettings(string sessionId)
    {
        if (_sessionSettings.TryGetValue(sessionId, out var settings))
        {
            return settings;
        }

        // Return default settings if not yet configured
        _logger.LogInformation("Creating default model settings for session {SessionId}", sessionId);
        var defaultSettings = new ModelSettings();
        _sessionSettings[sessionId] = defaultSettings;
        return defaultSettings;
    }

    public void UpdateSettings(string sessionId, ModelSettings settings)
    {
        _sessionSettings[sessionId] = settings.Clone();
        _logger.LogInformation(
            "Updated model settings for session {SessionId}: SessionModelId={SessionModelId}, Temperature={Temperature}, TopP={TopP}, MaxTokens={MaxTokens}",
            sessionId,
            settings.SessionModelId ?? "(default)",
            settings.Temperature,
            settings.TopP,
            settings.MaxTokens);
    }

    public void ClearSettings(string sessionId)
    {
        if (_sessionSettings.Remove(sessionId))
        {
            _logger.LogInformation("Cleared model settings for session {SessionId}", sessionId);
        }
    }
}
