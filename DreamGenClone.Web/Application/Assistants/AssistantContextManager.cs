using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Assistants;

/// <summary>
/// Context item with optional pinned flag for critical content.
/// </summary>
public sealed class AssistantContextItem
{
    public string Content { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
}

/// <summary>
/// Manages assistant conversation history with deterministic recency-based truncation.
/// </summary>
public interface IAssistantContextManager
{
    /// <summary>
    /// Adds a user message to the conversation history.
    /// </summary>
    void AddUserMessage(string sessionId, string message);

    /// <summary>
    /// Adds an assistant response to the conversation history.
    /// </summary>
    void AddAssistantResponse(string sessionId, string response);

    /// <summary>
    /// Adds pinned critical context that should be retained during truncation.
    /// </summary>
    void AddPinnedContext(string sessionId, string content);

    /// <summary>
    /// Gets the conversation history for a session, applying truncation if needed.
    /// </summary>
    IReadOnlyList<AssistantContextItem> GetContext(string sessionId, int maxItems = 20);

    /// <summary>
    /// Clears all conversation history for a session.
    /// </summary>
    void ClearChat(string sessionId);
}

/// <summary>
/// Context manager with deterministic recency truncation and pinned critical context retention.
/// </summary>
public sealed class AssistantContextManager : IAssistantContextManager
{
    private readonly Dictionary<string, List<AssistantContextItem>> _sessionContexts = new();
    private readonly ILogger<AssistantContextManager> _logger;

    public AssistantContextManager(ILogger<AssistantContextManager> logger)
    {
        _logger = logger;
    }

    public void AddUserMessage(string sessionId, string message)
    {
        EnsureSessionContext(sessionId);
        _sessionContexts[sessionId].Add(new AssistantContextItem
        {
            Content = $"[User] {message}",
            IsPinned = false
        });
        _logger.LogInformation("Added user message to assistant context for session {SessionId}", sessionId);
    }

    public void AddAssistantResponse(string sessionId, string response)
    {
        EnsureSessionContext(sessionId);
        _sessionContexts[sessionId].Add(new AssistantContextItem
        {
            Content = $"[Assistant] {response}",
            IsPinned = false
        });
        _logger.LogInformation("Added assistant response to context for session {SessionId}", sessionId);
    }

    public void AddPinnedContext(string sessionId, string content)
    {
        EnsureSessionContext(sessionId);
        _sessionContexts[sessionId].Insert(0, new AssistantContextItem
        {
            Content = $"[Pinned] {content}",
            IsPinned = true
        });
        _logger.LogInformation("Added pinned critical context for session {SessionId}", sessionId);
    }

    public IReadOnlyList<AssistantContextItem> GetContext(string sessionId, int maxItems = 20)
    {
        if (!_sessionContexts.TryGetValue(sessionId, out var context) || context.Count == 0)
        {
            return Array.Empty<AssistantContextItem>();
        }

        if (context.Count <= maxItems)
        {
            return context.AsReadOnly();
        }

        // Deterministic truncation: retain all pinned items + most recent unpinned items
        var pinned = context.Where(c => c.IsPinned).ToList();
        var unpinned = context.Where(c => !c.IsPinned).ToList();
        var availableSlots = maxItems - pinned.Count;

        if (availableSlots <= 0)
        {
            // If pinned items exceed limit, return only pinned (should be rare)
            _logger.LogWarning(
                "Pinned context items ({PinnedCount}) exceed maxItems ({MaxItems}) for session {SessionId}. Returning pinned items only.",
                pinned.Count,
                maxItems,
                sessionId);
            return pinned.Take(maxItems).ToList().AsReadOnly();
        }

        var recentUnpinned = unpinned.TakeLast(availableSlots).ToList();
        var truncated = pinned.Concat(recentUnpinned).ToList();

        _logger.LogInformation(
            "Truncated assistant context for session {SessionId}: {TotalItems} -> {RetainedItems} (Pinned: {PinnedCount}, Recent: {RecentCount})",
            sessionId,
            context.Count,
            truncated.Count,
            pinned.Count,
            recentUnpinned.Count);

        return truncated.AsReadOnly();
    }

    public void ClearChat(string sessionId)
    {
        if (_sessionContexts.Remove(sessionId))
        {
            _logger.LogInformation("Cleared assistant chat for session {SessionId}", sessionId);
        }
    }

    private void EnsureSessionContext(string sessionId)
    {
        if (!_sessionContexts.ContainsKey(sessionId))
        {
            _sessionContexts[sessionId] = new List<AssistantContextItem>();
        }
    }
}
