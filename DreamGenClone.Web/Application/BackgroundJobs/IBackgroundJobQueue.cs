namespace DreamGenClone.Web.Application.BackgroundJobs;

public interface IBackgroundJobQueue
{
    bool Enqueue(string jobType, string payloadJson, string? dedupeKey = null);

    ValueTask<BackgroundJobEnvelope> DequeueAsync(CancellationToken cancellationToken);

    void MarkProcessing(string jobId);

    void MarkCompleted(string jobId);

    void MarkFailed(string jobId, string errorMessage);
}
