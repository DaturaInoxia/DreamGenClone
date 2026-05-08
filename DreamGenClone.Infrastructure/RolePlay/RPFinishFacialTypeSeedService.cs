using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RPFinishFacialTypeSeedService
{
    private sealed record SeedEntry(
        string Name,
        string Description,
        string PhysicalCues,
        string EligibleDesire = "",
        string EligibleSelfRespect = "",
        string EligibleOtherManDom = "");

    private static readonly SeedEntry[] Seeds =
    [
        new("Open Mouth",
            "Her mouth is open, accepting or inviting.",
            "Lips parted, jaw slack, tongue visible.",
            "30-59,60-100", "", ""),

        new("Eyes Closed",
            "Her eyes are squeezed shut, braced or resigned.",
            "Lids pressed shut, brows furrowed or relaxed.",
            "", "", ""),

        new("Eyes Open",
            "Maintains eye contact or wide-eyed reaction.",
            "Direct gaze, pupils dilated, expression intense.",
            "30-59,60-100", "30-59,60-100", ""),

        new("Tongue Out",
            "Tongue extended, anticipatory or degraded.",
            "Tongue flat or curled forward, chin tilted up.",
            "30-59,60-100", "0-29,30-59", "30-59,60-100"),

        new("Cringing Turn-away",
            "She turns her face slightly away, flinching.",
            "Head angled aside, eyes squeezed, shoulders raised.",
            "0-29", "30-59,60-100", ""),

        new("Smiling Acceptance",
            "She smiles through the finish, fully willing.",
            "Relaxed smile, eyes soft or closed, posture open.",
            "60-100", "30-59,60-100", "0-29,30-59"),
    ];

    private readonly IRPThemeService _rpThemeService;
    private readonly ILogger<RPFinishFacialTypeSeedService> _logger;

    public RPFinishFacialTypeSeedService(IRPThemeService rpThemeService, ILogger<RPFinishFacialTypeSeedService> logger)
    {
        _rpThemeService = rpThemeService;
        _logger = logger;
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _rpThemeService.ListFinishFacialTypesAsync(includeDisabled: true, cancellationToken: cancellationToken);
        if (existing.Count > 0)
        {
            _logger.LogInformation("Finish facial type seed skipped: {Count} entries already present.", existing.Count);
            return;
        }

        var sortOrder = 0;
        foreach (var seed in Seeds)
        {
            await _rpThemeService.SaveFinishFacialTypeAsync(new RPFinishFacialType
            {
                Name = seed.Name,
                Description = seed.Description,
                PhysicalCues = seed.PhysicalCues,
                EligibleDesireBands = seed.EligibleDesire,
                EligibleSelfRespectBands = seed.EligibleSelfRespect,
                EligibleOtherManDominanceBands = seed.EligibleOtherManDom,
                SortOrder = sortOrder++,
                IsEnabled = true
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} finish facial type entries.", Seeds.Length);
    }
}
