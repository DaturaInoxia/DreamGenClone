using System.Text;
using System.Diagnostics;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Logging;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Assistants;

public sealed class RolePlayAssistantService : IRolePlayAssistantService
{
    private const int EvidenceEventTake = 80;
    private const int EvidenceLogLineTake = 160;

    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolver;
    private readonly IAssistantContextManager _contextManager;
    private readonly IRolePlayDiagnosticsService? _diagnosticsService;
    private readonly RolePlayDebugEventService? _debugEventService;
    private readonly ILogger<RolePlayAssistantService> _logger;

    private enum AssistantQueryMode
    {
        Standard = 0,
        EngineExpert = 1,
        JsonOptionGenerator = 2
    }

    public RolePlayAssistantService(
        ICompletionClient completionClient,
        IModelResolutionService modelResolver,
        IAssistantContextManager contextManager,
        ILogger<RolePlayAssistantService> logger,
        IRolePlayDiagnosticsService? diagnosticsService = null,
        RolePlayDebugEventService? debugEventService = null)
    {
        _completionClient = completionClient;
        _modelResolver = modelResolver;
        _contextManager = contextManager;
        _diagnosticsService = diagnosticsService;
        _debugEventService = debugEventService;
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
        var correlationId = string.IsNullOrWhiteSpace(context.CorrelationId)
            ? Guid.NewGuid().ToString("N")[..12]
            : context.CorrelationId;
        var totalStopwatch = Stopwatch.StartNew();

        // Add user message to conversation history
        _contextManager.AddUserMessage(sessionId, userPrompt);

        var queryMode = ClassifyQueryMode(userPrompt);
        var includeDiagnostics = queryMode == AssistantQueryMode.EngineExpert;
        var includeEvidence = includeDiagnostics && IsEvidenceRequested(userPrompt);

        // Build the user message with conversation context and session state
        var diagnosticsStopwatch = Stopwatch.StartNew();
        var diagnostics = !includeDiagnostics || _diagnosticsService is null
            ? null
            : await _diagnosticsService.GetSnapshotAsync(sessionId, cancellationToken: cancellationToken);
        diagnosticsStopwatch.Stop();

        var evidenceStopwatch = Stopwatch.StartNew();
        var evidenceBlock = includeEvidence
            ? await BuildEvidenceBlockAsync(sessionId, correlationId, userPrompt, cancellationToken)
            : string.Empty;
        evidenceStopwatch.Stop();

        var promptBuildStopwatch = Stopwatch.StartNew();
        var userMessage = BuildUserMessage(sessionId, context, userPrompt, diagnostics, queryMode, evidenceBlock);
        promptBuildStopwatch.Stop();

        _logger.LogInformation(
            "Role-play assistant request initiated: SessionId={SessionId}, CorrelationId={CorrelationId}, Mode={Mode}, DiagnosticsMs={DiagnosticsMs}, EvidenceMs={EvidenceMs}, PromptBuildMs={PromptBuildMs}, PromptLength={PromptLength}",
            sessionId,
            correlationId,
            queryMode,
            diagnosticsStopwatch.ElapsedMilliseconds,
            evidenceStopwatch.ElapsedMilliseconds,
            promptBuildStopwatch.ElapsedMilliseconds,
            userMessage.Length);

        var resolveStopwatch = Stopwatch.StartNew();
        var completionStopwatch = new Stopwatch();
        var normalizeStopwatch = new Stopwatch();
        string trimmedResponse;

        try
        {
            var systemPrompt = SelectSystemPrompt(queryMode);

            var resolved = await _modelResolver.ResolveAsync(
                AppFunction.RolePlayAssistant,
                sessionModelId: assistantModelId,
                sessionTemperature: assistantTemperature,
                sessionTopP: assistantTopP,
                sessionMaxTokens: assistantMaxTokens,
                cancellationToken: cancellationToken);
            resolveStopwatch.Stop();

            completionStopwatch.Start();
            var response = await _completionClient.GenerateAsync(
                systemPrompt,
                userMessage,
                resolved,
                cancellationToken);
            completionStopwatch.Stop();

            normalizeStopwatch.Start();
            trimmedResponse = CleanResponse(response);
            normalizeStopwatch.Stop();
        }
        catch (OperationCanceledException ex)
        {
            totalStopwatch.Stop();
            if (resolveStopwatch.IsRunning) resolveStopwatch.Stop();
            if (completionStopwatch.IsRunning) completionStopwatch.Stop();
            if (normalizeStopwatch.IsRunning) normalizeStopwatch.Stop();

            var cancelReason = cancellationToken.IsCancellationRequested ? "CallerCanceledToken" : "DownstreamTimeoutOrCancel";
            _logger.LogWarning(
                ex,
                "Role-play assistant request canceled: SessionId={SessionId}, CorrelationId={CorrelationId}, Reason={CancelReason}, DiagnosticsMs={DiagnosticsMs}, PromptBuildMs={PromptBuildMs}, ResolveMs={ResolveMs}, CompletionMs={CompletionMs}, NormalizeMs={NormalizeMs}, TotalMs={TotalMs}",
                sessionId,
                correlationId,
                cancelReason,
                diagnosticsStopwatch.ElapsedMilliseconds,
                promptBuildStopwatch.ElapsedMilliseconds,
                resolveStopwatch.ElapsedMilliseconds,
                completionStopwatch.ElapsedMilliseconds,
                normalizeStopwatch.ElapsedMilliseconds,
                totalStopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            if (resolveStopwatch.IsRunning) resolveStopwatch.Stop();
            if (completionStopwatch.IsRunning) completionStopwatch.Stop();
            if (normalizeStopwatch.IsRunning) normalizeStopwatch.Stop();

            _logger.LogError(
                ex,
                "Role-play assistant request failed: SessionId={SessionId}, CorrelationId={CorrelationId}, DiagnosticsMs={DiagnosticsMs}, PromptBuildMs={PromptBuildMs}, ResolveMs={ResolveMs}, CompletionMs={CompletionMs}, NormalizeMs={NormalizeMs}, TotalMs={TotalMs}",
                sessionId,
                correlationId,
                diagnosticsStopwatch.ElapsedMilliseconds,
                promptBuildStopwatch.ElapsedMilliseconds,
                resolveStopwatch.ElapsedMilliseconds,
                completionStopwatch.ElapsedMilliseconds,
                normalizeStopwatch.ElapsedMilliseconds,
                totalStopwatch.ElapsedMilliseconds);
            throw;
        }

        // Add assistant response to conversation history
        _contextManager.AddAssistantResponse(sessionId, trimmedResponse);

        if (diagnostics is not null)
        {
            _logger.LogInformation(
                RolePlayV2LogEvents.DiagnosticsSnapshotPublished,
                diagnostics.SessionId,
                diagnostics.CorrelationId,
                diagnostics.CandidateEvaluations.Count,
                diagnostics.TransitionEvents.Count,
                diagnostics.DecisionPoints.Count,
                diagnostics.CompatibilityErrors.Count);
        }

        totalStopwatch.Stop();
        _logger.LogInformation(
            "Role-play assistant suggestion generated: SessionId={SessionId}, CorrelationId={CorrelationId}, ResolveMs={ResolveMs}, CompletionMs={CompletionMs}, NormalizeMs={NormalizeMs}, TotalMs={TotalMs}, ResponseLength={ResponseLength}",
            sessionId,
            correlationId,
            resolveStopwatch.ElapsedMilliseconds,
            completionStopwatch.ElapsedMilliseconds,
            normalizeStopwatch.ElapsedMilliseconds,
            totalStopwatch.ElapsedMilliseconds,
            trimmedResponse.Length);
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

        var queryMode = ClassifyQueryMode(userPrompt);
        var includeDiagnostics = queryMode == AssistantQueryMode.EngineExpert;
        var includeEvidence = includeDiagnostics && IsEvidenceRequested(userPrompt);

        var diagnostics = !includeDiagnostics || _diagnosticsService is null
            ? null
            : await _diagnosticsService.GetSnapshotAsync(sessionId, cancellationToken: cancellationToken);

        var evidenceBlock = includeEvidence
            ? await BuildEvidenceBlockAsync(sessionId, context.CorrelationId, userPrompt, cancellationToken)
            : string.Empty;

        var userMessage = BuildUserMessage(sessionId, context, userPrompt, diagnostics, queryMode, evidenceBlock);
        _logger.LogInformation("Role-play assistant streaming request initiated for session {SessionId}", sessionId);

        var resolved = await _modelResolver.ResolveAsync(
            AppFunction.RolePlayAssistant,
            sessionModelId: assistantModelId,
            sessionTemperature: assistantTemperature,
            sessionTopP: assistantTopP,
            sessionMaxTokens: assistantMaxTokens,
            cancellationToken: cancellationToken);

        var systemPrompt = SelectSystemPrompt(queryMode);

        var response = await _completionClient.StreamGenerateAsync(
            systemPrompt,
            userMessage,
            resolved,
            onChunk,
            cancellationToken);

        var trimmedResponse = CleanResponse(response);

        _contextManager.AddAssistantResponse(sessionId, trimmedResponse);

        if (diagnostics is not null)
        {
            _logger.LogInformation(
                RolePlayV2LogEvents.DiagnosticsSnapshotPublished,
                diagnostics.SessionId,
                diagnostics.CorrelationId,
                diagnostics.CandidateEvaluations.Count,
                diagnostics.TransitionEvents.Count,
                diagnostics.DecisionPoints.Count,
                diagnostics.CompatibilityErrors.Count);
        }

        _logger.LogInformation("Role-play assistant streaming suggestion generated for session {SessionId}", sessionId);
        return trimmedResponse;
    }

    public void ClearChat(string sessionId)
    {
        _contextManager.ClearChat(sessionId);
        _logger.LogInformation("Cleared role-play assistant chat for session {SessionId}", sessionId);
    }

    private static string SelectSystemPrompt(AssistantQueryMode queryMode)
    {
        if (queryMode == AssistantQueryMode.JsonOptionGenerator)
        {
            return RolePlayAssistantPrompts.JsonOptionGeneratorSystemPrompt;
        }

        if (queryMode == AssistantQueryMode.EngineExpert)
        {
            return RolePlayAssistantPrompts.EngineExpertSystemPrompt;
        }

        return RolePlayAssistantPrompts.SystemPrompt;
    }

    private string BuildUserMessage(
        string sessionId,
        RolePlayAssistantContext context,
        string userPrompt,
        RolePlayV2DiagnosticsSnapshot? diagnostics,
        AssistantQueryMode queryMode,
        string evidenceBlock)
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

        if (!string.IsNullOrWhiteSpace(context.CurrentNarrativePhase)
            || !string.IsNullOrWhiteSpace(context.ActiveScenarioId)
            || !string.IsNullOrWhiteSpace(context.SelectedThemeProfileId)
            || !string.IsNullOrWhiteSpace(context.SelectedNarrativeGateProfileId))
        {
            sb.AppendLine($"[Engine State: phase={context.CurrentNarrativePhase ?? "(unknown)"}, activeScenario={context.ActiveScenarioId ?? "(none)"}, themeProfile={context.SelectedThemeProfileId ?? "(none)"}, gateProfile={context.SelectedNarrativeGateProfileId ?? "(none)"}]");
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

        if (!string.IsNullOrWhiteSpace(context.AdaptiveTransitionReason))
        {
            var transitionSummary = $"reason={context.AdaptiveTransitionReason}";
            if (!string.IsNullOrWhiteSpace(context.AdaptiveTransitionFromProfileId)
                || !string.IsNullOrWhiteSpace(context.AdaptiveTransitionToProfileId))
            {
                transitionSummary += $", from={context.AdaptiveTransitionFromProfileId ?? "(none)"}, to={context.AdaptiveTransitionToProfileId ?? "(none)"}";
            }

            if (context.AdaptiveTransitionUtc.HasValue)
            {
                transitionSummary += $", at={context.AdaptiveTransitionUtc.Value:O}";
            }

            if (context.AdaptiveTransitionBlockedByManualPin)
            {
                transitionSummary += ", manualPin=blocked";
            }

            sb.AppendLine($"[Adaptive Transition: {transitionSummary}]");
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
            if (!string.IsNullOrWhiteSpace(context.PersonaRole))
                sb.Append($" | Role: {context.PersonaRole}");
            if (!string.IsNullOrWhiteSpace(context.PersonaRelationSummary))
                sb.Append($" | Relation: {context.PersonaRelationSummary}");
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

        if (diagnostics is not null)
        {
            sb.AppendLine($"[Diagnostics: candidates={diagnostics.CandidateEvaluations.Count}, transitions={diagnostics.TransitionEvents.Count}, decisions={diagnostics.DecisionPoints.Count}, errors={diagnostics.CompatibilityErrors.Count}]");
            if (queryMode == AssistantQueryMode.EngineExpert)
            {
                AppendDetailedDiagnostics(sb, diagnostics);
            }
        }

        if (!string.IsNullOrWhiteSpace(evidenceBlock))
        {
            sb.AppendLine();
            sb.AppendLine("[Evidence]");
            sb.AppendLine(evidenceBlock);
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

    private static AssistantQueryMode ClassifyQueryMode(string userPrompt)
    {
        if (!string.IsNullOrWhiteSpace(userPrompt)
            && userPrompt.Contains("Return ONLY a JSON array of strings with no markdown and no extra text.", StringComparison.OrdinalIgnoreCase))
        {
            return AssistantQueryMode.JsonOptionGenerator;
        }

        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return AssistantQueryMode.Standard;
        }

        var normalized = userPrompt.Trim();
        var expertSignals = new[]
        {
            "why",
            "how does",
            "how do",
            "explain",
            "diagnostic",
            "debug",
            "theme gate",
            "next phase",
            "phase",
            "steering",
            "finish",
            "candidate",
            "profile",
            "character stat",
            "engine"
        };

        return expertSignals.Any(signal => normalized.Contains(signal, StringComparison.OrdinalIgnoreCase))
            ? AssistantQueryMode.EngineExpert
            : AssistantQueryMode.Standard;
    }

    private static bool IsEvidenceRequested(string userPrompt)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return false;
        }

        var signals = new[]
        {
            "why",
            "exact",
            "evidence",
            "prove",
            "show log",
            "logs",
            "database",
            "table",
            "debug",
            "what happened",
            "reason"
        };

        return signals.Any(signal => userPrompt.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> BuildEvidenceBlockAsync(
        string sessionId,
        string? correlationId,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        if (_debugEventService is null)
        {
            return string.Empty;
        }

        var searchText = BuildEvidenceSearchText(userPrompt);
        var events = await _debugEventService.QuerySessionEventsAsync(
            sessionId,
            search: searchText,
            take: EvidenceEventTake,
            cancellationToken: cancellationToken);

        var logs = await _debugEventService.GetRecentLogLinesAsync(
            sessionId,
            correlationId,
            take: EvidenceLogLineTake,
            cancellationToken: cancellationToken);

        if (events.Count == 0 && logs.Count == 0)
        {
            return "No matching debug events or log lines were found for the current session query.";
        }

        var sb = new StringBuilder();

        if (events.Count > 0)
        {
            sb.AppendLine("Debug events (recent):");
            foreach (var item in events.TakeLast(12))
            {
                sb.Append("- ");
                sb.Append(item.CreatedUtc.ToString("O"));
                sb.Append(" | ");
                sb.Append(item.EventKind);
                sb.Append(" | ");
                sb.Append(item.Severity);
                if (!string.IsNullOrWhiteSpace(item.Summary))
                {
                    sb.Append(" | ");
                    sb.Append(TruncateForPrompt(item.Summary, 220));
                }

                sb.AppendLine();
            }
        }

        if (logs.Count > 0)
        {
            sb.AppendLine("Recent log lines (filtered):");
            foreach (var line in logs.TakeLast(18))
            {
                sb.AppendLine($"- {TruncateForPrompt(line, 260)}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string? BuildEvidenceSearchText(string userPrompt)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return null;
        }

        var cleaned = userPrompt.Trim();
        if (cleaned.Length > 90)
        {
            cleaned = cleaned[..90];
        }

        return cleaned;
    }

    private static void AppendDetailedDiagnostics(StringBuilder sb, RolePlayV2DiagnosticsSnapshot diagnostics)
    {
        if (diagnostics.CandidateEvaluations.Count > 0)
        {
            sb.AppendLine("[Candidate Evaluations]");
            foreach (var candidate in diagnostics.CandidateEvaluations
                .OrderByDescending(x => x.EvaluatedUtc)
                .Take(6))
            {
                sb.AppendLine($"- Scenario={candidate.ScenarioId}, Tier={candidate.StageAWillingnessTier}, StageB={(candidate.StageBEligible ? "pass" : "fail")}, Fit={candidate.FitScore:F3}, Confidence={candidate.Confidence:F3}, Reason={TruncateForPrompt(candidate.Rationale, 180)}");
            }
        }

        if (diagnostics.TransitionEvents.Count > 0)
        {
            sb.AppendLine("[Recent Transitions]");
            foreach (var transition in diagnostics.TransitionEvents
                .OrderByDescending(x => x.OccurredUtc)
                .Take(6))
            {
                sb.AppendLine($"- {transition.FromPhase}->{transition.ToPhase}, Trigger={transition.TriggerType}, ReasonCode={transition.ReasonCode}, At={transition.OccurredUtc:O}");
            }
        }

        if (diagnostics.DecisionPoints.Count > 0)
        {
            sb.AppendLine("[Decision Points]");
            foreach (var decision in diagnostics.DecisionPoints
                .OrderByDescending(x => x.CreatedUtc)
                .Take(4))
            {
                sb.AppendLine($"- Phase={decision.Phase}, Trigger={decision.TriggerSource}, Target={decision.TargetActorId}, Summary={TruncateForPrompt(decision.ContextSummary, 180)}");
            }
        }

        if (diagnostics.CompatibilityErrors.Count > 0)
        {
            sb.AppendLine("[Compatibility Errors]");
            foreach (var error in diagnostics.CompatibilityErrors
                .OrderByDescending(x => x.EmittedUtc)
                .Take(4))
            {
                sb.AppendLine($"- Code={error.ErrorCode}, MissingStats={string.Join(",", error.MissingCanonicalStats)}, Guidance={TruncateForPrompt(error.RecoveryGuidance, 180)}");
            }
        }
    }

    private static string TruncateForPrompt(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength] + "...";
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
