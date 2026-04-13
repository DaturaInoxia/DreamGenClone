using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class StatWillingnessProfileService : IStatWillingnessProfileService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<StatWillingnessProfileService> _logger;

    public StatWillingnessProfileService(ISqlitePersistence persistence, ILogger<StatWillingnessProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<StatWillingnessProfile> SaveAsync(StatWillingnessProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await EnsureDefaultsAsync(cancellationToken);

        profile.Name = profile.Name.Trim();
        profile.Description = (profile.Description ?? string.Empty).Trim();
        profile.TargetStatName = string.IsNullOrWhiteSpace(profile.TargetStatName) ? "Desire" : profile.TargetStatName.Trim();
        profile.Thresholds = NormalizeThresholds(profile.Thresholds);

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("Willingness profile name is required.", nameof(profile));
        }

        ValidateCoverage(profile.Thresholds);

        var existing = await _persistence.LoadAllStatWillingnessProfilesAsync(cancellationToken);
        if (existing.Any(x => !string.Equals(x.Id, profile.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Willingness profile name already exists.");
        }

        profile.UpdatedUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = Guid.NewGuid().ToString();
            profile.CreatedUtc = DateTime.UtcNow;
        }

        await _persistence.SaveStatWillingnessProfileAsync(profile, cancellationToken);
        _logger.LogInformation("Stat willingness profile saved: {ProfileId}, Name={Name}", profile.Id, profile.Name);
        return profile;
    }

    public async Task<List<StatWillingnessProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);
        return await _persistence.LoadAllStatWillingnessProfilesAsync(cancellationToken);
    }

    public Task<StatWillingnessProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.LoadStatWillingnessProfileAsync(id, cancellationToken);

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.DeleteStatWillingnessProfileAsync(id, cancellationToken);

    public async Task<StatWillingnessProfile?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);
        return await _persistence.LoadDefaultStatWillingnessProfileAsync(cancellationToken);
    }

    private async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        var existing = await _persistence.LoadAllStatWillingnessProfilesAsync(cancellationToken);
        if (existing.Count > 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var seeded = new StatWillingnessProfile
        {
            Name = "Married Woman Baseline",
            Description = "Default Desire-driven willingness mapping seeded from v2 specs.",
            TargetStatName = "Desire",
            IsDefault = true,
            CreatedUtc = now,
            UpdatedUtc = now,
            Thresholds =
            [
                new() { SortOrder = 1, MinValue = 0, MaxValue = 5, ExplicitnessLevel = "Purely Emotional", Description = "Only words and emotional presence, no touching.", PromptGuideline = "Emotional words only and no physical contact.", ExampleScenarios = ["I love you", "emotional conversations", "looking into each other's eyes"] },
                new() { SortOrder = 2, MinValue = 6, MaxValue = 10, ExplicitnessLevel = "Hand-Holding", Description = "May hold hands and use gentle touches.", PromptGuideline = "Allow hand-holding and gentle arm touches; no kissing.", ExampleScenarios = ["holding hands", "hand on arm", "walking together"] },
                new() { SortOrder = 3, MinValue = 11, MaxValue = 15, ExplicitnessLevel = "Forehead Kisses", Description = "Forehead kisses and gentle embraces.", PromptGuideline = "Allow forehead kisses and gentle hugs; avoid lip contact.", ExampleScenarios = ["forehead kiss", "gentle hug", "cheek peck"] },
                new() { SortOrder = 4, MinValue = 16, MaxValue = 20, ExplicitnessLevel = "Closed Mouth Kissing", Description = "Brief closed-mouth kisses while still mostly emotional.", PromptGuideline = "Allow brief closed-mouth kisses; no tongue.", ExampleScenarios = ["peck on lips", "brief kisses", "tender moments"] },
                new() { SortOrder = 5, MinValue = 21, MaxValue = 25, ExplicitnessLevel = "Open Mouth Kissing", Description = "Soft open-mouth kissing and light caresses.", PromptGuideline = "Allow soft open-mouth kisses and light caresses over clothes.", ExampleScenarios = ["soft kisses", "caressing back", "gentle embraces"] },
                new() { SortOrder = 6, MinValue = 26, MaxValue = 30, ExplicitnessLevel = "Light Petting", Description = "Light petting over clothes with kissing.", PromptGuideline = "Allow light petting over clothing; avoid direct erogenous focus.", ExampleScenarios = ["caressing arm or leg", "kissing with embrace", "light touching"] },
                new() { SortOrder = 7, MinValue = 31, MaxValue = 35, ExplicitnessLevel = "Breast Over Clothes", Description = "May touch breasts lightly over clothing.", PromptGuideline = "Allow light breast touching over shirt or bra only.", ExampleScenarios = ["touching breast over shirt", "caressing over clothes", "kissing intensifies"] },
                new() { SortOrder = 8, MinValue = 36, MaxValue = 40, ExplicitnessLevel = "Under Clothes", Description = "Touching under clothes, still gentle.", PromptGuideline = "Allow light touching under clothes; avoid direct genital contact.", ExampleScenarios = ["hand under shirt", "light breast touching under clothes", "gentle exploration"] },
                new() { SortOrder = 9, MinValue = 41, MaxValue = 45, ExplicitnessLevel = "Genital Over Clothes", Description = "May touch genital area over clothing.", PromptGuideline = "Allow light genital touching over pants or underwear only.", ExampleScenarios = ["rubbing over pants", "light crotch touching", "arousal begins"] },
                new() { SortOrder = 10, MinValue = 46, MaxValue = 50, ExplicitnessLevel = "Manual Stimulation", Description = "Manual stimulation and vanilla missionary intimacy.", PromptGuideline = "Allow manual stimulation with loving vanilla intimacy framing.", ExampleScenarios = ["missionary", "hand stimulation", "loving intercourse"] },
                new() { SortOrder = 11, MinValue = 51, MaxValue = 55, ExplicitnessLevel = "Oral Receiving", Description = "Willing to receive oral intimacy.", PromptGuideline = "May receive oral while maintaining gentle and loving tone.", ExampleScenarios = ["receiving oral", "loving positions", "mutual pleasure"] },
                new() { SortOrder = 12, MinValue = 56, MaxValue = 60, ExplicitnessLevel = "Oral Giving", Description = "Willing to give oral intimacy.", PromptGuideline = "May give oral and be comfortable with reciprocation.", ExampleScenarios = ["giving oral", "69 position", "comfortable intimacy"] },
                new() { SortOrder = 13, MinValue = 61, MaxValue = 65, ExplicitnessLevel = "Cowgirl Top", Description = "Comfortable leading on top and varying positions.", PromptGuideline = "Allow riding and confident position changes.", ExampleScenarios = ["cowgirl", "riding", "multiple positions"] },
                new() { SortOrder = 14, MinValue = 66, MaxValue = 70, ExplicitnessLevel = "Doggy Style", Description = "Doggy style with early dirty talk.", PromptGuideline = "Allow doggy style and light dirty talk with vocal confidence.", ExampleScenarios = ["doggy style", "from behind", "vocal responses"] },
                new() { SortOrder = 15, MinValue = 71, MaxValue = 75, ExplicitnessLevel = "Confident Positions", Description = "All standard positions with confidence.", PromptGuideline = "Allow all common positions with confident and vocal framing.", ExampleScenarios = ["all standard positions", "confident intimacy", "vocal enjoyment"] },
                new() { SortOrder = 16, MinValue = 76, MaxValue = 80, ExplicitnessLevel = "Toys", Description = "Willing to use toys and experiment.", PromptGuideline = "Allow toys and exploratory play while preserving enthusiasm.", ExampleScenarios = ["vibrator use", "toy play", "exploratory intimacy"] },
                new() { SortOrder = 17, MinValue = 81, MaxValue = 85, ExplicitnessLevel = "Rough Play", Description = "Light rough play with clear enthusiasm.", PromptGuideline = "Allow light rough play with explicit enthusiastic participation.", ExampleScenarios = ["spanking", "hair pulling", "light restraint"] },
                new() { SortOrder = 18, MinValue = 86, MaxValue = 90, ExplicitnessLevel = "Anal", Description = "Anal play with preparation and consent framing.", PromptGuideline = "Allow anal play only when preparation and enthusiastic consent are explicit.", ExampleScenarios = ["anal play", "anal intimacy", "backdoor play"] },
                new() { SortOrder = 19, MinValue = 91, MaxValue = 95, ExplicitnessLevel = "Public Risk", Description = "Semi-public or risky settings.", PromptGuideline = "Allow semi-public risk framing with consent, caution, and continuity.", ExampleScenarios = ["car intimacy", "semi-public setting", "risk excitement"] },
                new() { SortOrder = 20, MinValue = 96, MaxValue = 100, ExplicitnessLevel = "Group Exploration", Description = "Full exploration and uninhibited fantasy fulfillment.", PromptGuideline = "Permit group and fantasy exploration with clear consensual framing.", ExampleScenarios = ["threesome", "group scenario", "fantasy fulfillment", "uninhibited passion"] }
            ]
        };

        await _persistence.SaveStatWillingnessProfileAsync(seeded, cancellationToken);
        _logger.LogInformation("Seeded default stat willingness profile.");
    }

    private static List<WillingnessThreshold> NormalizeThresholds(IReadOnlyList<WillingnessThreshold> thresholds)
    {
        return thresholds
            .Select((x, index) => new WillingnessThreshold
            {
                SortOrder = x.SortOrder <= 0 ? index + 1 : x.SortOrder,
                MinValue = Math.Clamp(x.MinValue, 0, 100),
                MaxValue = Math.Clamp(x.MaxValue, 0, 100),
                ExplicitnessLevel = (x.ExplicitnessLevel ?? string.Empty).Trim(),
                Description = (x.Description ?? string.Empty).Trim(),
                PromptGuideline = (x.PromptGuideline ?? string.Empty).Trim(),
                ExampleScenarios = x.ExampleScenarios
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.MinValue)
            .ToList();
    }

    private static void ValidateCoverage(IReadOnlyList<WillingnessThreshold> thresholds)
    {
        if (thresholds.Count == 0)
        {
            throw new ArgumentException("At least one threshold is required.");
        }

        foreach (var threshold in thresholds)
        {
            if (threshold.MinValue > threshold.MaxValue)
            {
                throw new ArgumentException($"Invalid threshold '{threshold.ExplicitnessLevel}': MinValue must be <= MaxValue.");
            }

            if (string.IsNullOrWhiteSpace(threshold.ExplicitnessLevel))
            {
                throw new ArgumentException("Each threshold requires an explicitness level name.");
            }

            if (string.IsNullOrWhiteSpace(threshold.PromptGuideline))
            {
                throw new ArgumentException($"Threshold '{threshold.ExplicitnessLevel}' requires a prompt guideline.");
            }
        }

        var ordered = thresholds.OrderBy(x => x.MinValue).ThenBy(x => x.MaxValue).ToList();
        if (ordered[0].MinValue != 0 || ordered[^1].MaxValue != 100)
        {
            throw new ArgumentException("Threshold ranges must cover 0..100.");
        }

        var expectedMin = 0;
        foreach (var threshold in ordered)
        {
            if (threshold.MinValue != expectedMin)
            {
                throw new ArgumentException("Threshold ranges must be contiguous without gaps.");
            }

            expectedMin = threshold.MaxValue + 1;
        }

        if (expectedMin != 101)
        {
            throw new ArgumentException("Threshold ranges must end at 100.");
        }
    }
}
