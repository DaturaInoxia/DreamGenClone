using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;

namespace DreamGenClone.Web.Application.BackgroundJobs;

public sealed class TextAnalysisDurableWorker : BackgroundService
{
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
        if (!await _repository.HasActiveJobsAsync(DurableJobLane.TextAnalysis, stoppingToken))
            await _queue.WaitForWorkAsync(stoppingToken);

        await using var configurationScope = _scopeFactory.CreateAsyncScope();
        var analyzer = await configurationScope.ServiceProvider
            .GetRequiredService<ISceneBeatAnalyzerResolver>()
            .ResolveAsync(stoppingToken);

        _logger.LogInformation(
            "TextAnalysis durable worker started: MaxConcurrentJobs={MaxConcurrentJobs}, PollMilliseconds={PollMilliseconds}",
            analyzer.MaxConcurrentJobs,
            analyzer.PollIntervalMilliseconds);
        var workers = Enumerable.Range(0, analyzer.MaxConcurrentJobs)
            .Select(index => RunWorkerAsync(index, analyzer, stoppingToken));
        await Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(
        int workerIndex,
        ResolvedSceneBeatAnalyzer analyzer,
        CancellationToken stoppingToken)
    {
        var leaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:text-analysis:{workerIndex}:{Guid.NewGuid():N}";
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimedUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var job = await _repository.TryClaimNextAsync(
                DurableJobLane.TextAnalysis,
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
    }
}