using System.Text;
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
    private readonly IToneProfileService _toneProfileService;
    private readonly ILogger<RolePlayContinuationService> _logger;

    public RolePlayContinuationService(
        ICompletionClient completionClient,
        IModelResolutionService modelResolver,
        IModelSettingsService modelSettingsService,
        IScenarioService scenarioService,
        IPromptDealbreakerService dealbreakerService,
        IThemePreferenceService themePreferenceService,
        IToneProfileService toneProfileService,
        ILogger<RolePlayContinuationService> logger)
    {
        _completionClient = completionClient;
        _modelResolver = modelResolver;
        _modelSettingsService = modelSettingsService;
        _scenarioService = scenarioService;
        _dealbreakerService = dealbreakerService;
        _themePreferenceService = themePreferenceService;
        _toneProfileService = toneProfileService;
        _logger = logger;
    }

    public async Task<RolePlayInteraction> ContinueAsync(
        RolePlaySession session,
        ContinueAsActor actor,
        string? customActorName,
        PromptIntent intent,
        string promptText,
        CancellationToken cancellationToken = default)
    {
        await ValidateDirectiveTextAsync(session, promptText, cancellationToken);

        var prompt = await BuildPromptAsync(session, actor, customActorName, intent, promptText, cancellationToken);

        var sessionSettings = _modelSettingsService.GetSettings(session.Id);
        var resolved = await _modelResolver.ResolveAsync(
            AppFunction.RolePlayGeneration,
            sessionModelId: sessionSettings.SessionModelId,
            sessionTemperature: sessionSettings.SessionModelId != null ? sessionSettings.Temperature : null,
            sessionTopP: sessionSettings.SessionModelId != null ? sessionSettings.TopP : null,
            sessionMaxTokens: sessionSettings.SessionModelId != null ? sessionSettings.MaxTokens : null,
            cancellationToken: cancellationToken);
        var output = await _completionClient.GenerateAsync(prompt, resolved, cancellationToken);

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
        if (string.IsNullOrWhiteSpace(session.SelectedRankingProfileId)
            || string.IsNullOrWhiteSpace(directiveText))
        {
            return;
        }

        var validation = await _dealbreakerService.ValidateAsync(directiveText, session.SelectedRankingProfileId, cancellationToken);
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
        ToneIntensity? baseToneIntensity = null;

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
                scenarioStyle = $"{scenario.Style.WritingStyle} / {scenario.Style.Tone}".Trim();
                sb.AppendLine($"- Style: {scenarioStyle}");

                if (!string.IsNullOrWhiteSpace(scenario.Style.PointOfView))
                {
                    sb.AppendLine($"- Preferred POV: {scenario.Style.PointOfView}");
                }

                if (scenario.Plot.Goals.Count > 0)
                {
                    sb.AppendLine("- Plot Goals:");
                    foreach (var goal in scenario.Plot.Goals.Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        sb.AppendLine($"  - {goal.Trim()}");
                    }
                }

                if (scenario.Plot.Conflicts.Count > 0)
                {
                    sb.AppendLine("- Plot Conflicts:");
                    foreach (var conflict in scenario.Plot.Conflicts.Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        sb.AppendLine($"  - {conflict.Trim()}");
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

                if (scenario.Style.StyleGuidelines.Count > 0)
                {
                    sb.AppendLine("- Style Guidelines:");
                    foreach (var guideline in scenario.Style.StyleGuidelines.Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        sb.AppendLine($"  - {guideline.Trim()}");
                    }
                }

                sb.AppendLine("Follow scenario goals, rules, and style guidelines unless they conflict with hard safety constraints.");
                if (!string.IsNullOrWhiteSpace(session.SelectedToneProfileId) || !string.IsNullOrWhiteSpace(scenario.Style.ToneProfileId))
                {
                    var toneProfileId = session.SelectedToneProfileId ?? scenario.Style.ToneProfileId;
                    sb.AppendLine($"- Tone Profile: {toneProfileId}");
                    if (!string.IsNullOrWhiteSpace(toneProfileId))
                    {
                        var toneProfile = await _toneProfileService.GetAsync(toneProfileId, cancellationToken);
                        if (toneProfile is not null)
                        {
                            baseToneIntensity = toneProfile.Intensity;
                        }
                    }
                }
                if (!string.IsNullOrWhiteSpace(session.StyleFloorOverride) || !string.IsNullOrWhiteSpace(session.StyleCeilingOverride))
                {
                    sb.AppendLine($"- Style Bounds: floor={session.StyleFloorOverride ?? "(none)"}, ceiling={session.StyleCeilingOverride ?? "(none)"}");
                }

                // Include all character descriptions so the AI can portray them accurately
                if (scenario.Characters.Count > 0)
                {
                    sb.AppendLine("Characters in this scene:");
                    foreach (var character in scenario.Characters)
                    {
                        if (!string.IsNullOrWhiteSpace(character.Name))
                        {
                            sb.AppendLine($"  {character.Name}: {character.Description?.Trim() ?? "(no description)"}");
                        }
                    }
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

        if (!string.IsNullOrWhiteSpace(session.SelectedRankingProfileId))
        {
            sb.AppendLine($"Hard safety constraints for this session derive from ranking profile '{session.SelectedRankingProfileId}'.");
            var profileThemes = await _themePreferenceService.ListByProfileAsync(session.SelectedRankingProfileId, cancellationToken);

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
        var (effectiveStyleLabel, effectiveStyleReason) = ResolveEffectiveStyle(session, baseToneIntensity);
        sb.AppendLine($"Effective Style Mode: {effectiveStyleLabel}");
        sb.AppendLine($"Style Resolution: {effectiveStyleReason}");

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
        else if (actor == ContinueAsActor.You)
        {
            // Persona is the POV character — writes in 1st person
            sb.AppendLine($"Write the next interaction as {actorName} in FIRST PERSON. Use \"I\" throughout. " +
                $"Include {actorName}'s dialogue, actions, physical sensations, and internal thoughts. " +
                "Refer to all other characters by name in third person. " +
                $"Match the scene's style ({styleHint}). Output 100-300 words.");
        }
        else
        {
            // NPC / Custom character writes in THIRD PERSON
            sb.AppendLine($"Write the next interaction for {actorName} in THIRD PERSON. " +
                $"Use \"{actorName}\" and \"he/she/they\" — NEVER use \"I\" or first person for {actorName}. " +
                $"Include {actorName}'s dialogue (in quotes), physical actions, body language, and observable behavior. " +
                $"Do NOT write {actorName}'s internal thoughts or feelings — only what can be seen and heard externally. " +
                $"Do NOT write from {personaName}'s perspective or include {personaName}'s thoughts. " +
                $"Match the scene's style ({styleHint}). Output 100-300 words.");
        }

        return sb.ToString();
    }

    private static (string Label, string Reason) ResolveEffectiveStyle(RolePlaySession session, ToneIntensity? baseToneIntensity)
    {
        var baseScale = baseToneIntensity.HasValue ? (int)baseToneIntensity.Value : 2;
        var reasonParts = new List<string>
        {
            $"base={(ToneIntensity)Math.Clamp(baseScale, 0, 5)}"
        };

        var arousalValues = session.AdaptiveState.CharacterStats.Values
            .SelectMany(x => x.Stats.Where(kvp => string.Equals(kvp.Key, "Arousal", StringComparison.OrdinalIgnoreCase)).Select(kvp => kvp.Value))
            .ToList();
        if (arousalValues.Count > 0)
        {
            var avgArousal = arousalValues.Average();
            if (avgArousal >= 85)
            {
                baseScale += 2;
                reasonParts.Add("arousal=very-high(+2)");
            }
            else if (avgArousal >= 70)
            {
                baseScale += 1;
                reasonParts.Add("arousal=high(+1)");
            }
            else if (avgArousal <= 35)
            {
                baseScale -= 1;
                reasonParts.Add("arousal=low(-1)");
            }
        }

        if (session.Interactions.Count >= 14)
        {
            baseScale += 1;
            reasonParts.Add("progression=late(+1)");
        }
        else if (session.Interactions.Count <= 4)
        {
            baseScale -= 1;
            reasonParts.Add("progression=early(-1)");
        }

        var primary = session.AdaptiveState.ThemeTracker.PrimaryThemeId ?? string.Empty;
        var secondary = session.AdaptiveState.ThemeTracker.SecondaryThemeId ?? string.Empty;
        if (IsEscalatingTheme(primary) || IsEscalatingTheme(secondary))
        {
            baseScale += 1;
            reasonParts.Add("theme=escalating(+1)");
        }

        var floor = ParseBoundScale(session.StyleFloorOverride);
        var ceiling = ParseBoundScale(session.StyleCeilingOverride);

        var clamped = Math.Clamp(baseScale, 0, 5);
        if (floor.HasValue && clamped < floor.Value)
        {
            clamped = floor.Value;
            reasonParts.Add($"floor={ToStyleLabel(floor.Value)}");
        }

        if (ceiling.HasValue && clamped > ceiling.Value)
        {
            clamped = ceiling.Value;
            reasonParts.Add($"ceiling={ToStyleLabel(ceiling.Value)}");
        }

        return (ToStyleLabel(clamped), string.Join(", ", reasonParts));
    }

    private static bool IsEscalatingTheme(string themeId)
    {
        return string.Equals(themeId, "dominance", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeId, "power-dynamics", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeId, "forbidden-risk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeId, "humiliation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeId, "infidelity", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ParseBoundScale(string? bound)
    {
        if (string.IsNullOrWhiteSpace(bound))
        {
            return null;
        }

        var value = bound.Trim().ToLowerInvariant();
        if (value.Contains("intro") || value.Contains("pg12") || value.Contains("pg-12")) return 0;
        if (value.Contains("emotional") || value.Contains("pg13") || value.Contains("pg-13")) return 1;
        if (value.Contains("suggestive")) return 2;
        if (value.Contains("sensual") || value.Contains("mature")) return 3;
        if (value.Contains("explicit") || value.Contains("erotic")) return 4;
        if (value.Contains("hardcore")) return 5;
        return null;
    }

    private static string ToStyleLabel(int scale)
    {
        return scale switch
        {
            0 => "Intro / PG-12",
            1 => "Emotional / PG-13",
            2 => "Suggestive / PG-13+",
            3 => "Sensual / Mature",
            4 => "Erotic / Explicit",
            _ => "Hardcore / Explicit+"
        };
    }
}
