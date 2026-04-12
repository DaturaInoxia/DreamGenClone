using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class ThemePreferenceService : IThemePreferenceService
{
    private readonly ISqlitePersistence _persistence;
    private readonly IThemeCatalogService _themeCatalogService;
    private readonly ILogger<ThemePreferenceService> _logger;

    public ThemePreferenceService(ISqlitePersistence persistence, IThemeCatalogService themeCatalogService, ILogger<ThemePreferenceService> logger)
    {
        _persistence = persistence;
        _themeCatalogService = themeCatalogService;
        _logger = logger;
    }

    public async Task<ThemePreference> CreateAsync(string profileId, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("ProfileId cannot be empty.", nameof(profileId));
        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new ArgumentException("Theme name cannot be empty.", nameof(name));
        var trimmedDescription = description?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedDescription))
            throw new ArgumentException("Theme description cannot be empty.", nameof(description));
        if (!Enum.IsDefined(tier))
            throw new ArgumentOutOfRangeException(nameof(tier), "Invalid theme tier.");

        var preference = new ThemePreference
        {
            ProfileId = profileId,
            Name = trimmedName,
            Description = trimmedDescription,
            Tier = tier,
            CatalogId = catalogId ?? string.Empty,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        await _persistence.SaveThemePreferenceAsync(preference, cancellationToken);
        _logger.LogInformation("Theme preference created: {Id}, ProfileId={ProfileId}, Name={Name}, Tier={Tier}", preference.Id, profileId, preference.Name, preference.Tier);
        return preference;
    }

    public async Task<List<ThemePreference>> ListAsync(CancellationToken cancellationToken = default)
    {
        var themes = await _persistence.LoadAllThemePreferencesAsync(cancellationToken);
        _logger.LogInformation("Theme preferences list loaded: {Count}", themes.Count);
        return themes;
    }

    public async Task<List<ThemePreference>> ListByProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var themes = await _persistence.LoadThemePreferencesByProfileAsync(profileId, cancellationToken);
        _logger.LogInformation("Theme preferences list loaded for profile {ProfileId}: {Count}", profileId, themes.Count);
        return themes;
    }

    public async Task<ThemePreference?> UpdateAsync(string id, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken cancellationToken = default)
    {
        var existing = await _persistence.LoadThemePreferenceAsync(id, cancellationToken);
        if (existing is null)
            return null;

        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new ArgumentException("Theme name cannot be empty.", nameof(name));
        var trimmedDescription = description?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedDescription))
            throw new ArgumentException("Theme description cannot be empty.", nameof(description));
        if (!Enum.IsDefined(tier))
            throw new ArgumentOutOfRangeException(nameof(tier), "Invalid theme tier.");

        existing.Name = trimmedName;
        existing.Description = trimmedDescription;
        existing.Tier = tier;
        existing.CatalogId = catalogId ?? string.Empty;
        existing.UpdatedUtc = DateTime.UtcNow;

        await _persistence.SaveThemePreferenceAsync(existing, cancellationToken);
        _logger.LogInformation("Theme preference updated: {Id}, Name={Name}, Tier={Tier}", existing.Id, existing.Name, existing.Tier);
        return existing;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var deleted = await _persistence.DeleteThemePreferenceAsync(id, cancellationToken);
        _logger.LogInformation("Theme preference deleted: {Id}, Success={Deleted}", id, deleted);
        return deleted;
    }

    public async Task<int> AutoLinkToCatalogAsync(CancellationToken cancellationToken = default)
    {
        var allPreferences = await _persistence.LoadAllThemePreferencesAsync(cancellationToken);
        var catalogEntries = await _themeCatalogService.GetAllAsync(includeDisabled: false, cancellationToken);

        var linked = 0;
        var unlinked = new List<string>();

        foreach (var pref in allPreferences)
        {
            if (!string.IsNullOrWhiteSpace(pref.CatalogId)) continue;

            var match = catalogEntries.FirstOrDefault(e =>
                string.Equals(e.Label, pref.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Id, pref.Name, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                pref.CatalogId = match.Id;
                pref.UpdatedUtc = DateTime.UtcNow;
                await _persistence.SaveThemePreferenceAsync(pref, cancellationToken);
                linked++;
            }
            else
            {
                unlinked.Add($"{pref.Name} (profile={pref.ProfileId})");
            }
        }

        if (unlinked.Count > 0)
        {
            _logger.LogInformation("Unlinked theme preferences (no catalog match): {Unlinked}", string.Join(", ", unlinked));
        }

        _logger.LogInformation("Auto-linked {Linked} theme preferences to catalog entries", linked);
        return linked;
    }
}
