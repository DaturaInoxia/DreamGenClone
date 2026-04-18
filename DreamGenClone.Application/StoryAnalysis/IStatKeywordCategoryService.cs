using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IStatKeywordCategoryService
{
    Task<List<StatKeywordCategory>> ListAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);

    Task<List<StatKeywordCategory>> ListEnabledAsync(CancellationToken cancellationToken = default);

    Task<StatKeywordCategory?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<StatKeywordCategory> SaveAsync(StatKeywordCategory category, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task SeedDefaultsAsync(CancellationToken cancellationToken = default);
}
