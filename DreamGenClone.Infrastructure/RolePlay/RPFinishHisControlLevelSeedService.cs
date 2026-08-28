using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RPFinishHisControlLevelSeedService
{
    private sealed record SeedEntry(string Name, string Description, string ExampleDialogue, string EscalationTier, string EligibleOtherManDominanceBands);

    private static readonly SeedEntry[] Seeds =
    [
        new("Asks",
            "He asks or checks before proceeding; power dynamic is soft and deferential.",
            "\"Is this okay? Tell me where you want it.\"",
            "Low",
            "0-29"),

        new("Leads",
            "He takes the lead decisively but reads her response; shared control.",
            "\"Come here \u2014 I want you like this.\"",
            "Medium",
            "30-59"),

        new("Commands",
            "He commands without asking; full unilateral control of the moment.",
            "\"Don't move. Stay exactly like that.\"",
            "High",
            "60-100"),
    ];


    private readonly IRPThemeService _rpThemeService;
    private readonly ILogger<RPFinishHisControlLevelSeedService> _logger;

    public RPFinishHisControlLevelSeedService(IRPThemeService rpThemeService, ILogger<RPFinishHisControlLevelSeedService> logger)
    {
        _rpThemeService = rpThemeService;
        _logger = logger;
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _rpThemeService.ListFinishHisControlLevelsAsync(includeDisabled: true, cancellationToken: cancellationToken);

        if (existing.Count > 0)
        {
            await UpdateExistingTiersAsync(existing, cancellationToken);
            return;
        }

        var sortOrder = 0;
        foreach (var seed in Seeds)
        {
            await _rpThemeService.SaveFinishHisControlLevelAsync(new RPFinishHisControlLevel
            {
                Name = seed.Name,
                Description = seed.Description,
                ExampleDialogue = seed.ExampleDialogue,
                EscalationTier = seed.EscalationTier,
                EligibleOtherManDominanceBands = seed.EligibleOtherManDominanceBands,
                SortOrder = sortOrder++,
                IsEnabled = true
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} finish his-control level entries.", Seeds.Length);
    }

    private async Task UpdateExistingTiersAsync(
        IReadOnlyList<RPFinishHisControlLevel> existing,
        CancellationToken cancellationToken)
    {
        var seedByName = Seeds.ToDictionary(e => e.Name, e => e, StringComparer.OrdinalIgnoreCase);
        var updated = 0;
        foreach (var hc in existing)
        {
            if (!seedByName.TryGetValue(hc.Name, out var seed)) continue;
            var tierChanged = !string.Equals(hc.EscalationTier, seed.EscalationTier, StringComparison.OrdinalIgnoreCase);
            var bandChanged = !string.Equals(hc.EligibleOtherManDominanceBands, seed.EligibleOtherManDominanceBands, StringComparison.OrdinalIgnoreCase);
            if (!tierChanged && !bandChanged) continue;
            hc.EscalationTier = seed.EscalationTier;
            hc.EligibleOtherManDominanceBands = seed.EligibleOtherManDominanceBands;
            await _rpThemeService.SaveFinishHisControlLevelAsync(hc, cancellationToken);
            updated++;
        }
        if (updated > 0)
            _logger.LogInformation("Updated EscalationTier/Bands on {Count} finish his-control level(s).", updated);
    }
}
