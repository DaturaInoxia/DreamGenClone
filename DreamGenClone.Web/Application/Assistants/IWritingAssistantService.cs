namespace DreamGenClone.Web.Application.Assistants;

public interface IWritingAssistantService
{
    /// <summary>
    /// Generates a writing suggestion based on scenario, recent story blocks, and user prompt.
    /// Conversation history is tracked per-session with automatic context truncation.
    /// </summary>
    Task<string> GenerateSuggestionAsync(
        string sessionId,
        string? scenarioSummary,
        IReadOnlyList<string> recentStoryBlocks,
        string userPrompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the assistant conversation history for a specific session.
    /// </summary>
    void ClearChat(string sessionId);
}
