using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IThemePreferenceService
{
    Task<ThemePreference> CreateAsync(string profileId, string name, string description, ThemeTier tier, CancellationToken cancellationToken = default);

    Task<List<ThemePreference>> ListAsync(CancellationToken cancellationToken = default);

    Task<List<ThemePreference>> ListByProfileAsync(string profileId, CancellationToken cancellationToken = default);

    Task<ThemePreference?> UpdateAsync(string id, string name, string description, ThemeTier tier, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
