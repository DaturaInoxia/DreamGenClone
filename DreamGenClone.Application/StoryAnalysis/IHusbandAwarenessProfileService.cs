using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IHusbandAwarenessProfileService
{
    Task<HusbandAwarenessProfile> SaveAsync(HusbandAwarenessProfile profile, CancellationToken cancellationToken = default);

    Task<List<HusbandAwarenessProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<HusbandAwarenessProfile?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
