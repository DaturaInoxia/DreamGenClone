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
        if ((context.CharacterBehavioralFrames is { Count: > 0 })
            || (context.CharacterStatStateTexts is { Count: > 0 }))
        {
            return true;
        }

        // B-034: also fire when the merged scenario guidance text carries the unified
        // "Wife Willingness to Cheat" band block (verdict + ceiling lines).
        return ContainsWillingnessBandLines(context.ScenarioGuidanceText);
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

        // ── B-034: Unified "Wife Willingness to Cheat" block ──
        // The merged scenario guidance text (from ScenarioGuidanceGenerator) carries the
        // verdict (Resistance band) + ceiling (Willingness band) lines for the Wife.
        // Render them as an authoritative HARD CONSTRAINT block so the model knows whether
        // she will cross and how far she will go with the other man.
        if (ContainsWillingnessBandLines(context.ScenarioGuidanceText))
        {
            AppendWillingnessBlock(sb, context.ScenarioGuidanceText!, isNarrative);
        }

        if (sb.Length == 0) return Task.FromResult(string.Empty);

        _logger.LogDebug(
            "BehavioralFramesSlot: SessionId={SessionId} Variant={Variant} FrameCount={FrameCount} StatStateCount={StatStateCount}",
            context.Session.Id, variant, frames?.Count ?? 0, statStates?.Count ?? 0);

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    private static bool ContainsWillingnessBandLines(string? scenarioGuidanceText)
    {
        if (string.IsNullOrWhiteSpace(scenarioGuidanceText)) return false;
        return scenarioGuidanceText.Contains("Verdict:", StringComparison.OrdinalIgnoreCase)
            || scenarioGuidanceText.Contains("Ceiling:", StringComparison.OrdinalIgnoreCase)
            || scenarioGuidanceText.Contains("Details:", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendWillingnessBlock(
        StringBuilder sb,
        string scenarioGuidanceText,
        bool isNarrative)
    {
        // B-034: the verdict/ceiling/ladder/details lines are appended space-separated into the
        // merged guidance text, so they are NOT guaranteed to start a line — extract them by
        // marker instead of by line-start. The guidance generator emits (contract §3.3):
        //   "Verdict: NO — …"
        //   "Ceiling: {ExplicitnessLevel} — {PromptGuideline} (Examples: …)"
        //   "Ladder: {b1}, {b2}, …, {ceiling band}"
        //   "Details: Willingness to Cheat = …; Ceiling = min(Desire, willingness) = …."
        // and the factory may append " Emphasize: …" / " Avoid: …" after them.
        var lines = ExtractBandLines(scenarioGuidanceText);

        if (lines.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine("HARD CONSTRAINT — Wife Willingness to Cheat (authoritative, overrides theme guidance):");
        foreach (var line in lines)
        {
            sb.AppendLine($"  {line}");
        }
    }

    /// <summary>
    /// B-034: extracts the "Verdict:", "Ceiling:" and "Details:" sentences from the merged
    /// scenario guidance text wherever they appear (they may sit mid-line after phase guidance
    /// prose), emitted in contract order (Verdict, Ceiling, Details), stopping at the next
    /// marker or the factory's " Emphasize:" / " Avoid:" suffix.
    /// </summary>
    private static List<string> ExtractBandLines(string? scenarioGuidanceText)
    {
        const string verdictMarker = "Verdict:";
        const string ceilingMarker = "Ceiling:";
        const string ladderMarker = "Ladder:";
        const string detailsMarker = "Details:";
        const string emphasizeSuffix = " Emphasize:";
        const string avoidSuffix = " Avoid:";

        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(scenarioGuidanceText)) return result;

        foreach (var marker in new[] { verdictMarker, ceilingMarker, ladderMarker, detailsMarker })
        {
            var start = scenarioGuidanceText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) continue;

            // Find the first terminator after the marker: the other markers or the
            // factory-appended Emphasize/Avoid suffixes.
            var searchFrom = start + marker.Length;
            var end = scenarioGuidanceText.Length;
            foreach (var terminator in new[]
                     {
                         verdictMarker, ceilingMarker, ladderMarker, detailsMarker,
                         emphasizeSuffix, avoidSuffix
                     })
            {
                var idx = scenarioGuidanceText.IndexOf(terminator, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && idx < end) end = idx;
            }

            var line = scenarioGuidanceText[start..end].Trim();
            if (line.Length > 0)
            {
                result.Add(line);
            }
        }

        return result;
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
