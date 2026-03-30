using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IRankingProfileService
{
    Task<RankingProfile> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task<List<RankingProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<RankingProfile?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<RankingProfile?> UpdateAsync(string id, string name, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task SetDefaultAsync(string id, CancellationToken cancellationToken = default);

    Task<RankingProfile?> GetDefaultAsync(CancellationToken cancellationToken = default);
}
