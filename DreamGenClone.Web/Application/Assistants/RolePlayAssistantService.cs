using System.Text;
using DreamGenClone.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Assistants;

public sealed class RolePlayAssistantService : IRolePlayAssistantService
{
    private readonly ILmStudioClient _lmStudioClient;
    private readonly IAssistantContextManager _contextManager;
    private readonly ILogger<RolePlayAssistantService> _logger;

    public RolePlayAssistantService(
        ILmStudioClient lmStudioClient,
        IAssistantContextManager contextManager,
        ILogger<RolePlayAssistantService> logger)
    {
        _lmStudioClient = lmStudioClient;
        _contextManager = contextManager;
        _logger = logger;
    }

    public async Task<string> GenerateSuggestionAsync(
        string sessionId,
        string? scenarioSummary,
        IReadOnlyList<string> recentInteractions,
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

        // Build prompt with conversation context and recent interactions
        var prompt = BuildAssistantPrompt(sessionId, recentInteractions, userPrompt);
        _logger.LogInformation("Role-play assistant request initiated for session {SessionId}", sessionId);

        var response = await _lmStudioClient.GenerateAsync(prompt, cancellationToken);

        var trimmedResponse = string.IsNullOrWhiteSpace(response) ? "(No suggestion generated)" : response.Trim();

        // Add assistant response to conversation history
        _contextManager.AddAssistantResponse(sessionId, trimmedResponse);

        _logger.LogInformation("Role-play assistant suggestion generated for session {SessionId}", sessionId);
        return trimmedResponse;
    }

    public void ClearChat(string sessionId)
    {
        _contextManager.ClearChat(sessionId);
        _logger.LogInformation("Cleared role-play assistant chat for session {SessionId}", sessionId);
    }

    private string BuildAssistantPrompt(string sessionId, IReadOnlyList<string> recentInteractions, string userPrompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a role-play assistant for an interactive narrative experience.");

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

        // Include recent role-play interactions as additional context
        if (recentInteractions.Count > 0)
        {
            sb.AppendLine("Recent role-play interactions:");
            foreach (var interaction in recentInteractions.TakeLast(8))
            {
                sb.AppendLine($"- {interaction}");
            }
        }

        sb.AppendLine("Current user request:");
        sb.AppendLine(userPrompt);
        sb.AppendLine("Respond with concise, actionable role-play guidance.");

        return sb.ToString();
    }
}
