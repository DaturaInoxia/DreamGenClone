using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class BackgroundCharacterProfileService : IBackgroundCharacterProfileService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<BackgroundCharacterProfileService> _logger;

    public BackgroundCharacterProfileService(ISqlitePersistence persistence, ILogger<BackgroundCharacterProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<BackgroundCharacterProfile> SaveAsync(BackgroundCharacterProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        profile.Name = (profile.Name ?? string.Empty).Trim();
        profile.Description = (profile.Description ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("Background character profile name is required.", nameof(profile));
        }

        var existing = await _persistence.LoadAllBackgroundCharacterProfilesAsync(cancellationToken);
        if (existing.Any(x => !string.Equals(x.Id, profile.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Background character profile name already exists.");
        }

        profile.UpdatedUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = Guid.NewGuid().ToString();
            profile.CreatedUtc = DateTime.UtcNow;
        }

        await _persistence.SaveBackgroundCharacterProfileAsync(profile, cancellationToken);
        _logger.LogInformation("Background character profile saved: {ProfileId}, Name={Name}", profile.Id, profile.Name);
        return profile;
    }

    public Task<List<BackgroundCharacterProfile>> ListAsync(CancellationToken cancellationToken = default)
        => _persistence.LoadAllBackgroundCharacterProfilesAsync(cancellationToken);

    public Task<BackgroundCharacterProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.LoadBackgroundCharacterProfileAsync(id, cancellationToken);

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.DeleteBackgroundCharacterProfileAsync(id, cancellationToken);
}