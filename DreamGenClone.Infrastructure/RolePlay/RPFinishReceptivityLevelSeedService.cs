using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RPFinishReceptivityLevelSeedService
{
    private sealed record SeedEntry(
        string Name,
        string Description,
        string PhysicalCues,
        string NarrativeCue,
        string EligibleDesire = "",
        string EligibleSelfRespect = "");

    // Exactly 8 canonical entries, SortOrder 0–7
    private static readonly SeedEntry[] Seeds =
    [
        new("Begging",
            "She is actively begging for it; desire overrides all restraint.",
            "Hips rocking forward, hands pulling him close, voice pleading.",
            "She begs him not to stop, her voice barely coherent.",
            "60-100", "0-29,30-59"),

        new("Enthusiastic",
            "Fully willing and energetically engaged; equal or greater initiative.",
            "Body arched toward him, moaning freely, eyes bright.",
            "Her enthusiasm matches or exceeds his own.",
            "60-100", "30-59,60-100"),

        new("Eager",
            "Willing and ready; leaning into the moment without reservation.",
            "Soft gasps, body relaxed and open, small encouraging sounds.",
            "She is ready and clearly wants this.",
            "30-59,60-100", "30-59,60-100"),

        new("Accepting",
            "Comfortable and consenting; no resistance, moderate engagement.",
            "Steady breathing, relaxed posture, neutral or soft expression.",
            "She accepts what is happening without protest.",
            "30-59,60-100", "30-59,60-100"),

        new("Tolerating",
            "Compliant but not engaged; enduring rather than enjoying.",
            "Quiet, still, eyes averted or half-closed, minimal movement.",
            "She endures without complaint, though her engagement is absent.",
            "0-29,30-59", "0-29,30-59"),

        new("Reluctant",
            "Some visible resistance or hesitation; she complies but shows reluctance.",
            "Slight flinch or tensing, hands braced, soft protest sounds.",
            "She hesitates but does not stop him.",
            "0-29", "30-59,60-100"),

        new("CumDodging",
            "Actively tries to avoid the finish or reposition; high self-respect low desire.",
            "Turning away, shoulders raised, small recoil at the moment.",
            "She instinctively angles away, though she doesn't refuse outright.",
            "0-29", "60-100"),

        new("Enduring",
            "Passive endurance; no agency, no engagement, full submission.",
            "Eyes closed, body limp or rigid, no vocal response.",
            "She has no say and does not expect one.",
            "0-29,30-59", "0-29"),
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
            _logger.LogInformation("Finish receptivity level seed skipped: {Count} entries already present.", existing.Count);
            return;
        }

        for (var i = 0; i < Seeds.Length; i++)
        {
            var seed = Seeds[i];
            await _rpThemeService.SaveFinishReceptivityLevelAsync(new RPFinishReceptivityLevel
            {
                Name = seed.Name,
                Description = seed.Description,
                PhysicalCues = seed.PhysicalCues,
                NarrativeCue = seed.NarrativeCue,
                EligibleDesireBands = seed.EligibleDesire,
                EligibleSelfRespectBands = seed.EligibleSelfRespect,
                SortOrder = i,
                IsEnabled = true
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} finish receptivity level entries.", Seeds.Length);
    }
}
