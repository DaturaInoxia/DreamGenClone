using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RPFinishTransitionActionSeedService
{
    private sealed record SeedEntry(string Name, string Description, string TransitionText, string EscalationTier);

    private static readonly SeedEntry[] Seeds =
    [
        // Low — gentle, cooperative
        new("Steps Back",
            "He creates distance at the last moment to finish externally.",
            "He pulls back slightly, creating the space needed for the chosen finish.",
            "Low"),
        new("Guides with Hands",
            "He uses gentle physical guidance to move her into the right position.",
            "He places his hands on her and eases her into the intended position.",
            "Low"),

        // Medium — he leads
        new("Pulls Close",
            "He draws her body flush against his during the final moment.",
            "He wraps an arm around her and pulls her tight as the finish begins.",
            "Medium"),
        new("Kneels",
            "She kneels in front of him, transitioning from standing or sitting.",
            "She lowers herself to her knees, positioning herself at his level.",
            "Medium"),

        // High — he controls
        new("Verbal Command",
            "He announces the finish verbally before acting.",
            "He tells her clearly what is about to happen and exactly where.",
            "High"),
        new("Holds in Place",
            "He grips her firmly to prevent her from moving during the transition.",
            "His hands tighten on her hips or shoulders, locking her in position.",
            "High"),
        new("Pushes Down",
            "He pushes her down or onto her back to achieve the desired angle.",
            "With firm pressure he moves her into the receiving position.",
            "High"),
    ];

    private readonly IRPThemeService _rpThemeService;
    private readonly ILogger<RPFinishTransitionActionSeedService> _logger;

    public RPFinishTransitionActionSeedService(IRPThemeService rpThemeService, ILogger<RPFinishTransitionActionSeedService> logger)
    {
        _rpThemeService = rpThemeService;
        _logger = logger;
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _rpThemeService.ListFinishTransitionActionsAsync(includeDisabled: true, cancellationToken: cancellationToken);

        if (existing.Count > 0)
        {
            await UpdateExistingTiersAsync(existing, cancellationToken);
            return;
        }

        var sortOrder = 0;
        foreach (var seed in Seeds)
        {
            await _rpThemeService.SaveFinishTransitionActionAsync(new RPFinishTransitionAction
            {
                Name = seed.Name,
                Description = seed.Description,
                TransitionText = seed.TransitionText,
                EscalationTier = seed.EscalationTier,
                SortOrder = sortOrder++,
                IsEnabled = true
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} finish transition action entries.", Seeds.Length);
    }

    private async Task UpdateExistingTiersAsync(
        IReadOnlyList<RPFinishTransitionAction> existing,
        CancellationToken cancellationToken)
    {
        var tierByName = Seeds.ToDictionary(e => e.Name, e => e.EscalationTier, StringComparer.OrdinalIgnoreCase);
        var updated = 0;
        foreach (var t in existing)
        {
            if (!tierByName.TryGetValue(t.Name, out var correctTier)) continue;
            if (string.Equals(t.EscalationTier, correctTier, StringComparison.OrdinalIgnoreCase)) continue;
            t.EscalationTier = correctTier;
            await _rpThemeService.SaveFinishTransitionActionAsync(t, cancellationToken);
            updated++;
        }
        if (updated > 0)
            _logger.LogInformation("Updated EscalationTier on {Count} finish transition action(s).", updated);
    }
}
