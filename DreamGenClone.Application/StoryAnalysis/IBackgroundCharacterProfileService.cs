using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IBackgroundCharacterProfileService
{
    Task<BackgroundCharacterProfile> SaveAsync(BackgroundCharacterProfile profile, CancellationToken cancellationToken = default);

    Task<List<BackgroundCharacterProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<BackgroundCharacterProfile?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}