using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 12, Zone C — Theme contract: active theme + directives + AI guidance + hard constraints + steering rank.
/// Phase guidance has moved to FinalInstructionSlot (Slot 17) as "Scene Direction."
/// Never trimmed.
/// </summary>
public sealed class ThemeContractSlot : IPromptSlot
{
    private readonly ILogger<ThemeContractSlot> _logger;

    public PromptSlotId Id => PromptSlotId.ThemeContract;
    public PromptZone Zone => PromptZone.C;
    public int Order => 12;
    public bool IsTrimEligible => false;

    public ThemeContractSlot(ILogger<ThemeContractSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context) => true;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var theme = context.Theme;
        var sb = new StringBuilder();

        // ── Opening phase: Potential Arcs (session profile themes, no commitment) ──
        if (theme.ActiveTheme is null && theme.AvailableArcLabels is { Count: > 0 })
        {
            sb.AppendLine("Potential Arcs (available narrative directions — none selected yet):");
            foreach (var arc in theme.AvailableArcLabels)
            {
                sb.Append($"  {arc.Label}");
                if (!string.IsNullOrWhiteSpace(arc.Description))
                {
                    sb.Append($" — {arc.Description}");
                }
                sb.AppendLine();
            }
        }

        // Note: Theme Contract, Scene Guidance, and Scene Direction have moved to
        // FinalInstructionSlot (Slot 17) for maximum recency. This slot now only
        // handles Potential Arcs (Opening phase) and AI Guidance Notes / Hard Constraints.

        // ── AI guidance notes ──
        if (theme.AiGuidanceNotes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("AI Guidance Notes:");
            foreach (var note in theme.AiGuidanceNotes)
            {
                if (!string.IsNullOrWhiteSpace(note.Text))
                {
                    var sectionLabel = FormatSection(note.Section);
                    sb.AppendLine($"  [{sectionLabel}] {note.Text.Trim()}");
                }
            }
        }

        // ── Hard constraints ──
        if (theme.HardConstraintLines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Hard Constraints:");
            foreach (var line in theme.HardConstraintLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    sb.AppendLine($"  - {line.Trim()}");
            }
        }

        _logger.LogDebug(
            "ThemeContractSlot: SessionId={SessionId} HasTheme={HasTheme} GuidanceCount={GuidanceCount} DirectiveCount={DirectiveCount}",
            context.Session.Id,
            theme.ActiveTheme is not null,
            theme.PhaseGuidanceLines.Count,
            theme.PhaseDirectiveLines.Count);

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }

    private static string FormatSection(RPThemeAIGuidanceSection section) => section switch
    {
        RPThemeAIGuidanceSection.KeyScenarioElement => "Key Element",
        RPThemeAIGuidanceSection.Avoidance => "Avoid",
        RPThemeAIGuidanceSection.InteractionDynamics => "Dynamics",
        RPThemeAIGuidanceSection.ScenarioDistinction => "Distinction",
        RPThemeAIGuidanceSection.Variation => "Variation",
        RPThemeAIGuidanceSection.FitNote => "Fit Note",
        RPThemeAIGuidanceSection.FitFormula => "Fit Formula",
        RPThemeAIGuidanceSection.FitPattern => "Fit Pattern",
        RPThemeAIGuidanceSection.HardConstraint => "Hard Constraint",
        _ => section.ToString(),
    };
}
