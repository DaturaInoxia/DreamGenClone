using System.Text;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.Story;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Story;

public sealed class StoryEngineService : IStoryEngineService
{
    private static readonly Dictionary<string, StorySession> Sessions = [];

    private readonly ILmStudioClient _lmStudioClient;
    private readonly IScenarioService _scenarioService;
    private readonly ISessionService _sessionService;
    private readonly AutoSaveCoordinator _autoSaveCoordinator;
    private readonly ILogger<StoryEngineService> _logger;

    public StoryEngineService(
        ILmStudioClient lmStudioClient,
        IScenarioService scenarioService,
        ISessionService sessionService,
        AutoSaveCoordinator autoSaveCoordinator,
        ILogger<StoryEngineService> logger)
    {
        _lmStudioClient = lmStudioClient;
        _scenarioService = scenarioService;
        _sessionService = sessionService;
        _autoSaveCoordinator = autoSaveCoordinator;
        _logger = logger;
    }

    public Task<StorySession> CreateSessionAsync(string title, string? scenarioId = null, CancellationToken cancellationToken = default)
    {
        var session = new StorySession
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled Story" : title.Trim(),
            ScenarioId = scenarioId
        };

        Sessions[session.Id] = session;
        _autoSaveCoordinator.QueueStorySessionSave(session, "story-session-created");
        _logger.LogInformation("Story session created: {SessionId} ({Title})", session.Id, session.Title);
        return Task.FromResult(session);
    }

    public async Task<IReadOnlyList<StorySession>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsurePersistedSessionsLoadedAsync(cancellationToken);

        IReadOnlyList<StorySession> results = Sessions.Values
            .OrderByDescending(x => x.ModifiedAt)
            .ToList();

        _logger.LogInformation("Retrieved {Count} story sessions", results.Count);
        return results;
    }

    public async Task<StorySession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (Sessions.TryGetValue(sessionId, out var session))
        {
            return session;
        }

        session = await _sessionService.LoadStorySessionAsync(sessionId, cancellationToken);
        if (session is not null)
        {
            Sessions[session.Id] = session;
        }

        return session;
    }

    public Task<StorySession> SaveSessionAsync(StorySession session, CancellationToken cancellationToken = default)
    {
        session.ModifiedAt = DateTime.UtcNow;
        Sessions[session.Id] = session;
        _autoSaveCoordinator.QueueStorySessionSave(session, "story-session-updated");
        _logger.LogInformation("Story session saved: {SessionId}, blocks={BlockCount}", session.Id, session.Blocks.Count);
        return Task.FromResult(session);
    }

    public async Task<StoryBlock> ContinueAsync(string sessionId, string? instruction = null, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            throw new InvalidOperationException($"Story session '{sessionId}' not found.");
        }

        var prompt = await BuildPromptAsync(session, instruction, cancellationToken);
        var aiOutput = await _lmStudioClient.GenerateAsync(prompt, cancellationToken);

        var block = new StoryBlock
        {
            BlockType = StoryBlockType.AiText,
            Author = "AI",
            Content = string.IsNullOrWhiteSpace(aiOutput) ? "(No output generated)" : aiOutput.Trim()
        };

        session.Blocks.Add(block);
        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueStorySessionSave(session, "story-continue-generated");

        _logger.LogInformation("Story continue generated block for session {SessionId}", sessionId);
        return block;
    }

    private async Task EnsurePersistedSessionsLoadedAsync(CancellationToken cancellationToken)
    {
        var persisted = await _sessionService.GetSessionsByTypeAsync(SessionService.StorySessionType, cancellationToken);
        foreach (var item in persisted)
        {
            if (Sessions.ContainsKey(item.Id))
            {
                continue;
            }

            var loaded = await _sessionService.LoadStorySessionAsync(item.Id, cancellationToken);
            if (loaded is not null)
            {
                Sessions[loaded.Id] = loaded;
            }
        }
    }

    private async Task<string> BuildPromptAsync(StorySession session, string? instruction, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are helping continue a creative story.");

        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null)
            {
                sb.AppendLine("Scenario:");
                sb.AppendLine($"- Name: {scenario.Name}");
                sb.AppendLine($"- Description: {scenario.Description}");
                sb.AppendLine($"- Plot: {scenario.Plot.Description}");
                sb.AppendLine($"- Setting: {scenario.Setting.WorldDescription}");
                sb.AppendLine($"- Style: {scenario.Style.WritingStyle} / {scenario.Style.Tone}");
            }
        }

        sb.AppendLine("Recent story blocks:");
        foreach (var block in session.Blocks.TakeLast(8))
        {
            sb.AppendLine($"[{block.BlockType}] {block.Author}: {block.Content}");
        }

        if (!string.IsNullOrWhiteSpace(instruction))
        {
            sb.AppendLine("Instruction for next continuation:");
            sb.AppendLine(instruction.Trim());
        }

        sb.AppendLine("Write only the next story continuation block.");
        return sb.ToString();
    }
}
