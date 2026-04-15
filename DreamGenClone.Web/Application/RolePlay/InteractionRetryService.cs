using System.Text;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.Models;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class InteractionRetryService : IInteractionRetryService
{
    private readonly IRolePlayEngineService _engineService;
    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolver;
    private readonly IModelSettingsService _modelSettingsService;
    private readonly IScenarioService _scenarioService;
    private readonly ILogger<InteractionRetryService> _logger;

    public InteractionRetryService(
        IRolePlayEngineService engineService,
        ICompletionClient completionClient,
        IModelResolutionService modelResolver,
        IModelSettingsService modelSettingsService,
        IScenarioService scenarioService,
        ILogger<InteractionRetryService> logger)
    {
        _engineService = engineService;
        _completionClient = completionClient;
        _modelResolver = modelResolver;
        _modelSettingsService = modelSettingsService;
        _scenarioService = scenarioService;
        _logger = logger;
    }

    public async Task<RolePlayInteraction> RetryAsync(
        RolePlaySession session,
        string interactionId,
        CancellationToken cancellationToken = default)
    {
        var original = ResolveOriginal(session, interactionId);
        var active = session.ResolveActiveAlternative(original);

        var prompt = await BuildRetryPromptAsync(session, active, null, cancellationToken);
        var resolved = await ResolveModelAsync(session, sessionModelId: null, cancellationToken);
        var output = await _completionClient.GenerateAsync(prompt, resolved, cancellationToken);

        var alternative = CreateAlternative(original, session, active.InteractionType, active.ActorName, output, resolved, "Retry");

        _logger.LogInformation(
            "Retry created alternative {AlternativeIndex} for interaction {InteractionId} in session {SessionId}",
            alternative.AlternativeIndex, original.Id, session.Id);

        return alternative;
    }

    public async Task<RolePlayInteraction> RetryWithModelAsync(
        RolePlaySession session,
        string interactionId,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        var original = ResolveOriginal(session, interactionId);
        var active = session.ResolveActiveAlternative(original);

        var prompt = await BuildRetryPromptAsync(session, active, null, cancellationToken);
        var resolved = await ResolveModelAsync(session, modelId, cancellationToken);
        var output = await _completionClient.GenerateAsync(prompt, resolved, cancellationToken);

        var alternative = CreateAlternative(original, session, active.InteractionType, active.ActorName, output, resolved, "RetryWithModel");

        _logger.LogInformation(
            "RetryWithModel created alternative {AlternativeIndex} for interaction {InteractionId} using model {ModelId} in session {SessionId}",
            alternative.AlternativeIndex, original.Id, modelId, session.Id);

        return alternative;
    }

    public async Task<RolePlayInteraction> RetryAsAsync(
        RolePlaySession session,
        string interactionId,
        ContinueAsActor actor,
        string? customActorName = null,
        CancellationToken cancellationToken = default)
    {
        var original = ResolveOriginal(session, interactionId);

        var interactionType = actor switch
        {
            ContinueAsActor.You => InteractionType.User,
            ContinueAsActor.Npc => InteractionType.Npc,
            ContinueAsActor.Custom => InteractionType.Custom,
            _ => InteractionType.System
        };
        var actorName = !string.IsNullOrWhiteSpace(customActorName)
            ? customActorName.Trim()
            : actor switch
            {
                ContinueAsActor.You => "You",
                ContinueAsActor.Npc => "NPC",
                _ => "Custom"
            };

        var prompt = await BuildRetryPromptAsync(session, original, $"Rewrite as character: {actorName}", cancellationToken);
        var resolved = await ResolveModelAsync(session, sessionModelId: null, cancellationToken);
        var output = await _completionClient.GenerateAsync(prompt, resolved, cancellationToken);

        var alternative = CreateAlternative(original, session, interactionType, actorName, output, resolved, "RetryAs");

        _logger.LogInformation(
            "RetryAs created alternative {AlternativeIndex} as {ActorName} for interaction {InteractionId} in session {SessionId}",
            alternative.AlternativeIndex, actorName, original.Id, session.Id);

        return alternative;
    }

    public async Task<RolePlayInteraction> MakeLongerAsync(
        RolePlaySession session,
        string interactionId,
        CancellationToken cancellationToken = default)
    {
        var original = ResolveOriginal(session, interactionId);
        var active = session.ResolveActiveAlternative(original);

        var prompt = await BuildRetryPromptAsync(session, active, "Rewrite the following interaction to be significantly longer and more detailed, expanding on descriptions, dialogue, and atmosphere.", cancellationToken);
        var resolved = await ResolveModelAsync(session, sessionModelId: null, cancellationToken);
        var output = await _completionClient.GenerateAsync(prompt, resolved, cancellationToken);

        var alternative = CreateAlternative(original, session, active.InteractionType, active.ActorName, output, resolved, "MakeLonger");

        _logger.LogInformation(
            "MakeLonger created alternative {AlternativeIndex} for interaction {InteractionId} in session {SessionId}",
            alternative.AlternativeIndex, original.Id, session.Id);

        return alternative;
    }

    public async Task<RolePlayInteraction> MakeShorterAsync(
        RolePlaySession session,
        string interactionId,
        CancellationToken cancellationToken = default)
    {
        var original = ResolveOriginal(session, interactionId);
        var active = session.ResolveActiveAlternative(original);

        var prompt = await BuildRetryPromptAsync(session, active, "Rewrite the following interaction to be shorter and more concise, keeping only the essential content.", cancellationToken);
        var resolved = await ResolveModelAsync(session, sessionModelId: null, cancellationToken);
        var output = await _completionClient.GenerateAsync(prompt, resolved, cancellationToken);

        var alternative = CreateAlternative(original, session, active.InteractionType, active.ActorName, output, resolved, "MakeShorter");

        _logger.LogInformation(
            "MakeShorter created alternative {AlternativeIndex} for interaction {InteractionId} in session {SessionId}",
            alternative.AlternativeIndex, original.Id, session.Id);

        return alternative;
    }

    public async Task<RolePlayInteraction> AskToRewriteAsync(
        RolePlaySession session,
        string interactionId,
        string instruction,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instruction))
        {
            throw new ArgumentException("Rewrite instruction cannot be empty.", nameof(instruction));
        }

        var original = ResolveOriginal(session, interactionId);
        var active = session.ResolveActiveAlternative(original);

        var prompt = await BuildRetryPromptAsync(session, active, $"Rewrite instruction: {instruction.Trim()}", cancellationToken);
        var resolved = await ResolveModelAsync(session, sessionModelId: null, cancellationToken);
        var output = await _completionClient.GenerateAsync(prompt, resolved, cancellationToken);

        var alternative = CreateAlternative(original, session, active.InteractionType, active.ActorName, output, resolved, "AskToRewrite");

        _logger.LogInformation(
            "AskToRewrite created alternative {AlternativeIndex} for interaction {InteractionId} in session {SessionId}",
            alternative.AlternativeIndex, original.Id, session.Id);

        return alternative;
    }

    private static RolePlayInteraction ResolveOriginal(RolePlaySession session, string interactionId)
    {
        var interaction = session.Interactions.FirstOrDefault(i => i.Id == interactionId)
            ?? throw new ArgumentException($"Interaction {interactionId} not found in session {session.Id}.", nameof(interactionId));

        if (interaction.ParentInteractionId is not null)
        {
            return session.Interactions.FirstOrDefault(i => i.Id == interaction.ParentInteractionId) ?? interaction;
        }

        return interaction;
    }

    private RolePlayInteraction CreateAlternative(
        RolePlayInteraction original,
        RolePlaySession session,
        InteractionType interactionType,
        string actorName,
        string output,
        ResolvedModel resolvedModel,
        string command)
    {
        var existingAlternatives = session.Interactions
            .Where(i => i.ParentInteractionId == original.Id)
            .ToList();

        var nextIndex = existingAlternatives.Count > 0
            ? existingAlternatives.Max(a => a.AlternativeIndex) + 1
            : 1;

        var alternative = new RolePlayInteraction
        {
            InteractionType = interactionType,
            ActorName = actorName,
            Content = string.IsNullOrWhiteSpace(output) ? "(No output generated)" : output.Trim(),
            ParentInteractionId = original.Id,
            AlternativeIndex = nextIndex,
            GeneratedByModelId = resolvedModel.ModelIdentifier,
            GeneratedByModelName = resolvedModel.ModelIdentifier,
            GeneratedByCommand = command,
            GeneratedByProvider = resolvedModel.ProviderName,
            GeneratedTemperature = resolvedModel.Temperature,
            GeneratedTopP = resolvedModel.TopP,
            GeneratedMaxTokens = resolvedModel.MaxTokens
        };

        original.ActiveAlternativeIndex = nextIndex;
        session.Interactions.Add(alternative);
        _engineService.SaveSessionAsync(session);

        return alternative;
    }

    private async Task<string> BuildRetryPromptAsync(
        RolePlaySession session,
        RolePlayInteraction target,
        string? additionalInstruction,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are continuing an interactive role-play scene.");
        sb.AppendLine($"Behavior mode: {session.BehaviorMode}");

        if (!string.IsNullOrWhiteSpace(session.PersonaDescription))
        {
            sb.AppendLine($"POV Persona ({session.PersonaName}):");
            sb.AppendLine(session.PersonaDescription.Trim());
        }
        else if (session.PersonaName != "You")
        {
            sb.AppendLine($"POV Persona: {session.PersonaName}");
        }

        List<string> scenarioGoals = [];
        List<string> scenarioConflicts = [];
        List<string> scenarioNarrativeGuidelines = [];

        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null)
            {
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
                sb.AppendLine($"- Narrative: {scenario.Narrative.ProseStyle} / {scenario.Narrative.NarrativeTone}");

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

                if (scenario.Characters.Count > 0)
                {
                    sb.AppendLine("Characters in this scene:");
                    foreach (var character in scenario.Characters.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
                    {
                        var roleText = string.IsNullOrWhiteSpace(character.Role)
                            ? string.Empty
                            : $" [Role: {character.Role.Trim()}]";
                        var relationText = RolePlayRelationFormatter.DescribeCharacterRelation(character, session, scenario.Characters);
                        var relationSuffix = string.IsNullOrWhiteSpace(relationText)
                            ? string.Empty
                            : $" [Relation: {relationText}]";
                        var description = string.IsNullOrWhiteSpace(character.Description)
                            ? "(no description)"
                            : character.Description.Trim();
                        sb.AppendLine($"  {character.Name!.Trim()}{roleText}{relationSuffix}: {description}");
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

        sb.AppendLine("Recent interaction history:");
        var contextView = session.GetContextView();
        foreach (var interaction in contextView.TakeLast(12))
        {
            sb.AppendLine($"[{interaction.InteractionType}] {interaction.ActorName}: {interaction.Content}");
        }

        sb.AppendLine($"Regenerate as: {target.ActorName}");
        sb.AppendLine("Original content to rewrite:");
        sb.AppendLine(target.Content);

        if (!string.IsNullOrWhiteSpace(additionalInstruction))
        {
            sb.AppendLine(additionalInstruction);
        }

        if (scenarioGoals.Count > 0 || scenarioConflicts.Count > 0 || scenarioNarrativeGuidelines.Count > 0)
        {
            sb.AppendLine("Scenario Priorities For This Rewrite:");
            foreach (var goal in scenarioGoals)
            {
                sb.AppendLine($"- Higher priority: move toward this goal when it fits naturally: {goal}");
            }

            foreach (var conflict in scenarioConflicts)
            {
                sb.AppendLine($"- Higher priority: keep this conflict active, meaningful, or unresolved unless a natural scene turn changes it: {conflict}");
            }

            foreach (var guideline in scenarioNarrativeGuidelines)
            {
                sb.AppendLine($"- Lower priority than goals/conflicts, but still prefer this when it fits naturally: {guideline}");
            }

            sb.AppendLine("Treat goals and conflicts as higher-level soft priorities than narrative guidelines. Advance them when the scene allows, but do not force abrupt jumps or resolve everything immediately. Ignore any of these only when the rewrite request, scene reality, or hard safety constraints require otherwise.");
        }

        var styleHint = BuildRetryStyleHint(session);
        var perspectiveMode = session.ResolvePerspectiveMode(target.InteractionType, target.ActorName);
        RolePlayPerspectivePromptBuilder.AppendInteractionInstruction(
            sb,
            perspectiveMode,
            target.ActorName,
            string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName,
            styleHint,
            "Output 100-300 words.");
        return sb.ToString();
    }

    private static string BuildRetryStyleHint(RolePlaySession session)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(session.SelectedIntensityProfileId))
        {
            parts.Add($"intensity profile: {session.SelectedIntensityProfileId}");
        }

        if (!string.IsNullOrWhiteSpace(session.IntensityFloorOverride) || !string.IsNullOrWhiteSpace(session.IntensityCeilingOverride))
        {
            parts.Add($"bounds: {session.IntensityFloorOverride ?? "(none)"} to {session.IntensityCeilingOverride ?? "(none)"}");
        }

        return parts.Count == 0 ? "current scene style" : string.Join(" | ", parts);
    }

    private async Task<ResolvedModel> ResolveModelAsync(
        RolePlaySession session,
        string? sessionModelId,
        CancellationToken cancellationToken)
    {
        var settings = _modelSettingsService.GetSettings(session.Id);
        return await _modelResolver.ResolveAsync(
            AppFunction.RolePlayGeneration,
            sessionModelId: sessionModelId ?? settings.SessionModelId,
            sessionTemperature: settings.SessionModelId != null ? settings.Temperature : null,
            sessionTopP: settings.SessionModelId != null ? settings.TopP : null,
            sessionMaxTokens: settings.SessionModelId != null ? settings.MaxTokens : null,
            cancellationToken: cancellationToken);
    }
}
