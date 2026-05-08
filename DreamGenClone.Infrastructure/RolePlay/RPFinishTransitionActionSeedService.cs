using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RPFinishTransitionActionSeedService
{
    private sealed record SeedEntry(
        string Name,
        string Description,
        string TransitionText,
        string EligibleDesire = "",
        string EligibleSelfRespect = "",
        string EligibleOtherManDom = "");

    private static readonly SeedEntry[] Seeds =
    [
        new("Verbal Command",
            "He announces the finish verbally before acting.",
            "He tells her clearly what is about to happen and exactly where.",
            "", "", "30-59,60-100"),

        new("Holds in Place",
            "He grips her firmly to prevent her from moving during the transition.",
            "His hands tighten on her hips or shoulders, locking her in position.",
            "", "", "30-59,60-100"),

        new("Guides with Hands",
            "He uses gentle physical guidance to move her into the right position.",
            "He places his hands on her and eases her into the intended position.",
            "", "", "0-29,30-59"),

        new("Pulls Close",
            "He draws her body flush against his during the final moment.",
            "He wraps an arm around her and pulls her tight as the finish begins.",
            "30-59,60-100", "", ""),

        new("Steps Back",
            "He creates distance at the last moment to finish externally.",
            "He pulls back slightly, creating the space needed for the chosen finish.",
            "", "30-59,60-100", "0-29"),

        new("Kneels",
            "She kneels in front of him, transitioning from standing or sitting.",
            "She lowers herself to her knees, positioning herself at his level.",
            "30-59,60-100", "0-29,30-59", "30-59,60-100"),

        new("Pushes Down",
            "He pushes her down or onto her back to achieve the desired angle.",
            "With firm pressure he moves her into the receiving position.",
            "", "0-29,30-59", "60-100"),
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
            _logger.LogInformation("Finish transition action seed skipped: {Count} entries already present.", existing.Count);
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
                EligibleDesireBands = seed.EligibleDesire,
                EligibleSelfRespectBands = seed.EligibleSelfRespect,
                EligibleOtherManDominanceBands = seed.EligibleOtherManDom,
                SortOrder = sortOrder++,
                IsEnabled = true
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} finish transition action entries.", Seeds.Length);
    }
}
