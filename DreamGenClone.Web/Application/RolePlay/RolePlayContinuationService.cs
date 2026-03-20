using System.Text;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayContinuationService : IRolePlayContinuationService
{
    private readonly ILmStudioClient _lmStudioClient;
    private readonly IScenarioService _scenarioService;
    private readonly ILogger<RolePlayContinuationService> _logger;

    public RolePlayContinuationService(
        ILmStudioClient lmStudioClient,
        IScenarioService scenarioService,
        ILogger<RolePlayContinuationService> logger)
    {
        _lmStudioClient = lmStudioClient;
        _scenarioService = scenarioService;
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
        var prompt = await BuildPromptAsync(session, actor, customActorName, intent, promptText, cancellationToken);
        var output = await _lmStudioClient.GenerateAsync(prompt, cancellationToken);

        var interaction = new RolePlayInteraction
        {
            InteractionType = actor switch
            {
                ContinueAsActor.You => InteractionType.User,
                ContinueAsActor.Npc => InteractionType.Npc,
                ContinueAsActor.Custom => InteractionType.Custom,
                _ => InteractionType.System
            },
            ActorName = actor == ContinueAsActor.Custom && !string.IsNullOrWhiteSpace(customActorName)
                ? customActorName.Trim()
                : actor switch
                {
                    ContinueAsActor.You => "You",
                    ContinueAsActor.Npc => "NPC",
                    _ => "Custom"
                },
            Content = string.IsNullOrWhiteSpace(output) ? "(No output generated)" : output.Trim()
        };

        _logger.LogInformation("Role-play continuation prepared for actor {Actor} in session {SessionId}", interaction.ActorName, session.Id);
        return interaction;
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

        // Include POV persona if set
        if (!string.IsNullOrWhiteSpace(session.PersonaDescription))
        {
            sb.AppendLine($"POV Persona ({session.PersonaName}):");
            sb.AppendLine(session.PersonaDescription.Trim());
        }
        else if (session.PersonaName != "You")
        {
            sb.AppendLine($"POV Persona: {session.PersonaName}");
        }

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
                sb.AppendLine($"- Style: {scenario.Style.WritingStyle} / {scenario.Style.Tone}");
            }
        }

        sb.AppendLine("Recent interaction history:");
        foreach (var interaction in session.Interactions.TakeLast(12))
        {
            sb.AppendLine($"[{interaction.InteractionType}] {interaction.ActorName}: {interaction.Content}");
        }

        var actorName = actor switch
        {
            ContinueAsActor.You => string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName,
            ContinueAsActor.Npc => "NPC",
            ContinueAsActor.Custom when !string.IsNullOrWhiteSpace(customActorName) => customActorName.Trim(),
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

        sb.AppendLine("Write one concise next interaction message only.");
        return sb.ToString();
    }
}
