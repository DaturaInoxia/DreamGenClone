using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 8, Zone B — Writing style: after consolidation (001-final-writing-instruction),
/// this slot emits only a reference line pointing to Slot 17's Writing Instruction.
/// All writing direction has moved to FinalInstructionSlot (Slot 17).
/// Trimmable under budget pressure.
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
        _logger.LogDebug(
            "WritingStyleSlot: SessionId={SessionId} — writing direction consolidated to Slot 17",
            context.Session.Id);

        return Task.FromResult("Writing direction: see Writing Instruction below.");
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        return text[..Math.Max(1, maxChars)];
    }
}
