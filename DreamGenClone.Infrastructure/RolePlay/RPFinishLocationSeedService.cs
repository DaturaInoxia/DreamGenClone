using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RPFinishLocationSeedService
{
    private sealed record SeedEntry(string Name, string Category, string Description, string EscalationTier);

    private static readonly SeedEntry[] Seeds =
    [
        // Low — gentle, common, universally available
        new("Creampie",          "Internal",   "Finishes inside her vagina.",                        "Low"),
        new("On Pussy",          "External",   "Finishes on her vulva or outer area.",               "Low"),
        new("On Stomach",        "External",   "Finishes on her stomach or abdomen.",                "Low"),
        new("On Back",           "External",   "Finishes on her back.",                              "Low"),
        new("On Tits",           "External",   "Finishes on her chest or breasts.",                  "Low"),
        new("On Thighs",         "OnBody",     "Finishes on her inner thighs.",                      "Low"),
        new("Pull-out",          "Withdrawal", "Pulls out and finishes externally without a specific target.", "Low"),

        // Medium — moderate intensity, he leads
        new("Pearl Necklace",    "External",   "Finishes across her collarbone/neck.",               "Medium"),
        new("On Ass",            "External",   "Finishes on her buttocks.",                          "Medium"),
        new("On Face",           "Facial",     "Finishes across her face (non-specific eye/mouth state).", "Medium"),
        new("Facial Eyes Closed","Facial",     "Facial finish while her eyes are closed.",           "Medium"),
        new("In Mouth No Swallow","Facial",    "Finishes in her mouth; she holds but does not swallow.", "Medium"),
        new("In Mouth Swallow",  "Internal",   "She swallows the finish.",                           "Medium"),

        // High — intense/dominant
        new("Facial Open Mouth", "Facial",     "Facial finish while her mouth is open.",             "High"),
        new("In Ass",            "Internal",   "Finishes inside her rectum.",                        "High"),
        new("On Feet",           "OnBody",     "Finishes on her feet (fetish variant).",             "High"),
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
            await UpdateExistingTiersAsync(existing, cancellationToken);
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
                EscalationTier = seed.EscalationTier,
                SortOrder = sortOrder++,
                IsEnabled = true
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} finish location entries.", Seeds.Length);
    }

    private async Task UpdateExistingTiersAsync(
        IReadOnlyList<RPFinishLocation> existing,
        CancellationToken cancellationToken)
    {
        var tierByName = Seeds.ToDictionary(e => e.Name, e => e.EscalationTier, StringComparer.OrdinalIgnoreCase);
        var updated = 0;
        foreach (var loc in existing)
        {
            if (!tierByName.TryGetValue(loc.Name, out var correctTier)) continue;
            if (string.Equals(loc.EscalationTier, correctTier, StringComparison.OrdinalIgnoreCase)) continue;
            loc.EscalationTier = correctTier;
            await _rpThemeService.SaveFinishLocationAsync(loc, cancellationToken);
            updated++;
        }
        if (updated > 0)
            _logger.LogInformation("Updated EscalationTier on {Count} finish location(s).", updated);
    }
}
