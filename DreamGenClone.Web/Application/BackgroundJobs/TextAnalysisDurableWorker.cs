using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;

namespace DreamGenClone.Web.Application.BackgroundJobs;

public sealed class TextAnalysisDurableWorker : BackgroundService
{
    private static readonly DurableJobLane[] SupportedLanes =
    [
        DurableJobLane.TextAnalysis,
        DurableJobLane.PromptCompilation,
        DurableJobLane.ImageRender,
        DurableJobLane.ImageEdit
    ];

    private readonly IDurableBackgroundJobRepository _repository;
    private readonly IDurableBackgroundJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TextAnalysisDurableWorker> _logger;

    public TextAnalysisDurableWorker(
        IDurableBackgroundJobRepository repository,
        IDurableBackgroundJobQueue queue,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<TextAnalysisDurableWorker> logger)
    {
        _repository = repository;
        _queue = queue;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hasActiveJobs = false;
        foreach (var lane in SupportedLanes)
        {
            hasActiveJobs |= await _repository.HasActiveJobsAsync(lane, stoppingToken);
        }
        if (!hasActiveJobs)
            await _queue.WaitForWorkAsync(stoppingToken);

        await using var configurationScope = _scopeFactory.CreateAsyncScope();
        var analyzer = await configurationScope.ServiceProvider
            .GetRequiredService<ISceneBeatAnalyzerResolver>()
            .ResolveAsync(stoppingToken);

        _logger.LogInformation(
            "Durable worker started: Lanes={Lanes}, MaxConcurrentJobsPerLane={MaxConcurrentJobs}, PollMilliseconds={PollMilliseconds}",
            string.Join(',', SupportedLanes),
            analyzer.MaxConcurrentJobs,
            analyzer.PollIntervalMilliseconds);
        var workers = SupportedLanes.SelectMany(lane => Enumerable.Range(0, analyzer.MaxConcurrentJobs)
            .Select(index => RunWorkerAsync(lane, index, analyzer, stoppingToken)));
        await Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(
        DurableJobLane lane,
        int workerIndex,
        ResolvedSceneBeatAnalyzer analyzer,
        CancellationToken stoppingToken)
    {
        var leaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{lane}:{workerIndex}:{Guid.NewGuid():N}";
        var nextLeaseRecoveryUtc = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claimedUtc = _timeProvider.GetUtcNow().UtcDateTime;
                if (claimedUtc >= nextLeaseRecoveryUtc)
                {
                    var recoveredCount = await _repository.RecoverExpiredLeasesAsync(claimedUtc, stoppingToken);
                    if (recoveredCount > 0)
                        _logger.LogWarning("Recovered {RecoveredCount} expired durable job lease(s)", recoveredCount);
                    nextLeaseRecoveryUtc = claimedUtc.AddSeconds(Math.Max(1, analyzer.LeaseSeconds / 2d));
                }

                var job = await _repository.TryClaimNextAsync(
                    lane,
                    leaseOwner,
                    claimedUtc,
                    claimedUtc.AddSeconds(analyzer.LeaseSeconds),
                    stoppingToken);
                if (job is null)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(analyzer.PollIntervalMilliseconds),
                        _timeProvider,
                        stoppingToken);
                    continue;
                }

                await using var executionScope = _scopeFactory.CreateAsyncScope();
                await executionScope.ServiceProvider
                    .GetRequiredService<TextAnalysisDurableJobExecutor>()
                    .ExecuteAsync(job, analyzer, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Durable worker loop faulted and will resume: Lane={Lane}, WorkerIndex={WorkerIndex}",
                    lane,
                    workerIndex);
                try
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(Math.Max(1000, analyzer.PollIntervalMilliseconds)),
                        _timeProvider,
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}