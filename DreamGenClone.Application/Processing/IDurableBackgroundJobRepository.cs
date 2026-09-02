using DreamGenClone.Domain.Processing;

namespace DreamGenClone.Application.Processing;

public interface IDurableBackgroundJobRepository
{
    Task<bool> TryEnqueueAsync(
        DurableBackgroundJob job,
        CancellationToken cancellationToken = default);

    Task<DurableBackgroundJob?> GetAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveJobsAsync(
        DurableJobLane lane,
        CancellationToken cancellationToken = default);

    Task<DurableBackgroundJob?> TryClaimNextAsync(
        DurableJobLane lane,
        string leaseOwner,
        DateTime claimedUtc,
        DateTime leaseExpiresUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryRenewLeaseAsync(
        string jobId,
        string leaseOwner,
        DateTime renewedUtc,
        DateTime leaseExpiresUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryScheduleRetryAsync(
        string jobId,
        string leaseOwner,
        string errorCode,
        string errorMessage,
        DateTime scheduledUtc,
        DateTime nextAttemptUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryCompleteAsync(
        string jobId,
        string leaseOwner,
        DateTime completedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryFailAsync(
        string jobId,
        string leaseOwner,
        string errorCode,
        string errorMessage,
        DateTime failedUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryCancelAsync(
        string jobId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default);

    Task<int> RecoverExpiredLeasesAsync(
        DateTime recoveredUtc,
        CancellationToken cancellationToken = default);
}