using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IStatWillingnessProfileService
{
    Task<StatWillingnessProfile> SaveAsync(StatWillingnessProfile profile, CancellationToken cancellationToken = default);

    Task<List<StatWillingnessProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<StatWillingnessProfile?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<StatWillingnessProfile?> GetDefaultAsync(CancellationToken cancellationToken = default);
}
