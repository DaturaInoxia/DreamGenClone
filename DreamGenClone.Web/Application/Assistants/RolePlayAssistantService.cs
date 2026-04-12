using System.Text;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Assistants;

public sealed class RolePlayAssistantService : IRolePlayAssistantService
{
    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolver;
    private readonly IAssistantContextManager _contextManager;
    private readonly ILogger<RolePlayAssistantService> _logger;

    public RolePlayAssistantService(
        ICompletionClient completionClient,
        IModelResolutionService modelResolver,
        IAssistantContextManager contextManager,
        ILogger<RolePlayAssistantService> logger)
    {
        _completionClient = completionClient;
        _modelResolver = modelResolver;
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
        var context = new RolePlayAssistantContext
        {
            SessionId = sessionId,
            ScenarioSummary = scenarioSummary,
            RecentInteractions = recentInteractions
        };
        return await GenerateSuggestionAsync(context, userPrompt, cancellationToken: cancellationToken);
    }

    public async Task<string> GenerateSuggestionAsync(
        RolePlayAssistantContext context,
        string userPrompt,
        string? assistantModelId = null,
        double? assistantTemperature = null,
        double? assistantTopP = null,
        int? assistantMaxTokens = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = context.SessionId;

        // Add user message to conversation history
        _contextManager.AddUserMessage(sessionId, userPrompt);

        // Build the user message with conversation context and session state
        var userMessage = BuildUserMessage(sessionId, context, userPrompt);
        _logger.LogInformation("Role-play assistant request initiated for session {SessionId}", sessionId);

        var resolved = await _modelResolver.ResolveAsync(
            AppFunction.RolePlayAssistant,
            sessionModelId: assistantModelId,
            sessionTemperature: assistantTemperature,
            sessionTopP: assistantTopP,
            sessionMaxTokens: assistantMaxTokens,
            cancellationToken: cancellationToken);

        var response = await _completionClient.GenerateAsync(
            RolePlayAssistantPrompts.SystemPrompt,
            userMessage,
            resolved,
            cancellationToken);

        var trimmedResponse = CleanResponse(response);

        // Add assistant response to conversation history
        _contextManager.AddAssistantResponse(sessionId, trimmedResponse);

        _logger.LogInformation("Role-play assistant suggestion generated for session {SessionId}", sessionId);
        return trimmedResponse;
    }

    public async Task<string> GenerateSuggestionStreamingAsync(
        RolePlayAssistantContext context,
        string userPrompt,
        Func<string, Task> onChunk,
        string? assistantModelId = null,
        double? assistantTemperature = null,
        double? assistantTopP = null,
        int? assistantMaxTokens = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = context.SessionId;

        _contextManager.AddUserMessage(sessionId, userPrompt);

        var userMessage = BuildUserMessage(sessionId, context, userPrompt);
        _logger.LogInformation("Role-play assistant streaming request initiated for session {SessionId}", sessionId);

        var resolved = await _modelResolver.ResolveAsync(
            AppFunction.RolePlayAssistant,
            sessionModelId: assistantModelId,
            sessionTemperature: assistantTemperature,
            sessionTopP: assistantTopP,
            sessionMaxTokens: assistantMaxTokens,
            cancellationToken: cancellationToken);

        var response = await _completionClient.StreamGenerateAsync(
            RolePlayAssistantPrompts.SystemPrompt,
            userMessage,
            resolved,
            onChunk,
            cancellationToken);

        var trimmedResponse = CleanResponse(response);

        _contextManager.AddAssistantResponse(sessionId, trimmedResponse);

        _logger.LogInformation("Role-play assistant streaming suggestion generated for session {SessionId}", sessionId);
        return trimmedResponse;
    }

    public void ClearChat(string sessionId)
    {
        _contextManager.ClearChat(sessionId);
        _logger.LogInformation("Cleared role-play assistant chat for session {SessionId}", sessionId);
    }

    private string BuildUserMessage(string sessionId, RolePlayAssistantContext context, string userPrompt)
    {
        var sb = new StringBuilder();

        // Scenario context
        if (!string.IsNullOrWhiteSpace(context.ScenarioSummary))
        {
            sb.AppendLine($"[Scenario: {context.ScenarioSummary}]");
        }

        // Narrative context — critical for pacing and prose guidance
        if (!string.IsNullOrWhiteSpace(context.ScenarioNarrativeTone) || !string.IsNullOrWhiteSpace(context.ScenarioProseStyle))
        {
            var styleParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(context.ScenarioNarrativeTone)) styleParts.Add($"NarrativeTone={context.ScenarioNarrativeTone}");
            if (!string.IsNullOrWhiteSpace(context.ScenarioProseStyle)) styleParts.Add($"ProseStyle={context.ScenarioProseStyle}");
            if (!string.IsNullOrWhiteSpace(context.ScenarioPointOfView)) styleParts.Add($"POV={context.ScenarioPointOfView}");
            sb.AppendLine($"[Narrative Surface: {string.Join(", ", styleParts)}]");
            if (context.ScenarioNarrativeGuidelines.Count > 0)
                sb.AppendLine($"[Narrative Guidelines: {string.Join("; ", context.ScenarioNarrativeGuidelines)}]");
        }

        // Plot drivers
        if (context.ScenarioConflicts.Count > 0)
            sb.AppendLine($"[Active Conflicts: {string.Join("; ", context.ScenarioConflicts)}]");
        if (context.ScenarioGoals.Count > 0)
            sb.AppendLine($"[Story Goals: {string.Join("; ", context.ScenarioGoals)}]");

        // World rules
        if (context.ScenarioWorldRules.Count > 0)
            sb.AppendLine($"[World Rules: {string.Join("; ", context.ScenarioWorldRules)}]");

        // Session state context
        if (!string.IsNullOrWhiteSpace(context.BehaviorMode))
        {
            sb.AppendLine($"[Current Behavior Mode: {context.BehaviorMode}]");
        }

        if (!string.IsNullOrWhiteSpace(context.EffectiveStyleMode))
        {
            sb.AppendLine($"[Resolved Intensity: {context.EffectiveStyleMode}]");
        }

        if (!string.IsNullOrWhiteSpace(context.ActiveIntensityProfile))
        {
            sb.AppendLine($"[Active Intensity Profile: {context.ActiveIntensityProfile}]");
        }

        if (!string.IsNullOrWhiteSpace(context.StyleResolutionReason))
        {
            sb.AppendLine($"[Resolution Reason: {context.StyleResolutionReason}]");
        }

        if (!string.IsNullOrWhiteSpace(context.SelectedThemeProfileId)
            || !string.IsNullOrWhiteSpace(context.SelectedIntensityProfileId)
            || !string.IsNullOrWhiteSpace(context.ActiveIntensityProfile)
            || !string.IsNullOrWhiteSpace(context.SelectedSteeringProfileId)
            || !string.IsNullOrWhiteSpace(context.IntensityFloorOverride)
            || !string.IsNullOrWhiteSpace(context.IntensityCeilingOverride))
        {
            sb.AppendLine($"[Adaptive Profiles: theme={context.SelectedThemeProfileId ?? "(none)"}, baseIntensity={context.SelectedIntensityProfileId ?? "(none)"}, activeIntensity={context.ActiveIntensityProfile ?? context.EffectiveStyleMode ?? "(none)"}, steering={context.SelectedSteeringProfileId ?? "(none)"}, intensityFloor={context.IntensityFloorOverride ?? "(none)"}, intensityCeiling={context.IntensityCeilingOverride ?? "(none)"}, manualPin={(context.IsIntensityManuallyPinned ? "on" : "off")}] ");
        }
        else if (context.IsIntensityManuallyPinned)
        {
            sb.AppendLine("[Adaptive Profiles: manualPin=on]");
        }

        if (context.ProfileSteeringThemes.Count > 0)
        {
            sb.AppendLine("[Profile Steering Themes]");
            foreach (var item in context.ProfileSteeringThemes)
            {
                sb.AppendLine($"- {item}");
            }
        }

        if (!string.IsNullOrWhiteSpace(context.PersonaName))
        {
            sb.Append($"[Persona: {context.PersonaName}");
            if (!string.IsNullOrWhiteSpace(context.PersonaDescription))
                sb.Append($" — {context.PersonaDescription}");
            sb.AppendLine("]");
        }

        // Full character details for field-specific advice
        if (context.FullCharacterDetails.Count > 0)
        {
            sb.AppendLine("[Scene Characters]");
            foreach (var character in context.FullCharacterDetails)
            {
                sb.AppendLine($"- {character}");
            }
        }
        else if (context.CharacterSummaries.Count > 0)
        {
            sb.AppendLine("[Scene Characters]");
            foreach (var character in context.CharacterSummaries)
            {
                sb.AppendLine($"- {character}");
            }
        }

        if (context.ContextWindowSize > 0)
        {
            sb.AppendLine($"[Context Window: {context.ContextWindowSize} interactions, {context.PinnedInteractionCount} pinned]");
        }

        if (!string.IsNullOrWhiteSpace(context.SessionModelId))
        {
            sb.AppendLine($"[Generation Model Settings: temp={context.SessionTemperature:F2}, topP={context.SessionTopP:F2}, maxTokens={context.SessionMaxTokens}]");
        }

        // Include conversation history with truncation
        var history = _contextManager.GetContext(sessionId, maxItems: 15);
        if (history.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Conversation history:");
            foreach (var item in history)
            {
                sb.AppendLine(item.Content);
            }
        }

        // Include recent role-play interactions as additional context
        if (context.RecentInteractions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recent role-play interactions:");
            foreach (var interaction in context.RecentInteractions.TakeLast(8))
            {
                sb.AppendLine($"- {interaction}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("User question:");
        sb.AppendLine(userPrompt);

        return sb.ToString();
    }

    /// <summary>
    /// Cleans up model responses — handles empty responses, reasoning-only output, and formatting.
    /// </summary>
    private static string CleanResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "(No suggestion generated)";

        var trimmed = response.Trim();

        // Remove common reasoning model prefixes/wrappers
        // Some models wrap output in <think>...</think> tags
        if (trimmed.StartsWith("<think>", StringComparison.OrdinalIgnoreCase))
        {
            var endTag = trimmed.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (endTag >= 0)
            {
                // Content after the think block is the actual answer
                var afterThink = trimmed[(endTag + 8)..].Trim();
                if (!string.IsNullOrWhiteSpace(afterThink))
                    return afterThink;
                // If nothing after think block, use the thinking content
                trimmed = trimmed[7..endTag].Trim();
            }
            else
            {
                // Unclosed think tag — strip it
                trimmed = trimmed[7..].Trim();
            }
        }

        return string.IsNullOrWhiteSpace(trimmed) ? "(No suggestion generated)" : trimmed;
    }
}
