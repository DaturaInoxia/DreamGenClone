using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Story;

namespace DreamGenClone.Web.Application.Sessions;

public interface ISessionService
{
    Task SaveStorySessionAsync(StorySession session, CancellationToken cancellationToken = default);

    Task SaveRolePlaySessionAsync(RolePlaySession session, CancellationToken cancellationToken = default);

    Task<StorySession?> LoadStorySessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<RolePlaySession?> LoadRolePlaySessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionListItem>> GetSessionsByTypeAsync(string sessionType, CancellationToken cancellationToken = default);

    Task<SessionExportEnvelope?> GetExportEnvelopeAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default);
}
