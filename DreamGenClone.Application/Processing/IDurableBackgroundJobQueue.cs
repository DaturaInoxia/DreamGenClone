using DreamGenClone.Domain.Processing;

namespace DreamGenClone.Application.Processing;

public interface IDurableBackgroundJobQueue
{
    Task<bool> TryEnqueueAsync(
        DurableBackgroundJob job,
        CancellationToken cancellationToken = default);

    Task<DurableBackgroundJob?> GetAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    Task<bool> TryCancelAsync(
        string jobId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default);

    Task WaitForWorkAsync(CancellationToken cancellationToken = default);
}