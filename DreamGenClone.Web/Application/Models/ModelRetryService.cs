using DreamGenClone.Application.Abstractions;
using DreamGenClone.Web.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Models;

/// <summary>
/// Service for retrying AI generation with updated model settings.
/// </summary>
public interface IModelRetryService
{
    /// <summary>
    /// Regenerates AI output using current model settings for the session.
    /// </summary>
    Task<string> RetryGenerationAsync(
        string sessionId,
        string prompt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Retry service that applies session-specific model settings to regenerate completions.
/// </summary>
public sealed class ModelRetryService : IModelRetryService
{
    private readonly ILmStudioClient _lmStudioClient;
    private readonly IModelSettingsService _modelSettingsService;
    private readonly ILogger<ModelRetryService> _logger;

    public ModelRetryService(
        ILmStudioClient lmStudioClient,
        IModelSettingsService modelSettingsService,
        ILogger<ModelRetryService> logger)
    {
        _lmStudioClient = lmStudioClient;
        _modelSettingsService = modelSettingsService;
        _logger = logger;
    }

    public async Task<string> RetryGenerationAsync(
        string sessionId,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var settings = _modelSettingsService.GetSettings(sessionId);

        _logger.LogInformation(
            "Retrying generation for session {SessionId} with Model={Model}, Temperature={Temperature}, TopP={TopP}, MaxTokens={MaxTokens}",
            sessionId,
            settings.Model,
            settings.Temperature,
            settings.TopP,
            settings.MaxTokens);

        var result = await _lmStudioClient.GenerateAsync(
            prompt,
            settings.Model,
            settings.Temperature,
            settings.TopP,
            settings.MaxTokens,
            cancellationToken);

        _logger.LogInformation("Retry generation completed for session {SessionId}", sessionId);

        return result;
    }
}
