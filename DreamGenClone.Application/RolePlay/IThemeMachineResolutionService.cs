using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface IThemeMachineResolutionService
{
    Task<RPThemeMachineDefinition?> ResolveAsync(
        string sessionId,
        string activeScenarioId,
        ThemeMachineSessionSnapshot? pinnedSnapshot,
        CancellationToken cancellationToken = default);
}
