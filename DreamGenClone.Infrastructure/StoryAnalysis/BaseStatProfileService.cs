using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class BaseStatProfileService : IBaseStatProfileService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<BaseStatProfileService> _logger;

    public BaseStatProfileService(ISqlitePersistence persistence, ILogger<BaseStatProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<BaseStatProfile> CreateAsync(string name, string description, IReadOnlyDictionary<string, int> defaultStats, string targetGender, string targetRole, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);

        var trimmedName = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Base stat profile name cannot be empty.", nameof(name));
        }

        var existingProfiles = await _persistence.LoadAllBaseStatProfilesAsync(cancellationToken);
        if (existingProfiles.Any(x => string.Equals(x.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Base stat profile name already exists.");
        }

        var profile = new BaseStatProfile
        {
            Name = trimmedName,
            Description = (description ?? string.Empty).Trim(),
            TargetGender = CharacterGenderCatalog.NormalizeForProfile(targetGender),
            TargetRole = CharacterRoleCatalog.Normalize(targetRole),
            DefaultStats = NormalizeStats(defaultStats),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        await _persistence.SaveBaseStatProfileAsync(profile, cancellationToken);
        _logger.LogInformation("Base stat profile created: {BaseStatProfileId}, Name={Name}", profile.Id, profile.Name);
        return profile;
    }

    public Task<List<BaseStatProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        return ListInternalAsync(cancellationToken);
    }

    public Task<BaseStatProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        return _persistence.LoadBaseStatProfileAsync(id, cancellationToken);
    }

    public async Task<BaseStatProfile?> UpdateAsync(string id, string name, string description, IReadOnlyDictionary<string, int> defaultStats, string targetGender, string targetRole, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);

        var existing = await _persistence.LoadBaseStatProfileAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var trimmedName = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Base stat profile name cannot be empty.", nameof(name));
        }

        var allProfiles = await _persistence.LoadAllBaseStatProfilesAsync(cancellationToken);
        if (allProfiles.Any(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Base stat profile name already exists.");
        }

        existing.Name = trimmedName;
        existing.Description = (description ?? string.Empty).Trim();
        existing.TargetGender = CharacterGenderCatalog.NormalizeForProfile(targetGender);
        existing.TargetRole = CharacterRoleCatalog.Normalize(targetRole);
        existing.DefaultStats = NormalizeStats(defaultStats);
        existing.UpdatedUtc = DateTime.UtcNow;

        await _persistence.SaveBaseStatProfileAsync(existing, cancellationToken);
        _logger.LogInformation("Base stat profile updated: {BaseStatProfileId}, Name={Name}", existing.Id, existing.Name);
        return existing;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);

        var deleted = await _persistence.DeleteBaseStatProfileAsync(id, cancellationToken);
        _logger.LogInformation("Base stat profile deleted: {BaseStatProfileId}, Success={Deleted}", id, deleted);
        return deleted;
    }

    private async Task<List<BaseStatProfile>> ListInternalAsync(CancellationToken cancellationToken)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);
        return await _persistence.LoadAllBaseStatProfilesAsync(cancellationToken);
    }

    private async Task EnsureDefaultProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = await _persistence.LoadAllBaseStatProfilesAsync(cancellationToken);
        if (profiles.Count > 0)
        {
            return;
        }

        var femaleProfile = new BaseStatProfile
        {
            Name = "Female: Balanced Baseline",
            Description = "Neutral female baseline for the canonical adaptive stat model.",
            TargetGender = CharacterGenderCatalog.Female,
            TargetRole = CharacterRoleCatalog.Wife,
            DefaultStats = AdaptiveStatCatalog.CreateDefaultStatMap(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        var maleProfile = new BaseStatProfile
        {
            Name = "Male: Balanced Baseline",
            Description = "Neutral male baseline for the canonical adaptive stat model.",
            TargetGender = CharacterGenderCatalog.Male,
            TargetRole = CharacterRoleCatalog.Husband,
            DefaultStats = AdaptiveStatCatalog.CreateDefaultStatMap(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        await _persistence.SaveBaseStatProfileAsync(femaleProfile, cancellationToken);
        await _persistence.SaveBaseStatProfileAsync(maleProfile, cancellationToken);
        _logger.LogInformation("Seeded default male/female base stat profiles for role-play sessions.");
    }

    private static Dictionary<string, int> NormalizeStats(IReadOnlyDictionary<string, int> defaultStats)
    {
        return AdaptiveStatCatalog.NormalizeComplete(defaultStats);
    }
}
