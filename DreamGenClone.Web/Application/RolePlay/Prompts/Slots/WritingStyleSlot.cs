using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 8, Zone B — Writing style: timeless description + example always kept,
/// phase Rule-of-Thumb from config, profile default as separate element.
/// Last-resort trimmable (priority 7). Fail-fast on missing phase RoT or profile default.
/// FR-014.
/// </summary>
public sealed class WritingStyleSlot : IPromptSlot
{
    private readonly ILogger<WritingStyleSlot> _logger;

    public PromptSlotId Id => PromptSlotId.WritingStyle;
    public PromptZone Zone => PromptZone.B;
    public int Order => 8;
    public bool IsTrimEligible => true;

    public WritingStyleSlot(ILogger<WritingStyleSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context) => true;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var style = context.WritingStyle;

        // Fail-fast on missing phase Rule-of-Thumb (FR-014).
        if (string.IsNullOrWhiteSpace(style.PhaseRuleOfThumb))
        {
            throw new InvalidOperationException(
                $"MissingPromptConfig: WritingStyle.PhaseRuleOfThumb is missing or empty. " +
                $"Session phase: '{context.Phase}'. FR-014 requires a PhaseRuleOfThumb row for every phase.");
        }

        // Fail-fast on missing profile default (FR-014).
        if (string.IsNullOrWhiteSpace(style.ProfileDefaultRuleOfThumb))
        {
            throw new InvalidOperationException(
                "MissingPromptConfig: WritingStyle.ProfileDefaultRuleOfThumb is missing or empty. FR-014 requires a profile default Rule-of-Thumb.");
        }

        var sb = new StringBuilder();
        sb.AppendLine("Writing Style:");

        // ── Timeless description (always kept) ──
        if (!string.IsNullOrWhiteSpace(style.Description))
        {
            sb.AppendLine($"  {style.Description.Trim()}");
        }

        // ── Timeless example (always kept) ──
        if (!string.IsNullOrWhiteSpace(style.Example))
        {
            sb.AppendLine($"  Example: {style.Example.Trim()}");
        }

        // ── Phase Rule-of-Thumb (trimmed only under extreme pressure) ──
        sb.AppendLine($"  Phase Rule of Thumb: {style.PhaseRuleOfThumb.Trim()}");

        // ── Profile default (separate element) ──
        sb.AppendLine($"  Profile Default: {style.ProfileDefaultRuleOfThumb.Trim()}");

        // ── Style hint ──
        if (!string.IsNullOrWhiteSpace(style.StyleHint))
        {
            sb.AppendLine($"  Style Hint: {style.StyleHint.Trim()}");
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        // Priority: keep timeless description + example, drop phase RoT first, then profile default.
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        var remaining = maxChars;

        // Phase 1: include timeless elements (description + example + header).
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("  Phase Rule of Thumb:") || trimmed.StartsWith("  Profile Default:"))
                continue; // Skip trimmables first.

            if (remaining <= 0)
                break;

            if (trimmed.Length + Environment.NewLine.Length <= remaining)
            {
                sb.AppendLine(trimmed);
                remaining -= trimmed.Length + Environment.NewLine.Length;
            }
        }

        // Phase 2: include profile default if room.
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith("  Profile Default:"))
                continue;

            if (remaining <= 0)
                break;

            if (trimmed.Length + Environment.NewLine.Length <= remaining)
            {
                sb.AppendLine(trimmed);
                remaining -= trimmed.Length + Environment.NewLine.Length;
            }
        }

        // Phase 3: include phase RoT (last resort) if room.
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith("  Phase Rule of Thumb:"))
                continue;

            if (remaining <= 0)
                break;

            if (trimmed.Length + Environment.NewLine.Length <= remaining)
            {
                sb.AppendLine(trimmed);
            }
            // Don't update remaining — last element.
        }

        var result = sb.ToString().TrimEnd();
        return string.IsNullOrEmpty(result) ? text[..Math.Min(maxChars, text.Length)] : result;
    }
}
