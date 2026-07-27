using System.Text;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 17, Zone C — Operational directive: only Narrative variant emits the Action
/// HARD CONSTRAINT block (zero dialogue, physical detail checklist). Character variant
/// suppresses Action entirely — UserDirection (Slot 16) is the sole operational
/// instruction for character actors. Never trimmed.
/// </summary>
public sealed class FinalInstructionSlot : IPromptSlot
{
    private readonly ILogger<FinalInstructionSlot> _logger;

    public PromptSlotId Id => PromptSlotId.FinalInstruction;
    public PromptZone Zone => PromptZone.C;
    public int Order => 17;
    public bool IsTrimEligible => false;

    public FinalInstructionSlot(ILogger<FinalInstructionSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context) => true;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var variant = context.Variant;
        var profile = context.ActorProfile;
        var isNarrative = variant == PromptVariant.Narrative || profile.Kind == ActorProfileKind.Narrative;
        var style = context.WritingStyle;
        var theme = context.Theme;

        var sb = new StringBuilder();

        // ── Context blocks (Character only) ──

        // Theme Contract — what theme is active
        if (!isNarrative && theme.ActiveTheme is not null)
        {
            sb.AppendLine($"Theme Contract: {theme.ActiveTheme.Label}");
            if (!string.IsNullOrWhiteSpace(theme.ActiveTheme.Description))
                sb.AppendLine($"  {theme.ActiveTheme.Description.Trim()}");
            sb.AppendLine();
        }

        // Scene Guidance — longer prose about what this phase should accomplish
        if (!isNarrative && theme.PhaseGuidanceLines.Count > 0)
        {
            sb.AppendLine("Scene Guidance:");
            foreach (var line in theme.PhaseGuidanceLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    sb.AppendLine($"  {line.Trim()}");
            }
            sb.AppendLine();
        }

        // Scene Direction — short explicit beats to hit this turn
        if (!isNarrative && theme.PhaseDirectiveLines.Count > 0)
        {
            sb.AppendLine("Scene Direction:");
            foreach (var line in theme.PhaseDirectiveLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    sb.AppendLine($"  {line.Trim()}");
            }
            sb.AppendLine();
        }

        // ── Operational directive ──
        // Action is suppressed for Character variant — UserDirection is the
        // sole operational instruction for character actors. Only Narrative
        // variant emits the HARD CONSTRAINT block.
        if (isNarrative)
        {
            sb.AppendLine("Action:");
            sb.AppendLine("  HARD CONSTRAINT: Zero dialogue. No character speech, no thoughts quoted, no inner monologue.");
            sb.AppendLine("  Synthesize only what the characters have already expressed in this turn.");
            sb.AppendLine("  Do not introduce new events, advance the plot, or have characters take new actions.");
            sb.AppendLine();
            sb.AppendLine("  Physical Detail Checklist (MUST cover from what was described):");
            sb.AppendLine("    - Body positions and spatial arrangement");
            sb.AppendLine("    - Physical contact points and pressure");
            sb.AppendLine("    - Sensory details (touch, smell, sound, taste)");
            sb.AppendLine("    - Rhythm and pacing of movement");
            sb.AppendLine("    - Environmental atmosphere and ambient details");
        }

        _logger.LogDebug(
            "FinalInstructionSlot: SessionId={SessionId} Variant={Variant} Actor={Actor}",
            context.Session.Id, variant, profile.ActorName);

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }
}
