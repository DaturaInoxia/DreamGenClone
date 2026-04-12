using System.Text;
using System.Text.Json;
using System.Diagnostics;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.Models;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayContinuationService : IRolePlayContinuationService
{
    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolver;
    private readonly IModelSettingsService _modelSettingsService;
    private readonly IScenarioService _scenarioService;
    private readonly IPromptDealbreakerService _dealbreakerService;
    private readonly IThemePreferenceService _themePreferenceService;
    private readonly IIntensityProfileService _intensityProfileService;
    private readonly ISteeringProfileService _steeringProfileService;
    private readonly IRolePlayDebugEventSink _debugEventSink;
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
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayContinuationService> logger)
    {
        _completionClient = completionClient;
        _modelResolver = modelResolver;
        _modelSettingsService = modelSettingsService;
        _scenarioService = scenarioService;
        _dealbreakerService = dealbreakerService;
        _themePreferenceService = themePreferenceService;
        _intensityProfileService = toneProfileService;
        _steeringProfileService = styleProfileService;
        _debugEventSink = debugEventSink;
        _logger = logger;
    }

    public async Task<RolePlayInteraction> ContinueAsync(
        RolePlaySession session,
        ContinueAsActor actor,
        string? customActorName,
        PromptIntent intent,
        string promptText,
        Func<string, Task>? onChunk = null,
        CancellationToken cancellationToken = default)
    {
        await ValidateDirectiveTextAsync(session, promptText, cancellationToken);

        var correlationId = Guid.NewGuid().ToString("N");
        var actorLabel = string.IsNullOrWhiteSpace(customActorName) ? actor.ToString() : customActorName;
        var prompt = await BuildPromptAsync(session, actor, customActorName, intent, promptText, cancellationToken);
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
        try
        {
            output = onChunk is null
                ? await _completionClient.GenerateAsync(prompt, resolved, cancellationToken)
                : await _completionClient.StreamGenerateAsync(prompt, resolved, onChunk, cancellationToken);
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
                    ContinueAsActor.You => "You",
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
            GeneratedMaxTokens = resolved.MaxTokens
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
                ? "Move the role-play story forward with scene description and tone."
                : promptText;

            await ValidateDirectiveTextAsync(session, narrativePrompt, cancellationToken);

            var prompt = await BuildPromptAsync(
                session,
                ContinueAsActor.Npc,
                null,
                PromptIntent.Narrative,
                narrativePrompt,
                cancellationToken);
            var narrativeSettings = _modelSettingsService.GetSettings(session.Id);
            var narrativeResolved = await _modelResolver.ResolveAsync(
                AppFunction.RolePlayGeneration,
                sessionModelId: narrativeSettings.SessionModelId,
                sessionTemperature: narrativeSettings.SessionModelId != null ? narrativeSettings.Temperature : null,
                sessionTopP: narrativeSettings.SessionModelId != null ? narrativeSettings.TopP : null,
                sessionMaxTokens: narrativeSettings.SessionModelId != null ? narrativeSettings.MaxTokens : null,
                cancellationToken: cancellationToken);
            var output = await _completionClient.GenerateAsync(prompt, narrativeResolved, cancellationToken);
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
                GeneratedMaxTokens = narrativeResolved.MaxTokens
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
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are continuing an interactive role-play scene.");
        sb.AppendLine($"Behavior mode: {session.BehaviorMode}");

        // Include POV persona
        if (!string.IsNullOrWhiteSpace(session.PersonaDescription))
        {
            sb.AppendLine($"POV Persona ({session.PersonaName}):");
            sb.AppendLine(session.PersonaDescription.Trim());
        }
        else if (session.PersonaName != "You")
        {
            sb.AppendLine($"POV Persona: {session.PersonaName}");
        }

        string scenarioStyle = string.Empty;
        IntensityLevel? baseIntensityLevel = null;
        string? scenarioSteeringProfileId = null;
        List<string> scenarioGoals = [];
        List<string> scenarioConflicts = [];
        List<string> scenarioNarrativeGuidelines = [];

        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null)
            {
                sb.AppendLine("Scenario:");
                sb.AppendLine($"- Name: {scenario.Name}");
                sb.AppendLine($"- Description: {scenario.Description}");
                sb.AppendLine($"- Plot: {scenario.Plot.Description}");
                sb.AppendLine($"- Setting: {scenario.Setting.WorldDescription}");
                scenarioStyle = $"{scenario.Narrative.ProseStyle} / {scenario.Narrative.NarrativeTone}".Trim();
                scenarioSteeringProfileId = scenario.DefaultSteeringProfileId;
                sb.AppendLine($"- Narrative: {scenarioStyle}");

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
                    sb.AppendLine("- World Rules:");
                    foreach (var rule in scenario.Setting.WorldRules.Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        sb.AppendLine($"  - {rule.Trim()}");
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
                        }
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
                            sb.AppendLine($"  {character.Name}{roleText}: {character.Description?.Trim() ?? "(no description)"}");
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

        sb.AppendLine("Recent interaction history:");
        var contextView = session.GetContextView();
        var windowSize = Math.Max(12, session.ContextWindowSize);
        foreach (var interaction in contextView.TakeLast(windowSize))
        {
            sb.AppendLine($"[{interaction.InteractionType}] {interaction.ActorName}: {interaction.Content}");
        }

        if (session.AdaptiveState.CharacterStats.Count > 0)
        {
            sb.AppendLine("Adaptive Character Stats:");
            foreach (var kvp in session.AdaptiveState.CharacterStats.OrderBy(x => x.Key).Take(8))
            {
                var summary = string.Join(", ", kvp.Value.Stats.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}"));
                sb.AppendLine($"- {kvp.Key}: {summary}");
            }
        }

        if (session.AdaptiveState.ThemeTracker.Themes.Count > 0)
        {
            sb.AppendLine("Active Theme Tracker:");
            var tracker = session.AdaptiveState.ThemeTracker;
            sb.AppendLine($"- Selection Rule: {tracker.ThemeSelectionRule}");

            var selectedThemes = new List<ThemeTrackerItem>();
            if (!string.IsNullOrWhiteSpace(tracker.PrimaryThemeId)
                && tracker.Themes.TryGetValue(tracker.PrimaryThemeId, out var primaryTheme))
            {
                selectedThemes.Add(primaryTheme);
            }

            if (!string.IsNullOrWhiteSpace(tracker.SecondaryThemeId)
                && tracker.Themes.TryGetValue(tracker.SecondaryThemeId, out var secondaryTheme)
                && !string.Equals(secondaryTheme.ThemeId, tracker.PrimaryThemeId, StringComparison.OrdinalIgnoreCase))
            {
                selectedThemes.Add(secondaryTheme);
            }

            foreach (var item in selectedThemes)
            {
                sb.AppendLine($"- {item.ThemeName}: intensity={item.Intensity}, score={item.Score:F1}");
            }

            var latestEvidence = session.AdaptiveState.ThemeTracker.RecentEvidence.TakeLast(3).ToList();
            if (latestEvidence.Count > 0)
            {
                sb.AppendLine("Recent Theme Evidence:");
                foreach (var evidence in latestEvidence)
                {
                    sb.AppendLine($"- theme={evidence.ThemeId}, delta={evidence.Delta:F1}, confidence={evidence.Confidence:F2}, why={evidence.Rationale}");
                }
            }
        }

        var actorName = !string.IsNullOrWhiteSpace(customActorName)
            ? customActorName.Trim()
            : actor switch
            {
                ContinueAsActor.You => string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName,
                ContinueAsActor.Npc => "NPC",
                _ => "Custom"
            };

        sb.AppendLine($"Continue as: {actorName}");

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

        // Three distinct interaction types with different POV rules:
        // Narrative = 3rd person omniscient storyteller setting scenes
        // Persona   = 1st person POV ("I felt...", "I watched...")
        // NPC       = 3rd person external (dialogue + observable behavior only)
        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName;
        var (effectiveStyleLabel, effectiveStyleReason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, baseIntensityLevel);
        sb.AppendLine($"Resolved Intensity: {effectiveStyleLabel}");
        sb.AppendLine($"Resolution Reason: {effectiveStyleReason}");
        sb.AppendLine($"Manual Intensity Pin: {(session.IsIntensityManuallyPinned ? "ON" : "OFF")}");

        var styleHint = string.IsNullOrWhiteSpace(scenarioStyle)
            ? effectiveStyleLabel
            : $"{scenarioStyle} | effective mode: {effectiveStyleLabel}";

        if (intent == PromptIntent.Narrative)
        {
            sb.AppendLine($"Write the next narrative passage in THIRD PERSON. " +
                $"Refer to {personaName} by name when needed — NEVER use \"I\" or first person. " +
                "Treat this as omniscient scene narration: describe environment, pacing, transitions, and multi-character flow. " +
                "Do not center the passage on one character's private feelings or inner monologue unless the user explicitly asks for that. " +
                "Prefer externally observable actions, dialogue, body language, and scene-level state changes. " +
                $"Use vivid sensory details and match the established tone ({styleHint}). Output 100-300 words.");
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

}
