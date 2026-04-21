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
        new("60-100", "60-100", "High", "Any", "Cowgirl,Reverse Cowgirl", "Face-to-face sitting", "She's in control, adventurous"),
        new("60-100", "60-100", "Any", "Low", "Missionary,Lotus", "Spooning,Scissors", "Gentle, collaborative"),
        new("60-100", "60-100", "Any", "Medium", "Missionary,Doggy", "Lotus,Cowgirl", "Mixed, equal participation"),
        new("60-100", "60-100", "Any", "High", "Doggy,Missionary", "Cowgirl,Face-sitting", "He directs, she's willing"),

        // Desire 60-100 / SelfRespect 30-59
        new("60-100", "30-59", "Low", "Any", "Missionary,Doggy", "Cowgirl,Standing", "She's eager, suggests"),
        new("60-100", "30-59", "Medium", "Any", "Doggy,Cowgirl", "Reverse Cowgirl,Missionary", "Enthusiastic, she asks"),
        new("60-100", "30-59", "High", "Any", "Cowgirl,Reverse Cowgirl", "Doggy,Face-sitting", "She wants to please"),
        new("60-100", "30-59", "Any", "Low", "Missionary,Spooning", "Doggy,Lotus", "She welcomes guidance"),
        new("60-100", "30-59", "Any", "Medium", "Doggy,Missionary", "Cowgirl,Face-sitting", "She enjoys direction"),
        new("60-100", "30-59", "Any", "High", "Doggy,Face-sitting", "Cowgirl,Reverse Cowgirl", "He decides, she's into it"),

        // Desire 60-100 / SelfRespect 0-29
        new("60-100", "0-29", "Low", "Any", "Doggy,Face-sitting", "Reverse Cowgirl,Standing", "Submissive, she begs"),
        new("60-100", "0-29", "Medium", "Any", "Face-sitting,Doggy", "Reverse Cowgirl,Cowgirl", "She pleases, desperate"),
        new("60-100", "0-29", "High", "Any", "Face-sitting,Reverse Cowgirl", "Doggy,Piledriver", "She'll do anything"),
        new("60-100", "0-29", "Any", "Low", "Doggy,Spooning", "Missionary,Standing", "She accepts, submissive"),
        new("60-100", "0-29", "Any", "Medium", "Doggy,Face-sitting", "Missionary,Cowgirl", "He guides, she follows"),
        new("60-100", "0-29", "Any", "High", "Face-sitting,Doggy", "Reverse Cowgirl,Piledriver", "He commands, she obeys"),

        // Desire 30-59 / SelfRespect 60-100
        new("30-59", "60-100", "Low", "Any", "Missionary,Spooning", "Lotus,Standing", "Reluctant but willing"),
        new("30-59", "60-100", "Medium", "Any", "Missionary,Lotus", "Spooning,Scissors", "Comfortable, she agrees"),
        new("30-59", "60-100", "High", "Any", "Cowgirl,Missionary", "Lotus,Spooning", "She's okay with it"),
        new("30-59", "60-100", "Any", "Low", "Missionary,Spooning", "Lotus,Standing", "She's hesitant but cooperative"),
        new("30-59", "60-100", "Any", "Medium", "Missionary,Lotus", "Spooning,Doggy", "She goes along"),
        new("30-59", "60-100", "Any", "High", "Missionary,Doggy", "Lotus,Cowgirl", "She agrees to his direction"),

        // Desire 30-59 / SelfRespect 30-59
        new("30-59", "30-59", "Low", "Any", "Missionary,Doggy", "Spooning,Standing", "Cooperative"),
        new("30-59", "30-59", "Medium", "Any", "Missionary,Doggy", "Cowgirl,Spooning", "Willing, she accepts"),
        new("30-59", "30-59", "High", "Any", "Cowgirl,Doggy", "Missionary,Lotus", "She's into it"),
        new("30-59", "30-59", "Any", "Low", "Missionary,Spooning", "Doggy,Lotus", "She accommodates"),
        new("30-59", "30-59", "Any", "Medium", "Doggy,Missionary", "Cowgirl,Spooning", "He suggests, she agrees"),
        new("30-59", "30-59", "Any", "High", "Doggy,Cowgirl", "Missionary,Face-sitting", "He decides, she accepts"),

        // Desire 30-59 / SelfRespect 0-29
        new("30-59", "0-29", "Low", "Any", "Doggy,Face-sitting", "Cowgirl,Standing", "Resigned, submissive"),
        new("30-59", "0-29", "Medium", "Any", "Doggy,Cowgirl", "Face-sitting,Reverse Cowgirl", "She'll do what he wants"),
        new("30-59", "0-29", "High", "Any", "Face-sitting,Cowgirl", "Doggy,Reverse Cowgirl", "She wants to please him"),
        new("30-59", "0-29", "Any", "Low", "Doggy,Spooning", "Missionary,Standing", "She has no say"),
        new("30-59", "0-29", "Any", "Medium", "Doggy,Face-sitting", "Cowgirl,Missionary", "He directs, she complies"),
        new("30-59", "0-29", "Any", "High", "Face-sitting,Doggy", "Reverse Cowgirl,Piledriver", "He takes control"),

        // Desire 0-29 / SelfRespect 60-100
        new("0-29", "60-100", "Low", "Any", "Missionary,Spooning", "Lotus,Scissors", "Very reluctant"),
        new("0-29", "60-100", "Medium", "Any", "Missionary,Lotus", "Spooning,Scissors", "Uncomfortable, resists"),
        new("0-29", "60-100", "High", "Any", "Missionary,Lotus", "Spooning", "She needs to be convinced"),
        new("0-29", "60-100", "Any", "Low", "Missionary,Spooning", "Lotus,Scissors", "She tries to avoid"),
        new("0-29", "60-100", "Any", "Medium", "Missionary,Spooning", "Lotus,Doggy", "She resists, uncomfortable"),
        new("0-29", "60-100", "Any", "High", "Missionary,Lotus", "Doggy", "She's uncomfortable, nervous"),

        // Desire 0-29 / SelfRespect 30-59
        new("0-29", "30-59", "Low", "Any", "Missionary,Spooning", "Lotus,Standing", "Not really into it"),
        new("0-29", "30-59", "Medium", "Any", "Missionary,Lotus", "Spooning,Scissors", "Going through motions"),
        new("0-29", "30-59", "High", "Any", "Missionary,Cowgirl", "Lotus,Spooning", "She's not enthusiastic"),
        new("0-29", "30-59", "Any", "Low", "Missionary,Spooning", "Lotus,Standing", "Passive, reluctant"),
        new("0-29", "30-59", "Any", "Medium", "Missionary,Doggy", "Lotus,Spooning", "She lets him lead, uninterested"),
        new("0-29", "30-59", "Any", "High", "Doggy,Missionary", "Cowgirl,Lotus", "He pushes, she doesn't fight"),

        // Desire 0-29 / SelfRespect 0-29
        new("0-29", "0-29", "Low", "Any", "Doggy,Face-sitting", "Cowgirl,Piledriver", "Broken, no resistance"),
        new("0-29", "0-29", "Medium", "Any", "Doggy,Face-sitting", "Reverse Cowgirl,Piledriver", "She doesn't care"),
        new("0-29", "0-29", "High", "Any", "Face-sitting,Reverse Cowgirl", "Doggy,Piledriver", "Fully submissive"),
        new("0-29", "0-29", "Any", "Low", "Doggy,Spooning", "Missionary,Standing", "No agency"),
        new("0-29", "0-29", "Any", "Medium", "Doggy,Face-sitting", "Cowgirl,Missionary", "He uses her"),
        new("0-29", "0-29", "Any", "High", "Face-sitting,Doggy", "Reverse Cowgirl,Piledriver", "She's fully controlled")
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
