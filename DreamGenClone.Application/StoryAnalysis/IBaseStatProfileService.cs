using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IBaseStatProfileService
{
    Task<BaseStatProfile> CreateAsync(string name, string description, IReadOnlyDictionary<string, int> defaultStats, CancellationToken cancellationToken = default);

    Task<List<BaseStatProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<BaseStatProfile?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<BaseStatProfile?> UpdateAsync(string id, string name, string description, IReadOnlyDictionary<string, int> defaultStats, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
