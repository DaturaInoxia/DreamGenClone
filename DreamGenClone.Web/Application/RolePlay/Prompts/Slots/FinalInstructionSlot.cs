using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 17, Zone C — Final writing instruction: POV, word target, variant constraints.
/// Last content before generation per FR-023. Never trimmed.
/// Absorbs FinalDirectiveInjector.
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

        var sb = new StringBuilder();

        if (isNarrative)
        {
            // ── Narrative variant: third-person omniscient scene synthesis ──
            // Narrative summarizes what characters already wrote in this turn — it does NOT
            // invent new events, advance the plot independently, or introduce unplayed character
            // actions. Characters drive the story; Narrative synthesizes their interactions
            // into a unified third-person scene description.
            sb.AppendLine("Writing Instruction:");
            sb.AppendLine("  Write in third-person omniscient point of view.");
            sb.AppendLine("  HARD CONSTRAINT: Zero dialogue. No character speech, no thoughts quoted, no inner monologue.");
            sb.AppendLine("  Synthesize only what the characters have already expressed in this turn.");
            sb.AppendLine("  Do not introduce new events, advance the plot, or have characters take new actions.");
            sb.AppendLine("  Target 200-400 words of scene synthesis.");
            sb.AppendLine();
            sb.AppendLine("  Physical Detail Checklist (MUST cover from what was described):");
            sb.AppendLine("    - Body positions and spatial arrangement");
            sb.AppendLine("    - Physical contact points and pressure");
            sb.AppendLine("    - Sensory details (touch, smell, sound, taste)");
            sb.AppendLine("    - Rhythm and pacing of movement");
            sb.AppendLine("    - Environmental atmosphere and ambient details");
        }
        else
        {
            // ── Phase guidance directive (moved from ThemeContractSlot for recency priority) ──
            // Characters get the phase directive to drive story progression.
            // Narrative does NOT get this — it summarizes, characters advance the story.
            var theme = context.Theme;
            if (theme.PhaseGuidanceLines.Count > 0)
            {
                sb.AppendLine("Phase Directive (incorporate into this response):");
                foreach (var line in theme.PhaseGuidanceLines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        sb.AppendLine($"  {line.Trim()}");
                }
                sb.AppendLine();
            }

            // ── Character variant: first-person POV ──
            var actorName = profile.ActorName;
            sb.AppendLine("Writing Instruction:");
            sb.AppendLine($"  Write in first-person from {actorName}'s point of view.");
            sb.AppendLine("  Stay inside this character's perceptions, thoughts, feelings, and physical sensations.");
            sb.AppendLine("  Target 200-400 words.");
            sb.AppendLine("  Respond to the scene naturally.");
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
