using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Text;

namespace DreamGenClone.Web.Application.Assistants;

public sealed class ScenarioAssistantService : IScenarioAssistantService
{
    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolver;
    private readonly IAssistantContextManager _contextManager;
    private readonly ILogger<ScenarioAssistantService> _logger;

    public ScenarioAssistantService(
        ICompletionClient completionClient,
        IModelResolutionService modelResolver,
        IAssistantContextManager contextManager,
        ILogger<ScenarioAssistantService> logger)
    {
        _completionClient = completionClient;
        _modelResolver = modelResolver;
        _contextManager = contextManager;
        _logger = logger;
    }

    public async Task<string> GenerateSuggestionAsync(
        ScenarioAssistantContext context,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.SessionId))
        {
            throw new ArgumentException("SessionId is required.", nameof(context));
        }

        _contextManager.AddPinnedContext(context.SessionId, $"Scenario={context.ScenarioName}; Target={context.Target}");
        _contextManager.AddUserMessage(context.SessionId, userPrompt);

        var userMessage = BuildUserMessage(context, userPrompt);

        ResolvedModel resolved;
        try
        {
            resolved = await _modelResolver.ResolveAsync(AppFunction.ScenarioAssistant, cancellationToken: cancellationToken);
        }
        catch (ModelResolutionException ex)
        {
            _logger.LogWarning(ex, "ScenarioAssistant model default unavailable. Falling back to WritingAssistant model.");
            resolved = await _modelResolver.ResolveAsync(AppFunction.WritingAssistant, cancellationToken: cancellationToken);
        }

        _logger.LogInformation(
            "Scenario assistant request initiated: Session={SessionId}, Target={Target}",
            context.SessionId,
            context.Target);

        var response = await _completionClient.GenerateAsync(
            ScenarioAssistantPrompts.SystemPrompt,
            userMessage,
            resolved,
            cancellationToken);

        var trimmedResponse = CleanResponse(response);

        if (!HasUsableApplyBlock(trimmedResponse, context.Target))
        {
            _logger.LogInformation(
                "Scenario assistant response missing usable apply block. Retrying with repair instruction: Session={SessionId}, Target={Target}",
                context.SessionId,
                context.Target);

            var repairMessage = new StringBuilder()
                .AppendLine(userMessage)
                .AppendLine()
                .AppendLine("Validation failed on previous response: missing or placeholder apply block for target.")
                .AppendLine("Regenerate now and include exactly one valid apply block for the target with finished content.")
                .AppendLine("Do not use placeholders such as '...' or 'TBD'.")
                .ToString();

            var repaired = await _completionClient.GenerateAsync(
                ScenarioAssistantPrompts.SystemPrompt,
                repairMessage,
                resolved,
                cancellationToken);

            trimmedResponse = CleanResponse(repaired);
        }

        trimmedResponse = NormalizeAdvisoryResponse(context.Target, userPrompt, trimmedResponse);

        _contextManager.AddAssistantResponse(context.SessionId, trimmedResponse);

        _logger.LogInformation(
            "Scenario assistant suggestion generated: Session={SessionId}, Target={Target}",
            context.SessionId,
            context.Target);

        return trimmedResponse;
    }

    public void ClearChat(string sessionId)
    {
        _contextManager.ClearChat(sessionId);
        _logger.LogInformation("Cleared scenario assistant chat for session {SessionId}", sessionId);
    }

    private string BuildUserMessage(ScenarioAssistantContext context, string userPrompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ScenarioAssistantPrompts.BuildUserMessage(context, userPrompt));

        var history = _contextManager.GetContext(context.SessionId, maxItems: 15);
        if (history.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Conversation history:");
            foreach (var item in history)
            {
                sb.AppendLine(item.Content);
            }
        }

        return sb.ToString();
    }

    private static string CleanResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return "(No suggestion generated)";
        }

        var trimmed = response.Trim();

        if (trimmed.StartsWith("<think>", StringComparison.OrdinalIgnoreCase))
        {
            var endTag = trimmed.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (endTag >= 0)
            {
                var afterThink = trimmed[(endTag + 8)..].Trim();
                if (!string.IsNullOrWhiteSpace(afterThink))
                {
                    return afterThink;
                }

                trimmed = trimmed[7..endTag].Trim();
            }
            else
            {
                trimmed = trimmed[7..].Trim();
            }
        }

        return string.IsNullOrWhiteSpace(trimmed) ? "(No suggestion generated)" : trimmed;
    }

    private static bool HasUsableApplyBlock(string response, ScenarioAssistantTarget target)
    {
        if (target == ScenarioAssistantTarget.General)
        {
            return true;
        }

        var token = target switch
        {
            ScenarioAssistantTarget.PlotDescription => "PlotDescription",
            ScenarioAssistantTarget.WorldDescription => "WorldDescription",
            ScenarioAssistantTarget.StyleGuidelines => "StyleGuidelines",
            ScenarioAssistantTarget.Locations => "Locations",
            ScenarioAssistantTarget.Objects => "Objects",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        var startTag = $"[[SCENARIO_FIELD:{token}]]";
        const string endTag = "[[/SCENARIO_FIELD]]";

        var startIndex = response.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return false;
        }

        var contentStart = startIndex + startTag.Length;
        var endIndex = response.IndexOf(endTag, contentStart, StringComparison.OrdinalIgnoreCase);
        if (endIndex < 0)
        {
            return false;
        }

        var content = response[contentStart..endIndex].Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (content.Contains("...", StringComparison.Ordinal) ||
            content.Contains("TBD", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("insert here", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return content.Length >= 24;
    }

    private static string NormalizeAdvisoryResponse(ScenarioAssistantTarget target, string userPrompt, string response)
    {
        if (target != ScenarioAssistantTarget.General)
        {
            return response;
        }

        if (UserExplicitlyRequestedApplyContent(userPrompt))
        {
            return response;
        }

        var normalized = response;

        normalized = Regex.Replace(
            normalized,
            @"\[\[SCENARIO_FIELD:[^\]]+\]\][\s\S]*?\[\[/SCENARIO_FIELD\]\]",
            string.Empty,
            RegexOptions.IgnoreCase);

        normalized = Regex.Replace(
            normalized,
            @"(?im)^\s*##\s*Apply\s*Blocks\s*$[\s\S]*$",
            string.Empty,
            RegexOptions.IgnoreCase);

        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        normalized = normalized.Trim();

        return string.IsNullOrWhiteSpace(normalized) ? response : normalized;
    }

    private static bool UserExplicitlyRequestedApplyContent(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        var text = prompt.ToLowerInvariant();
        return text.Contains("apply") ||
               text.Contains("paste") ||
               text.Contains("draft") ||
               text.Contains("rewrite") ||
               text.Contains("give me text") ||
               text.Contains("structured") ||
               text.Contains("scenario_field");
    }
}
