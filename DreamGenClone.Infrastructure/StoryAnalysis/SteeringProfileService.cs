using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class SteeringProfileService : ISteeringProfileService
{
    private sealed record DefaultStyleProfile(
        string Name,
        string Description,
        string Example,
        string RuleOfThumb,
        Dictionary<string, int>? ThemeAffinities = null,
        List<string>? EscalatingThemeIds = null,
        Dictionary<string, int>? StatBias = null);

    private static readonly DefaultStyleProfile[] DefaultProfiles =
    [
        new(
            "Sultry",
            "Evocative and moody, layered with ambiguity, seductive undertones, lush descriptions, slow-burn tension. Atmospheric settings that hint at danger, intense emotional undercurrents, intricate plotting. Subtle but pervasive sense of intrigue. Use lots of sensory details.",
            "The room felt warmer the longer she remained near him, the soft amber light turning every glance into a private dare.",
            "Favor atmosphere, tension, and sensory detail over speed. Let desire accumulate before anything explicit happens.",
            ThemeAffinities: new(StringComparer.OrdinalIgnoreCase) { ["intimacy"] = 5, ["voyeurism"] = 4, ["forbidden-risk"] = 2 },
            EscalatingThemeIds: ["dominance", "power-dynamics", "forbidden-risk", "humiliation", "infidelity"],
            StatBias: new(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 5, ["Restraint"] = 5 })
    ];

    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<SteeringProfileService> _logger;

    public SteeringProfileService(ISqlitePersistence persistence, ILogger<SteeringProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<SteeringProfile> CreateAsync(string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);

        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Style profile name cannot be empty.", nameof(name));
        }

        var existingProfiles = await _persistence.LoadAllStyleProfilesAsync(cancellationToken);
        if (existingProfiles.Any(x => string.Equals(x.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Style profile name already exists.");
        }

        var profile = new SteeringProfile
        {
            Name = trimmedName,
            Description = description?.Trim() ?? string.Empty,
            Example = example?.Trim() ?? string.Empty,
            RuleOfThumb = ruleOfThumb?.Trim() ?? string.Empty,
            ThemeAffinities = themeAffinities ?? new(StringComparer.OrdinalIgnoreCase),
            EscalatingThemeIds = escalatingThemeIds ?? [],
            StatBias = statBias ?? new(StringComparer.OrdinalIgnoreCase),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        await _persistence.SaveStyleProfileAsync(profile, cancellationToken);
        _logger.LogInformation("Style profile created: {StyleProfileId}, Name={Name}", profile.Id, profile.Name);
        return profile;
    }

    public Task<List<SteeringProfile>> ListAsync(CancellationToken cancellationToken = default)
        => ListInternalAsync(cancellationToken);

    public Task<SteeringProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.LoadStyleProfileAsync(id, cancellationToken);

    public async Task<SteeringProfile?> UpdateAsync(string id, string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);

        var existing = await _persistence.LoadStyleProfileAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Style profile name cannot be empty.", nameof(name));
        }

        var profiles = await _persistence.LoadAllStyleProfilesAsync(cancellationToken);
        if (profiles.Any(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Style profile name already exists.");
        }

        existing.Name = trimmedName;
        existing.Description = description?.Trim() ?? string.Empty;
        existing.Example = example?.Trim() ?? string.Empty;
        existing.RuleOfThumb = ruleOfThumb?.Trim() ?? string.Empty;
        if (themeAffinities is not null) existing.ThemeAffinities = themeAffinities;
        if (escalatingThemeIds is not null) existing.EscalatingThemeIds = escalatingThemeIds;
        if (statBias is not null) existing.StatBias = statBias;
        existing.UpdatedUtc = DateTime.UtcNow;

        await _persistence.SaveStyleProfileAsync(existing, cancellationToken);
        _logger.LogInformation("Style profile updated: {StyleProfileId}, Name={Name}", existing.Id, existing.Name);
        return existing;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);

        var deleted = await _persistence.DeleteStyleProfileAsync(id, cancellationToken);
        _logger.LogInformation("Style profile deleted: {StyleProfileId}, Success={Deleted}", id, deleted);
        return deleted;
    }

    private async Task<List<SteeringProfile>> ListInternalAsync(CancellationToken cancellationToken)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);
        return await _persistence.LoadAllStyleProfilesAsync(cancellationToken);
    }

    private async Task EnsureDefaultProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = await _persistence.LoadAllStyleProfilesAsync(cancellationToken);
        var changed = false;

        foreach (var item in DefaultProfiles)
        {
            var existing = profiles.FirstOrDefault(profile => string.Equals(profile.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                await _persistence.SaveStyleProfileAsync(new SteeringProfile
                {
                    Name = item.Name,
                    Description = item.Description,
                    Example = item.Example,
                    RuleOfThumb = item.RuleOfThumb,
                    ThemeAffinities = item.ThemeAffinities ?? new(StringComparer.OrdinalIgnoreCase),
                    EscalatingThemeIds = item.EscalatingThemeIds ?? [],
                    StatBias = item.StatBias ?? new(StringComparer.OrdinalIgnoreCase),
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow
                }, cancellationToken);
                changed = true;
                continue;
            }

            if (RequiresSultryNormalization(existing))
            {
                existing.Description = item.Description;
                existing.Example = item.Example;
                existing.RuleOfThumb = item.RuleOfThumb;
                existing.ThemeAffinities = item.ThemeAffinities ?? new(StringComparer.OrdinalIgnoreCase);
                existing.EscalatingThemeIds = item.EscalatingThemeIds ?? [];
                existing.StatBias = item.StatBias ?? new(StringComparer.OrdinalIgnoreCase);
                existing.UpdatedUtc = DateTime.UtcNow;
                await _persistence.SaveStyleProfileAsync(existing, cancellationToken);
                changed = true;
            }
        }

        if (changed)
        {
            _logger.LogInformation("Seeded or normalized {Count} default style profiles.", DefaultProfiles.Length);
        }
    }

    private static bool RequiresSultryNormalization(SteeringProfile profile)
    {
        return profile.ThemeAffinities.ContainsKey("romantic-tension")
            || profile.ThemeAffinities.ContainsKey("emotional-vulnerability")
            || (profile.StatBias.TryGetValue("Desire", out var desire) && desire == 1)
            || profile.StatBias.ContainsKey("Connection");
    }
}