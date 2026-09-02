using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;

namespace DreamGenClone.Web.Application.BackgroundJobs;

public sealed class TextAnalysisDurableJobExecutor
{
    private readonly IDurableBackgroundJobRepository _repository;
    private readonly IReadOnlyList<IDurableBackgroundJobHandler> _handlers;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TextAnalysisDurableJobExecutor> _logger;

    public TextAnalysisDurableJobExecutor(
        IDurableBackgroundJobRepository repository,
        IEnumerable<IDurableBackgroundJobHandler> handlers,
        TimeProvider timeProvider,
        ILogger<TextAnalysisDurableJobExecutor> logger)
    {
        _repository = repository;
        _handlers = handlers.ToList();
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        DurableBackgroundJob job,
        ResolvedSceneBeatAnalyzer analyzer,
        CancellationToken stoppingToken = default)
    {
        if (job.Status != DurableBackgroundJobStatus.Processing || string.IsNullOrWhiteSpace(job.LeaseOwner))
            throw new InvalidOperationException("A claimed durable job with a lease owner is required.");
        if (job.Lane != DurableJobLane.TextAnalysis)
            throw new InvalidOperationException("The TextAnalysis executor cannot process another lane.");

        var matchingHandlers = _handlers
            .Where(handler => string.Equals(handler.JobType, job.JobType, StringComparison.Ordinal))
            .ToList();
        Exception? failure = matchingHandlers.Count switch
        {
            0 => new DurableJobFailureException(
                "durable_handler_missing",
                $"No durable handler is registered for job type '{job.JobType}'.",
                isTransient: false),
            > 1 => new DurableJobFailureException(
                "durable_handler_ambiguous",
                $"Multiple durable handlers are registered for job type '{job.JobType}'.",
                isTransient: false),
            _ => null
        };

        var leaseLost = 0;
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewalTask = RenewLeaseAsync(
            job,
            analyzer.LeaseSeconds,
            executionCancellation,
            () => Interlocked.Exchange(ref leaseLost, 1));

        if (failure is null)
        {
            try
            {
                await matchingHandlers[0].HandleAsync(job, executionCancellation.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }

        executionCancellation.Cancel();
        try
        {
            await renewalTask;
        }
        catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
        {
        }

        if (Volatile.Read(ref leaseLost) != 0 || stoppingToken.IsCancellationRequested)
            return;

        var transitionedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        if (failure is null)
        {
            await _repository.TryCompleteAsync(job.Id, job.LeaseOwner, transitionedUtc, stoppingToken);
            return;
        }

        if (failure is DurableJobFailureException { IsTransient: true } transient
            && job.AttemptCount <= analyzer.RetryDelaysSeconds.Count
            && job.AttemptCount < job.MaxAttempts)
        {
            var retryDelay = analyzer.RetryDelaysSeconds[job.AttemptCount - 1];
            await _repository.TryScheduleRetryAsync(
                job.Id,
                job.LeaseOwner,
                transient.ErrorCode,
                transient.Message,
                transitionedUtc,
                transitionedUtc.AddSeconds(retryDelay),
                stoppingToken);
            return;
        }

        var errorCode = failure is DurableJobFailureException classified
            ? classified.ErrorCode
            : "durable_handler_unclassified_failure";
        var errorMessage = failure is DurableJobFailureException durableFailure
            ? durableFailure.Message
            : "The durable job handler failed permanently.";
        _logger.LogError(
            failure,
            "TextAnalysis durable job failed: JobType={JobType}, JobId={JobId}, ErrorCode={ErrorCode}",
            job.JobType,
            job.Id,
            errorCode);
        await _repository.TryFailAsync(
            job.Id,
            job.LeaseOwner,
            errorCode,
            errorMessage,
            transitionedUtc,
            stoppingToken);
    }

    private async Task RenewLeaseAsync(
        DurableBackgroundJob job,
        int leaseSeconds,
        CancellationTokenSource executionCancellation,
        Action onLeaseLost)
    {
        var renewalInterval = TimeSpan.FromSeconds(leaseSeconds / 2d);
        while (!executionCancellation.IsCancellationRequested)
        {
            await Task.Delay(renewalInterval, _timeProvider, executionCancellation.Token);
            var renewedUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var renewed = await _repository.TryRenewLeaseAsync(
                job.Id,
                job.LeaseOwner!,
                renewedUtc,
                renewedUtc.AddSeconds(leaseSeconds),
                executionCancellation.Token);
            if (renewed)
                continue;

            onLeaseLost();
            executionCancellation.Cancel();
            return;
        }
    }
}