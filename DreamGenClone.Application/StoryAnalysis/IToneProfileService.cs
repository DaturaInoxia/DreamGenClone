using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IToneProfileService
{
    Task<ToneProfile> CreateAsync(string name, string description, ToneIntensity intensity, CancellationToken cancellationToken = default);

    Task<List<ToneProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<ToneProfile?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<ToneProfile?> UpdateAsync(string id, string name, string description, ToneIntensity intensity, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}