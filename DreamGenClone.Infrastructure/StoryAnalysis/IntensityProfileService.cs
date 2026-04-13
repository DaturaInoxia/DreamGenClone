using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class IntensityProfileService : IIntensityProfileService
{
    private sealed record DefaultToneProfile(string Name, string Description, IntensityLevel Intensity);

    private static readonly DefaultToneProfile[] PocDefaultProfiles =
    [
        new(IntensityLadder.GetLabel(IntensityLevel.Intro), IntensityLadder.GetDefaultDescription(IntensityLevel.Intro), IntensityLevel.Intro),
        new(IntensityLadder.GetLabel(IntensityLevel.Emotional), IntensityLadder.GetDefaultDescription(IntensityLevel.Emotional), IntensityLevel.Emotional),
        new(IntensityLadder.GetLabel(IntensityLevel.SuggestivePg12), IntensityLadder.GetDefaultDescription(IntensityLevel.SuggestivePg12), IntensityLevel.SuggestivePg12),
        new(IntensityLadder.GetLabel(IntensityLevel.SensualMature), IntensityLadder.GetDefaultDescription(IntensityLevel.SensualMature), IntensityLevel.SensualMature),
        new(IntensityLadder.GetLabel(IntensityLevel.Explicit), IntensityLadder.GetDefaultDescription(IntensityLevel.Explicit), IntensityLevel.Explicit),
        new(IntensityLadder.GetLabel(IntensityLevel.Hardcore), IntensityLadder.GetDefaultDescription(IntensityLevel.Hardcore), IntensityLevel.Hardcore)
    ];

    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<IntensityProfileService> _logger;

    public IntensityProfileService(ISqlitePersistence persistence, ILogger<IntensityProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<IntensityProfile> CreateAsync(string name, string description, IntensityLevel intensity, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);

        var existingProfiles = await _persistence.LoadAllToneProfilesAsync(cancellationToken);
        if (existingProfiles.Count >= PocDefaultProfiles.Length)
        {
            throw new InvalidOperationException($"POC is limited to {PocDefaultProfiles.Length} tone profiles.");
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Tone profile name cannot be empty.", nameof(name));
        }

        if (existingProfiles.Any(x => string.Equals(x.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Tone profile name already exists.");
        }

        if (existingProfiles.Any(x => x.Intensity == intensity))
        {
            throw new InvalidOperationException($"An intensity profile already exists for level '{intensity}'. Exactly one profile per intensity level is supported.");
        }

        var profile = new IntensityProfile
        {
            Name = trimmedName,
            Description = description?.Trim() ?? string.Empty,
            Intensity = intensity,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        await _persistence.SaveToneProfileAsync(profile, cancellationToken);
        _logger.LogInformation("Tone profile created: {ToneProfileId}, Name={Name}", profile.Id, profile.Name);
        return profile;
    }

    public Task<List<IntensityProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        return ListInternalAsync(cancellationToken);
    }

    public Task<IntensityProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        return _persistence.LoadToneProfileAsync(id, cancellationToken);
    }

    public async Task<IntensityProfile?> UpdateAsync(string id, string name, string description, IntensityLevel intensity, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);

        var existing = await _persistence.LoadToneProfileAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Tone profile name cannot be empty.", nameof(name));
        }

        var profiles = await _persistence.LoadAllToneProfilesAsync(cancellationToken);
        if (profiles.Any(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Tone profile name already exists.");
        }

        if (profiles.Any(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)
            && x.Intensity == intensity))
        {
            throw new InvalidOperationException($"An intensity profile already exists for level '{intensity}'. Exactly one profile per intensity level is supported.");
        }

        existing.Name = trimmedName;
        existing.Description = description?.Trim() ?? string.Empty;
        existing.Intensity = intensity;
        existing.UpdatedUtc = DateTime.UtcNow;

        await _persistence.SaveToneProfileAsync(existing, cancellationToken);
        _logger.LogInformation("Tone profile updated: {ToneProfileId}, Name={Name}", existing.Id, existing.Name);
        return existing;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);

        var deleted = await _persistence.DeleteToneProfileAsync(id, cancellationToken);
        _logger.LogInformation("Tone profile deleted: {ToneProfileId}, Success={Deleted}", id, deleted);
        return deleted;
    }

    private async Task<List<IntensityProfile>> ListInternalAsync(CancellationToken cancellationToken)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);
        return await _persistence.LoadAllToneProfilesAsync(cancellationToken);
    }

    private async Task EnsureDefaultProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = await _persistence.LoadAllToneProfilesAsync(cancellationToken);
        var changed = false;

        foreach (var item in PocDefaultProfiles)
        {
            var existing = profiles.FirstOrDefault(x => x.Intensity == item.Intensity);
            if (existing is null)
            {
                var profile = new IntensityProfile
                {
                    Name = item.Name,
                    Description = item.Description,
                    Intensity = item.Intensity,
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow
                };

                await _persistence.SaveToneProfileAsync(profile, cancellationToken);
                changed = true;
                continue;
            }

            if (!string.Equals(existing.Name, item.Name, StringComparison.Ordinal)
                || !string.Equals(existing.Description, item.Description, StringComparison.Ordinal))
            {
                existing.Name = item.Name;
                existing.Description = item.Description;
                existing.UpdatedUtc = DateTime.UtcNow;
                await _persistence.SaveToneProfileAsync(existing, cancellationToken);
                changed = true;
            }
        }

        if (changed)
        {
            _logger.LogInformation("Ensured {Count} canonical tone profiles for adaptive POC.", PocDefaultProfiles.Length);
        }
    }
}