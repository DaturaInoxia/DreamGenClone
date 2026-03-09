namespace DreamGenClone.Domain.Contracts;

public interface ISessionService
{
    Task SaveAsync(Guid sessionId, string sessionType, string payloadJson, CancellationToken cancellationToken = default);

    Task<string?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
