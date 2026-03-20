namespace DreamGenClone.Web.Application.Assistants;

public interface IRolePlayAssistantService
{
    /// <summary>
    /// Generates a role-play suggestion based on scenario, recent interactions, and user prompt.
    /// Conversation history is tracked per-session with automatic context truncation.
    /// </summary>
    Task<string> GenerateSuggestionAsync(
        string sessionId,
        string? scenarioSummary,
        IReadOnlyList<string> recentInteractions,
        string userPrompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the assistant conversation history for a specific session.
    /// </summary>
    void ClearChat(string sessionId);
}
