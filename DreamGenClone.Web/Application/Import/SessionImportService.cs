using System.Text.Json;
using DreamGenClone.Application.Validation;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Story;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Import;

public sealed class SessionImportService : ISessionImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SessionImportValidator _validator;
    private readonly ISessionService _sessionService;
    private readonly ILogger<SessionImportService> _logger;

    public SessionImportService(SessionImportValidator validator, ISessionService sessionService, ILogger<SessionImportService> logger)
    {
        _validator = validator;
        _sessionService = sessionService;
        _logger = logger;
    }

    public async Task<(bool IsSuccess, string Message)> ImportJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(json);
        if (!validation.IsValid)
        {
            _logger.LogInformation("Session import rejected by validator: {Error}", validation.Error);
            return (false, validation.Error ?? "Import validation failed.");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var sessionType = root.GetProperty("sessionType").GetString() ?? string.Empty;
        var payload = root.GetProperty("payload");

        if (string.Equals(sessionType, SessionService.StorySessionType, StringComparison.OrdinalIgnoreCase))
        {
            var story = payload.Deserialize<StorySession>(JsonOptions);
            if (story is null)
            {
                return (false, "Import payload could not be parsed as story session.");
            }

            story.Id = Guid.NewGuid().ToString();
            story.Title = string.IsNullOrWhiteSpace(story.Title) ? "Imported Story Session" : story.Title;
            await _sessionService.SaveStorySessionAsync(story, cancellationToken);
            _logger.LogInformation("Imported story session {SessionId}", story.Id);
            return (true, $"Imported story session '{story.Title}'.");
        }

        if (string.Equals(sessionType, SessionService.RolePlaySessionType, StringComparison.OrdinalIgnoreCase))
        {
            var rolePlay = payload.Deserialize<RolePlaySession>(JsonOptions);
            if (rolePlay is null)
            {
                return (false, "Import payload could not be parsed as role-play session.");
            }

            rolePlay.Id = Guid.NewGuid().ToString();
            rolePlay.Title = string.IsNullOrWhiteSpace(rolePlay.Title) ? "Imported Role-Play Session" : rolePlay.Title;
            await _sessionService.SaveRolePlaySessionAsync(rolePlay, cancellationToken);
            _logger.LogInformation("Imported role-play session {SessionId}", rolePlay.Id);
            return (true, $"Imported role-play session '{rolePlay.Title}'.");
        }

        var message = $"Unsupported sessionType '{sessionType}'. Expected 'story' or 'roleplay'.";
        _logger.LogInformation("Session import rejected: {Error}", message);
        return (false, message);
    }
}
