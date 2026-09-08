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
        using var operationTimeout = CancellationTokenSource.CreateLinkedTokenSource(executionCancellation.Token);
        var hasStructuredTextTimeout = job.Lane is DurableJobLane.TextAnalysis or DurableJobLane.PromptCompilation;
        if (hasStructuredTextTimeout)
        {
            // A handler may perform more than one structured-text provider call in a single
            // execution (e.g. the decomposed multi-pass Beat Production flow). When it declares
            // that budget, scale the whole-run operation watchdog accordingly so a healthy
            // multi-pass run is not killed after a single provider-timeout window. Handlers
            // that do not opt in keep the default multiplier of 1.
            var multiplier = matchingHandlers.Count == 1
                && matchingHandlers[0] is IDurableJobOperationBudget budget
                ? Math.Max(1, budget.OperationTimeoutMultiplier)
                : 1;
            operationTimeout.CancelAfter(TimeSpan.FromSeconds(analyzer.Model.ProviderTimeoutSeconds * multiplier));
        }
        var renewalTask = RenewLeaseAsync(
            job,
            analyzer.LeaseSeconds,
            executionCancellation,
            () => Interlocked.Exchange(ref leaseLost, 1));

        if (failure is null)
        {
            try
            {
                var handlerTask = matchingHandlers[0].HandleAsync(job, operationTimeout.Token);
                var completedTask = await Task.WhenAny(
                    handlerTask,
                    Task.Delay(Timeout.InfiniteTimeSpan, operationTimeout.Token));
                if (completedTask != handlerTask)
                {
                    failure = new DurableJobFailureException(
                        hasStructuredTextTimeout ? "structured_text_timeout" : "durable_job_timeout",
                        hasStructuredTextTimeout
                            ? "The structured text provider exceeded its configured timeout."
                            : $"The durable {job.Lane} job exceeded its configured timeout.",
                        isTransient: true);
                    _ = ObserveHandlerCompletionAsync(handlerTask);
                }
                else
                {
                    await handlerTask;
                }
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
            : failure.Message;
        _logger.LogError(
            failure,
            "Durable job failed: Lane={Lane}, JobType={JobType}, JobId={JobId}, ErrorCode={ErrorCode}",
            job.Lane,
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

    private static async Task ObserveHandlerCompletionAsync(Task handlerTask)
    {
        try
        {
            await handlerTask.ConfigureAwait(false);
        }
        catch
        {
        }
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