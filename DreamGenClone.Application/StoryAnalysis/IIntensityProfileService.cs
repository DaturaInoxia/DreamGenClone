using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IIntensityProfileService
{
    Task<IntensityProfile> CreateAsync(
        string name,
        string description,
        IntensityLevel intensity,
        int buildUpPhaseOffset,
        int committedPhaseOffset,
        int approachingPhaseOffset,
        int climaxPhaseOffset,
        int resetPhaseOffset,
        CancellationToken cancellationToken = default);

    Task<List<IntensityProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<IntensityProfile?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<IntensityProfile?> UpdateAsync(
        string id,
        string name,
        string description,
        IntensityLevel intensity,
        int buildUpPhaseOffset,
        int committedPhaseOffset,
        int approachingPhaseOffset,
        int climaxPhaseOffset,
        int resetPhaseOffset,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}