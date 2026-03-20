using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public interface IBehaviorModeService
{
    void SetMode(RolePlaySession session, BehaviorMode mode);

    bool IsContinuationAllowed(BehaviorMode mode, ContinueAsActor actor);

    IReadOnlyList<ContinueAsActor> GetAllowedActors(BehaviorMode mode);
}
