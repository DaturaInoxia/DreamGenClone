using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// B-077: Computes willingness-to-cheat gap and derives per-role, per-stat
/// gap-closing event hints from the active theme's semantic stat mappings.
/// Shared by both steer-generation paths (UI inline + background job).
/// </summary>
public static class WillingnessSteerGapResolver
{
    private static readonly HashSet<string> GapTargetStats = new(StringComparer.OrdinalIgnoreCase)
    {
        "Loyalty", "Restraint"
    };

    /// <summary>
    /// Resolves gap-closing steering hints. Returns <c>HasGap=false</c> when:
    /// - settings not enabled or directive is empty (fail-fast per repo rules)
    /// - Wife is not present in snapshots
    /// - current willingness verdict already meets or exceeds the target
    /// </summary>
    public static WillingnessGapSteerResult Resolve(
        IReadOnlyDictionary<string, CharacterStatProfileV2>? snapshots,
        ScenarioEngineSettings settings,
        IReadOnlyList<RPSemanticStatMapping>? semanticStatMappings,
        string targetVerdict = "MAYBE")
    {
        return ResolveCore(
            snapshots,
            settings.WillingnessGapSteeringEnabled,
            settings.WillingnessGapSteeringDirective,
            settings.WillingnessDesireLoyaltyWeight,
            settings.WillingnessBehaviorWeight,
            settings.WillingnessMaritalDeficitWeight,
            settings.WillingnessVerdictNoMax,
            settings.WillingnessVerdictMaybeMax,
            semanticStatMappings,
            targetVerdict);
    }

    /// <summary>
    /// Resolves gap-closing hints using already-snapshot config values
    /// (for the background steer-generation path, which can't read live settings).
    /// </summary>
    public static WillingnessGapSteerResult ResolveFromPayload(
        IReadOnlyDictionary<string, CharacterStatProfileV2>? snapshots,
        bool enabled,
        string directive,
        double desireLoyaltyWeight,
        double behaviorWeight,
        double maritalDeficitWeight,
        int verdictNoMax,
        int verdictMaybeMax,
        IReadOnlyList<RPSemanticStatMapping>? semanticStatMappings,
        string targetVerdict = "MAYBE")
    {
        return ResolveCore(
            snapshots,
            enabled,
            directive,
            desireLoyaltyWeight,
            behaviorWeight,
            maritalDeficitWeight,
            verdictNoMax,
            verdictMaybeMax,
            semanticStatMappings,
            targetVerdict);
    }

    private static WillingnessGapSteerResult ResolveCore(
        IReadOnlyDictionary<string, CharacterStatProfileV2>? snapshots,
        bool enabled,
        string directive,
        double desireLoyaltyWeight,
        double behaviorWeight,
        double maritalDeficitWeight,
        int verdictNoMax,
        int verdictMaybeMax,
        IReadOnlyList<RPSemanticStatMapping>? semanticStatMappings,
        string targetVerdict)
    {
        // Fail-fast: enabled but no directive → should not happen in production.
        if (!enabled || string.IsNullOrWhiteSpace(directive))
        {
            return new WillingnessGapSteerResult { HasGap = false };
        }

        var wife = FindRole(snapshots, "Wife");
        if (wife is null)
        {
            return new WillingnessGapSteerResult { HasGap = false };
        }

        var husband = FindRole(snapshots, "Husband");

        var willingness = WifeWillingnessCalculator.ComputeWillingnessToCheat(
            wife.Desire,
            wife.Loyalty,
            GetEncounterStat(wife, "SeductionReceptivity"),
            GetEncounterStat(wife, "BoundaryFirmness"),
            GetEncounterStat(husband, "Attentiveness"),
            GetEncounterStat(husband, "IntimacyAvailability"),
            desireLoyaltyWeight,
            behaviorWeight,
            maritalDeficitWeight);

        var verdict = WifeWillingnessCalculator.ResolveVerdict(
            willingness, verdictNoMax, verdictMaybeMax);

        var ceiling = WifeWillingnessCalculator.ComputeCeiling(willingness, wife.Desire);

        // Gap check: is current verdict below target?
        if (VerdictRank(verdict) >= VerdictRank(targetVerdict))
        {
            return new WillingnessGapSteerResult
            {
                HasGap = false,
                Willingness = willingness,
                Verdict = verdict,
                Ceiling = ceiling
            };
        }

        // Extract gap-closing hints from semantic mappings.
        // Filter: TargetStat ∈ {Loyalty, Restraint}, Direction = "decrease".
        var gapHints = ExtractGapClosingHints(semanticStatMappings);

        // Determine wife name for the block prose.
        var wifeName = wife.CharacterId;
        // If CharacterId is a GUID-like string, try to find a display name.
        // The snapshots dictionary key is usually the display name.
        if (snapshots is not null)
        {
            foreach (var kvp in snapshots)
            {
                if (kvp.Value == wife || kvp.Value.CharacterId == wife.CharacterId)
                {
                    // Use dictionary key as display name if it's not a GUID.
                    if (!Guid.TryParse(kvp.Key, out _))
                    {
                        wifeName = kvp.Key;
                    }
                    break;
                }
            }
        }

        return new WillingnessGapSteerResult
        {
            HasGap = true,
            Willingness = willingness,
            Verdict = verdict,
            Ceiling = ceiling,
            WifeName = wifeName,
            TargetVerdict = targetVerdict,
            GapClosingHints = gapHints
        };
    }

    /// <summary>
    /// Builds the gap-aware context block text from a resolved result.
    /// Appends the configured directive template (with placeholders filled)
    /// followed by per-role behavioral directives derived from the targeted stats.
    /// </summary>
    public static string BuildGapBlockProse(WillingnessGapSteerResult result, string directiveTemplate)
    {
        if (!result.HasGap || string.IsNullOrWhiteSpace(directiveTemplate))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();

        // Fill directive template.
        var filled = directiveTemplate
            .Replace("{WifeName}", result.WifeName)
            .Replace("{Willingness}", result.Willingness.ToString())
            .Replace("{Verdict}", result.Verdict)
            .Replace("{Ceiling}", result.Ceiling.ToString())
            .Replace("{TargetVerdict}", result.TargetVerdict);

        sb.AppendLine(filled);
        sb.AppendLine($"To close the gap to {result.TargetVerdict}, each character's TOWARDS and HARD options should drive these specific behaviors (the AWAY and NEUTRAL options should do the opposite):");

        // Derive per-role behavioral directives from the targeted stats.
        if (result.GapClosingHints.Count > 0)
        {
            // Collect which stats each role needs to target.
            var roleStats = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var hint in result.GapClosingHints)
            {
                foreach (var eventId in hint.EventIds)
                {
                    var role = MapEventToRole(eventId);
                    if (!roleStats.TryGetValue(role, out var stats))
                    {
                        stats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        roleStats[role] = stats;
                    }
                    stats.Add(hint.StatName);
                }
            }

            foreach (var (role, stats) in roleStats.OrderBy(r => r.Key switch
                     {
                         "Husband" => 0,
                         "OtherMan" => 1,
                         _ => 2
                     }))
            {
                sb.AppendLine($"  - {role}: {DescribeRoleDirective(role, stats)}");
            }
        }

        sb.AppendLine("Each option must be grounded in character state and recent scene context. The TOWARDS and HARD options should actively drive the emotional shifts described above — not just arrange physical proximity.");

        return sb.ToString();
    }

    /// <summary>
    /// Produces a concrete behavioral directive for a role based on which stats
    /// their gap-closing events target. Uses role-specific emotional language to
    /// avoid convergence on generic "leave them alone" patterns.
    /// </summary>
    private static string DescribeRoleDirective(string role, HashSet<string> targetStats)
    {
        var hasLoyalty = targetStats.Contains("Loyalty");
        var hasRestraint = targetStats.Contains("Restraint");

        return role.ToLowerInvariant() switch
        {
            "husband" => (hasLoyalty, hasRestraint) switch
            {
                // Loyalty↓: be emotionally absent, not just physically gone.
                // The harm is in making her feel invisible, not just unsupervised.
                (true, true) => "be emotionally unavailable — dismiss her attempts at connection, prioritize trivial things over her, respond with indifference when she reaches out. Make her feel unseen so her loyalty feels one-sided. Physical absence alone is not enough — show her she does not matter to him.",
                (true, false) => "be emotionally unavailable — dismiss her attempts at connection, respond with indifference, make her feel unseen and unvalued. The bond weakens when she stops expecting anything from him, not just when he is not in the room.",
                _ => "be emotionally unavailable — respond with indifference, make her feel unheard."
            },
            "otherman" => (hasLoyalty, hasRestraint) switch
            {
                // Restraint↓: classic seduction — make her WANT him, not just have access.
                // Use universal tropes that work regardless of the character's occupation.
                (true, true) => "actively seduce her using classic romantic tropes — compliment her genuinely, make her laugh, help her with tasks she struggles with (heavy lifting, reaching, fixing), create small moments where she sees your competence and strength, find reasons for casual physical contact, create situations where she needs your help. Make her feel desired and seen — the seduction should feel natural and earned, not mechanical. ALSO: challenge her loyalty by making her compare how you make her feel to how her husband does not.",
                (false, true) => "actively seduce her — compliment her genuinely, make her laugh, help her with tasks, create moments where your strength or skill is visible (physical labor, fixing things), find natural reasons for casual touch, create 'damsel in distress' moments where she needs you. Make her feel desired and drawn in — the seduction should feel earned, not assumed.",
                _ => "seduce her — compliment, connect, help, attract."
            },
            _ => (hasLoyalty, hasRestraint) switch  // Wife
            {
                // Loyalty↓ + Restraint↓: internal war, not just sneaking.
                // The drama is in her head — guilt, wanting, conflict. 
                (true, true) => "show her internal war — let her waver between guilt and wanting, question whether her marriage gives her what she needs, feel the pull of temptation as something she cannot simply ignore. Her restraint cracks under the weight of her own conflicted desire — this is about what she FEELS, not just what she does.",
                (true, false) => "feel the emotional distance from her husband — question her commitment, wonder if he still sees her, feel the pull away from a bond that no longer holds her. The doubt itself is the movement.",
                (false, true) => "feel her own wanting — let desire surface in her thoughts and words, show her restraint cracking under the weight of wanting more than she allows herself. This is about her internal experience, not just seeking opportunity.",
                _ => "show internal movement — let her feel conflicted, drawn in two directions."
            }
        };
    }

    private static int VerdictRank(string verdict)
        => verdict.ToUpperInvariant() switch
        {
            "NO" => 0,
            "MAYBE" => 1,
            "YES" => 2,
            _ => 0
        };

    private static IReadOnlyList<GapClosingStatHint> ExtractGapClosingHints(
        IReadOnlyList<RPSemanticStatMapping>? mappings)
    {
        if (mappings is null || mappings.Count == 0) return [];

        return mappings
            .Where(m => GapTargetStats.Contains(m.TargetStat)
                        && string.Equals(m.Direction, "decrease", StringComparison.OrdinalIgnoreCase))
            .GroupBy(m => m.TargetStat, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GapClosingStatHint
            {
                StatName = g.Key,
                EventIds = g.Select(m => m.EventId).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            })
            .ToList();
    }

    /// <summary>
    /// Maps a semantic event ID to the role that plausibly produces it.
    /// </summary>
    private static string MapEventToRole(string eventId)
    {
        if (eventId.StartsWith("husband-", StringComparison.OrdinalIgnoreCase))
            return "Husband";

        return eventId switch
        {
            "tension-spike" => "OtherMan",
            "forbidden-touch" => "OtherMan",
            "mutual-engagement" => "OtherMan",
            "focus-shift" => "OtherMan",
            "seduction-push" => "OtherMan",
            "consensual-corruption" => "OtherMan",
            "exclusion-felt" => "OtherMan",
            _ => "Wife"
        };
    }

    private static CharacterStatProfileV2? FindRole(
        IReadOnlyDictionary<string, CharacterStatProfileV2>? snapshots,
        string role)
    {
        if (snapshots is null) return null;
        return snapshots.Values.FirstOrDefault(s =>
            string.Equals(s.CharacterRole, role, StringComparison.OrdinalIgnoreCase));
    }

    private static int GetEncounterStat(CharacterStatProfileV2? profile, string key)
    {
        if (profile?.RuntimeEncounterStats is null) return 50;
        return profile.RuntimeEncounterStats.TryGetValue(key, out var val) ? val : 50;
    }
}

/// <summary>
/// Result of the B-077 willingness-gap steering resolution.
/// </summary>
public sealed class WillingnessGapSteerResult
{
    public bool HasGap { get; init; }
    public int Willingness { get; init; }
    public string Verdict { get; init; } = string.Empty;
    public int Ceiling { get; init; }
    public string WifeName { get; init; } = string.Empty;
    public string TargetVerdict { get; init; } = "MAYBE";

    /// <summary>
    /// Per-stat gap-closing event hints derived from the active theme's
    /// SemanticStatMappings (filtered to Loyalty↓ / Restraint↓ only).
    /// </summary>
    public IReadOnlyList<GapClosingStatHint> GapClosingHints { get; init; } = [];
}

/// <summary>
/// A single stat's gap-closing event hints.
/// </summary>
public sealed class GapClosingStatHint
{
    public string StatName { get; init; } = string.Empty;
    public IReadOnlyList<string> EventIds { get; init; } = [];
}
