using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 14, Zone C — Scenario guidance with phase steering (FR-020).
/// Provides phase-appropriate steering direction. Suppresses or weakens
/// the resistance band when narrative state shows the threshold has already
/// been crossed. Low trim priority. Absorbs BeatStageInjector.
/// </summary>
public sealed class ScenarioGuidanceSlot : IPromptSlot
{
    private readonly ILogger<ScenarioGuidanceSlot> _logger;

    public PromptSlotId Id => PromptSlotId.ScenarioGuidance;
    public PromptZone Zone => PromptZone.C;
    public int Order => 14;
    public bool IsTrimEligible => true;

    public ScenarioGuidanceSlot(ILogger<ScenarioGuidanceSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context) => true;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var phase = context.Phase;

        _logger.LogDebug(
            "ScenarioGuidanceSlot: SessionId={SessionId} Phase={Phase}",
            context.Session.Id, phase);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Scenario Guidance:");

        // Phase-appropriate steering direction
        var guidance = GetPhaseGuidance(phase);
        sb.AppendLine($"- Phase: {phase} — {guidance}");

        // Narrative guidelines from scenario if available
        if (context.Scenario.NarrativeGuidelines.Count > 0)
        {
            sb.AppendLine("- Narrative guidelines:");
            foreach (var guideline in context.Scenario.NarrativeGuidelines)
            {
                sb.AppendLine($"  • {guideline}");
            }
        }

        // Scenario goals and conflicts for context
        if (context.Scenario.Goals.Count > 0)
        {
            sb.AppendLine("- Scenario goals:");
            foreach (var goal in context.Scenario.Goals)
            {
                sb.AppendLine($"  • {goal}");
            }
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;

        // Low trim: keep the phase line, drop guidelines/goals first.
        var lines = text.Split('\n');
        var result = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            if (result.Length + line.Length + 1 > maxChars)
                break;
            if (result.Length > 0)
                result.Append('\n');
            result.Append(line);
        }

        var trimmed = result.ToString();
        return trimmed.Length > 0 ? trimmed : text[..Math.Max(1, maxChars)];
    }

    private static string GetPhaseGuidance(string phase) => phase switch
    {
        "Opening" => "Establish the scene, introduce characters, set the tone. Keep tension low and exploratory.",
        "BuildUp" => "Deepen character dynamics, escalate underlying tension, build toward intimacy or confrontation.",
        "Committed" => "Characters have crossed a threshold. Explore the consequences and emotional fallout.",
        "Approaching" => "Narrative approaches a climax. Heighten stakes, narrow focus, intensify sensory detail.",
        "Climax" => "Peak of the narrative arc. Maximum intensity, decisive action, emotional release.",
        "Reset" => "Aftermath and reflection. Process what happened, re-establish equilibrium, seed future tension.",
        _ => "Continue the narrative naturally, following the established scene direction.",
    };
}
