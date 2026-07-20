using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 17, Zone C — Consolidated writing instruction: all writing direction in one block.
/// Scene Direction (Character only, when phase active) positioned BEFORE Writing Instruction.
/// Writing Instruction is the absolute last content the model reads (per R1).
/// Never trimmed.
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
        var intensity = context.Intensity;
        var theme = context.Theme;
        var narrativeTone = context.NarrativeTone;

        var sb = new StringBuilder();

        // ── Scene Direction (Character only, before Writing Instruction per R1) ──
        if (!isNarrative && theme.PhaseGuidanceLines.Count > 0)
        {
            sb.AppendLine("Scene Direction:");
            foreach (var line in theme.PhaseGuidanceLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    sb.AppendLine($"  {line.Trim()}");
            }
            sb.AppendLine();
        }

        // ── Writing Instruction (consolidated 9-component block) ──
        sb.AppendLine("Writing Instruction:");

        // 1. Prose Style
        if (!string.IsNullOrWhiteSpace(style.ProfileName))
        {
            sb.Append($"  Prose Style: {style.ProfileName}");
            if (!string.IsNullOrWhiteSpace(style.Description))
                sb.Append($" — {style.Description.Trim()}");
            sb.AppendLine();
        }
        else if (!string.IsNullOrWhiteSpace(style.Description))
        {
            sb.AppendLine($"  Prose Style: {style.Description.Trim()}");
        }

        // 2. Voice
        if (!string.IsNullOrWhiteSpace(style.ProfileDefaultRuleOfThumb))
            sb.AppendLine($"  Voice: {style.ProfileDefaultRuleOfThumb.Trim()}");

        // 3. Tone (+ Register)
        if (!string.IsNullOrWhiteSpace(narrativeTone?.Tone))
        {
            sb.Append($"  Tone: {narrativeTone.Tone.Trim()}");
            if (!string.IsNullOrWhiteSpace(narrativeTone.Register))
                sb.Append($" — {narrativeTone.Register.Trim()}");
            sb.AppendLine();
        }

        // 4. Focus
        if (!string.IsNullOrWhiteSpace(narrativeTone?.Focus))
            sb.AppendLine($"  Focus: {narrativeTone.Focus.Trim()}");

        // 5. Heat Level
        if (!string.IsNullOrWhiteSpace(intensity.ResolvedLabel))
        {
            sb.Append($"  Heat Level: {intensity.ResolvedLabel}");
            if (!string.IsNullOrWhiteSpace(intensity.Description))
                sb.Append($" — {intensity.Description.Trim()}");
            sb.AppendLine();
        }

        // 6. Pacing
        if (intensity.SceneDirection is not null)
        {
            var pacingText = intensity.SceneDirection.Pacing switch
            {
                ScenePacing.Slow => "Slow pace — linger on sensory detail, internal reflection, and atmosphere. Let moments stretch.",
                ScenePacing.Fast => "Fast pace — drive toward the next beat. Keep actions crisp and dialogue forward-moving.",
                _ => "Medium pace — advance the scene naturally, not rushed, not stalled. Let moments breathe without dragging."
            };
            sb.AppendLine($"  Pacing: {pacingText}");
        }

        // 7. POV
        if (isNarrative)
        {
            sb.AppendLine("  POV: Write in third-person omniscient point of view.");
        }
        else
        {
            var actorName = profile.ActorName;
            sb.AppendLine($"  POV: Write in first-person from {actorName}'s point of view.");
        }

        // 8. Immersion (Character only)
        if (!isNarrative && !string.IsNullOrWhiteSpace(style.ImmersionDirective))
            sb.AppendLine($"  Immersion: {style.ImmersionDirective.Trim()}");

        // 9. Word Target
        if (isNarrative)
        {
            sb.AppendLine($"  Word Target: Target {style.NarrativeWordTargetMin}-{style.NarrativeWordTargetMax} words of scene synthesis.");
        }
        else
        {
            sb.AppendLine($"  Word Target: Target {style.WordTargetMin}-{style.WordTargetMax} words.");
        }

        // 10. Action (Character) or Narrative Constraints
        if (isNarrative)
        {
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
        else if (!string.IsNullOrWhiteSpace(style.ActionDirective))
        {
            sb.AppendLine($"  Action: {style.ActionDirective.Trim()}");
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
