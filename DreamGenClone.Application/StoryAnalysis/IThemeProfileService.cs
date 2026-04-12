using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IThemeProfileService
{
    Task<ThemeProfile> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<List<ThemeProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<ThemeProfile?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<ThemeProfile?> UpdateAsync(string id, string name, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task SetDefaultAsync(string id, CancellationToken cancellationToken = default);

    Task<ThemeProfile?> GetDefaultAsync(CancellationToken cancellationToken = default);
}
