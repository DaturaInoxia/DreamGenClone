using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class OverrideAuthorizationService : IOverrideAuthorizationService
{
    private readonly ILogger<OverrideAuthorizationService> _logger;

    public OverrideAuthorizationService(ILogger<OverrideAuthorizationService> logger)
    {
        _logger = logger;
    }

    public Task<OverrideAuthorizationResult> AuthorizeAsync(OverrideRequest request, CancellationToken cancellationToken = default)
    {
        var authorized = request.ActorRole is OverrideActorRole.Admin or OverrideActorRole.Operator
            || string.Equals(request.ActorId, request.SessionOwnerActorId, StringComparison.OrdinalIgnoreCase);

        if (!authorized)
        {
            _logger.LogInformation(
                RolePlayV2LogEvents.OverrideDenied,
                request.SessionId,
                request.ActorId,
                request.ActorRole,
                "Only session owner/operator/admin can override scenarios.");
        }

        return Task.FromResult(new OverrideAuthorizationResult
        {
            Authorized = authorized,
            Reason = authorized
                ? "Override authorized."
                : "Only session owner/operator/admin can override scenarios."
        });
    }
}
