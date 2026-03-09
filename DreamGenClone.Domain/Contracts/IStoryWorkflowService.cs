namespace DreamGenClone.Domain.Contracts;

public interface IStoryWorkflowService
{
    Task<string> ContinueAsync(Guid sessionId, string instruction, CancellationToken cancellationToken = default);

    Task RewindAsync(Guid sessionId, int toStepIndex, CancellationToken cancellationToken = default);
}
