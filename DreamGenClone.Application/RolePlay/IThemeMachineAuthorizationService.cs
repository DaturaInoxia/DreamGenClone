using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface IThemeMachineAuthorizationService
{
    Task<ThemeMachineAuthorizationResult> AuthorizeMutationAsync(
        ThemeMachineAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}