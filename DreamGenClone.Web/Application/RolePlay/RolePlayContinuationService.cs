using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Logging;
using DreamGenClone.Web.Application.Models;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NarrativePhase = DreamGenClone.Domain.RolePlay.NarrativePhase;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayContinuationService : IRolePlayContinuationService
{
    private const int NarrativeValidationRetryLimit = 1;
    private const int NarrativeQuotedBlockRetryThreshold = 2;
    private const int NarrativeQuotedBlockHardViolationThreshold = 4;
    private const double NarrativeQuotedTextRatioRetryThreshold = 0.20;

    // Number of non-excluded interactions during which other characters are suppressed from
    // narrative focus, allowing the persona-partner relationship to be established first.
    // Other characters remain in the scene but unnamed and unaddressed until this threshold is passed.
    private const int OpeningPeripheralTurnCount = 6;

    private static readonly Regex QuotedBlockRegex = new("\"[^\"\\r\\n]{2,}\"", RegexOptions.Compiled);
    private static readonly Regex FirstPersonLeakRegex = new("\\b(I|me|my|mine|myself)\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CharacterInteriorityRegex = new("\\b[A-Z][a-zA-Z'-]{1,}\\s+(thought|felt|wondered|remembered|realized|decided|knew)\\b", RegexOptions.Compiled);
    private static readonly Regex DialogueAttributionRegex = new("\\b(said|asked|whispered|murmured|replied|snapped|called)\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolver;
    private readonly IModelSettingsService _modelSettingsService;
    private readonly IScenarioService _scenarioService;
    private readonly IPromptDealbreakerService _dealbreakerService;
    private readonly IThemePreferenceService _themePreferenceService;
    private readonly IIntensityProfileService _intensityProfileService;
    private readonly ISteeringProfileService _steeringProfileService;
    private readonly IScenarioGuidanceContextFactory _scenarioGuidanceContextFactory;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly IRPThemeService? _rpThemeService;
    private readonly IRolePlayDiagnosticsService? _diagnosticsService;
    private readonly IClimaxBeatRepository? _climaxBeatRepository;
    private readonly IHusbandAwarenessProfileService? _husbandAwarenessProfileService;
    private readonly bool _enableLocationServices;
    private readonly bool _includeCandidateMenuWhileObserving;
    private readonly IOptions<RolePlayMemoryOptions>? _memoryOptions;
    private readonly SceneDirectionCoordinator _coordinator;
    private readonly ILogger<RolePlayContinuationService> _logger;

    public RolePlayContinuationService(
        ICompletionClient completionClient,
        IModelResolutionService modelResolver,
        IModelSettingsService modelSettingsService,
        IScenarioService scenarioService,
        IPromptDealbreakerService dealbreakerService,
        IThemePreferenceService themePreferenceService,
        IIntensityProfileService toneProfileService,
        ISteeringProfileService styleProfileService,
        IScenarioGuidanceContextFactory scenarioGuidanceContextFactory,
        IRolePlayDebugEventSink debugEventSink,
        SceneDirectionCoordinator coordinator,
        ILogger<RolePlayContinuationService> logger,
        IRolePlayDiagnosticsService? diagnosticsService = null,
        IRPThemeService? rpThemeService = null,
        IClimaxBeatRepository? climaxBeatRepository = null,
        IHusbandAwarenessProfileService? husbandAwarenessProfileService = null,
        IOptions<RolePlayDecisionOptions>? rolePlayDecisionOptions = null,
        IOptions<RolePlayFeatureFlagsOptions>? rolePlayFeatureFlagsOptions = null,
        IOptions<RolePlayMemoryOptions>? memoryOptions = null)
    {
        _completionClient = completionClient;
        _modelResolver = modelResolver;
        _modelSettingsService = modelSettingsService;
        _scenarioService = scenarioService;
        _dealbreakerService = dealbreakerService;
        _themePreferenceService = themePreferenceService;
        _intensityProfileService = toneProfileService;
        _steeringProfileService = styleProfileService;
        _scenarioGuidanceContextFactory = scenarioGuidanceContextFactory;
        _debugEventSink = debugEventSink;
        _coordinator = coordinator;
        _rpThemeService = rpThemeService;
        _diagnosticsService = diagnosticsService;
        _climaxBeatRepository = climaxBeatRepository;
        _husbandAwarenessProfileService = husbandAwarenessProfileService;
        _enableLocationServices = rolePlayDecisionOptions?.Value.EnableLocationServices ?? true;
        _includeCandidateMenuWhileObserving = rolePlayFeatureFlagsOptions?.Value.IncludeCandidateMenuWhileObserving ?? true;
        _memoryOptions = memoryOptions;
        _logger = logger;
    }

    public async Task<RolePlayInteraction> ContinueAsync(
        RolePlaySession session,
        ContinueAsActor actor,
        string? customActorName,
        PromptIntent intent,
        string promptText,
        Func<string, Task>? onChunk = null,
        CancellationToken cancellationToken = default,
        int? turnIndex = null,
        int? positionInTurn = null,
        int? turnActorCount = null)
    {
        await ValidateDirectiveTextAsync(session, promptText, cancellationToken);

        var correlationId = Guid.NewGuid().ToString("N");
        var actorLabel = string.IsNullOrWhiteSpace(customActorName) ? actor.ToString() : customActorName;
        var diagnostics = _diagnosticsService is null
            ? null
            : await _diagnosticsService.GetSnapshotAsync(session.Id, correlationId, cancellationToken);

        var prompt = await BuildPromptAsync(session, actor, customActorName, intent, promptText, cancellationToken,
            turnIndex, positionInTurn, turnActorCount);
        
        // Capture prompt text for storage (best-effort, truncated to reduce size)
        string? capturedPromptText = null;
        try
        {
            capturedPromptText = PromptTextTruncation.TrimInteractionHistoryBlock(prompt);
            if (capturedPromptText != null)
            {
                _logger.LogInformation("Successfully captured prompt text for interaction in session {SessionId}, length: {Length}", 
                    session.Id, capturedPromptText.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture prompt text for interaction in session {SessionId}", session.Id);
        }
        
        if (diagnostics is not null)
        {
            prompt = $"[V2 Diagnostics: candidates={diagnostics.CandidateEvaluations.Count}, transitions={diagnostics.TransitionEvents.Count}, decisions={diagnostics.DecisionPoints.Count}]\n{prompt}";
            _logger.LogInformation(
                RolePlayV2LogEvents.DiagnosticsSnapshotPublished,
                diagnostics.SessionId,
                diagnostics.CorrelationId,
                diagnostics.CandidateEvaluations.Count,
                diagnostics.TransitionEvents.Count,
                diagnostics.DecisionPoints.Count,
                diagnostics.CompatibilityErrors.Count);
        }
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            CorrelationId = correlationId,
            EventKind = "PromptBuilt",
            Severity = "Info",
            ActorName = actorLabel,
            Summary = $"Prompt prepared for {actor} ({intent})",
            MetadataJson = JsonSerializer.Serialize(new
            {
                actor,
                customActorName,
                intent,
                prompt,
                promptLength = prompt.Length
            })
        }, cancellationToken);

        var sessionSettings = _modelSettingsService.GetSettings(session.Id);
        var resolved = await _modelResolver.ResolveAsync(
            AppFunction.RolePlayGeneration,
            sessionModelId: sessionSettings.SessionModelId,
            sessionTemperature: sessionSettings.SessionModelId != null ? sessionSettings.Temperature : null,
            sessionTopP: sessionSettings.SessionModelId != null ? sessionSettings.TopP : null,
            sessionMaxTokens: sessionSettings.SessionModelId != null ? sessionSettings.MaxTokens : null,
            cancellationToken: cancellationToken);
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            CorrelationId = correlationId,
            EventKind = "LlmRequestSent",
            Severity = "Info",
            ActorName = actorLabel,
            ModelIdentifier = resolved.ModelIdentifier,
            ProviderName = resolved.ProviderName,
            Summary = "Dispatching completion request",
            MetadataJson = JsonSerializer.Serialize(new
            {
                resolved.ModelIdentifier,
                resolved.ProviderName,
                resolved.ProviderBaseUrl,
                resolved.ChatCompletionsPath,
                resolved.Temperature,
                resolved.TopP,
                resolved.MaxTokens,
                resolved.ProviderTimeoutSeconds
            })
        }, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        string output;
        string? reasoningContent = null;
        try
        {
            if (onChunk is null)
            {
                var (content, reasoning) = await _completionClient.GenerateWithReasoningAsync(prompt, resolved, cancellationToken);
                output = content;
                reasoningContent = reasoning;
            }
            else
            {
                var (content, reasoning) = await _completionClient.StreamGenerateWithReasoningAsync(prompt, resolved, onChunk, cancellationToken);
                output = content;
                reasoningContent = reasoning;
            }
        }
        catch (Exception ex)
        {
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                CorrelationId = correlationId,
                EventKind = "ErrorRaised",
                Severity = "Error",
                ActorName = actorLabel,
                ModelIdentifier = resolved.ModelIdentifier,
                ProviderName = resolved.ProviderName,
                Summary = "Completion request failed",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    ex.Message,
                    ExceptionType = ex.GetType().Name
                })
            }, cancellationToken);

            throw;
        }

        stopwatch.Stop();
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            CorrelationId = correlationId,
            EventKind = "LlmResponseReceived",
            Severity = "Info",
            ActorName = actorLabel,
            ModelIdentifier = resolved.ModelIdentifier,
            ProviderName = resolved.ProviderName,
            DurationMs = (int)stopwatch.ElapsedMilliseconds,
            Summary = "Completion response received",
            MetadataJson = JsonSerializer.Serialize(new
            {
                output,
                outputLength = output.Length,
                reasoningContent,
                reasoningLength = reasoningContent?.Length ?? 0,
                durationMs = stopwatch.ElapsedMilliseconds
            })
        }, cancellationToken);

        var interaction = new RolePlayInteraction
        {
            InteractionType = actor switch
            {
                ContinueAsActor.You => InteractionType.User,
                ContinueAsActor.Npc => InteractionType.Npc,
                ContinueAsActor.Custom => InteractionType.Custom,
                _ => InteractionType.System
            },
            ActorName = !string.IsNullOrWhiteSpace(customActorName)
                ? customActorName.Trim()
                : actor switch
                {
                    ContinueAsActor.You => string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim(),
                    ContinueAsActor.Npc => "NPC",
                    _ => "Custom"
                },
            Content = string.IsNullOrWhiteSpace(output) ? "(No output generated)" : output.Trim(),
            GeneratedByModelId = resolved.ModelIdentifier,
            GeneratedByModelName = resolved.ModelIdentifier,
            GeneratedByCommand = "Continue",
            GeneratedByProvider = resolved.ProviderName,
            GeneratedTemperature = resolved.Temperature,
            GeneratedTopP = resolved.TopP,
            GeneratedMaxTokens = resolved.MaxTokens,
            ReasoningContent = reasoningContent,
            NarrativePhaseAtCreation = session.AdaptiveState.CurrentPhase,
            PromptText = capturedPromptText
        };

        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            CorrelationId = correlationId,
            InteractionId = interaction.Id,
            EventKind = "InteractionPrepared",
            Severity = "Info",
            ActorName = interaction.ActorName,
            ModelIdentifier = resolved.ModelIdentifier,
            ProviderName = resolved.ProviderName,
            Summary = "Role-play interaction prepared from model output",
            MetadataJson = JsonSerializer.Serialize(new
            {
                interaction.Id,
                interaction.ActorName,
                interaction.InteractionType,
                interaction.Content
            })
        }, cancellationToken);

        _logger.LogInformation("Role-play continuation prepared for actor {Actor} in session {SessionId}", interaction.ActorName, session.Id);
        return interaction;
    }

    public async Task<RolePlayInteraction> ContinueNarrativeAsync(
        RolePlaySession session,
        string actorName,
        string promptText,
        CancellationToken cancellationToken = default,
        int? turnIndex = null,
        int? turnActorCount = null)
    {
        var narrativePrompt = string.IsNullOrWhiteSpace(promptText)
            ? "Synthesize the scene with vivid narrative description."
            : promptText;

        await ValidateDirectiveTextAsync(session, narrativePrompt, cancellationToken);

        var prompt = await BuildPromptAsync(
            session,
            ContinueAsActor.Npc,
            actorName,
            PromptIntent.Narrative,
            narrativePrompt,
            cancellationToken,
            turnIndex,
            positionInTurn: null,
            turnActorCount);

        // Capture prompt text for storage (best-effort, truncated to reduce size)
        string? capturedPromptText = null;
        try
        {
            capturedPromptText = PromptTextTruncation.TrimInteractionHistoryBlock(prompt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture prompt text for narrative interaction in session {SessionId}", session.Id);
        }

        // Log the narrative prompt for debugging (same as character prompts).
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            EventKind = "PromptBuilt",
            Severity = "Info",
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "Narrative" : actorName,
            Summary = $"Prompt prepared for Narrative ({PromptIntent.Narrative})",
            MetadataJson = JsonSerializer.Serialize(new
            {
                actor = actorName,
                intent = PromptIntent.Narrative.ToString(),
                prompt,
                promptLength = prompt.Length
            })
        }, cancellationToken);

        var settings = _modelSettingsService.GetSettings(session.Id);
        var resolved = await _modelResolver.ResolveAsync(
            AppFunction.RolePlayGeneration,
            sessionModelId: settings.SessionModelId,
            sessionTemperature: settings.SessionModelId != null ? settings.Temperature : null,
            sessionTopP: settings.SessionModelId != null ? settings.TopP : null,
            sessionMaxTokens: settings.SessionModelId != null ? settings.MaxTokens : null,
            cancellationToken: cancellationToken);

        var output = await GenerateNarrativeWithValidationAsync(session, prompt, resolved, cancellationToken);

        return new RolePlayInteraction
        {
            InteractionType = InteractionType.System,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "Narrative" : actorName,
            Content = string.IsNullOrWhiteSpace(output) ? "(No output generated)" : output.Trim(),
            GeneratedByModelId = resolved.ModelIdentifier,
            GeneratedByModelName = resolved.ModelIdentifier,
            GeneratedByCommand = "Narrative",
            GeneratedByProvider = resolved.ProviderName,
            GeneratedTemperature = resolved.Temperature,
            GeneratedTopP = resolved.TopP,
            GeneratedMaxTokens = resolved.MaxTokens,
            NarrativePhaseAtCreation = session.AdaptiveState.CurrentPhase,
            PromptText = capturedPromptText
        };
    }

    public async Task<ContinueAsResult> ContinueBatchAsync(
        RolePlaySession session,
        IReadOnlyList<ContinueAsActor> actors,
        bool includeNarrative,
        string? customActorName,
        string promptText,
        CancellationToken cancellationToken = default)
    {
        var result = new ContinueAsResult { Success = true };
        foreach (var actor in ContinueAsOrdering.OrderDistinct(actors))
        {
            var interaction = await ContinueAsync(
                session,
                actor,
                customActorName,
                PromptIntent.Message,
                promptText,
                null,
                cancellationToken);
            result.ParticipantOutputs.Add(interaction);
        }

        if (includeNarrative)
        {
            var narrativePrompt = string.IsNullOrWhiteSpace(promptText)
                ? "Synthesize the scene with vivid narrative description."
                : promptText;

            await ValidateDirectiveTextAsync(session, narrativePrompt, cancellationToken);

            var prompt = await BuildPromptAsync(
                session,
                ContinueAsActor.Npc,
                null,
                PromptIntent.Narrative,
                narrativePrompt,
                cancellationToken);

            // Capture prompt text for storage (best-effort, truncated to reduce size)
            string? capturedPromptText = null;
            try
            {
                capturedPromptText = PromptTextTruncation.TrimInteractionHistoryBlock(prompt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to capture prompt text for batch narrative interaction in session {SessionId}", session.Id);
            }

            var narrativeSettings = _modelSettingsService.GetSettings(session.Id);
            var narrativeResolved = await _modelResolver.ResolveAsync(
                AppFunction.RolePlayGeneration,
                sessionModelId: narrativeSettings.SessionModelId,
                sessionTemperature: narrativeSettings.SessionModelId != null ? narrativeSettings.Temperature : null,
                sessionTopP: narrativeSettings.SessionModelId != null ? narrativeSettings.TopP : null,
                sessionMaxTokens: narrativeSettings.SessionModelId != null ? narrativeSettings.MaxTokens : null,
                cancellationToken: cancellationToken);
            var output = await GenerateNarrativeWithValidationAsync(session, prompt, narrativeResolved, cancellationToken);
            result.NarrativeOutput = new RolePlayInteraction
            {
                InteractionType = InteractionType.System,
                ActorName = "Narrative",
                Content = string.IsNullOrWhiteSpace(output) ? "(No output generated)" : output.Trim(),
                GeneratedByModelId = narrativeResolved.ModelIdentifier,
                GeneratedByModelName = narrativeResolved.ModelIdentifier,
                GeneratedByCommand = "Narrative",
                GeneratedByProvider = narrativeResolved.ProviderName,
                GeneratedTemperature = narrativeResolved.Temperature,
                GeneratedTopP = narrativeResolved.TopP,
                GeneratedMaxTokens = narrativeResolved.MaxTokens,
                NarrativePhaseAtCreation = session.AdaptiveState.CurrentPhase,
                PromptText = capturedPromptText
            };
        }

        return result;
    }

    private async Task ValidateDirectiveTextAsync(RolePlaySession session, string directiveText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.SelectedThemeProfileId)
            || string.IsNullOrWhiteSpace(directiveText))
        {
            return;
        }

        var validation = await _dealbreakerService.ValidateAsync(directiveText, session.SelectedThemeProfileId, cancellationToken);
        if (!validation.IsAllowed)
        {
            throw new InvalidOperationException(validation.Message ?? "Prompt violated a hard dealbreaker.");
        }
    }

    private async Task<string> BuildPromptAsync(
        RolePlaySession session,
        ContinueAsActor actor,
        string? customActorName,
        PromptIntent intent,
        string promptText,
        CancellationToken cancellationToken,
        int? turnIndex = null,
        int? positionInTurn = null,
        int? turnActorCount = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are continuing an interactive role-play scene.");
        sb.AppendLine($"Behavior mode: {session.BehaviorMode}");

        // ── Turn Context block ──
        if (turnIndex.HasValue && turnActorCount.HasValue)
        {
            sb.AppendLine();
            if (positionInTurn.HasValue)
            {
                sb.AppendLine($"Turn Context: turn {turnIndex.Value}, response {positionInTurn.Value} of {turnActorCount.Value}");
                sb.AppendLine($"- {turnActorCount.Value} character responses this turn, in sequence, then a narrative close.");

                if (positionInTurn.Value == 1)
                {
                    sb.AppendLine("- You are first this turn. Establish the scene beat — advance from where the previous turn left off.");
                    if (turnActorCount.Value > 1)
                        sb.AppendLine($"- The other {turnActorCount.Value - 1} character(s) will describe this same moment from their perspectives after you.");
                    sb.AppendLine("- Do not leave the beat unresolved — give it clear shape so others can react to it.");
                }
                else if (positionInTurn.Value == turnActorCount.Value)
                {
                    // Persona (last before narrative): neutral continuation — they may
                    // or may not be witnessing the active scene beat.
                    sb.AppendLine("- Continue from your character's perspective — what you observe, feel, or what occupies your attention in this moment.");
                    sb.AppendLine("- The narrative closes the turn after your response.");
                }
                else
                {
                    sb.AppendLine("- Describe the same scene beat established this turn, from your character's perspective.");
                    sb.AppendLine("- Give your sensations, reactions, dialogue, and internal experience of this exact moment.");
                    sb.AppendLine("- Do NOT advance to a new act, position, or story beat.");
                }
            }
            else
            {
                // Narrative
                sb.AppendLine($"Turn Context: turn {turnIndex.Value}, narrative close");
                sb.AppendLine($"- All {turnActorCount.Value} character responses for this turn are complete.");
                sb.AppendLine("- Write an omniscient account: setting, character positions, sensations, atmosphere.");
                sb.AppendLine("- Synthesize character perspectives into a rich, unified picture.");
                sb.AppendLine("- Do NOT advance the scene beyond what the characters established this turn.");
            }
        }

        // Include POV persona
        var hasAnyIntimateAttributes = false;
        if (!string.IsNullOrWhiteSpace(session.PersonaDescription))
        {
            sb.AppendLine($"POV Persona ({session.PersonaName}):");
            sb.AppendLine(session.PersonaDescription.Trim());
            var personaAppearance = PhysicalAttributesFormatter.FormatBlock(
                session.PersonaPhysicalAttributes, session.PersonaGender);
            if (!string.IsNullOrEmpty(personaAppearance))
            {
                sb.AppendLine(personaAppearance);
            }

            // Inject intimate behavioral self-awareness text for persona
            if (session.PersonaPhysicalAttributes is not null)
            {
                hasAnyIntimateAttributes = true;
                var awarenessLevel = ResolvePersonaAwarenessLevel(session);
                var selfAwareness = IntimateBehavioralTextBuilder.BuildSelfAwarenessText(
                    session.PersonaPhysicalAttributes, session.PersonaGender,
                    awarenessLevel, session.PersonaName);
                if (!string.IsNullOrEmpty(selfAwareness))
                    sb.AppendLine(selfAwareness);
            }
        }
        else if (session.PersonaName != "You")
        {
            sb.AppendLine($"POV Persona: {session.PersonaName}");
        }

        // Inject global behavioral rules when intimate attributes are configured
        if (hasAnyIntimateAttributes)
        {
            sb.AppendLine(IntimateBehavioralTextBuilder.BuildBehavioralRules());
        }

        // Inject scene location lock at the top — before scenario and interaction history —
        // so the model cannot teleport characters to a new location without a written transition.
        if (_enableLocationServices && !string.IsNullOrWhiteSpace(session.AdaptiveState.CurrentSceneLocation))
        {
            sb.AppendLine($"HARD CONSTRAINT — Scene Location: The current scene is at \"{NarrativeLocationLabel(session.AdaptiveState.CurrentSceneLocation)}\". Do not move any character to a different location without writing an explicit transition in the narration. Do not jump to a new place between responses.");
        }
        else
        {
            // Always enforce location continuity even when the location service is not active.
            // The physical setting must not change silently between turns; any movement must be
            // written as an explicit narrative transition within this response.
            sb.AppendLine("HARD CONSTRAINT — Location Continuity: The physical setting established in the previous response must be maintained in this response. Do not silently relocate any character to a different place. If a character moves, write the transition explicitly in the narration.");
        }

        string scenarioStyle = string.Empty;
        IntensityLevel? baseIntensityLevel = null;
        IntensityLevel? adaptiveIntensityLevel = null;
        string? selectedIntensityDescription = null;
        string? adaptiveIntensityDescription = null;
        string? scenarioSteeringProfileId = null;
        List<string> scenarioGoals = [];
        List<string> scenarioConflicts = [];
        List<string> scenarioNarrativeGuidelines = [];
        List<string> scenarioWorldRules = [];
        IReadOnlyList<ScenarioCharacter>? scenarioCharacters = null;

        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null)
            {
                scenarioCharacters = scenario.Characters
                    .Select(c => new ScenarioCharacter(c.Id, c.Name ?? string.Empty, c.Role))
                    .ToList();
                var personaRelation = RolePlayRelationFormatter.DescribePersonaRelation(session, scenario.Characters);
                var personaRole = CharacterRoleCatalog.Normalize(session.PersonaRole);
                if (!string.Equals(personaRole, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(personaRelation))
                {
                    if (!string.Equals(personaRole, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"- Persona Role: {personaRole}");
                    }

                    if (!string.IsNullOrWhiteSpace(personaRelation))
                    {
                        sb.AppendLine($"- Persona Relation: {personaRelation}");
                    }
                }

                sb.AppendLine("Scenario:");
                sb.AppendLine($"- Name: {scenario.Name}");
                sb.AppendLine($"- Description: {scenario.Description}");
                sb.AppendLine($"- Plot: {scenario.Plot.Description}");
                sb.AppendLine($"- Setting: {scenario.Setting.WorldDescription}");
                if (!string.IsNullOrWhiteSpace(scenario.Setting.TimeFrame))
                {
                    sb.AppendLine($"- Time Frame: {scenario.Setting.TimeFrame.Trim()}");
                    sb.AppendLine("- Time Span Reminder: This entire story takes place within the time frame above. Scenes may skip forward in time; a new response does not have to be the immediate continuation of the last moment.");
                }
                scenarioStyle = string.Join(" / ", new[]
                {
                    scenario.Narrative.ProseStyle,
                    scenario.Narrative.NarrativeTone
                }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
                scenarioSteeringProfileId = scenario.DefaultSteeringProfileId;
                if (!string.IsNullOrWhiteSpace(scenarioStyle))
                {
                    sb.AppendLine($"- Narrative: {scenarioStyle}");
                }

                if (!string.IsNullOrWhiteSpace(scenario.Narrative.PointOfView))
                {
                    sb.AppendLine($"- Preferred POV: {scenario.Narrative.PointOfView}");
                }

                if (scenario.Plot.Goals.Count > 0)
                {
                    scenarioGoals = scenario.Plot.Goals
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .ToList();

                    sb.AppendLine("- Plot Goals:");
                    foreach (var goal in scenarioGoals)
                    {
                        sb.AppendLine($"  - {goal}");
                    }
                }

                if (scenario.Plot.Conflicts.Count > 0)
                {
                    scenarioConflicts = scenario.Plot.Conflicts
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .ToList();

                    sb.AppendLine("- Plot Conflicts:");
                    foreach (var conflict in scenarioConflicts)
                    {
                        sb.AppendLine($"  - {conflict}");
                    }
                }

                if (scenario.Setting.WorldRules.Count > 0)
                {
                    scenarioWorldRules = scenario.Setting.WorldRules
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .ToList();
                    sb.AppendLine("- World Rules:");
                    foreach (var rule in scenarioWorldRules)
                    {
                        sb.AppendLine($"  - {rule}");
                    }
                }

                if (scenario.Setting.EnvironmentalDetails.Count > 0)
                {
                    sb.AppendLine("- Environmental Details:");
                    foreach (var detail in scenario.Setting.EnvironmentalDetails.Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        sb.AppendLine($"  - {detail.Trim()}");
                    }
                }

                if (scenario.Narrative.NarrativeGuidelines.Count > 0)
                {
                    scenarioNarrativeGuidelines = scenario.Narrative.NarrativeGuidelines
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .ToList();

                    sb.AppendLine("- Narrative Guidelines:");
                    foreach (var guideline in scenarioNarrativeGuidelines)
                    {
                        sb.AppendLine($"  - {guideline}");
                    }
                }

                sb.AppendLine("Follow scenario goals, rules, and narrative guidelines unless they conflict with hard safety constraints.");
                if (!string.IsNullOrWhiteSpace(session.SelectedIntensityProfileId) || !string.IsNullOrWhiteSpace(scenario.DefaultIntensityProfileId))
                {
                    var intensityProfileId = session.SelectedIntensityProfileId ?? scenario.DefaultIntensityProfileId;
                    sb.AppendLine($"- Intensity Profile: {intensityProfileId}");
                    if (!string.IsNullOrWhiteSpace(intensityProfileId))
                    {
                        var toneProfile = await _intensityProfileService.GetAsync(intensityProfileId, cancellationToken);
                        if (toneProfile is not null)
                        {
                            baseIntensityLevel = toneProfile.Intensity;
                            selectedIntensityDescription = string.IsNullOrWhiteSpace(toneProfile.Description)
                                ? null
                                : toneProfile.Description.Trim();
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(session.AdaptiveIntensityProfileId))
                {
                    var adaptiveProfile = await _intensityProfileService.GetAsync(session.AdaptiveIntensityProfileId, cancellationToken);
                    if (adaptiveProfile is not null)
                    {
                        adaptiveIntensityLevel = adaptiveProfile.Intensity;
                        adaptiveIntensityDescription = string.IsNullOrWhiteSpace(adaptiveProfile.Description)
                            ? null
                            : adaptiveProfile.Description.Trim();
                    }
                }

                if (!string.IsNullOrWhiteSpace(session.IntensityFloorOverride) || !string.IsNullOrWhiteSpace(session.IntensityCeilingOverride))
                {
                    sb.AppendLine($"- Intensity Bounds: floor={session.IntensityFloorOverride ?? "(none)"}, ceiling={session.IntensityCeilingOverride ?? "(none)"}");
                }

                // Include all character details so the AI can portray them accurately.
                if (scenario.Characters.Count > 0)
                {
                    sb.AppendLine("Characters in this scene:");
                    foreach (var character in scenario.Characters)
                    {
                        if (!string.IsNullOrWhiteSpace(character.Name))
                        {
                            var roleText = string.IsNullOrWhiteSpace(character.Role)
                                ? string.Empty
                                : $" [Role: {character.Role.Trim()}]";
                            var relationText = RolePlayRelationFormatter.DescribeCharacterRelation(character, session, scenario.Characters);
                            var relationSuffix = string.IsNullOrWhiteSpace(relationText)
                                ? string.Empty
                                : $" [Relation: {relationText}]";
                            sb.AppendLine($"  {character.Name}{roleText}{relationSuffix}: {character.Description?.Trim() ?? "(no description)"}");
                            var charAppearance = PhysicalAttributesFormatter.FormatBlock(
                                character.PhysicalAttributes, character.Gender);
                            if (!string.IsNullOrEmpty(charAppearance))
                            {
                                sb.AppendLine($"    {charAppearance}");
                            }

                            // Inject intimate behavioral texts for this character
                            InjectCharacterBehavioralTexts(sb, character, scenario.Characters, session);
                            if (character.PhysicalAttributes is not null)
                                hasAnyIntimateAttributes = true;
                        }
                    }
                }

                if (scenario.Locations.Count > 0)
                {
                    sb.AppendLine("Locations:");
                    foreach (var location in scenario.Locations
                        .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                        .Take(8))
                    {
                        var description = string.IsNullOrWhiteSpace(location.Description)
                            ? "(no description)"
                            : location.Description.Trim();
                        sb.AppendLine($"  {location.Name.Trim()}: {description}");
                    }
                }

                if (scenario.Objects.Count > 0)
                {
                    sb.AppendLine("Objects/Items:");
                    foreach (var item in scenario.Objects
                        .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                        .Take(8))
                    {
                        var description = string.IsNullOrWhiteSpace(item.Description)
                            ? "(no description)"
                            : item.Description.Trim();
                        sb.AppendLine($"  {item.Name.Trim()}: {description}");
                    }
                }
            }
        }

        var selectedStyleProfileId = session.SelectedSteeringProfileId ?? scenarioSteeringProfileId;
        if (!string.IsNullOrWhiteSpace(selectedStyleProfileId))
        {
            sb.AppendLine($"Writing Style Profile: {selectedStyleProfileId}");
            var styleProfile = await _steeringProfileService.GetAsync(selectedStyleProfileId, cancellationToken);
            if (styleProfile is not null)
            {
                if (!string.IsNullOrWhiteSpace(styleProfile.Description))
                {
                    sb.AppendLine($"- Writing Style Description: {styleProfile.Description}");
                }

                if (!string.IsNullOrWhiteSpace(styleProfile.Example))
                {
                    sb.AppendLine($"- Writing Style Example: {styleProfile.Example}");
                }

                if (!string.IsNullOrWhiteSpace(styleProfile.RuleOfThumb))
                {
                    sb.AppendLine($"- Writing Style Rule of Thumb: {styleProfile.RuleOfThumb}");
                }
            }
        }

        sb.AppendLine("Recent interaction history — exact scene continuity. Session Memory below = summarized past events for long-term context:");
        var contextView = session.GetContextView();
        var windowSize = Math.Max(12, session.ContextWindowSize);
        foreach (var interaction in contextView.TakeLast(windowSize))
        {
            sb.AppendLine($"[{interaction.InteractionType}] {interaction.ActorName}: {interaction.Content}");
        }

        if (_memoryOptions is not null && session.AdaptiveState.EncounterSummaries.Count > 0)
        {
            var effectiveMilestones = session.MaxMilestonesToInject ?? _memoryOptions.Value.MaxMilestonesToInject;
            var effectiveArcCompletions = session.MaxArcCompletionsToInject ?? _memoryOptions.Value.MaxArcCompletionsToInject;
            var effectiveEncounterCompletions = session.MaxEncounterCompletionsToInject ?? _memoryOptions.Value.MaxEncounterCompletionsToInject;
            InjectSessionMemoryBlock(sb, session.AdaptiveState.EncounterSummaries, effectiveMilestones, effectiveArcCompletions, effectiveEncounterCompletions, session.AdaptiveState.CycleIndex);
        }

        if (_enableLocationServices
            && (!string.IsNullOrWhiteSpace(session.AdaptiveState.CurrentSceneLocation)
            || session.AdaptiveState.CharacterLocations.Count > 0)
           )
        {
            sb.AppendLine("Scene Continuity Anchor:");
            sb.AppendLine($"- Current Scene Location: {NarrativeLocationLabel(session.AdaptiveState.CurrentSceneLocation) ?? "(unknown)"}");

            if (session.AdaptiveState.CharacterLocations.Count > 0)
            {
                sb.AppendLine("- Character Locations (truth state):");
                foreach (var truth in session.AdaptiveState.CharacterLocations
                    .Where(x => !string.IsNullOrWhiteSpace(x.CharacterId))
                    .OrderBy(x => x.CharacterId, StringComparer.OrdinalIgnoreCase)
                    .Take(8))
                {
                    var label = ResolvePromptActorLabel(session, truth.CharacterId);
                    var location = string.IsNullOrWhiteSpace(truth.TrueLocation) ? "(unknown)" : truth.TrueLocation;
                    var hidden = truth.IsHidden ? " [hidden]" : string.Empty;
                    sb.AppendLine($"  - {label}: {location}{hidden}");
                }
            }

            if (session.AdaptiveState.CharacterLocationPerceptions.Count > 0)
            {
                sb.AppendLine("- Key Location Perceptions:");
                foreach (var perception in session.AdaptiveState.CharacterLocationPerceptions
                    .Where(x => !string.IsNullOrWhiteSpace(x.ObserverCharacterId) && !string.IsNullOrWhiteSpace(x.TargetCharacterId))
                    .OrderByDescending(x => x.Confidence)
                    .Take(6))
                {
                    var observer = ResolvePromptActorLabel(session, perception.ObserverCharacterId);
                    var target = ResolvePromptActorLabel(session, perception.TargetCharacterId);
                    var where = string.IsNullOrWhiteSpace(perception.PerceivedLocation) ? "(unknown)" : perception.PerceivedLocation;
                    sb.AppendLine($"  - {observer} perceives {target} at {where} (confidence={perception.Confidence}, LOS={(perception.HasLineOfSight ? "Y" : "N")}, Near={(perception.IsInProximity ? "Y" : "N")})");
                }
            }

            sb.AppendLine("- Keep continuity with this location state. Do not teleport characters or jump to a new place without an explicit transition in the narration.");
        }

        if (session.AdaptiveState.CharacterStats.Count > 0)
        {
            sb.AppendLine("Adaptive Character Stats:");
            foreach (var kvp in session.AdaptiveState.CharacterStats.OrderBy(x => x.Key).Take(8))
            {
                var summary = string.Join(", ", CharacterStatProfileV2Accessor.GetAllStats(kvp.Value).OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}"));
                sb.AppendLine($"- {kvp.Key}: {summary}");
            }
        }

        if (session.AdaptiveState.ThemeScores.Count > 0)
        {
            sb.AppendLine("Active Theme Tracker:");
            sb.AppendLine($"- Selection Rule: {session.AdaptiveState.ThemeSelectionRule}");

            var selectedThemes = new List<ThemeScoreState>();
            if (!string.IsNullOrWhiteSpace(session.AdaptiveState.PrimaryThemeId)
                && session.AdaptiveState.ThemeScores.TryGetValue(session.AdaptiveState.PrimaryThemeId, out var primaryTheme))
            {
                selectedThemes.Add(primaryTheme);
            }

            if (!string.IsNullOrWhiteSpace(session.AdaptiveState.SecondaryThemeId)
                && session.AdaptiveState.ThemeScores.TryGetValue(session.AdaptiveState.SecondaryThemeId, out var secondaryTheme)
                && !string.Equals(secondaryTheme.ThemeId, session.AdaptiveState.PrimaryThemeId, StringComparison.OrdinalIgnoreCase))
            {
                selectedThemes.Add(secondaryTheme);
            }

            foreach (var item in selectedThemes)
            {
                sb.AppendLine($"- {item.ThemeName}: intensity={item.Intensity}, score={item.Score:F1}");
            }

            var latestEvidence = session.AdaptiveState.RecentEvidence.TakeLast(3).ToList();
            if (latestEvidence.Count > 0)
            {
                sb.AppendLine("Recent Theme Evidence:");
                foreach (var evidence in latestEvidence)
                {
                    sb.AppendLine($"- theme={evidence.ThemeId}, delta={evidence.Delta:F1}, confidence={evidence.Confidence:F2}, why={evidence.Rationale}");
                }
            }
        }

        var currentPhase = session.AdaptiveState.CurrentPhase.ToString();
        var activeScenarioId = session.AdaptiveState.ActiveScenarioId;
        var suppressedScenarioIds = session.AdaptiveState.ThemeScores.Values
            .Where(x => !string.Equals(x.ThemeId, activeScenarioId, StringComparison.OrdinalIgnoreCase)
                && (x.SuppressedHitCount > 0 || x.IsScenarioCandidate))
            .Select(x => x.ThemeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        // ── Theme guidance MUST appear BEFORE behavioral frames ─────────────────
        // Theme contract is the highest steering rank directive. Character behavioral
        // frames and stat state texts describe tendencies, not requirements — the theme
        // contract overrides them when they conflict.
        RPTheme? activeTheme = null;
        var activeThemeHardConstraints = Array.Empty<string>();

        if (_rpThemeService is not null
            && !string.IsNullOrWhiteSpace(activeScenarioId))
        {
            try
            {
                activeTheme = await _rpThemeService.GetThemeAsync(activeScenarioId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unable to load RP theme AI guidance notes for active scenario/theme {ThemeId} in session {SessionId}.", activeScenarioId, session.Id);
            }

            AppendActiveThemeContract(sb, activeTheme, currentPhase);
        }

        // Only scenario-bound characters (those with a CharacterRole) contribute to stat averages.
        var trackedStatsForGuidance = session.AdaptiveState.CharacterStats.Values
            .Where(x => !string.IsNullOrEmpty(x.CharacterRole))
            .ToList();
        var guidanceContext = await _scenarioGuidanceContextFactory.CreateAsync(
            new ScenarioGuidanceInput(
                SessionId: session.Id,
                CurrentPhase: currentPhase,
                ActiveScenarioId: activeScenarioId,
                VariantId: session.AdaptiveState.ActiveVariantId,
                AverageDesire: trackedStatsForGuidance.Count == 0
                    ? 50
                    : trackedStatsForGuidance.Average(x => CharacterStatProfileV2Accessor.GetStatOrDefault(x, "Desire", 50)),
                AverageRestraint: trackedStatsForGuidance.Count == 0
                    ? 50
                    : trackedStatsForGuidance.Average(x => CharacterStatProfileV2Accessor.GetStatOrDefault(x, "Restraint", 50)),
                AverageDominance: trackedStatsForGuidance.Count == 0
                    ? 50
                    : trackedStatsForGuidance.Average(x => CharacterStatProfileV2Accessor.GetStatOrDefault(x, "Dominance", 50)),
                AverageLoyalty: trackedStatsForGuidance.Count == 0
                    ? 50
                    : trackedStatsForGuidance.Average(x => CharacterStatProfileV2Accessor.GetStatOrDefault(x, "Loyalty", 50)),
                SelectedWillingnessProfileId: session.AdaptiveState.SelectedWillingnessProfileId,
                CharacterEncounterProfileIds: session.AdaptiveState.CharacterEncounterProfileIds,
                Characters: BuildCharactersWithPersona(scenarioCharacters, session),
                SuppressedScenarioIds: suppressedScenarioIds,
                CharacterRuntimeStats: session.AdaptiveState.CharacterStats.Count > 0
                    ? session.AdaptiveState.CharacterStats
                    : null,
                SelectedResistanceProfileId: session.AdaptiveState.SelectedResistanceProfileId),
            cancellationToken);

        RolePlayAssistantPrompts.AppendScenarioGuidance(sb, guidanceContext, []);

        if (_rpThemeService is not null
            && !string.IsNullOrWhiteSpace(activeScenarioId)
            && activeTheme is not null)
        {
            var maxThemeHardConstraints = Math.Clamp(session.MaxThemeAIGuidanceNotes, 1, 10);
            activeThemeHardConstraints = [.. RolePlayAssistantPrompts.GetThemeHardConstraintLines(activeTheme, maxThemeHardConstraints)];
            RolePlayAssistantPrompts.AppendThemeHardConstraints(sb, activeTheme, maxThemeHardConstraints);

            if (session.UseThemeAIGuidanceNotesInPrompt)
            {
                RolePlayAssistantPrompts.AppendThemeAIGuidance(
                    sb,
                    activeTheme,
                    currentPhase,
                    session.ThemeAIGuidanceInfluencePercent,
                    session.MaxThemeAIGuidanceNotes);

                RPTheme? secondaryTheme = null;
                if (_rpThemeService is not null
                    && session.ThemeAIGuidanceInfluencePercent > 0
                    && string.Equals(session.AdaptiveState.ThemeSelectionRule, "Top2Blend", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(session.AdaptiveState.SecondaryThemeId)
                    && !string.Equals(session.AdaptiveState.SecondaryThemeId, activeScenarioId, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        secondaryTheme = await _rpThemeService.GetThemeAsync(session.AdaptiveState.SecondaryThemeId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Unable to load secondary RP theme AI guidance notes for theme {ThemeId} in session {SessionId}.", session.AdaptiveState.SecondaryThemeId, session.Id);
                    }
                }

                if (secondaryTheme is not null)
                {
                    var secondaryInfluencePercent = Math.Max(1, session.ThemeAIGuidanceInfluencePercent / 2);
                    var secondaryMaxNotes = Math.Max(1, session.MaxThemeAIGuidanceNotes / 2);
                    RolePlayAssistantPrompts.AppendThemeAIGuidance(
                        sb,
                        secondaryTheme,
                        currentPhase,
                        secondaryInfluencePercent,
                        secondaryMaxNotes);
                }
            }
        }
        else if (_includeCandidateMenuWhileObserving
            && _rpThemeService is not null
            && string.IsNullOrWhiteSpace(activeScenarioId))
        {
            await AppendObservingCandidateMenuAsync(sb, session, currentPhase, cancellationToken);
        }

        // Steer guidance should still be phase/theme grounded whenever we have an active scenario
        // that maps to an RP theme, even if the RP theme subsystem flag is disabled.
        if (activeTheme is null
            && _rpThemeService is not null
            && !string.IsNullOrWhiteSpace(activeScenarioId))
        {
            try
            {
                activeTheme = await _rpThemeService.GetThemeAsync(activeScenarioId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unable to load RP theme for steer grounding {ThemeId} in session {SessionId}.", activeScenarioId, session.Id);
            }
        }

        var steerDirective = ResolveSteerDirective(session, promptText, intent);
        if (!string.IsNullOrWhiteSpace(steerDirective))
        {
            AppendSteerGuidance(
                sb,
                session,
                currentPhase,
                activeTheme,
                steerDirective,
                session.UseThemeAIGuidanceNotesInPrompt,
                session.MaxThemeAIGuidanceNotes,
                _enableLocationServices);
        }

        var timeSkipLabel = ResolveTimeSkipDirective(session, promptText, intent);
        if (!string.IsNullOrWhiteSpace(timeSkipLabel))
        {
            AppendTimeSkipGuidance(sb, timeSkipLabel, currentPhase);
        }

        _logger.LogInformation(
            "Guidance context generated for session {SessionId}: phase={Phase}, activeScenarioId={ActiveScenarioId}, excludedCount={ExcludedCount}",
            session.Id,
            currentPhase,
            activeScenarioId,
            guidanceContext.ExcludedScenarioIds.Count);

        var actorName = !string.IsNullOrWhiteSpace(customActorName)
            ? customActorName.Trim()
            : actor switch
            {
                ContinueAsActor.You => string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName,
                ContinueAsActor.Npc => "NPC",
                _ => "Custom"
            };

        sb.AppendLine($"Continue as: {actorName}");

        AppendScenarioPriorities(sb, scenarioGoals, scenarioConflicts, scenarioNarrativeGuidelines);

        if (!string.IsNullOrWhiteSpace(session.SelectedThemeProfileId))
        {
            sb.AppendLine($"Hard safety constraints for this session derive from theme profile '{session.SelectedThemeProfileId}'.");
            var profileThemes = await _themePreferenceService.ListByProfileAsync(session.SelectedThemeProfileId, cancellationToken);

            if (profileThemes.Count > 0)
            {
                sb.AppendLine("Active ranking profile themes (apply all):");
                foreach (var theme in profileThemes
                    .OrderBy(x => x.Tier)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var description = string.IsNullOrWhiteSpace(theme.Description)
                        ? "(no description)"
                        : theme.Description.Trim();
                    sb.AppendLine($"- [{theme.Tier}] {theme.Name}: {description}");
                }
            }

            var mustHave = profileThemes
                .Where(x => x.Tier == ThemeTier.MustHave)
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var stronglyPrefer = profileThemes
                .Where(x => x.Tier == ThemeTier.StronglyPrefer)
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var niceToHave = profileThemes
                .Where(x => x.Tier == ThemeTier.NiceToHave)
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var dislikes = profileThemes
                .Where(x => x.Tier == ThemeTier.Dislike)
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var neutral = profileThemes
                .Where(x => x.Tier == ThemeTier.Neutral)
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (mustHave.Count > 0)
            {
                sb.AppendLine($"Must-have themes to actively include when possible: {string.Join(", ", mustHave)}.");
            }

            if (stronglyPrefer.Count > 0)
            {
                sb.AppendLine($"Strongly-preferred themes to bias toward: {string.Join(", ", stronglyPrefer)}.");
            }

            if (niceToHave.Count > 0)
            {
                sb.AppendLine($"Nice-to-have themes to optionally weave in: {string.Join(", ", niceToHave)}.");
            }

            if (dislikes.Count > 0)
            {
                sb.AppendLine($"Disliked themes to minimize or avoid unless absolutely required by continuity: {string.Join(", ", dislikes)}.");
            }

            if (neutral.Count > 0)
            {
                sb.AppendLine($"Neutral themes (no explicit preference): {string.Join(", ", neutral)}.");
            }

            if (mustHave.Count > 0 || stronglyPrefer.Count > 0)
            {
                sb.AppendLine("When multiple directions are possible, prefer outputs that satisfy must-have and strongly-preferred themes.");
            }

            var hardDealbreakers = profileThemes
                .Where(x => x.Tier == ThemeTier.HardDealBreaker)
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (hardDealbreakers.Count > 0)
            {
                sb.AppendLine($"Hard dealbreakers: {string.Join(", ", hardDealbreakers)}.");
                sb.AppendLine("Do not generate, imply, or pivot into any hard dealbreaker themes.");
            }
            else
            {
                sb.AppendLine("Do not generate content that violates hard dealbreaker themes for the active profile.");
            }
        }

        // SceneDirectionCoordinator: marker-driven behavioral directives
        var sceneDirection = SceneDirectionResolver.Resolve(currentPhase, activeTheme, ClimaxSubPhase.None, intent);
        var ctx = new PromptInjectionContext { Session = session, SceneDirection = sceneDirection, Phase = currentPhase, Intent = intent, PositionInTurn = positionInTurn, TurnActorCount = turnActorCount, ActorName = actorName, ActiveTheme = activeTheme, ActorStats = ResolvePromptActorStats(session, actorName), PhaseGuidanceLines = RolePlayAssistantPrompts.GetThemePhaseGuidanceLines(activeTheme, currentPhase), PhaseDirectiveLines = RolePlayAssistantPrompts.GetThemePhaseDirectiveLines(activeTheme, currentPhase), AiGuidanceNotes = activeTheme?.AIGenerationNotes ?? [], ThemeHardConstraintLines = activeThemeHardConstraints, IsActorInScene = RolePlayScenePresenceHelper.IsActorInScene(session, actorName) };
        sb.Append(_coordinator.BuildPrompt(ctx));

        // Three distinct interaction types with different POV rules:
        // Narrative = 3rd person omniscient storyteller setting scenes
        // Persona   = 1st person POV ("I felt...", "I watched...")
        // NPC       = 3rd person external (dialogue + observable behavior only)
        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName;
        var (effectiveStyleLabel, effectiveStyleReason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, baseIntensityLevel, adaptiveIntensityLevel);

        var resolvedIntensityDescription = string.Empty;
        IntensityProfile? resolvedProfile = null;
        var resolvedScale = RolePlayStyleResolver.ParseBoundScale(effectiveStyleLabel);
        if (resolvedScale.HasValue)
        {
            var intensityProfiles = await _intensityProfileService.ListAsync(cancellationToken);
            resolvedProfile = intensityProfiles.FirstOrDefault(x => (int)x.Intensity == resolvedScale.Value && !string.IsNullOrWhiteSpace(x.Description))
                ?? intensityProfiles.FirstOrDefault(x => (int)x.Intensity == resolvedScale.Value);

            resolvedIntensityDescription = !string.IsNullOrWhiteSpace(resolvedProfile?.Description)
                ? resolvedProfile.Description.Trim()
                : IntensityLadder.GetDefaultDescription((IntensityLevel)resolvedScale.Value);
        }

        sb.AppendLine($"Resolved Intensity: {effectiveStyleLabel}");
        sb.AppendLine($"Resolution Reason: {effectiveStyleReason}");
        if (!string.IsNullOrWhiteSpace(resolvedIntensityDescription))
        {
            sb.AppendLine($"Resolved Intensity Description: {resolvedIntensityDescription}");
        }
        sb.AppendLine("Intensity Writing Contract:");
        sb.AppendLine("- Treat the resolved intensity description above as a required style contract for this turn.");
        sb.AppendLine("- This contract governs WRITING STYLE and EXPLICITNESS LEVEL only — it does not override active Phase Guidance.");
        sb.AppendLine("- Phase Guidance specifies WHAT scene actions and beats must occur; the intensity contract specifies HOW they are written.");
        sb.AppendLine("- Do not de-escalate below the resolved intensity level unless safety constraints require it.");
        sb.AppendLine($"Manual Intensity Pin: {(session.IsIntensityManuallyPinned ? "ON (resolved follows selected)" : "OFF (adaptive mode)")}");
        await AppendPositionListAsync(sb, session, currentPhase, intent, cancellationToken);
var styleHint = string.IsNullOrWhiteSpace(scenarioStyle)
            ? effectiveStyleLabel
            : $"{scenarioStyle} | effective mode: {effectiveStyleLabel}";

        // Beat cursor context: inject current sub-beat framing when in Climax phase.
        // Only active when the theme has [BeatStyle:episodic] in its Climax guidance — the tag
        // signals that this theme uses brief episodic disappearances, making the staged beat
        // catalog meaningful and correctly paced. Themes without the tag do not use the beat sheet.
        if (string.Equals(currentPhase, "Climax", StringComparison.OrdinalIgnoreCase)
            && intent != PromptIntent.Instruction
            && intent != PromptIntent.Narrative
            && !string.IsNullOrWhiteSpace(session.AdaptiveState.CurrentBeatCode)
            && _climaxBeatRepository is not null
            && RolePlayAssistantPrompts.IsEpisodicBeatStyle(activeTheme, currentPhase))
        {
            var beatEntry = await _climaxBeatRepository.GetByCodeAsync(session.AdaptiveState.CurrentBeatCode, cancellationToken);
            if (beatEntry is null)
            {
                _logger.LogWarning(
                    "ClimaxBeatCursor: BeatCode {BeatCode} not found in repository — skipping beat context injection",
                    session.AdaptiveState.CurrentBeatCode);
            }
            else
            {
                sb.AppendLine("Beat Stage Context:");
                sb.AppendLine($"Stage {beatEntry.StageNumber} — {beatEntry.StageName} / {beatEntry.BeatCode} — {beatEntry.SubBeatName}");
                foreach (var hint in beatEntry.Hints)
                    sb.AppendLine($"- {hint}");
                if (beatEntry.NextBeatCode is not null)
                {
                    var nextEntry = await _climaxBeatRepository.GetByCodeAsync(beatEntry.NextBeatCode, cancellationToken);
                    var nextLabel = nextEntry is not null ? $"{beatEntry.NextBeatCode} — {nextEntry.SubBeatName}" : beatEntry.NextBeatCode;
                    sb.AppendLine($"Next: {nextLabel}");
                }
                var isEpisodic = RolePlayAssistantPrompts.IsEpisodicBeatStyle(activeTheme, currentPhase);
                sb.AppendLine(isEpisodic
                    ? "This is a brief, urgent encounter — explicit and heated, not slow or romantic. Let the scene flow naturally through this beat and advance across the next 2-3 beats as urgency drives escalation. Be explicit: name body parts, describe movements and sensations directly. Each encounter must be MORE physically advanced than the previous one — escalating across disappearances toward full intercourse. Override: the 'advance only one stage per response' and 'multiple turns per stage' rules do NOT apply here — a rushed episode covers multiple stages. Close at a natural stopping point (they return to the social space). The next encounter resumes from the next beat."
                    : "Do not skip ahead — write this beat, then advance one beat when complete. Each beat should have a natural arc: build-up, peak, resolution. When the physical act has been sufficiently explored for this stage, resolve it and move on. Do not stretch a single beat past its natural length. The encounter should feel like a series of distinct, satisfying moments.");
            }
        }

        // Re-inject the most recent plain instruction (non-/steer) from history so it stays
        // authoritative regardless of how far back it sits in the rolling context window.
        if (intent != PromptIntent.Instruction)
        {
            var instrContextView = session.GetContextView();
            var instrWindowSize = Math.Max(12, session.ContextWindowSize);
            var recentInstruction = instrContextView
                .TakeLast(instrWindowSize)
                .LastOrDefault(x => string.Equals(x.ActorName, "Instruction", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(x.Content)
                    && !x.Content.TrimStart().StartsWith("/steer", StringComparison.OrdinalIgnoreCase)
                    && !x.Content.TrimStart().StartsWith("/nextphase", StringComparison.OrdinalIgnoreCase)
                    && !x.Content.TrimStart().StartsWith("/timeskip", StringComparison.OrdinalIgnoreCase)
                    && !x.Content.TrimStart().StartsWith("/endclimax", StringComparison.OrdinalIgnoreCase)
                    && !x.Content.TrimStart().StartsWith("/completeclimax", StringComparison.OrdinalIgnoreCase));
            if (recentInstruction is not null)
            {
                sb.AppendLine("Active Instruction (persistent — follow for this turn and continue until explicitly overridden):");
                sb.AppendLine(recentInstruction.Content.Trim());
            }
        }

        // Place the per-turn prompt text immediately before the final writing instruction
        // so it carries maximum authority (LLMs weight instructions near the end of long prompts).
        if (!string.IsNullOrWhiteSpace(promptText))
        {
            var intentLabel = intent switch
            {
                PromptIntent.Message => "Message",
                PromptIntent.Narrative => "Narrative Direction",
                PromptIntent.Instruction => "Instruction",
                _ => "Prompt"
            };

            sb.AppendLine($"{intentLabel}:");
            sb.AppendLine(promptText.Trim());
        }

        foreach (var (label, frameText) in guidanceContext.CharacterBehavioralFrames)
        {
            sb.AppendLine($"CHARACTER TENDENCY — enforce in this response: {label} behavioral frame (yields to theme contract): {frameText}");
            if (guidanceContext.CharacterStatStateTexts.TryGetValue(label, out var statStateText))
            {
                sb.AppendLine($"CHARACTER TENDENCY — enforce in this response: {label} current state (yields to theme contract): {statStateText}");
            }
        }

        foreach (var themeConstraint in activeThemeHardConstraints)
        {
            sb.AppendLine($"HARD CONSTRAINT — enforce in this response: {themeConstraint}");
        }

        // Re-inject scenario world rules immediately before the writing directive so they carry
        // the same end-of-prompt authority as behavioral frames and theme constraints.
        foreach (var rule in scenarioWorldRules)
        {
            sb.AppendLine($"HARD CONSTRAINT — scenario rule (must not be violated): {rule}");
        }

        // Opening peripheral constraint: during the first N turns, suppress other characters
        // from narrative focus so the persona-partner relationship is established before introducing them.
        // Other characters are present in the scene but must not be named or focused on.
        // Applies to both Narrative and Message turns so the constraint holds across all generated content.
        var openingInteractionCount = session.Interactions.Count(i => !i.IsExcluded);
        if (intent == PromptIntent.Narrative || intent == PromptIntent.Message)
        {
            if (openingInteractionCount <= OpeningPeripheralTurnCount)
            {
                sb.AppendLine("HARD CONSTRAINT — Opening Peripheral Focus: Other characters are present in this scene but must remain peripheral background presence only. " +
                    "They are NOT the focus of any character's attention, thoughts, or dialogue. Do not refer to them by name in this passage.");
                _logger.LogDebug(
                    "PeripheralConstraint: injected for turn {Turn} of {Threshold} SessionId={SessionId}",
                    openingInteractionCount, OpeningPeripheralTurnCount, session.Id);
            }
        }

        if (intent == PromptIntent.Narrative)
        {
            if (string.Equals(currentPhase, "Climax", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"Write a detailed omniscient narrative of the physical scene as it stands this turn. " +
                    $"Refer to {personaName} by name — NEVER use \"I\" or first person. " +
                    "Describe the following in full physical detail: (1) exact body part positions and how characters are positioned relative to each other; " +
                    "(2) physical contact points — what is touching what; " +
                    "(3) clothing and undress state; " +
                    "(4) physical sensations — texture, pressure, heat, weight; " +
                    "(5) sounds — breathing, movement, ambient environment; " +
                    "(6) rhythm and motion. " +
                    "Write as a detailed physical account of what is occurring right now — positions, contact, sensation, and movement. " +
                    "HARD CONSTRAINT — Include zero quoted speech. Do not write any dialogue in this passage. " +
                    "HARD CONSTRAINT — Do not advance the scene beyond what the characters have already established in their responses this turn. Synthesize what occurred — describe positions, sensations, and atmosphere — but do not introduce new actions, new positions, or new story beats. " +
                    "HARD CONSTRAINT: Do not write departure scenes, farewells, or any narrative framing that concludes the story's time frame. The Climax phase is ongoing — sustain the scene within the encounter's moment. " +
                    $"Match the established tone ({styleHint}). Write at least 300 words.");
            }
            else if (openingInteractionCount == 0)
            {
                // Opening narrative — use the extended word target to match the couple-focused opening prompt.
                sb.AppendLine($"Write the opening narrative passage as an omniscient narrator in THIRD PERSON. " +
                    $"Refer to {personaName} by name — NEVER use \"I\" or first person. " +
                    "Describe the following: (1) spatial layout — where the persona and their partner are, and how they are positioned in the environment; " +
                    "(2) lighting, temperature, sounds, and sensory atmosphere; " +
                    "(3) their positions and body language relative to each other and the space; " +
                    "(4) externally observable actions, movements, and the emotional texture between them. " +
                    "HARD CONSTRAINT — Include zero quoted speech. Include one brief spoken fragment only if it is absolutely required for scene continuity and cannot be omitted. " +
                    "Do not write back-and-forth dialogue. Do not write character inner thoughts or feelings. " +
                    "HARD CONSTRAINT — Do not advance the scene beyond what the characters have already established in their responses this turn. Synthesize and describe, do not introduce new actions. " +
                    $"Match the established tone ({styleHint}). Write 300–500 words.");
            }
            else
            {
                sb.AppendLine($"Write the next narrative passage as an omniscient narrator in THIRD PERSON. " +
                    $"Refer to {personaName} by name — NEVER use \"I\" or first person. " +
                    "Describe the following: (1) spatial layout — where characters are and how they are positioned in the environment; " +
                    "(2) lighting, temperature, sounds, and sensory atmosphere; " +
                    "(3) character positions and body language relative to each other and the space; " +
                    "(4) externally observable actions, movements, and scene-level state changes. " +
                    "Your priority is the physical scene and environment — where characters are, how they are positioned, what surrounds them, what sounds and sensory details exist. " +
                    "HARD CONSTRAINT — Include zero quoted speech. Include one brief spoken fragment only if it is absolutely required for scene continuity and cannot be omitted. " +
                    "Do not write back-and-forth dialogue. Do not write character inner thoughts or feelings. " +
                    "HARD CONSTRAINT — Do not advance the scene beyond what the characters have already established in their responses this turn. Synthesize and describe, do not introduce new actions. " +
                    $"Match the established tone ({styleHint}). Write at least 200 words; target 250-400 words.");
            }
        }
        else
        {
            var perspectiveMode = session.ResolvePerspectiveMode(actor, actorName);
            RolePlayPerspectivePromptBuilder.AppendInteractionInstruction(
                sb,
                perspectiveMode,
                actorName,
                personaName,
                styleHint,
                "Output 100-300 words.");
        }

        return sb.ToString();
    }

    private async Task AppendObservingCandidateMenuAsync(
        StringBuilder sb,
        RolePlaySession session,
        string currentPhase,
        CancellationToken cancellationToken)
    {
        if (_rpThemeService is null)
        {
            return;
        }

        var themeIds = (session.SessionThemeSelections ?? [])
            .Select(x => x.ThemeId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (themeIds.Count < 2)
        {
            // Need at least two candidates for the menu to be meaningful as an option space.
            return;
        }

        var candidateLabels = new List<string>(themeIds.Count);
        foreach (var themeId in themeIds)
        {
            try
            {
                var theme = await _rpThemeService.GetThemeAsync(themeId, cancellationToken);
                if (theme is null)
                {
                    continue;
                }
                var label = string.IsNullOrWhiteSpace(theme.Label) ? theme.Id : theme.Label;
                candidateLabels.Add(label);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Unable to resolve candidate theme label for {ThemeId} in session {SessionId} (observing menu).",
                    themeId,
                    session.Id);
            }
        }

        if (candidateLabels.Count < 2)
        {
            return;
        }

        sb.AppendLine("Theme Observation (no theme committed yet):");
        sb.AppendLine($"- Current narrative phase: {currentPhase}");
        sb.AppendLine($"- Candidates: {string.Join(", ", candidateLabels)}");
        sb.AppendLine("- Let natural events emerge from the persona, characters and recent context. Do not steer the scene toward any single candidate; treat this list only as awareness of the option space.");
    }

    private static void InjectSessionMemoryBlock(
        StringBuilder sb,
        List<DreamGenClone.Domain.RolePlay.EncounterSummaryRecord> summaries,
        int effectiveMilestones,
        int effectiveArcCompletions,
        int effectiveEncounterCompletions,
        int currentCycleIndex)
    {
        // Arc completions: take most recent N (DESC by OccurredUtc, then reverse to chronological)
        var arcCompletions = summaries
            .Where(s => s.SummaryType == DreamGenClone.Domain.RolePlay.EncounterSummaryType.ArcCompletion)
            .OrderByDescending(s => s.OccurredUtc)
            .Take(effectiveArcCompletions)
            .OrderBy(s => s.OccurredUtc)
            .ToList();

        // Encounter completions: only current arc, take most recent N (DESC, then reverse to chronological)
        var encounterCompletions = summaries
            .Where(s => s.SummaryType == DreamGenClone.Domain.RolePlay.EncounterSummaryType.EncounterCompletion && s.CycleIndex == currentCycleIndex)
            .OrderByDescending(s => s.OccurredUtc)
            .Take(effectiveEncounterCompletions)
            .OrderBy(s => s.OccurredUtc)
            .ToList();

        // Phase milestones: only current arc, take most recent N (DESC, then reverse)
        var milestones = summaries
            .Where(s => s.SummaryType == DreamGenClone.Domain.RolePlay.EncounterSummaryType.PhaseMilestone && s.CycleIndex == currentCycleIndex)
            .OrderByDescending(s => s.OccurredUtc)
            .Take(effectiveMilestones)
            .OrderBy(s => s.OccurredUtc)
            .ToList();

        if (arcCompletions.Count == 0 && encounterCompletions.Count == 0 && milestones.Count == 0)
        {
            return;
        }

        sb.AppendLine("Session Memory:");

        // Render order: arc completions → encounter completions → phase milestones
        foreach (var record in arcCompletions)
        {
            sb.AppendLine($"[Arc {record.CycleIndex + 1} Complete — {record.CharacterId}]");
            if (!string.IsNullOrWhiteSpace(record.ActiveSummary))
            {
                sb.AppendLine(record.ActiveSummary);
            }
        }

        foreach (var record in encounterCompletions)
        {
            // Per-character encounter memory — number as "N/M" where M is the total encounters
            // in the arc (we don't know M cheaply, so we render the encounter number alone with
            // the arc index for context; the LLM prose already says "encounter N of arc").
            sb.AppendLine($"[Encounter {record.EncounterNumber} — {record.CharacterId}]");
            if (!string.IsNullOrWhiteSpace(record.ActiveSummary))
            {
                sb.AppendLine(record.ActiveSummary);
            }
        }

        foreach (var record in milestones)
        {
            sb.AppendLine($"[{record.FromPhase} → {record.ToPhase} — {record.CharacterId}]");
            if (!string.IsNullOrWhiteSpace(record.ActiveSummary))
            {
                sb.AppendLine(record.ActiveSummary);
            }
        }
    }

    private static void AppendActiveThemeContract(StringBuilder sb, RPTheme? activeTheme, string phase)
    {
        if (activeTheme is null)
        {
            return;
        }

        var label = string.IsNullOrWhiteSpace(activeTheme.Label) ? activeTheme.Id : activeTheme.Label;
        sb.AppendLine("Active Adaptive Theme Contract:");
        sb.AppendLine($"- Theme: {label} ({activeTheme.Id})");

        if (!string.IsNullOrWhiteSpace(activeTheme.Description))
        {
            sb.AppendLine($"- Theme Description: {activeTheme.Description.Trim()}");
        }

        var phaseGuidance = activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.IsNullOrWhiteSpace(x.GuidanceText))
            .OrderBy(x => x.GuidanceText, StringComparer.OrdinalIgnoreCase)
            .Select(x => Regex.Replace(x.GuidanceText.Trim(), @"\[Beat[^\]]*\]", "", RegexOptions.IgnoreCase).Trim())
            .Select(x => Regex.Replace(x, @"\[Aftermath:husband-contrast\]", "", RegexOptions.IgnoreCase).Trim())
            .ToList();
        if (phaseGuidance.Count > 0)
        {
            sb.AppendLine($"- Required Phase Constraints ({phase}) — these directives are hard requirements for this response:");
            foreach (var line in phaseGuidance)
            {
                sb.AppendLine($"  - {line}");
            }
        }

        var keyEmphasis = activeTheme.GuidancePoints
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.PointType == RPThemeGuidancePointType.Emphasis)
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Text.Trim())
            .ToList();
        if (keyEmphasis.Count > 0)
        {
            sb.AppendLine($"- Key Emphasis ({phase}): {string.Join(" | ", keyEmphasis)}");
        }

        sb.AppendLine("- STEERING RANK: This theme contract is the highest-ranking directive in this prompt. Character behavioral frames and stat state texts describe tendencies, not requirements — the theme contract overrides them when they conflict.");
        sb.AppendLine("- If multiple continuations are possible, choose the one that best matches this theme description while preserving continuity and safety constraints.");
    }

    private static void AppendSteerGuidance(
        StringBuilder sb,
        RolePlaySession session,
        string currentPhase,
        RPTheme? activeTheme,
        string steerDirective,
        bool includeThemeNotes,
        int maxThemeNotes,
        bool enableLocationServices)
    {
        sb.AppendLine("Steer Flow Guidance:");
        sb.AppendLine($"- Requested steer direction: {steerDirective}");
        sb.AppendLine($"- Current narrative phase: {currentPhase}");

        var location = session.AdaptiveState.CurrentSceneLocation;
        if (enableLocationServices && !string.IsNullOrWhiteSpace(location))
        {
            sb.AppendLine($"- Keep this steer plausible for the current surroundings at '{location}'.");
            sb.AppendLine("- If the steer implies changing location, add an explicit transition beat before characters arrive in a new place.");
        }

        if (activeTheme is not null)
        {
            var themeLabel = string.IsNullOrWhiteSpace(activeTheme.Label) ? activeTheme.Id : activeTheme.Label;
            sb.AppendLine($"- Active theme anchor: {themeLabel} ({activeTheme.Id}).");

            var phaseGuidance = RolePlayAssistantPrompts.GetThemePhaseGuidanceLines(activeTheme, currentPhase);
            if (phaseGuidance.Count > 0)
            {
                sb.AppendLine($"- Apply theme phase guidance for {currentPhase}:");
                foreach (var line in phaseGuidance)
                {
                    sb.AppendLine($"  - {line}");
                }
            }

            if (includeThemeNotes)
            {
                var notes = RolePlayAssistantPrompts.GetPhaseRelevantThemeAIGuidanceNotes(
                    activeTheme,
                    currentPhase,
                    Math.Clamp(maxThemeNotes, 1, 6),
                    includeFormulaNotes: false);
                if (notes.Count > 0)
                {
                    sb.AppendLine("- Relevant theme AI guidance notes for this phase:");
                    foreach (var note in notes)
                    {
                        sb.AppendLine($"  - [{note.Section}] {note.Text.Trim()}");
                    }
                }
            }
        }
        else
        {
            var availableThemes = session.AdaptiveState.ThemeScores.Values
                .Where(x => !x.Blocked)
                .OrderByDescending(x => x.Score)
                .Take(3)
                .ToList();

            if (availableThemes.Count > 0)
            {
                sb.AppendLine("- No active theme is currently locked. Guide direction toward one of these available high-fit themes:");
                foreach (var theme in availableThemes)
                {
                    var name = string.IsNullOrWhiteSpace(theme.ThemeName) ? theme.ThemeId : theme.ThemeName;
                    sb.AppendLine($"  - {name} ({theme.ThemeId}), score={theme.Score:F1}, intensity={theme.Intensity}");
                }
                sb.AppendLine("- Choose a direction that naturally increases coherence with one of the above themes without abrupt pivots.");
            }
            else
            {
                sb.AppendLine("- No active or available themes are established yet. Steer toward a coherent, phase-appropriate direction and preserve scene plausibility.");
            }
        }
    }

        private static string? ResolveSteerDirective(RolePlaySession session, string promptText, PromptIntent intent)
    {
        if (intent == PromptIntent.Instruction
            && TryExtractSteerDirective(promptText, out var directive))
        {
            return directive;
        }

        return null;
    }

    private static bool TryExtractSteerDirective(string promptText, out string directive)
    {
        directive = string.Empty;
        if (string.IsNullOrWhiteSpace(promptText))
        {
            return false;
        }

        var raw = promptText.Trim();
        if (!raw.StartsWith("/steer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remaining = raw.Length > 6 ? raw[6..].Trim() : string.Empty;
        directive = string.IsNullOrWhiteSpace(remaining)
            ? "Steer the scene in a meaningful, phase-consistent direction."
            : remaining;
        return true;
    }

    private static bool TryExtractTimeSkipDirective(string promptText, out string label)
    {
        label = string.Empty;
        if (string.IsNullOrWhiteSpace(promptText))
        {
            return false;
        }

        var raw = promptText.Trim();
        if (!raw.StartsWith("/timeskip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remaining = raw.Length > 9 ? raw[9..].Trim() : string.Empty;
        label = string.IsNullOrWhiteSpace(remaining) ? "the next scene" : remaining;
        return true;
    }

    /// <summary>
    /// Time skips are one-shot — only resolved from the current turn, never carried forward from history.
    /// </summary>
    private static string? ResolveTimeSkipDirective(RolePlaySession session, string promptText, PromptIntent intent)
    {
        if (intent == PromptIntent.Instruction
            && TryExtractTimeSkipDirective(promptText, out var label))
        {
            return label;
        }

        return null;
    }

    private static void AppendTimeSkipGuidance(StringBuilder sb, string label, string currentPhase)
    {
        sb.AppendLine("Narrative Time Skip:");
        sb.AppendLine($"- Time advance: {label}");
        sb.AppendLine("- The current scene moment ends here. Open the next passage with a brief orienting sentence that anchors the new time and place.");
        sb.AppendLine("- Do not narrate the passage of time in detail — cut directly to the new moment.");
        sb.AppendLine("- Characters react naturally to the new setting; do not recap prior events.");
        sb.AppendLine($"- Maintain narrative phase continuity: current phase is {currentPhase}.");
        sb.AppendLine("- After the time-skip opening, continue the narrative from the new moment forward as normal.");
    }

    private static string ResolvePromptActorLabel(RolePlaySession session, string? actorIdOrName)
    {
        if (string.IsNullOrWhiteSpace(actorIdOrName))
        {
            return "Unknown";
        }

        var token = actorIdOrName.Trim();
        if (!string.IsNullOrWhiteSpace(session.PersonaName)
            && string.Equals(token, session.PersonaName, StringComparison.OrdinalIgnoreCase))
        {
            return session.PersonaName;
        }

        var perspective = session.CharacterPerspectives.FirstOrDefault(x =>
            string.Equals(x.CharacterId, token, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.CharacterName, token, StringComparison.OrdinalIgnoreCase));
        if (perspective is not null && !string.IsNullOrWhiteSpace(perspective.CharacterName))
        {
            return perspective.CharacterName;
        }

        return token;
    }

    private static Dictionary<string, int>? ResolvePromptActorStats(RolePlaySession session, string actorName)
    {
        if (string.IsNullOrWhiteSpace(actorName))
        {
            return null;
        }

        var direct = session.AdaptiveState.CharacterStats
            .FirstOrDefault(x => string.Equals(x.Key, actorName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(direct.Key))
        {
            return CharacterStatProfileV2Accessor.GetAllStats(direct.Value);
        }

        var byCharacterId = session.AdaptiveState.CharacterStats.Values.FirstOrDefault(x =>
            string.Equals(x.CharacterId, actorName, StringComparison.OrdinalIgnoreCase));
        return byCharacterId is null ? null : CharacterStatProfileV2Accessor.GetAllStats(byCharacterId);
    }

    private static int ResolveStat(IReadOnlyDictionary<string, int>? stats, string statName, double fallback)
    {
        if (stats is not null && stats.TryGetValue(statName, out var value))
        {
            return value;
        }

        return (int)Math.Round(fallback, MidpointRounding.AwayFromZero);
    }

    private async Task AppendPositionListAsync(
        StringBuilder sb,
        RolePlaySession session,
        string currentPhase,
        PromptIntent intent,
        CancellationToken cancellationToken)
    {
        if (_rpThemeService is null) return;
        if (intent == PromptIntent.Instruction) return;
        if (!string.Equals(currentPhase, "Approaching", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(currentPhase, "Climax", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            // Only scenario-bound characters (those with a CharacterRole) contribute to stat averages.
            var trackedStatsForPositions = session.AdaptiveState.CharacterStats.Values
                .Where(x => !string.IsNullOrEmpty(x.CharacterRole))
                .ToList();
            var avgDesire = trackedStatsForPositions.Count == 0 ? 50.0
                : trackedStatsForPositions.Average(x => CharacterStatProfileV2Accessor.GetStatOrDefault(x, "Desire", 50));
            var avgDominance = trackedStatsForPositions.Count == 0 ? 50.0
                : trackedStatsForPositions.Average(x => CharacterStatProfileV2Accessor.GetStatOrDefault(x, "Dominance", 50));
            var otherManDominance = trackedStatsForPositions.Count == 0 ? avgDominance
                : trackedStatsForPositions.Average(x =>
                {
                    var allStats = CharacterStatProfileV2Accessor.GetAllStats(x);
                    if (allStats.TryGetValue("OtherManDominance", out var d)) return (double)d;
                    if (allStats.TryGetValue("OtherManDom", out var d2)) return (double)d2;
                    if (allStats.TryGetValue("RivalDominance", out var d3)) return (double)d3;
                    if (allStats.TryGetValue("BullDominance", out var d4)) return (double)d4;
                    return avgDominance;
                });

            var tier = DerivePositionEscalationTier(avgDesire, otherManDominance);
            var all = await _rpThemeService.ListPositionsAsync(cancellationToken);
            var available = all.Where(p => PositionTierRank(p.EscalationTier) <= PositionTierRank(tier))
                               .OrderBy(p => p.SortOrder)
                               .ToList();

            if (available.Count == 0) return;

            sb.AppendLine($"Available Positions (tier: {tier}):");
            foreach (var pos in available)
            {
                sb.AppendLine($"- {pos.Name}: {pos.ShortDescription}");
            }

            sb.AppendLine("Use positions from this list when describing physical acts. Do not invent positions not on this list.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to load position list for continuation prompt.");
        }
    }

    private static string DerivePositionEscalationTier(double desire, double otherManDominance)
    {
        if (otherManDominance >= 70 || desire >= 70) return "High";
        if (otherManDominance >= 40 || desire >= 40) return "Medium";
        return "Low";
    }

    private static int PositionTierRank(string? tier) => (tier ?? "Low") switch
    {
        "High" => 3,
        "Medium" => 2,
        _ => 1
    };

    private static void AppendScenarioPriorities(
        StringBuilder sb,
        IReadOnlyList<string> goals,
        IReadOnlyList<string> conflicts,
        IReadOnlyList<string> guidelines)
    {
        if (goals.Count == 0 && conflicts.Count == 0 && guidelines.Count == 0)
        {
            return;
        }

        sb.AppendLine("Scenario Priorities For The Next Response:");
        foreach (var goal in goals)
        {
            sb.AppendLine($"- Higher priority: move toward this goal when it fits naturally: {goal}");
        }

        foreach (var conflict in conflicts)
        {
            sb.AppendLine($"- Higher priority: keep this conflict active, meaningful, or unresolved unless a natural scene turn changes it: {conflict}");
        }

        foreach (var guideline in guidelines)
        {
            sb.AppendLine($"- Lower priority than goals/conflicts, but still prefer this when it fits naturally: {guideline}");
        }

        sb.AppendLine("Treat goals and conflicts as higher-level soft priorities than narrative guidelines. Advance them when the scene allows, but do not force abrupt jumps or resolve everything immediately. Ignore any of these only when the current instruction, scene reality, or hard safety constraints require otherwise.");
    }

    private async Task<string> GenerateNarrativeWithValidationAsync(
        RolePlaySession session,
        string prompt,
        ResolvedModel resolved,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var climaxMode = session.AdaptiveState.CurrentPhase == NarrativePhase.Climax;

        var firstOutput = await _completionClient.GenerateAsync(prompt, resolved, cancellationToken);
        var firstAnalysis = AnalyzeNarrativeOutput(firstOutput, climaxMode);
        await WriteNarrativeValidationEventAsync(session, correlationId, 0, firstAnalysis, cancellationToken);

        if (!firstAnalysis.ShouldRetry)
        {
            return string.IsNullOrWhiteSpace(firstOutput) ? "(No output generated)" : firstOutput.Trim();
        }

        var retryPrompt = BuildNarrativeCorrectionPrompt(prompt, firstAnalysis);
        var bestOutput = firstOutput;
        var bestAnalysis = firstAnalysis;

        for (var attempt = 1; attempt <= NarrativeValidationRetryLimit; attempt++)
        {
            var retryOutput = await _completionClient.GenerateAsync(retryPrompt, resolved, cancellationToken);
            var retryAnalysis = AnalyzeNarrativeOutput(retryOutput, climaxMode);
            await WriteNarrativeValidationEventAsync(session, correlationId, attempt, retryAnalysis, cancellationToken);

            if (retryAnalysis.Score <= bestAnalysis.Score)
            {
                bestOutput = retryOutput;
                bestAnalysis = retryAnalysis;
            }

            if (!retryAnalysis.ShouldRetry)
            {
                break;
            }
        }

        if (bestAnalysis.HasViolation)
        {
            _logger.LogWarning(
                "Narrative validation retained best-effort output in session {SessionId}; score={Score}, quotedBlocks={QuotedBlocks}",
                session.Id,
                bestAnalysis.Score,
                bestAnalysis.QuotedBlockCount);
        }

        return string.IsNullOrWhiteSpace(bestOutput) ? "(No output generated)" : bestOutput.Trim();
    }

    private async Task WriteNarrativeValidationEventAsync(
        RolePlaySession session,
        string correlationId,
        int attempt,
        NarrativeValidationResult analysis,
        CancellationToken cancellationToken)
    {
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            CorrelationId = correlationId,
            EventKind = "NarrativeValidation",
            Severity = analysis.HasViolation ? "Warning" : "Info",
            ActorName = "Narrative",
            Summary = analysis.HasViolation
                ? $"Narrative output flagged on attempt {attempt}"
                : $"Narrative output accepted on attempt {attempt}",
            MetadataJson = JsonSerializer.Serialize(new
            {
                attempt,
                analysis.Score,
                analysis.HasViolation,
                analysis.ShouldRetry,
                analysis.QuotedBlockCount,
                analysis.QuotedTextRatio,
                analysis.DialogueAttributionCount,
                analysis.FirstPersonLeakCount,
                analysis.CharacterInteriorityCount
            })
        }, cancellationToken);
    }

    private static string? NarrativeLocationLabel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        // Strip subtitle after the first separator (em-dash, en-dash, hyphen, or colon with spaces).
        foreach (var sep in new[] { " \u2014 ", " \u2013 ", " - ", " : " })
        {
            var idx = raw.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0)
            {
                return raw[..idx].Trim();
            }
        }

        return raw.Trim();
    }

    private static string BuildNarrativeCorrectionPrompt(string originalPrompt, NarrativeValidationResult analysis)
    {
        var sb = new StringBuilder();
        sb.Append(originalPrompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Revision required: rewrite as pure scene narration.");

        if (analysis.QuotedBlockCount >= NarrativeQuotedBlockRetryThreshold)
        {
            sb.AppendLine($"Found {analysis.QuotedBlockCount} quoted blocks — reduce to zero. Do not write any dialogue.");
        }

        if (analysis.FirstPersonLeakCount > 0)
        {
            sb.AppendLine("Found first-person pronoun in narrator body — write in third person throughout; do not use 'I', 'me', 'my', 'mine', or 'myself' outside of a quoted fragment.");
        }

        if (analysis.CharacterInteriorityCount > 0)
        {
            sb.AppendLine("Found inner-thought phrases — remove sentences about what characters thought, felt, wondered, realized, or decided; describe only externally observable actions, positions, and states.");
        }

        sb.Append("Rewrite focusing on physical scene: positions, surroundings, sensations, and movement.");
        return sb.ToString();
    }

    private static NarrativeValidationResult AnalyzeNarrativeOutput(string? output, bool climaxMode = false)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new NarrativeValidationResult(HasViolation: false, ShouldRetry: false, Score: 0, QuotedBlockCount: 0, QuotedTextRatio: 0d, DialogueAttributionCount: 0, FirstPersonLeakCount: 0, CharacterInteriorityCount: 0);
        }

        var text = output.Trim();
        var quotedMatches = QuotedBlockRegex.Matches(text);
        var quotedLength = quotedMatches.Cast<Match>().Sum(m => m.Length);
        var quotedRatio = text.Length == 0 ? 0d : (double)quotedLength / text.Length;
        var quotedCount = quotedMatches.Count;
        var quotedThreshold = climaxMode ? 1 : NarrativeQuotedBlockRetryThreshold;
        var attributionCount = DialogueAttributionRegex.Matches(text).Count;
        var narratorBodyOnly = QuotedBlockRegex.Replace(text, string.Empty);
        var firstPersonCount = FirstPersonLeakRegex.Matches(narratorBodyOnly).Count;
        var interiorityCount = CharacterInteriorityRegex.Matches(text).Count;

        var score = 0;
        if (quotedCount >= quotedThreshold)
        {
            score += 3;
        }

        if (quotedCount >= NarrativeQuotedBlockHardViolationThreshold)
        {
            score += 3;
        }

        if (quotedRatio >= NarrativeQuotedTextRatioRetryThreshold)
        {
            score += 2;
        }

        if (attributionCount >= 2 && quotedCount >= 2)
        {
            score += 2;
        }

        if (firstPersonCount > 0)
        {
            score += 2;
        }

        if (interiorityCount > 0)
        {
            score += 2;
        }

        var hasViolation = score > 0;
        var shouldRetry = quotedCount >= quotedThreshold
            || quotedRatio >= NarrativeQuotedTextRatioRetryThreshold
            || (attributionCount >= 2 && quotedCount >= 2)
            || firstPersonCount > 0
            || interiorityCount > 0;

        return new NarrativeValidationResult(
            HasViolation: hasViolation,
            ShouldRetry: shouldRetry,
            Score: score,
            QuotedBlockCount: quotedCount,
            QuotedTextRatio: Math.Round(quotedRatio, 4),
            DialogueAttributionCount: attributionCount,
            FirstPersonLeakCount: firstPersonCount,
            CharacterInteriorityCount: interiorityCount);
    }

    private static IReadOnlyList<ScenarioCharacter> BuildCharactersWithPersona(
        IReadOnlyList<ScenarioCharacter>? scenarioCharacters,
        RolePlaySession session)
    {
        var list = scenarioCharacters is not null
            ? new List<ScenarioCharacter>(scenarioCharacters)
            : [];

        // The persona may already be present when the scenario's Characters list includes
        // an IsPersona character (unified persona-as-character refactor). Only inject the
        // legacy synthetic "__persona__" entry when no existing entry matches the persona
        // name — this preserves the frame-generator resolution path for old sessions that
        // still rely on the agg-slot token.
        var personaName = !string.IsNullOrWhiteSpace(session.PersonaName) ? session.PersonaName : "Persona";
        var alreadyPresent = list.Any(c =>
            string.Equals(c.Name, personaName, StringComparison.OrdinalIgnoreCase));
        if (alreadyPresent)
        {
            return list;
        }

        var personaRole = !string.IsNullOrWhiteSpace(session.PersonaRole) ? session.PersonaRole : string.Empty;
        list.Add(new ScenarioCharacter("__persona__", personaName, personaRole));

        return list;
    }

    private sealed record NarrativeValidationResult(
        bool HasViolation,
        bool ShouldRetry,
        int Score,
        int QuotedBlockCount,
        double QuotedTextRatio,
        int DialogueAttributionCount,
        int FirstPersonLeakCount,
        int CharacterInteriorityCount);

    // ── Intimate behavioral text injection helpers ──────────────────────────

    private static void InjectCharacterBehavioralTexts(
        StringBuilder sb,
        DreamGenClone.Web.Domain.Scenarios.Character character,
        System.Collections.Generic.IReadOnlyList<DreamGenClone.Web.Domain.Scenarios.Character> allCharacters,
        RolePlaySession session)
    {
        if (character.PhysicalAttributes is null) return;

        // 1. Self-awareness text for every character with intimate attributes
        var selfAwareness = IntimateBehavioralTextBuilder.BuildSelfAwarenessText(
            character.PhysicalAttributes, character.Gender, awarenessLevel: null, character.Name);
        if (!string.IsNullOrEmpty(selfAwareness))
            sb.AppendLine($"    {selfAwareness}");

        // 2. Partner perspective: for female characters related to male persona/character
        var isFemale = string.Equals(character.Gender, "Female", StringComparison.OrdinalIgnoreCase);
        if (!isFemale || session.PersonaPhysicalAttributes is null) return;

        var personaIsMale = string.Equals(session.PersonaGender, "Male", StringComparison.OrdinalIgnoreCase);
        if (!personaIsMale) return;

        // Check if this female character is related to the male persona
        var hasRelationToPersona = HasRelationToPersona(character, session, allCharacters);
        if (!hasRelationToPersona) return;

        // Partner perspective: this female character → persona
        var partnerPerspective = IntimateBehavioralTextBuilder.BuildPartnerPerspectiveText(
            session.PersonaPhysicalAttributes, session.PersonaGender,
            character.PhysicalAttributes, character.Gender,
            session.PersonaName, character.Name!);
        if (!string.IsNullOrEmpty(partnerPerspective))
            sb.AppendLine($"    {partnerPerspective}");

        // 3. Partner perspective + comparison: for other male characters in scene
        var otherMales = allCharacters
            .Where(c => c != character
                && string.Equals(c.Gender, "Male", StringComparison.OrdinalIgnoreCase)
                && c.PhysicalAttributes is not null
                && HasAnyIntimateFields(c.PhysicalAttributes))
            .ToList();

        string? firstOtherMaleName = null;
        DreamGenClone.Domain.Templates.PhysicalAttributes? firstOtherMaleAttrs = null;

        foreach (var otherMale in otherMales)
        {
            // B-058 Phase 6.2 gate: knowledge of the other man's intimate attributes now
            // depends on whether an EncounterCompletion record exists for him. Pre-encounter
            //   → attraction without knowledge (BuildPartnerPreEncounterText)
            // Post-encounter → full partner perspective (BuildPartnerPerspectiveText)
            // Comparison text is gated post-encounter only.
            //
            // B-058 Phase 6.3 EXCEPTION: the husband (or any male character related to this
            // female character) has an established intimate history — the encounter gate is
            // designed for new partners (the "other man"), not the spouse. Always treat
            // related males as post-encounter.
            var isRelatedMale = !string.IsNullOrWhiteSpace(otherMale.RelationTargetId)
                && string.Equals(otherMale.RelationTargetId, character.Name, StringComparison.OrdinalIgnoreCase);

            var hasEncounterCompletion = isRelatedMale
                || IntimateBehavioralTextBuilder.HasEncounterCompletionForCharacter(
                    otherMale.Id,
                    session.AdaptiveState.EncounterSummaries,
                    session.PersonaName);

            if (!hasEncounterCompletion)
            {
                var anticipation = IntimateBehavioralTextBuilder.BuildPartnerPreEncounterText(
                    otherMale.Name!, otherMale.Gender,
                    otherMale.PhysicalAttributes,
                    character.Name!, character.Gender);
                if (!string.IsNullOrEmpty(anticipation))
                    sb.AppendLine($"    {anticipation}");
                // Pre-encounter: do NOT register as first-other-male — comparison is gated post-encounter.
                continue;
            }

            var otherPerspective = IntimateBehavioralTextBuilder.BuildPartnerPerspectiveText(
                otherMale.PhysicalAttributes!, otherMale.Gender,
                character.PhysicalAttributes, character.Gender,
                otherMale.Name!, character.Name!);
            if (!string.IsNullOrEmpty(otherPerspective))
                sb.AppendLine($"    {otherPerspective}");

            if (firstOtherMaleName is null)
            {
                firstOtherMaleName = otherMale.Name;
                firstOtherMaleAttrs = otherMale.PhysicalAttributes;
            }
        }

        // 4. Comparison text: when there are two male partners
        if (firstOtherMaleName is not null && firstOtherMaleAttrs is not null)
        {
            var comparison = IntimateBehavioralTextBuilder.BuildComparisonText(
                session.PersonaPhysicalAttributes, session.PersonaName,
                firstOtherMaleAttrs, firstOtherMaleName,
                character.PhysicalAttributes, character.Name!);
            if (!string.IsNullOrEmpty(comparison))
                sb.AppendLine($"    {comparison}");
        }
    }

    private static bool HasRelationToPersona(
        DreamGenClone.Web.Domain.Scenarios.Character character,
        RolePlaySession session,
        System.Collections.Generic.IReadOnlyList<DreamGenClone.Web.Domain.Scenarios.Character> allCharacters)
    {
        // Direct relation: character's RelationTargetId points to persona
        if (!string.IsNullOrWhiteSpace(character.RelationTargetId))
        {
            // Check if the target is the persona (by name match or special token)
            if (string.Equals(character.RelationTargetId, session.PersonaName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Role-based: Wife role character and Husband role persona
        var charRole = CharacterRoleCatalog.Normalize(character.Role);
        var personaRole = CharacterRoleCatalog.Normalize(session.PersonaRole);
        if (string.Equals(charRole, "Wife", StringComparison.OrdinalIgnoreCase)
            && string.Equals(personaRole, "Husband", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static int? ResolvePersonaAwarenessLevel(RolePlaySession session)
    {
        // Try to find a CharacterEncounterProfileId for the persona by matching name or role
        foreach (var kvp in session.AdaptiveState.CharacterEncounterProfileIds)
        {
            if (string.Equals(kvp.Key, session.PersonaName, StringComparison.OrdinalIgnoreCase))
                return ResolveAwarenessFromProfileId(kvp.Value);
        }

        // Fallback: match by persona role
        foreach (var kvp in session.AdaptiveState.CharacterEncounterProfileIds)
        {
            if (string.Equals(kvp.Key, session.PersonaRole, StringComparison.OrdinalIgnoreCase))
                return ResolveAwarenessFromProfileId(kvp.Value);
        }

        return null;
    }

    private static int? ResolveAwarenessFromProfileId(string? profileId)
    {
        // Awareness is derived from CharacterProfile dimensions.
        // Without ICharacterProfileService injected into static helpers,
        // we return null. The caller handles null by omitting awareness framing.
        // Future enhancement: wire up ICharacterProfileService for full resolution.
        return null;
    }

    private static bool HasAnyIntimateFields(DreamGenClone.Domain.Templates.PhysicalAttributes attrs)
    {
        return !string.IsNullOrWhiteSpace(attrs.Scent)
            || !string.IsNullOrWhiteSpace(attrs.SexualDrive)
            || !string.IsNullOrWhiteSpace(attrs.SexualConfidence)
            || !string.IsNullOrWhiteSpace(attrs.SexualSkill)
            || !string.IsNullOrWhiteSpace(attrs.OralSkill)
            || !string.IsNullOrWhiteSpace(attrs.EndowmentLength)
            || !string.IsNullOrWhiteSpace(attrs.EndowmentGirth)
            || !string.IsNullOrWhiteSpace(attrs.Stamina)
            || !string.IsNullOrWhiteSpace(attrs.Recovery)
            || !string.IsNullOrWhiteSpace(attrs.EjaculationIntensity)
            || !string.IsNullOrWhiteSpace(attrs.VaginalTightness)
            || !string.IsNullOrWhiteSpace(attrs.Sensitivity)
            || !string.IsNullOrWhiteSpace(attrs.Lubrication)
            || !string.IsNullOrWhiteSpace(attrs.OrgasmicCapacity);
    }

}
