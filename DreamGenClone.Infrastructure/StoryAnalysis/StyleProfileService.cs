using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class StyleProfileService : IStyleProfileService
{
    private sealed record DefaultStyleProfile(string Name, string Description, string Example, string RuleOfThumb);

    private static readonly DefaultStyleProfile[] DefaultProfiles =
    [
        new(
            "Sultry",
            "Evocative and moody, layered with ambiguity, seductive undertones, lush descriptions, slow-burn tension. Atmospheric settings that hint at danger, intense emotional undercurrents, intricate plotting. Subtle but pervasive sense of intrigue. Use lots of sensory details.",
            "The room felt warmer the longer she remained near him, the soft amber light turning every glance into a private dare.",
            "Favor atmosphere, tension, and sensory detail over speed. Let desire accumulate before anything explicit happens.")
    ];

    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<StyleProfileService> _logger;

    public StyleProfileService(ISqlitePersistence persistence, ILogger<StyleProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<StyleProfile> CreateAsync(string name, string description, string example, string ruleOfThumb, CancellationToken cancellationToken = default)
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

        var profile = new StyleProfile
        {
            Name = trimmedName,
            Description = description?.Trim() ?? string.Empty,
            Example = example?.Trim() ?? string.Empty,
            RuleOfThumb = ruleOfThumb?.Trim() ?? string.Empty,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        await _persistence.SaveStyleProfileAsync(profile, cancellationToken);
        _logger.LogInformation("Style profile created: {StyleProfileId}, Name={Name}", profile.Id, profile.Name);
        return profile;
    }

    public Task<List<StyleProfile>> ListAsync(CancellationToken cancellationToken = default)
        => ListInternalAsync(cancellationToken);

    public Task<StyleProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.LoadStyleProfileAsync(id, cancellationToken);

    public async Task<StyleProfile?> UpdateAsync(string id, string name, string description, string example, string ruleOfThumb, CancellationToken cancellationToken = default)
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

    private async Task<List<StyleProfile>> ListInternalAsync(CancellationToken cancellationToken)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);
        return await _persistence.LoadAllStyleProfilesAsync(cancellationToken);
    }

    private async Task EnsureDefaultProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = await _persistence.LoadAllStyleProfilesAsync(cancellationToken);
        if (profiles.Count > 0)
        {
            return;
        }

        foreach (var item in DefaultProfiles)
        {
            await _persistence.SaveStyleProfileAsync(new StyleProfile
            {
                Name = item.Name,
                Description = item.Description,
                Example = item.Example,
                RuleOfThumb = item.RuleOfThumb,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} default style profiles.", DefaultProfiles.Length);
    }
}