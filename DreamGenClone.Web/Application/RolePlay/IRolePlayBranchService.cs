using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public interface IRolePlayBranchService
{
    Task<RolePlaySession?> ForkSessionAsync(
        string sourceSessionId,
        string branchTitle,
        int fromInteractionIndexInclusive,
        CancellationToken cancellationToken = default);

    Task<RolePlaySession?> ForkAboveAsync(
        string sourceSessionId,
        string interactionId,
        string branchTitle,
        CancellationToken cancellationToken = default);

    Task<RolePlaySession?> ForkBelowAsync(
        string sourceSessionId,
        string interactionId,
        string branchTitle,
        CancellationToken cancellationToken = default);
}
