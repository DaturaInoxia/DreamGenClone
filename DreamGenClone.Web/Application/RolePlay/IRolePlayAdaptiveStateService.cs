using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

public interface IRolePlayAdaptiveStateService
{
    Task<RolePlayAdaptiveState> UpdateFromInteractionAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        CancellationToken cancellationToken = default);

    Task SeedFromScenarioAsync(
        RolePlaySession session,
        Scenario scenario,
        CancellationToken cancellationToken = default);
}