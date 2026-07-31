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
    /// <summary>
    /// Opening period duration in complete turns (001-opening-period).
    /// Keep in sync with RolePlayEngineService.OpeningPeriodTurnCount.
    /// </summary>
    private const int OpeningPeriodTurnCount = 3;

    /// <summary>
    /// Default opening-period direction used when a scenario has no OpeningGuidanceText
    /// (FR-016 seed text, revised 2026-07-31 per user intent).
    /// </summary>
    private const string DefaultOpeningGuidanceText =
        "Introduce the characters and the scenario — who they are, how they fit into their world, and the situation they are in now — grounded in the character profiles and descriptions. State the marriage as it currently is: a settled, long-established couple with a sex life that matches their stats. When their Desire is high and their Restraint is low, they are sexually active and recently intimate — comfortable with each other's bodies, past courtship and discovery. When their stats are muted, show that instead: a physical life that is routine or subdued. On-screen intimacy is allowed in the opening only when their profiles and current state support it. This is not about them reconnecting or reaching for emotional closeness; their dynamic is already fixed. Sketch their routines, the rhythm of their days, and the setting. Let the potential arcs foreshadow quietly in the background. Keep the focus on the husband and wife; other characters remain in the background.";

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

        // ── Opening period (001-opening-period): inject the dedicated opening direction ──
        // The first 3 turns of a new session establish the husband-wife dynamic, sex-life
        // status quo, and setting. Theme guidance is absent during this window, so this
        // direction replaces the generic phase line (FR-002 / FR-003 / FR-016).
        var isOpeningPeriod = string.Equals(phase, "Opening", StringComparison.OrdinalIgnoreCase)
            && context.Session.AdaptiveState.ObservedTurnCount <= OpeningPeriodTurnCount;

        if (isOpeningPeriod)
        {
            var direction = string.IsNullOrWhiteSpace(context.Scenario.OpeningGuidanceText)
                ? DefaultOpeningGuidanceText
                : context.Scenario.OpeningGuidanceText;
            sb.AppendLine($"HARD CONSTRAINT — Opening Period Direction: {direction}");

            _logger.LogInformation(
                "OpeningPeriodDirectionInjected: SessionId={SessionId} Actor={Actor} Phase={Phase} Turn={Turn} Source={Source}",
                context.Session.Id, context.ActorProfile.ActorName, phase,
                context.Session.AdaptiveState.ObservedTurnCount,
                string.IsNullOrWhiteSpace(context.Scenario.OpeningGuidanceText) ? "default" : "scenario");
        }
        else
        {
            // Phase-appropriate steering direction
            var guidance = GetPhaseGuidance(phase);
            sb.AppendLine($"- Phase: {phase} — {guidance}");
        }

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
