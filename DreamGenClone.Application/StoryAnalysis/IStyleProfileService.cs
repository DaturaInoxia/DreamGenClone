using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IStyleProfileService
{
    Task<StyleProfile> CreateAsync(string name, string description, string example, string ruleOfThumb, CancellationToken cancellationToken = default);

    Task<List<StyleProfile>> ListAsync(CancellationToken cancellationToken = default);

    Task<StyleProfile?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<StyleProfile?> UpdateAsync(string id, string name, string description, string example, string ruleOfThumb, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}