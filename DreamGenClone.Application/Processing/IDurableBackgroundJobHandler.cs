using DreamGenClone.Domain.Processing;

namespace DreamGenClone.Application.Processing;

public interface IDurableBackgroundJobHandler
{
    string JobType { get; }

    Task HandleAsync(
        DurableBackgroundJob job,
        CancellationToken cancellationToken = default);
}