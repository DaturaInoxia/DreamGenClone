using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IScenarioDefinitionService
{
    Task<ScenarioDefinitionEntity?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScenarioDefinitionEntity>> GetAllAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);

    Task SaveAsync(ScenarioDefinitionEntity definition, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
