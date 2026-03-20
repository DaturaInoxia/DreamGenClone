using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class BehaviorModeService : IBehaviorModeService
{
    private readonly ILogger<BehaviorModeService> _logger;

    public BehaviorModeService(ILogger<BehaviorModeService> logger)
    {
        _logger = logger;
    }

    public void SetMode(RolePlaySession session, BehaviorMode mode)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.BehaviorMode = mode;
        _logger.LogInformation("Role-play mode changed for session {SessionId}: {Mode}", session.Id, mode);
    }

    public bool IsContinuationAllowed(BehaviorMode mode, ContinueAsActor actor)
    {
        return mode switch
        {
            BehaviorMode.TakeTurns => true,
            BehaviorMode.Spectate => actor != ContinueAsActor.You,
            BehaviorMode.NpcOnly => actor == ContinueAsActor.Npc,
            _ => false
        };
    }

    public IReadOnlyList<ContinueAsActor> GetAllowedActors(BehaviorMode mode)
    {
        return mode switch
        {
            BehaviorMode.TakeTurns => [ContinueAsActor.You, ContinueAsActor.Npc, ContinueAsActor.Custom],
            BehaviorMode.Spectate => [ContinueAsActor.Npc, ContinueAsActor.Custom],
            BehaviorMode.NpcOnly => [ContinueAsActor.Npc],
            _ => []
        };
    }
}
