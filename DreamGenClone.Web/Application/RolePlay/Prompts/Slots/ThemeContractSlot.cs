using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 12, Zone C — Theme contract: active theme + phase guidance + directives + steering rank.
/// Appears exactly once per FR-018, FR-027. Never trimmed.
/// Absorbs ThemeContractInjector + ThemeAIGuidanceInjector.
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

        // ── Active theme header ──
        if (theme.ActiveTheme is not null)
        {
            sb.AppendLine($"Theme Contract: {theme.ActiveTheme.Label}");
            if (!string.IsNullOrWhiteSpace(theme.ActiveTheme.Description))
            {
                sb.AppendLine(theme.ActiveTheme.Description.Trim());
            }
        }

        // ── Phase guidance prose ──
        // MOVED: Phase guidance now appears in FinalInstructionSlot (Slot 17) right before
        // the writing instruction, giving it maximum recency priority. The model reads it
        // last, making it the most influential directive for what should happen next.
        // To restore here: uncomment the block below and remove from FinalInstructionSlot.
        /*
        if (theme.PhaseGuidanceLines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Phase Guidance:");
            foreach (var line in theme.PhaseGuidanceLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    sb.AppendLine($"  {line.Trim()}");
            }
        }
        */

        // ── Theme directives ──
        if (theme.PhaseDirectiveLines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Theme Directives:");
            foreach (var line in theme.PhaseDirectiveLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    sb.AppendLine($"  {line.Trim()}");
            }
        }

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
