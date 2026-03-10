using DreamGenClone.Web.Domain.Story;

namespace DreamGenClone.Web.Application.Story;

public interface IStoryCommandService
{
    Task CaptureCheckpointAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> RewindAsync(string sessionId, int toBlockIndexInclusive, CancellationToken cancellationToken = default);

    Task<bool> UndoAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> AppendUserTextAsync(string sessionId, string content, CancellationToken cancellationToken = default);

    Task<bool> AppendInstructionAsync(string sessionId, string content, CancellationToken cancellationToken = default);
}
