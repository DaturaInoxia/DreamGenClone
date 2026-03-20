using System.Text;
using DreamGenClone.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Assistants;

public sealed class WritingAssistantService : IWritingAssistantService
{
    private readonly ILmStudioClient _lmStudioClient;
    private readonly IAssistantContextManager _contextManager;
    private readonly ILogger<WritingAssistantService> _logger;

    public WritingAssistantService(
        ILmStudioClient lmStudioClient,
        IAssistantContextManager contextManager,
        ILogger<WritingAssistantService> logger)
    {
        _lmStudioClient = lmStudioClient;
        _contextManager = contextManager;
        _logger = logger;
    }

    public async Task<string> GenerateSuggestionAsync(
        string sessionId,
        string? scenarioSummary,
        IReadOnlyList<string> recentStoryBlocks,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        // Add pinned scenario context if present and not already added
        if (!string.IsNullOrWhiteSpace(scenarioSummary))
        {
            _contextManager.AddPinnedContext(sessionId, $"Scenario: {scenarioSummary}");
        }

        // Add user message to conversation history
        _contextManager.AddUserMessage(sessionId, userPrompt);

        // Build prompt with conversation context and recent story blocks
        var prompt = BuildAssistantPrompt(sessionId, recentStoryBlocks, userPrompt);
        _logger.LogInformation("Writing assistant request initiated for session {SessionId}", sessionId);

        var response = await _lmStudioClient.GenerateAsync(prompt, cancellationToken);

        var trimmedResponse = string.IsNullOrWhiteSpace(response) ? "(No suggestion generated)" : response.Trim();

        // Add assistant response to conversation history
        _contextManager.AddAssistantResponse(sessionId, trimmedResponse);

        _logger.LogInformation("Writing assistant suggestion generated for session {SessionId}", sessionId);
        return trimmedResponse;
    }

    public void ClearChat(string sessionId)
    {
        _contextManager.ClearChat(sessionId);
        _logger.LogInformation("Cleared writing assistant chat for session {SessionId}", sessionId);
    }

    private string BuildAssistantPrompt(string sessionId, IReadOnlyList<string> recentStoryBlocks, string userPrompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a writing assistant for a story composition workflow.");

        // Include conversation history with truncation
        var context = _contextManager.GetContext(sessionId, maxItems: 15);
        if (context.Count > 0)
        {
            sb.AppendLine("Conversation history:");
            foreach (var item in context)
            {
                sb.AppendLine(item.Content);
            }
        }

        // Include recent story blocks as additional context
        if (recentStoryBlocks.Count > 0)
        {
            sb.AppendLine("Recent story context:");
            foreach (var block in recentStoryBlocks.TakeLast(8))
            {
                sb.AppendLine($"- {block}");
            }
        }

        sb.AppendLine("Current user request:");
        sb.AppendLine(userPrompt);
        sb.AppendLine("Respond with concise, actionable writing guidance.");

        return sb.ToString();
    }
}
