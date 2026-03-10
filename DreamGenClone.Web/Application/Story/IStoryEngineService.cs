using DreamGenClone.Web.Domain.Story;

namespace DreamGenClone.Web.Application.Story;

public interface IStoryEngineService
{
    Task<StorySession> CreateSessionAsync(string title, string? scenarioId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StorySession>> GetSessionsAsync(CancellationToken cancellationToken = default);

    Task<StorySession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<StorySession> SaveSessionAsync(StorySession session, CancellationToken cancellationToken = default);

    Task<StoryBlock> ContinueAsync(string sessionId, string? instruction = null, CancellationToken cancellationToken = default);
}
