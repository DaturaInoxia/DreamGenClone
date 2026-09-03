using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.Sessions;

namespace DreamGenClone.Web.Application.RolePlay;

public interface ISceneImageProductionSessionGuard
{
    Task RequireCurrentAsync(string sessionId, CancellationToken cancellationToken = default);
}

public sealed class SceneImageProductionSessionGuard : ISceneImageProductionSessionGuard
{
    private readonly ISessionService _sessionService;

    public SceneImageProductionSessionGuard(ISessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public async Task RequireCurrentAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("Scene image production requires a session id.");

        var session = await _sessionService.LoadRolePlaySessionAsync(sessionId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{sessionId}' was not found.");
        if (session.SceneImageProductionSchemaGeneration != SceneImageProductionSchema.CurrentGeneration)
        {
            throw new InvalidOperationException(
                $"Role-play session '{session.Id}' does not use scene image production schema generation " +
                $"{SceneImageProductionSchema.CurrentGeneration}. Create a new role-play session to use Production Studio.");
        }
    }
}