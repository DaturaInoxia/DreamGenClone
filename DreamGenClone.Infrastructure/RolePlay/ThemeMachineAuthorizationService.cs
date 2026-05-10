using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ThemeMachineAuthorizationService : IThemeMachineAuthorizationService
{
    private readonly ILogger<ThemeMachineAuthorizationService> _logger;

    public ThemeMachineAuthorizationService(ILogger<ThemeMachineAuthorizationService> logger)
    {
        _logger = logger;
    }

    public Task<ThemeMachineAuthorizationResult> AuthorizeMutationAsync(
        ThemeMachineAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new InvalidOperationException("SessionId is required for theme machine authorization.");
        }

        if (string.IsNullOrWhiteSpace(request.ActorId))
        {
            throw new InvalidOperationException("ActorId is required for theme machine authorization.");
        }

        if (string.IsNullOrWhiteSpace(request.Operation))
        {
            throw new InvalidOperationException("Operation is required for theme machine authorization.");
        }

        var actorRole = request.ActorRole?.Trim();
        var authorized = string.Equals(actorRole, "Admin", StringComparison.OrdinalIgnoreCase);

        if (!authorized)
        {
            _logger.LogWarning(
                "Theme machine mutation denied. SessionId={SessionId} ActorId={ActorId} ActorRole={ActorRole} Operation={Operation}",
                request.SessionId,
                request.ActorId,
                request.ActorRole,
                request.Operation);
        }

        return Task.FromResult(new ThemeMachineAuthorizationResult
        {
            Authorized = authorized,
            Reason = authorized
                ? "Theme machine mutation authorized."
                : "Theme machine mutation requires Admin role."
        });
    }
}
