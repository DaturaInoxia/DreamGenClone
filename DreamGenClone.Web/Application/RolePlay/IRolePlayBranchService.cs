using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public interface IRolePlayBranchService
{
    Task<RolePlaySession?> ForkSessionAsync(
        string sourceSessionId,
        string branchTitle,
        int fromInteractionIndexInclusive,
        CancellationToken cancellationToken = default);
}
