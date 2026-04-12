namespace DreamGenClone.Application.RolePlay;

public interface IOverrideAuthorizationService
{
	Task<OverrideAuthorizationResult> AuthorizeAsync(OverrideRequest request, CancellationToken cancellationToken = default);
}
