using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class FinishingMoveMatrixSeedService
{
    private static readonly SeedRow[] SeedRows =
    [
        // Desire 60-100, SelfRespect 60-100
        new("60-100", "60-100", "0-29", "Creampie,On Pussy,On Stomach", "On Tits,On Back", "Enthusiastic, she asks", "Enthusiastic"),
        new("60-100", "60-100", "30-59", "Creampie,In Mouth,On Pussy", "Facial Open Mouth,On Tits,On Back", "Enthusiastic, mutual", "Enthusiastic"),
        new("60-100", "60-100", "60-100", "Facial Open Mouth,In Mouth,On Face", "Creampie,In Ass,On Ass", "He commands, she's eager", "Eager"),

        // Desire 60-100, SelfRespect 30-59
        new("60-100", "30-59", "0-29", "In Mouth,On Tits,On Pussy", "Facial Open Mouth,On Stomach,On Back", "Willing, eager", "Eager"),
        new("60-100", "30-59", "30-59", "Facial Open Mouth,In Mouth,On Face", "Creampie,Pearl Necklace,On Pussy", "She begs for it", "Begging"),
        new("60-100", "30-59", "60-100", "Facial Open Mouth,On Ass,In Mouth,In Ass", "Creampie,On Tits,On Back", "Aggressive, she wants it", "Begging"),

        // Desire 60-100, SelfRespect 0-29
        new("60-100", "0-29", "0-29", "Facial Eyes Closed,In Mouth,On Face", "On Ass,Creampie,On Back", "Submissive, desperate", "Accepting"),
        new("60-100", "0-29", "30-59", "Facial Open Mouth,On Ass,In Mouth,In Ass", "Creampie,On Tits,On Back", "Begging, degrading", "Begging"),
        new("60-100", "0-29", "60-100", "Facial Open Mouth,On Ass,In Mouth,Creampie,In Ass", "", "He commands, she has no say", "Enduring"),

        // Desire 30-59, SelfRespect 60-100
        new("30-59", "60-100", "0-29", "On Stomach,Creampie,On Pussy", "On Tits,On Back", "Willing but reserved", "Accepting"),
        new("30-59", "60-100", "30-59", "In Mouth,On Stomach,On Pussy", "Creampie,On Back", "Comfortable", "Accepting"),
        new("30-59", "60-100", "60-100", "In Mouth,On Tits,On Face", "Creampie,On Pussy", "He directs, she agrees", "Accepting"),

        // Desire 30-59, SelfRespect 30-59
        new("30-59", "30-59", "0-29", "In Mouth,On Stomach,On Tits", "Facial Eyes Closed,On Back", "Cooperative", "Accepting"),
        new("30-59", "30-59", "30-59", "Facial Eyes Closed,In Mouth,On Tits", "On Stomach,On Back,On Pussy", "Accepting", "Tolerating"),
        new("30-59", "30-59", "60-100", "Facial Eyes Closed,In Mouth,On Tits,On Face", "Creampie,On Pussy", "He decides", "Tolerating"),

        // Desire 30-59, SelfRespect 0-29
        new("30-59", "0-29", "0-29", "Facial Eyes Closed,In Mouth,On Face", "On Ass,On Stomach,On Back", "Resigned", "Tolerating"),
        new("30-59", "0-29", "30-59", "Facial Eyes Closed,On Ass,In Mouth,In Ass", "Creampie,On Back", "Submissive", "Tolerating"),
        new("30-59", "0-29", "60-100", "Facial Eyes Closed,On Ass,In Mouth,In Ass", "", "Completely commanded", "Enduring"),

        // Desire 0-29, SelfRespect 60-100
        new("0-29", "60-100", "0-29", "On Stomach,Pull-out,On Back", "On Tits", "Reluctant, prefers control", "CumDodging"),
        new("0-29", "60-100", "30-59", "On Stomach,On Tits,On Back", "In Mouth,On Pussy", "Hesitant", "Reluctant"),
        new("0-29", "60-100", "60-100", "On Tits,On Stomach,On Back", "In Mouth,On Face", "Uncomfortable", "CumDodging"),

        // Desire 0-29, SelfRespect 30-59
        new("0-29", "30-59", "0-29", "On Tits,On Stomach,On Back", "In Mouth,On Pussy", "Willing to please", "Reluctant"),
        new("0-29", "30-59", "30-59", "In Mouth,On Tits,On Stomach", "On Back,On Pussy", "Accommodating", "Tolerating"),
        new("0-29", "30-59", "60-100", "In Mouth,Facial Eyes Closed,On Face", "On Tits,On Stomach,On Back", "Pushed", "Tolerating"),

        // Desire 0-29, SelfRespect 0-29
        new("0-29", "0-29", "0-29", "In Mouth,Facial Eyes Closed,On Face", "On Ass,On Tits,On Back", "Broken, no resistance", "Enduring"),
        new("0-29", "0-29", "30-59", "Facial Eyes Closed,On Ass,In Mouth,In Ass", "Creampie,On Back", "No agency", "Enduring"),
        new("0-29", "0-29", "60-100", "Facial Eyes Closed,On Ass,In Mouth,Creampie,In Ass", "", "Fully controlled", "Enduring")
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
                OtherManDominanceBand = seed.OtherManDominanceBand,
                PrimaryLocations = ParseCsv(seed.PrimaryLocationsCsv),
                SecondaryLocations = ParseCsv(seed.SecondaryLocationsCsv),
                ExcludedLocations = [],
                WifeReceptivity = seed.Receptivity,
                WifeBehaviorModifier = seed.Behavior,
                OtherManBehaviorModifier = seed.OtherManDominanceBand switch
                {
                    "0-29" => "Asks and follows her cue where possible.",
                    "30-59" => "Leads decisively while reading her response.",
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
        string OtherManDominanceBand,
        string PrimaryLocationsCsv,
        string SecondaryLocationsCsv,
        string Behavior,
        string Receptivity);
}
