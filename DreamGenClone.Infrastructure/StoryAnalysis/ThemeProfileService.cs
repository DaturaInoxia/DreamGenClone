using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class ThemeProfileService : IThemeProfileService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<ThemeProfileService> _logger;

    public ThemeProfileService(ISqlitePersistence persistence, ILogger<ThemeProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<ThemeProfile> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new ArgumentException("Profile name cannot be empty.", nameof(name));

        var profile = new ThemeProfile
        {
            Name = trimmedName,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        await _persistence.SaveThemeProfileAsync(profile, cancellationToken);
        _logger.LogInformation("Ranking profile created: {ProfileId}, Name={Name}", profile.Id, profile.Name);
        return profile;
    }

    public async Task<List<ThemeProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadAllThemeProfilesAsync(cancellationToken);
    }

    public async Task<ThemeProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadThemeProfileAsync(id, cancellationToken);
    }

    public async Task<ThemeProfile?> UpdateAsync(string id, string name, CancellationToken cancellationToken = default)
    {
        var existing = await _persistence.LoadThemeProfileAsync(id, cancellationToken);
        if (existing is null) return null;

        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new ArgumentException("Profile name cannot be empty.", nameof(name));

        existing.Name = trimmedName;
        existing.UpdatedUtc = DateTime.UtcNow;

        await _persistence.SaveThemeProfileAsync(existing, cancellationToken);
        _logger.LogInformation("Ranking profile updated: {ProfileId}, Name={Name}", existing.Id, existing.Name);
        return existing;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var deleted = await _persistence.DeleteThemeProfileAsync(id, cancellationToken);
        _logger.LogInformation("Ranking profile deleted: {ProfileId}, Success={Deleted}", id, deleted);
        return deleted;
    }

    public async Task SetDefaultAsync(string id, CancellationToken cancellationToken = default)
    {
        await _persistence.SetDefaultThemeProfileAsync(id, cancellationToken);
        _logger.LogInformation("Set default ranking profile: {ProfileId}", id);
    }

    public async Task<ThemeProfile?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadDefaultThemeProfileAsync(cancellationToken);
    }
}
