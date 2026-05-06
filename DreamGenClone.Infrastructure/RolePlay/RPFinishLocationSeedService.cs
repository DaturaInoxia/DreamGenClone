using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RPFinishLocationSeedService
{
    private sealed record SeedEntry(
        string Name,
        string Category,
        string Description,
        string EligibleDesire = "",
        string EligibleSelfRespect = "",
        string EligibleOtherManDom = "");

    private static readonly SeedEntry[] Seeds =
    [
        // Internal
        new("Creampie", "Internal", "Finishes inside her vagina.", "", "", ""),
        new("In Ass", "Internal", "Finishes inside her rectum.", "0-29,30-59", "0-29", "30-59,60-100"),
        new("In Mouth Swallow", "Internal", "She swallows the finish.", "", "", ""),

        // External – body
        new("On Tits", "External", "Finishes on her chest or breasts.", "", "", ""),
        new("On Stomach", "External", "Finishes on her stomach or abdomen.", "", "", ""),
        new("On Back", "External", "Finishes on her back.", "", "", ""),
        new("On Ass", "External", "Finishes on her buttocks.", "", "", ""),
        new("On Pussy", "External", "Finishes on her vulva or outer area.", "", "", ""),
        new("Pearl Necklace", "External", "Finishes across her collarbone/neck.", "30-59,60-100", "", ""),

        // Facial
        new("Facial Open Mouth", "Facial", "Facial finish while her mouth is open.", "30-59,60-100", "", ""),
        new("Facial Eyes Closed", "Facial", "Facial finish while her eyes are closed.", "0-29,30-59", "", ""),
        new("On Face", "Facial", "Finishes across her face (non-specific eye/mouth state).", "", "", ""),
        new("In Mouth No Swallow", "Facial", "Finishes in her mouth; she holds but does not swallow.", "0-29,30-59", "0-29,30-59", ""),

        // OnBody
        new("On Thighs", "OnBody", "Finishes on her inner thighs.", "", "", ""),
        new("On Feet", "OnBody", "Finishes on her feet (fetish variant).", "30-59,60-100", "", "60-100"),

        // Withdrawal
        new("Pull-out", "Withdrawal", "Pulls out and finishes externally without a specific target.", "", "60-100", "0-29"),
    ];

    private readonly IRPThemeService _rpThemeService;
    private readonly ILogger<RPFinishLocationSeedService> _logger;

    public RPFinishLocationSeedService(IRPThemeService rpThemeService, ILogger<RPFinishLocationSeedService> logger)
    {
        _rpThemeService = rpThemeService;
        _logger = logger;
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _rpThemeService.ListFinishLocationsAsync(includeDisabled: true, cancellationToken: cancellationToken);
        if (existing.Count > 0)
        {
            _logger.LogInformation("Finish location seed skipped: {Count} entries already present.", existing.Count);
            return;
        }

        var sortOrder = 0;
        foreach (var seed in Seeds)
        {
            await _rpThemeService.SaveFinishLocationAsync(new RPFinishLocation
            {
                Name = seed.Name,
                Category = seed.Category,
                Description = seed.Description,
                EligibleDesireBands = seed.EligibleDesire,
                EligibleSelfRespectBands = seed.EligibleSelfRespect,
                EligibleOtherManDominanceBands = seed.EligibleOtherManDom,
                SortOrder = sortOrder++,
                IsEnabled = true
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} finish location entries.", Seeds.Length);
    }
}
