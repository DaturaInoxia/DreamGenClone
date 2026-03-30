using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class RankingProfileService : IRankingProfileService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<RankingProfileService> _logger;

    public RankingProfileService(ISqlitePersistence persistence, ILogger<RankingProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<RankingProfile> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new ArgumentException("Profile name cannot be empty.", nameof(name));

        var profile = new RankingProfile
        {
            Name = trimmedName,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        await _persistence.SaveRankingProfileAsync(profile, cancellationToken);
        _logger.LogInformation("Ranking profile created: {ProfileId}, Name={Name}", profile.Id, profile.Name);
        return profile;
    }

    public async Task<List<RankingProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadAllRankingProfilesAsync(cancellationToken);
    }

    public async Task<RankingProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadRankingProfileAsync(id, cancellationToken);
    }

    public async Task<RankingProfile?> UpdateAsync(string id, string name, CancellationToken cancellationToken = default)
    {
        var existing = await _persistence.LoadRankingProfileAsync(id, cancellationToken);
        if (existing is null) return null;

        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new ArgumentException("Profile name cannot be empty.", nameof(name));

        existing.Name = trimmedName;
        existing.UpdatedUtc = DateTime.UtcNow;

        await _persistence.SaveRankingProfileAsync(existing, cancellationToken);
        _logger.LogInformation("Ranking profile updated: {ProfileId}, Name={Name}", existing.Id, existing.Name);
        return existing;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var deleted = await _persistence.DeleteRankingProfileAsync(id, cancellationToken);
        _logger.LogInformation("Ranking profile deleted: {ProfileId}, Success={Deleted}", id, deleted);
        return deleted;
    }

    public async Task SetDefaultAsync(string id, CancellationToken cancellationToken = default)
    {
        await _persistence.SetDefaultRankingProfileAsync(id, cancellationToken);
        _logger.LogInformation("Set default ranking profile: {ProfileId}", id);
    }

    public async Task<RankingProfile?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        return await _persistence.LoadDefaultRankingProfileAsync(cancellationToken);
    }
}
