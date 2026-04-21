using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class FinishingMoveMatrixSeedService
{
    private static readonly SeedRow[] SeedRows =
    [
        // Desire 75-100, SelfRespect 60-100
        new("75-100", "60-100", "Low", "Creampie,Stomach", "Tits", "Enthusiastic, she asks"),
        new("75-100", "60-100", "Medium", "Creampie,Mouth", "Face,Tits", "Enthusiastic, mutual"),
        new("75-100", "60-100", "High", "Face,Mouth", "Creampie,Ass", "He commands, she's eager"),

        // Desire 75-100, SelfRespect 30-59
        new("75-100", "30-59", "Low", "Mouth,Tits", "Face,Stomach", "Willing, eager"),
        new("75-100", "30-59", "Medium", "Face,Mouth", "Creampie,Pearl Necklace", "She begs for it"),
        new("75-100", "30-59", "High", "Face,Ass,Mouth", "Creampie,Tits", "Aggressive, she wants it"),

        // Desire 75-100, SelfRespect 0-29
        new("75-100", "0-29", "Low", "Face,Mouth", "Ass,Creampie", "Submissive, desperate"),
        new("75-100", "0-29", "Medium", "Face,Ass,Mouth", "Creampie,Tits", "Begging, degrading"),
        new("75-100", "0-29", "High", "Face,Ass,Mouth,Creampie", "", "He commands, she has no say"),

        // Desire 50-74, SelfRespect 60-100
        new("50-74", "60-100", "Low", "Stomach,Creampie", "Tits", "Willing but reserved"),
        new("50-74", "60-100", "Medium", "Mouth,Stomach", "Creampie", "Comfortable"),
        new("50-74", "60-100", "High", "Mouth,Tits", "Creampie", "He directs, she agrees"),

        // Desire 50-74, SelfRespect 30-59
        new("50-74", "30-59", "Low", "Mouth,Stomach", "Tits,Face", "Cooperative"),
        new("50-74", "30-59", "Medium", "Face,Mouth", "Tits,Stomach", "Accepting"),
        new("50-74", "30-59", "High", "Face,Mouth,Tits", "Creampie", "He decides"),

        // Desire 50-74, SelfRespect 0-29
        new("50-74", "0-29", "Low", "Face,Mouth", "Ass,Stomach", "Resigned"),
        new("50-74", "0-29", "Medium", "Face,Ass,Mouth", "Creampie", "Submissive"),
        new("50-74", "0-29", "High", "Face,Ass,Mouth", "", "Completely commanded"),

        // Desire 0-49, SelfRespect 60-100
        new("0-49", "60-100", "Low", "Stomach,Pull-out", "Tits", "Reluctant, prefers control"),
        new("0-49", "60-100", "Medium", "Stomach,Tits", "Mouth", "Hesitant"),
        new("0-49", "60-100", "High", "Tits,Stomach", "Mouth", "Uncomfortable"),

        // Desire 0-49, SelfRespect 30-59
        new("0-49", "30-59", "Low", "Tits,Stomach", "Mouth", "Willing to please"),
        new("0-49", "30-59", "Medium", "Mouth,Tits", "Stomach", "Accommodating"),
        new("0-49", "30-59", "High", "Mouth,Face", "Tits,Stomach", "Pushed"),

        // Desire 0-49, SelfRespect 0-29
        new("0-49", "0-29", "Low", "Mouth,Face", "Ass,Tits", "Broken, no resistance"),
        new("0-49", "0-29", "Medium", "Face,Ass,Mouth", "Creampie", "No agency"),
        new("0-49", "0-29", "High", "Face,Ass,Mouth,Creampie", "", "Fully controlled")
    ];

    private readonly IRPThemeService _rpThemeService;
    private readonly ILogger<FinishingMoveMatrixSeedService> _logger;

    public FinishingMoveMatrixSeedService(IRPThemeService rpThemeService, ILogger<FinishingMoveMatrixSeedService> logger)
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

        var existing = await _rpThemeService.ListFinishingMoveMatrixRowsAsync(cancellationToken);
        if (existing.Count > 0)
        {
            _logger.LogInformation("Finishing move matrix seed skipped: {Count} base rows already present.", existing.Count);
            return;
        }

        var sortOrder = 0;
        foreach (var seed in SeedRows)
        {
            await _rpThemeService.SaveFinishingMoveMatrixRowAsync(new RPFinishingMoveMatrixRow
            {
                DesireBand = seed.DesireBand,
                SelfRespectBand = seed.SelfRespectBand,
                DominanceBand = seed.DominanceBand,
                PrimaryLocations = ParseCsv(seed.PrimaryLocationsCsv),
                SecondaryLocations = ParseCsv(seed.SecondaryLocationsCsv),
                ExcludedLocations = [],
                WifeBehaviorModifier = seed.Behavior,
                OtherManBehaviorModifier = seed.DominanceBand switch
                {
                    "Low" => "Asks and follows her cue where possible.",
                    "Medium" => "Leads decisively while reading her response.",
                    _ => "Commands and controls the finish without asking."
                },
                TransitionInstruction = "If current positioning does not allow the finish location, include an explicit repositioning beat before the finish.",
                SortOrder = sortOrder++,
                IsEnabled = true
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded finishing move matrix defaults from v2 spec: {Count} base rows.", SeedRows.Length);
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
        string DominanceBand,
        string PrimaryLocationsCsv,
        string SecondaryLocationsCsv,
        string Behavior);
}
