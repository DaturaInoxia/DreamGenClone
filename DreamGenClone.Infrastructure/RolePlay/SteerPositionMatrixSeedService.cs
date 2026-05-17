using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class SteerPositionMatrixSeedService
{
    private static readonly SeedRow[] SeedRows =
    [
        // Desire 60-100 / SelfRespect 60-100
        new("60-100", "60-100", "Low", "Any", "Missionary,Spooning", "Cowgirl,Lotus", "Mutual, she suggests"),
        new("60-100", "60-100", "Medium", "Any", "Cowgirl,Lotus", "Reverse Cowgirl,Missionary", "She takes lead, enthusiastic"),
        new("60-100", "60-100", "High", "Any", "Cowgirl,Reverse Cowgirl", "Side-by-Side (Face to Face)", "She's in control, adventurous"),
        new("60-100", "60-100", "Any", "Low", "Missionary,Lotus", "Spooning,Scissors", "Gentle, collaborative"),
        new("60-100", "60-100", "Any", "Medium", "Missionary,Doggy Style", "Lotus,Cowgirl", "Mixed, equal participation"),
        new("60-100", "60-100", "Any", "High", "Doggy Style,Missionary", "Cowgirl,Face-Sitting", "He directs, she's willing"),

        // Desire 60-100 / SelfRespect 30-59
        new("60-100", "30-59", "Low", "Any", "Missionary,Doggy Style", "Cowgirl,Standing", "She's eager, suggests"),
        new("60-100", "30-59", "Medium", "Any", "Doggy Style,Cowgirl", "Reverse Cowgirl,Missionary", "Enthusiastic, she asks"),
        new("60-100", "30-59", "High", "Any", "Cowgirl,Reverse Cowgirl", "Doggy Style,Face-Sitting", "She wants to please"),
        new("60-100", "30-59", "Any", "Low", "Missionary,Spooning", "Doggy Style,Lotus", "She welcomes guidance"),
        new("60-100", "30-59", "Any", "Medium", "Doggy Style,Missionary", "Cowgirl,Face-Sitting", "She enjoys direction"),
        new("60-100", "30-59", "Any", "High", "Doggy Style,Face-Sitting", "Cowgirl,Reverse Cowgirl", "He decides, she's into it"),

        // Desire 60-100 / SelfRespect 0-29
        new("60-100", "0-29", "Low", "Any", "Doggy Style,Face-Sitting", "Reverse Cowgirl,Standing", "Submissive, she begs"),
        new("60-100", "0-29", "Medium", "Any", "Face-Sitting,Doggy Style", "Reverse Cowgirl,Cowgirl", "She pleases, desperate"),
        new("60-100", "0-29", "High", "Any", "Face-Sitting,Reverse Cowgirl", "Doggy Style,Piledriver", "She'll do anything"),
        new("60-100", "0-29", "Any", "Low", "Doggy Style,Spooning", "Missionary,Standing", "She accepts, submissive"),
        new("60-100", "0-29", "Any", "Medium", "Doggy Style,Face-Sitting", "Missionary,Cowgirl", "He guides, she follows"),
        new("60-100", "0-29", "Any", "High", "Face-Sitting,Doggy Style", "Reverse Cowgirl,Piledriver", "He commands, she obeys"),

        // Desire 30-59 / SelfRespect 60-100
        new("30-59", "60-100", "Low", "Any", "Missionary,Spooning", "Lotus,Standing", "Reluctant but willing"),
        new("30-59", "60-100", "Medium", "Any", "Missionary,Lotus", "Spooning,Scissors", "Comfortable, she agrees"),
        new("30-59", "60-100", "High", "Any", "Cowgirl,Missionary", "Lotus,Spooning", "She's okay with it"),
        new("30-59", "60-100", "Any", "Low", "Missionary,Spooning", "Lotus,Standing", "She's hesitant but cooperative"),
        new("30-59", "60-100", "Any", "Medium", "Missionary,Lotus", "Spooning,Doggy Style", "She goes along"),
        new("30-59", "60-100", "Any", "High", "Missionary,Doggy Style", "Lotus,Cowgirl", "She agrees to his direction"),

        // Desire 30-59 / SelfRespect 30-59
        new("30-59", "30-59", "Low", "Any", "Missionary,Doggy Style", "Spooning,Standing", "Cooperative"),
        new("30-59", "30-59", "Medium", "Any", "Missionary,Doggy Style", "Cowgirl,Spooning", "Willing, she accepts"),
        new("30-59", "30-59", "High", "Any", "Cowgirl,Doggy Style", "Missionary,Lotus", "She's into it"),
        new("30-59", "30-59", "Any", "Low", "Missionary,Spooning", "Doggy Style,Lotus", "She accommodates"),
        new("30-59", "30-59", "Any", "Medium", "Doggy Style,Missionary", "Cowgirl,Spooning", "He suggests, she agrees"),
        new("30-59", "30-59", "Any", "High", "Doggy Style,Cowgirl", "Missionary,Face-Sitting", "He decides, she accepts"),

        // Desire 30-59 / SelfRespect 0-29
        new("30-59", "0-29", "Low", "Any", "Doggy Style,Face-Sitting", "Cowgirl,Standing", "Resigned, submissive"),
        new("30-59", "0-29", "Medium", "Any", "Doggy Style,Cowgirl", "Face-Sitting,Reverse Cowgirl", "She'll do what he wants"),
        new("30-59", "0-29", "High", "Any", "Face-Sitting,Cowgirl", "Doggy Style,Reverse Cowgirl", "She wants to please him"),
        new("30-59", "0-29", "Any", "Low", "Doggy Style,Spooning", "Missionary,Standing", "She has no say"),
        new("30-59", "0-29", "Any", "Medium", "Doggy Style,Face-Sitting", "Cowgirl,Missionary", "He directs, she complies"),
        new("30-59", "0-29", "Any", "High", "Face-Sitting,Doggy Style", "Reverse Cowgirl,Piledriver", "He takes control"),

        // Desire 0-29 / SelfRespect 60-100
        new("0-29", "60-100", "Low", "Any", "Missionary,Spooning", "Lotus,Scissors", "Very reluctant"),
        new("0-29", "60-100", "Medium", "Any", "Missionary,Lotus", "Spooning,Scissors", "Uncomfortable, resists"),
        new("0-29", "60-100", "High", "Any", "Missionary,Lotus", "Spooning", "She needs to be convinced"),
        new("0-29", "60-100", "Any", "Low", "Missionary,Spooning", "Lotus,Scissors", "She tries to avoid"),
        new("0-29", "60-100", "Any", "Medium", "Missionary,Spooning", "Lotus,Doggy Style", "She resists, uncomfortable"),
        new("0-29", "60-100", "Any", "High", "Missionary,Lotus", "Doggy Style", "She's uncomfortable, nervous"),

        // Desire 0-29 / SelfRespect 30-59
        new("0-29", "30-59", "Low", "Any", "Missionary,Spooning", "Lotus,Standing", "Not really into it"),
        new("0-29", "30-59", "Medium", "Any", "Missionary,Lotus", "Spooning,Scissors", "Going through motions"),
        new("0-29", "30-59", "High", "Any", "Missionary,Cowgirl", "Lotus,Spooning", "She's not enthusiastic"),
        new("0-29", "30-59", "Any", "Low", "Missionary,Spooning", "Lotus,Standing", "Passive, reluctant"),
        new("0-29", "30-59", "Any", "Medium", "Missionary,Doggy Style", "Lotus,Spooning", "She lets him lead, uninterested"),
        new("0-29", "30-59", "Any", "High", "Doggy Style,Missionary", "Cowgirl,Lotus", "He pushes, she doesn't fight"),

        // Desire 0-29 / SelfRespect 0-29
        new("0-29", "0-29", "Low", "Any", "Doggy Style,Face-Sitting", "Cowgirl,Piledriver", "Broken, no resistance"),
        new("0-29", "0-29", "Medium", "Any", "Doggy Style,Face-Sitting", "Reverse Cowgirl,Piledriver", "She doesn't care"),
        new("0-29", "0-29", "High", "Any", "Face-Sitting,Reverse Cowgirl", "Doggy Style,Piledriver", "Fully submissive"),
        new("0-29", "0-29", "Any", "Low", "Doggy Style,Spooning", "Missionary,Standing", "No agency"),
        new("0-29", "0-29", "Any", "Medium", "Doggy Style,Face-Sitting", "Cowgirl,Missionary", "He uses her"),
        new("0-29", "0-29", "Any", "High", "Face-Sitting,Doggy Style", "Reverse Cowgirl,Piledriver", "She's fully controlled")
    ];

    private readonly IRPThemeService _rpThemeService;
    private readonly ILogger<SteerPositionMatrixSeedService> _logger;

    public SteerPositionMatrixSeedService(IRPThemeService rpThemeService, ILogger<SteerPositionMatrixSeedService> logger)
    {
        _rpThemeService = rpThemeService;
        _logger = logger;
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var profileId = IRPThemeService.GlobalThemeLibraryProfileId;
        var profile = await _rpThemeService.GetProfileAsync(profileId, cancellationToken);
        if (profile is null)
        {
            await _rpThemeService.SaveProfileAsync(new RPThemeProfile
            {
                Id = profileId,
                Name = "Global Theme Library",
                Description = "Shared RP theme definitions used across profiles.",
                IsDefault = false
            }, cancellationToken);
        }

        var existing = await _rpThemeService.ListSteerPositionMatrixRowsAsync(cancellationToken);
        if (existing.Count > 0)
        {
            // Migrate any rows that still store plain text names instead of position IDs
            await MigrateTextPositionsToIdsAsync(existing, cancellationToken);
            _logger.LogInformation("Steer position matrix seed skipped: {Count} base rows already present.", existing.Count);
            return;
        }

        var sortOrder = 0;
        foreach (var seed in SeedRows)
        {
            await _rpThemeService.SaveSteerPositionMatrixRowAsync(new RPSteerPositionMatrixRow
            {
                DesireBand = seed.DesireBand,
                SelfRespectBand = seed.SelfRespectBand,
                WifeDominanceBand = seed.WifeDominanceBand,
                OtherManDominanceBand = seed.OtherManDominanceBand,
                PrimaryPositions = ParseCsv(seed.PrimaryPositionsCsv),
                SecondaryPositions = ParseCsv(seed.SecondaryPositionsCsv),
                ExcludedPositions = [],
                WifeBehaviorModifier = seed.Behavior,
                OtherManBehaviorModifier = BuildOtherManBehavior(seed.WifeDominanceBand, seed.OtherManDominanceBand),
                TransitionInstruction = "Respect transition complexity from the current position; add explicit repositioning beats when needed.",
                SortOrder = sortOrder++,
                IsEnabled = true
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded steer position matrix defaults from v2 spec: {Count} base rows.", SeedRows.Length);
    }

    private static string BuildOtherManBehavior(string wifeBand, string otherBand)
    {
        if (!string.Equals(otherBand, "Any", StringComparison.OrdinalIgnoreCase))
        {
            return otherBand switch
            {
                "Low" => "He asks and guides gently.",
                "Medium" => "He leads with balanced direction.",
                _ => "He commands the pacing and positioning."
            };
        }

        return wifeBand switch
        {
            "Low" => "He stays collaborative and responsive.",
            "Medium" => "He alternates leading and following cues.",
            _ => "He follows her lead and adapts quickly."
        };
    }

    /// <summary>
    /// One-time migration: if existing matrix rows still have plain text names (not GUIDs) in their
    /// position lists, look up the matching <see cref="RPPosition"/> record and replace the name with
    /// the position's Id. Rows that already contain only valid IDs are left untouched.
    /// </summary>
    private async Task MigrateTextPositionsToIdsAsync(
        IReadOnlyList<RPSteerPositionMatrixRow> rows,
        CancellationToken cancellationToken)
    {
        var positions = await _rpThemeService.ListPositionsAsync(cancellationToken);
        if (positions.Count == 0) return;

        // Build a name→Id lookup (case-insensitive)
        var nameToId = positions.ToDictionary(
            p => p.Name,
            p => p.Id,
            StringComparer.OrdinalIgnoreCase);

        // Build a set of known IDs so we can detect already-migrated values
        var knownIds = positions.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var migratedCount = 0;
        foreach (var row in rows)
        {
            var updatedPrimary = MigrateList(row.PrimaryPositions, nameToId, knownIds);
            var updatedSecondary = MigrateList(row.SecondaryPositions, nameToId, knownIds);
            var updatedExcluded = MigrateList(row.ExcludedPositions, nameToId, knownIds);

            // Only save if something actually changed
            if (!ListsEqual(row.PrimaryPositions, updatedPrimary)
                || !ListsEqual(row.SecondaryPositions, updatedSecondary)
                || !ListsEqual(row.ExcludedPositions, updatedExcluded))
            {
                row.PrimaryPositions = updatedPrimary;
                row.SecondaryPositions = updatedSecondary;
                row.ExcludedPositions = updatedExcluded;
                await _rpThemeService.SaveSteerPositionMatrixRowAsync(row, cancellationToken);
                migratedCount++;
            }
        }

        if (migratedCount > 0)
            _logger.LogInformation("Migrated {Count} steer position matrix rows from text names to position IDs.", migratedCount);
    }

    private static List<string> MigrateList(
        List<string> values,
        Dictionary<string, string> nameToId,
        HashSet<string> knownIds)
    {
        // Aliases for names used in old seed data that differ from current catalog names.
        // Key = old name (lower-case), Value = current catalog name.
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["doggy"] = "Doggy Style",
            ["face-to-face sitting"] = "Side-by-Side (Face to Face)",
        };

        if (values.Count == 0) return values;
        return values
            .Select(v =>
            {
                if (knownIds.Contains(v)) return v; // already an ID
                // Try exact name match first, then alias resolution
                if (nameToId.TryGetValue(v, out var id)) return id;
                if (aliases.TryGetValue(v, out var canonicalName) && nameToId.TryGetValue(canonicalName, out var aliasId)) return aliasId;
                return v; // keep as-is if no match
            })
            .ToList();
    }

    private static bool ListsEqual(List<string> a, List<string> b)
        => a.Count == b.Count && a.SequenceEqual(b);

    private static List<string> ParseCsv(string csv)
        => string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

    private sealed record SeedRow(
        string DesireBand,
        string SelfRespectBand,
        string WifeDominanceBand,
        string OtherManDominanceBand,
        string PrimaryPositionsCsv,
        string SecondaryPositionsCsv,
        string Behavior);
}
