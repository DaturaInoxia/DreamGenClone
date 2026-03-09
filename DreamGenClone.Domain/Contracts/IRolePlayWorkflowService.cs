namespace DreamGenClone.Domain.Contracts;

public interface IRolePlayWorkflowService
{
    Task<string> ContinueAsAsync(Guid sessionId, string actor, CancellationToken cancellationToken = default);

    Task<Guid> ForkAsync(Guid sessionId, int interactionIndex, CancellationToken cancellationToken = default);
}
