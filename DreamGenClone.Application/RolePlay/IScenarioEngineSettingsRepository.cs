using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface IScenarioEngineSettingsRepository
{
    Task<ScenarioEngineSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ScenarioEngineSettings settings, CancellationToken cancellationToken = default);
}
