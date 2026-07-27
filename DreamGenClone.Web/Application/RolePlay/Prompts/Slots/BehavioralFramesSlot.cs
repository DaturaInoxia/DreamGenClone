using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 13, Zone C — Behavioral frames + stat state texts: character tendencies
/// and current state filtered by actor. Appears exactly once per FR-019, FR-027.
/// Non-present frames are trimmable for Character variant.
/// Absorbs BehavioralFrameInjector + HusbandAftermathInjector.
/// Stat state texts (from CharacterStatTextCatalog) are injected alongside behavioral frames
/// as "current state" companion blocks.
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
        // Fire if either behavioral frames or stat state texts are available.
        return (context.CharacterBehavioralFrames is { Count: > 0 })
            || (context.CharacterStatStateTexts is { Count: > 0 });
    }

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var frames = context.CharacterBehavioralFrames;
        var statStates = context.CharacterStatStateTexts;

        var profile = context.ActorProfile;
        var variant = context.Variant;
        var isNarrative = variant == PromptVariant.Narrative || profile.Kind == ActorProfileKind.Narrative;
        var presentIds = new HashSet<string>(profile.PresentCharacterIds, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("Character Behavioral Frames:");

        // ── Behavioral frames ──
        if (frames is { Count: > 0 })
        {
            AppendFrames(sb, frames, profile, presentIds, isNarrative);
        }

        // ── Stat state texts (current state from runtime stats) ──
        if (statStates is { Count: > 0 })
        {
            AppendStatStates(sb, statStates, profile, presentIds, isNarrative);
        }

        if (sb.Length == 0) return Task.FromResult(string.Empty);

        _logger.LogDebug(
            "BehavioralFramesSlot: SessionId={SessionId} Variant={Variant} FrameCount={FrameCount} StatStateCount={StatStateCount}",
            context.Session.Id, variant, frames?.Count ?? 0, statStates?.Count ?? 0);

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    private static void AppendFrames(
        StringBuilder sb,
        IReadOnlyDictionary<string, string> frames,
        ActorProfile profile,
        HashSet<string> presentIds,
        bool isNarrative)
    {
        if (isNarrative)
        {
            foreach (var (characterId, frameText) in frames)
            {
                if (string.IsNullOrWhiteSpace(frameText)) continue;
                sb.AppendLine($"  [{characterId}]:");
                sb.AppendLine(frameText);
            }
        }
        else
        {
            // Character variant: actor's own frame first, then other present characters.
            if (frames.TryGetValue(profile.ActorName, out var ownFrame) && !string.IsNullOrWhiteSpace(ownFrame))
            {
                sb.AppendLine($"  [{profile.ActorName} — your character]:");
                sb.AppendLine(ownFrame);
            }

            foreach (var (characterId, frameText) in frames)
            {
                if (string.IsNullOrWhiteSpace(frameText)) continue;
                if (string.Equals(characterId, profile.ActorName, StringComparison.OrdinalIgnoreCase)) continue;

                if (presentIds.Contains(characterId))
                {
                    sb.AppendLine($"  [{characterId}]:");
                    sb.AppendLine(frameText);
                }
                else
                {
                    sb.AppendLine($"  [{characterId} — not present]:");
                    sb.AppendLine(frameText);
                }
            }
        }
    }

    private static void AppendStatStates(
        StringBuilder sb,
        IReadOnlyDictionary<string, string> statStates,
        ActorProfile profile,
        HashSet<string> presentIds,
        bool isNarrative)
    {
        if (isNarrative)
        {
            foreach (var (characterLabel, stateText) in statStates)
            {
                if (string.IsNullOrWhiteSpace(stateText)) continue;
                sb.AppendLine($"  [{characterLabel} current state]:");
                sb.AppendLine(stateText);
            }
        }
        else
        {
            // Actor's own state first.
            if (statStates.TryGetValue(profile.ActorName, out var ownState) && !string.IsNullOrWhiteSpace(ownState))
            {
                sb.AppendLine($"  [{profile.ActorName} — your current state]:");
                sb.AppendLine(ownState);
            }

            foreach (var (characterLabel, stateText) in statStates)
            {
                if (string.IsNullOrWhiteSpace(stateText)) continue;
                if (string.Equals(characterLabel, profile.ActorName, StringComparison.OrdinalIgnoreCase)) continue;

                if (presentIds.Contains(characterLabel))
                {
                    sb.AppendLine($"  [{characterLabel} current state]:");
                    sb.AppendLine(stateText);
                }
                else
                {
                    sb.AppendLine($"  [{characterLabel} — not present, current state]:");
                    sb.AppendLine(stateText);
                }
            }
        }
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
