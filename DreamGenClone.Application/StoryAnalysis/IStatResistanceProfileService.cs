using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IStatResistanceProfileService
{
    Task<StatResistanceProfile> SaveAsync(StatResistanceProfile profile, CancellationToken cancellationToken = default);
    Task<List<StatResistanceProfile>> ListAsync(CancellationToken cancellationToken = default);
    Task<StatResistanceProfile?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<StatResistanceProfile?> GetDefaultAsync(CancellationToken cancellationToken = default);
}
