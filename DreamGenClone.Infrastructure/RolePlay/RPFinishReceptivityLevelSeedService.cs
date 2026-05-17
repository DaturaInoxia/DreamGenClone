using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RPFinishReceptivityLevelSeedService
{
    private sealed record SeedEntry(string Name, string PhysicalCues, string NarrativeCue, string EscalationTier);

    private static readonly SeedEntry[] Seeds =
    [
        // Low
        new("CumDodging",
            "She squirms, turns away, tries to move from the target.",
            "Her body reacts as if to avoid the finish.",
            "Low"),
        new("Reluctant",
            "She stiffens slightly, minimal body movement.",
            "She tolerates but does not embrace the finish.",
            "Low"),

        // Medium
        new("Accepting",
            "Still and compliant without resistance.",
            "She takes the finish without complaint.",
            "Medium"),
        new("Tolerating",
            "Minor tension in jaw or shoulders, eyes may close.",
            "She holds herself for him even if not enthusiastic.",
            "Medium"),
        new("Eager",
            "Leans slightly toward him, mouth slightly open.",
            "She wants to please him and shows mild anticipation.",
            "Medium"),

        // High
        new("Enthusiastic",
            "Leans in, open posture, eyes wide or smiling.",
            "She actively wants it and makes that clear.",
            "High"),
        new("Begging",
            "Strained forward, pleading expression, gasping.",
            "She begs him to finish on or in her.",
            "High"),
        new("Enduring",
            "Rigid or limp, jaw tight or slack, eyes fixed.",
            "She has no say; she takes whatever he chooses.",
            "High"),
    ];

    private readonly IRPThemeService _rpThemeService;
    private readonly ILogger<RPFinishReceptivityLevelSeedService> _logger;

    public RPFinishReceptivityLevelSeedService(IRPThemeService rpThemeService, ILogger<RPFinishReceptivityLevelSeedService> logger)
    {
        _rpThemeService = rpThemeService;
        _logger = logger;
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _rpThemeService.ListFinishReceptivityLevelsAsync(includeDisabled: true, cancellationToken: cancellationToken);

        if (existing.Count > 0)
        {
            await UpdateExistingTiersAsync(existing, cancellationToken);
            return;
        }

        var sortOrder = 0;
        foreach (var seed in Seeds)
        {
            await _rpThemeService.SaveFinishReceptivityLevelAsync(new RPFinishReceptivityLevel
            {
                Name = seed.Name,
                PhysicalCues = seed.PhysicalCues,
                NarrativeCue = seed.NarrativeCue,
                EscalationTier = seed.EscalationTier,
                SortOrder = sortOrder++,
                IsEnabled = true
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} finish receptivity level entries.", Seeds.Length);
    }

    private async Task UpdateExistingTiersAsync(
        IReadOnlyList<RPFinishReceptivityLevel> existing,
        CancellationToken cancellationToken)
    {
        var tierByName = Seeds.ToDictionary(e => e.Name, e => e.EscalationTier, StringComparer.OrdinalIgnoreCase);
        var updated = 0;
        foreach (var r in existing)
        {
            if (!tierByName.TryGetValue(r.Name, out var correctTier)) continue;
            if (string.Equals(r.EscalationTier, correctTier, StringComparison.OrdinalIgnoreCase)) continue;
            r.EscalationTier = correctTier;
            await _rpThemeService.SaveFinishReceptivityLevelAsync(r, cancellationToken);
            updated++;
        }
        if (updated > 0)
            _logger.LogInformation("Updated EscalationTier on {Count} finish receptivity level(s).", updated);
    }
}
