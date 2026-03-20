namespace DreamGenClone.Web.Application.Sessions;

public interface ISessionCloneForkService
{
    Task<string?> CloneAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<string?> ForkAsync(string sessionId, int fromIndexInclusive, CancellationToken cancellationToken = default);
}
