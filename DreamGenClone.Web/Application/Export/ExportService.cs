using System.Text;
using System.Text.Json;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Story;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Export;

public sealed class ExportService : IExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ISessionService _sessionService;
    private readonly ILogger<ExportService> _logger;

    public ExportService(ISessionService sessionService, ILogger<ExportService> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    public async Task<string?> ExportJsonAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var envelope = await _sessionService.GetExportEnvelopeAsync(sessionId, cancellationToken);
        if (envelope is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        _logger.LogInformation("Exported session {SessionId} to JSON", sessionId);
        return json;
    }

    public async Task<string?> ExportMarkdownAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var story = await _sessionService.LoadStorySessionAsync(sessionId, cancellationToken);
        if (story is not null)
        {
            _logger.LogInformation("Exported story session {SessionId} to Markdown", sessionId);
            return BuildStoryMarkdown(story);
        }

        var rolePlay = await _sessionService.LoadRolePlaySessionAsync(sessionId, cancellationToken);
        if (rolePlay is not null)
        {
            _logger.LogInformation("Exported role-play session {SessionId} to Markdown", sessionId);
            return BuildRolePlayMarkdown(rolePlay);
        }

        return null;
    }

    private static string BuildStoryMarkdown(StorySession story)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {story.Title}");
        sb.AppendLine();
        sb.AppendLine($"- Session Type: story");
        sb.AppendLine($"- Session Id: {story.Id}");
        sb.AppendLine($"- Updated (UTC): {story.ModifiedAt:O}");
        sb.AppendLine();

        foreach (var block in story.Blocks)
        {
            sb.AppendLine($"## {block.Author} ({block.BlockType})");
            sb.AppendLine();
            sb.AppendLine(block.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildRolePlayMarkdown(RolePlaySession rolePlay)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {rolePlay.Title}");
        sb.AppendLine();
        sb.AppendLine($"- Session Type: roleplay");
        sb.AppendLine($"- Session Id: {rolePlay.Id}");
        sb.AppendLine($"- Behavior Mode: {rolePlay.BehaviorMode}");
        sb.AppendLine($"- Updated (UTC): {rolePlay.ModifiedAt:O}");
        sb.AppendLine();

        foreach (var interaction in rolePlay.Interactions)
        {
            sb.AppendLine($"## {interaction.ActorName} ({interaction.InteractionType})");
            sb.AppendLine();
            sb.AppendLine(interaction.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
