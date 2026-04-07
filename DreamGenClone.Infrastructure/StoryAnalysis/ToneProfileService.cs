using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class ToneProfileService : IToneProfileService
{
    private sealed record DefaultToneProfile(string Name, string Description, ToneIntensity Intensity);

    private static readonly DefaultToneProfile[] PocDefaultProfiles =
    [
        new("Intro", "Low-intensity setup and atmosphere-first tone.", ToneIntensity.Intro),
        new("Emotional", "Emotion-forward intimacy and relationship focus.", ToneIntensity.Emotional),
        new("Suggestive", "Flirty, suggestive tone with restrained explicitness.", ToneIntensity.SuggestivePg12),
        new("Sensual", "Sensory, mature tone emphasizing tension and pacing.", ToneIntensity.SensualMature),
        new("Explicit", "Direct, explicit language and high-intensity delivery.", ToneIntensity.Explicit)
    ];

    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<ToneProfileService> _logger;

    public ToneProfileService(ISqlitePersistence persistence, ILogger<ToneProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<ToneProfile> CreateAsync(string name, string description, ToneIntensity intensity, CancellationToken cancellationToken = default)
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

        var profile = new ToneProfile
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

    public Task<List<ToneProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        return ListInternalAsync(cancellationToken);
    }

    public Task<ToneProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        return _persistence.LoadToneProfileAsync(id, cancellationToken);
    }

    public async Task<ToneProfile?> UpdateAsync(string id, string name, string description, ToneIntensity intensity, CancellationToken cancellationToken = default)
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

    private async Task<List<ToneProfile>> ListInternalAsync(CancellationToken cancellationToken)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);
        return await _persistence.LoadAllToneProfilesAsync(cancellationToken);
    }

    private async Task EnsureDefaultProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = await _persistence.LoadAllToneProfilesAsync(cancellationToken);
        if (profiles.Count > 0)
        {
            return;
        }

        foreach (var item in PocDefaultProfiles)
        {
            var profile = new ToneProfile
            {
                Name = item.Name,
                Description = item.Description,
                Intensity = item.Intensity,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            await _persistence.SaveToneProfileAsync(profile, cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} default tone profiles for adaptive POC.", PocDefaultProfiles.Length);
    }
}