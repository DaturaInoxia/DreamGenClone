using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IThemeCatalogService
{
    Task<ThemeCatalogEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ThemeCatalogEntry>> GetAllAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);

    Task SaveAsync(ThemeCatalogEntry entry, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task SeedDefaultsAsync(CancellationToken cancellationToken = default);
}
