using DreamGenClone.Web.Domain.Story;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Story;

public sealed class StoryCommandService : IStoryCommandService
{
    private static readonly Dictionary<string, Stack<List<StoryBlock>>> History = [];

    private readonly IStoryEngineService _engine;
    private readonly ILogger<StoryCommandService> _logger;

    public StoryCommandService(IStoryEngineService engine, ILogger<StoryCommandService> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public async Task CaptureCheckpointAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _engine.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return;
        }

        if (!History.TryGetValue(sessionId, out var stack))
        {
            stack = new Stack<List<StoryBlock>>();
            History[sessionId] = stack;
        }

        var snapshot = session.Blocks
            .Select(CloneBlock)
            .ToList();

        stack.Push(snapshot);
        _logger.LogInformation("Checkpoint captured for story session {SessionId}, depth={Depth}", sessionId, stack.Count);
    }

    public async Task<bool> RewindAsync(string sessionId, int toBlockIndexInclusive, CancellationToken cancellationToken = default)
    {
        var session = await _engine.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return false;
        }

        await CaptureCheckpointAsync(sessionId, cancellationToken);

        if (toBlockIndexInclusive < 0)
        {
            session.Blocks.Clear();
        }
        else if (toBlockIndexInclusive < session.Blocks.Count - 1)
        {
            session.Blocks = session.Blocks
                .Take(toBlockIndexInclusive + 1)
                .ToList();
        }

        await _engine.SaveSessionAsync(session, cancellationToken);
        _logger.LogInformation("Rewind applied for story session {SessionId} to index {Index}", sessionId, toBlockIndexInclusive);
        return true;
    }

    public async Task<bool> UndoAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _engine.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return false;
        }

        if (!History.TryGetValue(sessionId, out var stack) || stack.Count == 0)
        {
            return false;
        }

        session.Blocks = stack.Pop();
        await _engine.SaveSessionAsync(session, cancellationToken);

        _logger.LogInformation("Undo restored previous checkpoint for story session {SessionId}", sessionId);
        return true;
    }

    public async Task<bool> AppendUserTextAsync(string sessionId, string content, CancellationToken cancellationToken = default)
    {
        var session = await _engine.GetSessionAsync(sessionId, cancellationToken);
        if (session is null || string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        await CaptureCheckpointAsync(sessionId, cancellationToken);

        session.Blocks.Add(new StoryBlock
        {
            BlockType = StoryBlockType.UserText,
            Author = "You",
            Content = content.Trim()
        });

        await _engine.SaveSessionAsync(session, cancellationToken);
        _logger.LogInformation("Appended user story block to session {SessionId}", sessionId);
        return true;
    }

    public async Task<bool> AppendInstructionAsync(string sessionId, string content, CancellationToken cancellationToken = default)
    {
        var session = await _engine.GetSessionAsync(sessionId, cancellationToken);
        if (session is null || string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        await CaptureCheckpointAsync(sessionId, cancellationToken);

        session.Blocks.Add(new StoryBlock
        {
            BlockType = StoryBlockType.Instruction,
            Author = "Instruction",
            Content = content.Trim()
        });

        await _engine.SaveSessionAsync(session, cancellationToken);
        _logger.LogInformation("Appended instruction block to session {SessionId}", sessionId);
        return true;
    }

    private static StoryBlock CloneBlock(StoryBlock block)
    {
        return new StoryBlock
        {
            Id = block.Id,
            BlockType = block.BlockType,
            Author = block.Author,
            Content = block.Content,
            CreatedAt = block.CreatedAt
        };
    }
}
