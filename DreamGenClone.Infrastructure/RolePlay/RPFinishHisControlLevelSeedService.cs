using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RPFinishHisControlLevelSeedService
{
    private sealed record SeedEntry(
        string Name,
        string Description,
        string ExampleDialogue,
        string EligibleOtherManDom);

    // Exactly 3 canonical entries, SortOrder 0–2
    private static readonly SeedEntry[] Seeds =
    [
        new("Asks",
            "He asks or checks before proceeding; power dynamic is soft and deferential.",
            "\"Is this okay? Tell me where you want it.\"",
            "0-29"),

        new("Leads",
            "He takes the lead decisively but reads her response; shared control.",
            "\"Come here — I want you like this.\"",
            "30-59"),

        new("Commands",
            "He commands without asking; full unilateral control of the moment.",
            "\"Don't move. Stay exactly like that.\"",
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
            _logger.LogInformation("Finish his-control level seed skipped: {Count} entries already present.", existing.Count);
            return;
        }

        for (var i = 0; i < Seeds.Length; i++)
        {
            var seed = Seeds[i];
            await _rpThemeService.SaveFinishHisControlLevelAsync(new RPFinishHisControlLevel
            {
                Name = seed.Name,
                Description = seed.Description,
                ExampleDialogue = seed.ExampleDialogue,
                EligibleOtherManDominanceBands = seed.EligibleOtherManDom,
                SortOrder = i,
                IsEnabled = true
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} finish his-control level entries.", Seeds.Length);
    }
}
