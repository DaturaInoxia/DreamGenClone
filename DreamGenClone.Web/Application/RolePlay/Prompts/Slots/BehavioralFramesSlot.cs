using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 13, Zone C — Behavioral frames: character tendencies filtered by actor.
/// Appears exactly once per FR-019, FR-027. Non-present frames are trimmable for Character variant.
/// Absorbs BehavioralFrameInjector + HusbandAftermathInjector.
/// </summary>
public sealed class BehavioralFramesSlot : IPromptSlot
{
    private readonly ILogger<BehavioralFramesSlot> _logger;

    public PromptSlotId Id => PromptSlotId.BehavioralFrames;
    public PromptZone Zone => PromptZone.C;
    public int Order => 13;
    public bool IsTrimEligible => true; // Non-present frames trimmable

    public BehavioralFramesSlot(ILogger<BehavioralFramesSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context)
    {
        // Only write if there are behavioral frames to display.
        return context.CharacterBehavioralFrames is { Count: > 0 };
    }

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var frames = context.CharacterBehavioralFrames;
        if (frames is null || frames.Count == 0)
            return Task.FromResult(string.Empty);

        var profile = context.ActorProfile;
        var variant = context.Variant;
        var isNarrative = variant == PromptVariant.Narrative || profile.Kind == ActorProfileKind.Narrative;
        var presentIds = new HashSet<string>(profile.PresentCharacterIds, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("Character Behavioral Frames (yields to theme contract):");

        if (isNarrative)
        {
            // Narrative: show all frames.
            foreach (var (characterId, frameText) in frames)
            {
                if (string.IsNullOrWhiteSpace(frameText)) continue;
                sb.AppendLine($"  [{characterId}]: {frameText.Trim()}");
            }
        }
        else
        {
            // Character variant: actor's own frame first (always), then other present characters.
            // Non-present character frames are included but tagged as trimmable.

            // Own frame first.
            if (frames.TryGetValue(profile.ActorName, out var ownFrame) && !string.IsNullOrWhiteSpace(ownFrame))
            {
                sb.AppendLine($"  [{profile.ActorName} — your character]: {ownFrame.Trim()}");
            }

            // Present characters (not self).
            foreach (var (characterId, frameText) in frames)
            {
                if (string.IsNullOrWhiteSpace(frameText)) continue;
                if (string.Equals(characterId, profile.ActorName, StringComparison.OrdinalIgnoreCase)) continue;

                if (presentIds.Contains(characterId))
                {
                    sb.AppendLine($"  [{characterId}]: {frameText.Trim()}");
                }
                else
                {
                    // Non-present: included but flagged for potential trim.
                    sb.AppendLine($"  [{characterId} — not present]: {frameText.Trim()}");
                }
            }
        }

        _logger.LogDebug(
            "BehavioralFramesSlot: SessionId={SessionId} Variant={Variant} FrameCount={FrameCount}",
            context.Session.Id, variant, frames.Count);

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;

        // Trim non-present frames first (lines containing "— not present"), then truncate from end.
        var lines = text.Split('\n');
        var keptLines = new List<string>();
        var nonPresentLines = new List<string>();

        foreach (var line in lines)
        {
            if (line.Contains("— not present"))
                nonPresentLines.Add(line);
            else
                keptLines.Add(line);
        }

        // Start with essential lines.
        var result = string.Join("\n", keptLines);
        if (result.Length <= maxChars) return result;

        // Still over budget — truncate.
        return result[..Math.Max(1, maxChars)];
    }
}
