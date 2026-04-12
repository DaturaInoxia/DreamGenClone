using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface ISteeringProfileService
{
    Task<SteeringProfile> CreateAsync(string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken cancellationToken = default);

    Task<List<SteeringProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<SteeringProfile?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<SteeringProfile?> UpdateAsync(string id, string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}