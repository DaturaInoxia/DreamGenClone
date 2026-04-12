using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IIntensityProfileService
{
    Task<IntensityProfile> CreateAsync(string name, string description, IntensityLevel intensity, CancellationToken cancellationToken = default);

    Task<List<IntensityProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<IntensityProfile?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<IntensityProfile?> UpdateAsync(string id, string name, string description, IntensityLevel intensity, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}