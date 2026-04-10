using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
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
    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolver;
    private readonly IModelSettingsService _modelSettingsService;
    private readonly ILogger<ModelRetryService> _logger;

    public ModelRetryService(
        ICompletionClient completionClient,
        IModelResolutionService modelResolver,
        IModelSettingsService modelSettingsService,
        ILogger<ModelRetryService> logger)
    {
        _completionClient = completionClient;
        _modelResolver = modelResolver;
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
            "Retrying generation for session {SessionId} with Temperature={Temperature}, TopP={TopP}, MaxTokens={MaxTokens}",
            sessionId,
            settings.Temperature,
            settings.TopP,
            settings.MaxTokens);

        var resolved = await _modelResolver.ResolveAsync(
            AppFunction.RolePlayGeneration,
            sessionModelId: null,
            sessionTemperature: settings.Temperature,
            sessionTopP: settings.TopP,
            sessionMaxTokens: settings.MaxTokens,
            cancellationToken: cancellationToken);

        var result = await _completionClient.GenerateAsync(prompt, resolved, cancellationToken);

        _logger.LogInformation("Retry generation completed for session {SessionId}", sessionId);

        return result;
    }
}
