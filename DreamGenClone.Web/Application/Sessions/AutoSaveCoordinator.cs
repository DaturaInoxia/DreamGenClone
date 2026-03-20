using CoreAutoSaveCoordinator = DreamGenClone.Application.Sessions.IAutoSaveCoordinator;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Story;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Sessions;

public sealed class AutoSaveCoordinator
{
    private readonly CoreAutoSaveCoordinator _coreAutoSave;
    private readonly ISessionService _sessionService;
    private readonly ILogger<AutoSaveCoordinator> _logger;

    public AutoSaveCoordinator(CoreAutoSaveCoordinator coreAutoSave, ISessionService sessionService, ILogger<AutoSaveCoordinator> logger)
    {
        _coreAutoSave = coreAutoSave;
        _sessionService = sessionService;
        _logger = logger;
    }

    public void QueueStorySessionSave(StorySession session, string reason)
    {
        _coreAutoSave.RequestSave(reason, async cancellationToken =>
        {
            await _sessionService.SaveStorySessionAsync(session, cancellationToken);
            _logger.LogInformation("Autosaved story session {SessionId} due to {Reason}", session.Id, reason);
        });
    }

    public void QueueRolePlaySessionSave(RolePlaySession session, string reason)
    {
        _coreAutoSave.RequestSave(reason, async cancellationToken =>
        {
            await _sessionService.SaveRolePlaySessionAsync(session, cancellationToken);
            _logger.LogInformation("Autosaved role-play session {SessionId} due to {Reason}", session.Id, reason);
        });
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return _coreAutoSave.FlushAsync(cancellationToken);
    }
}
