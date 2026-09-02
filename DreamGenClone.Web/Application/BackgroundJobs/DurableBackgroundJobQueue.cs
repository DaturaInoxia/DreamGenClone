using DreamGenClone.Application.Processing;
using DreamGenClone.Domain.Processing;

namespace DreamGenClone.Web.Application.BackgroundJobs;

public sealed class DurableBackgroundJobQueue : IDurableBackgroundJobQueue
{
    private readonly IDurableBackgroundJobRepository _repository;
    private readonly SemaphoreSlim _workSignal = new(0, 1);

    public DurableBackgroundJobQueue(IDurableBackgroundJobRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> TryEnqueueAsync(
        DurableBackgroundJob job,
        CancellationToken cancellationToken = default)
    {
        var enqueued = await _repository.TryEnqueueAsync(job, cancellationToken);
        if (enqueued && _workSignal.CurrentCount == 0)
            _workSignal.Release();
        return enqueued;
    }

    public Task<DurableBackgroundJob?> GetAsync(
        string jobId,
        CancellationToken cancellationToken = default)
        => _repository.GetAsync(jobId, cancellationToken);

    public Task<bool> TryCancelAsync(
        string jobId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default)
        => _repository.TryCancelAsync(jobId, cancelledUtc, cancellationToken);

    public Task WaitForWorkAsync(CancellationToken cancellationToken = default)
        => _workSignal.WaitAsync(cancellationToken);
}