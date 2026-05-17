using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RPFinishFacialTypeSeedService
{
    private sealed record SeedEntry(string Name, string Description, string PhysicalCues, string EscalationTier);

    private static readonly SeedEntry[] Seeds =
    [
        // Low
        new("Eyes Closed",
            "Her eyes are squeezed shut, braced or resigned.",
            "Lids pressed shut, brows furrowed or relaxed.",
            "Low"),
        new("Smiling Acceptance",
            "She smiles through the finish, fully willing.",
            "Relaxed smile, eyes soft or closed, posture open.",
            "Low"),

        // Medium
        new("Eyes Open",
            "Maintains eye contact or wide-eyed reaction.",
            "Direct gaze, pupils dilated, expression intense.",
            "Medium"),
        new("Cringing Turn-away",
            "She turns her face slightly away, flinching.",
            "Head angled aside, eyes squeezed, shoulders raised.",
            "Medium"),

        // High
        new("Open Mouth",
            "Her mouth is open, accepting or inviting.",
            "Lips parted, jaw slack, tongue visible.",
            "High"),
        new("Tongue Out",
            "Tongue extended, anticipatory or degraded.",
            "Tongue flat or curled forward, chin tilted up.",
            "High"),
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
            await UpdateExistingTiersAsync(existing, cancellationToken);
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
                EscalationTier = seed.EscalationTier,
                SortOrder = sortOrder++,
                IsEnabled = true
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} finish facial type entries.", Seeds.Length);
    }

    private async Task UpdateExistingTiersAsync(
        IReadOnlyList<RPFinishFacialType> existing,
        CancellationToken cancellationToken)
    {
        var tierByName = Seeds.ToDictionary(e => e.Name, e => e.EscalationTier, StringComparer.OrdinalIgnoreCase);
        var updated = 0;
        foreach (var ft in existing)
        {
            if (!tierByName.TryGetValue(ft.Name, out var correctTier)) continue;
            if (string.Equals(ft.EscalationTier, correctTier, StringComparison.OrdinalIgnoreCase)) continue;
            ft.EscalationTier = correctTier;
            await _rpThemeService.SaveFinishFacialTypeAsync(ft, cancellationToken);
            updated++;
        }
        if (updated > 0)
            _logger.LogInformation("Updated EscalationTier on {Count} finish facial type(s).", updated);
    }
}
