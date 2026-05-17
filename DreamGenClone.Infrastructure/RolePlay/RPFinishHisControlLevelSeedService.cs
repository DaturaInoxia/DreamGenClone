using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RPFinishHisControlLevelSeedService
{
    private sealed record SeedEntry(string Name, string Description, string ExampleDialogue, string EscalationTier);

    private static readonly SeedEntry[] Seeds =
    [
        new("Asks",
            "He asks or checks before proceeding; power dynamic is soft and deferential.",
            "\"Is this okay? Tell me where you want it.\"",
            "Low"),

        new("Leads",
            "He takes the lead decisively but reads her response; shared control.",
            "\"Come here — I want you like this.\"",
            "Medium"),

        new("Commands",
            "He commands without asking; full unilateral control of the moment.",
            "\"Don't move. Stay exactly like that.\"",
            "High"),
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
        var tierByName = Seeds.ToDictionary(e => e.Name, e => e.EscalationTier, StringComparer.OrdinalIgnoreCase);
        var updated = 0;
        foreach (var hc in existing)
        {
            if (!tierByName.TryGetValue(hc.Name, out var correctTier)) continue;
            if (string.Equals(hc.EscalationTier, correctTier, StringComparison.OrdinalIgnoreCase)) continue;
            hc.EscalationTier = correctTier;
            await _rpThemeService.SaveFinishHisControlLevelAsync(hc, cancellationToken);
            updated++;
        }
        if (updated > 0)
            _logger.LogInformation("Updated EscalationTier on {Count} finish his-control level(s).", updated);
    }
}
