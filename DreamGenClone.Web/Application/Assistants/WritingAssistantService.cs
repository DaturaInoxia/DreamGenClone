using System.Text;
using DreamGenClone.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Assistants;

public sealed class WritingAssistantService : IWritingAssistantService
{
    private readonly ILmStudioClient _lmStudioClient;
    private readonly ILogger<WritingAssistantService> _logger;

    public WritingAssistantService(ILmStudioClient lmStudioClient, ILogger<WritingAssistantService> logger)
    {
        _lmStudioClient = lmStudioClient;
        _logger = logger;
    }

    public async Task<string> GenerateSuggestionAsync(
        string? scenarioSummary,
        IReadOnlyList<string> recentStoryBlocks,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildAssistantPrompt(scenarioSummary, recentStoryBlocks, userPrompt);
        var response = await _lmStudioClient.GenerateAsync(prompt, cancellationToken);

        _logger.LogInformation("Writing assistant suggestion generated");
        return string.IsNullOrWhiteSpace(response) ? "(No suggestion generated)" : response.Trim();
    }

    private static string BuildAssistantPrompt(string? scenarioSummary, IReadOnlyList<string> recentStoryBlocks, string userPrompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a writing assistant for a story composition workflow.");

        if (!string.IsNullOrWhiteSpace(scenarioSummary))
        {
            sb.AppendLine("Scenario context:");
            sb.AppendLine(scenarioSummary.Trim());
        }

        if (recentStoryBlocks.Count > 0)
        {
            sb.AppendLine("Recent story context:");
            foreach (var block in recentStoryBlocks.TakeLast(8))
            {
                sb.AppendLine($"- {block}");
            }
        }

        sb.AppendLine("User request:");
        sb.AppendLine(userPrompt);
        sb.AppendLine("Respond with concise, actionable writing guidance.");

        return sb.ToString();
    }
}
