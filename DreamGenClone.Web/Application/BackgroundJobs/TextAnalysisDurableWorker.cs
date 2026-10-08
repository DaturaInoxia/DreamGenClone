using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;

namespace DreamGenClone.Web.Application.BackgroundJobs;

public sealed class TextAnalysisDurableWorker : BackgroundService
{
    private static readonly DurableJobLane[] SupportedLanes =
    [
        DurableJobLane.TextAnalysis,
        DurableJobLane.PromptCompilation,
        DurableJobLane.ImageRender,
        DurableJobLane.ImageEdit,
        DurableJobLane.VideoRender
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

        // The video lane's bounds are its OWN configured function default: a video render occupies the single GPU
        // for ~25 to ~100 minutes, so it must not inherit the analyzer's concurrency or poll interval. When that
        // configuration is missing or invalid the lane is disabled with an explicit error; no other lane's
        // behaviour changes and no analyzer value is substituted for it.
        DurableLaneSettings? videoLane = null;
        try
        {
            videoLane = await configurationScope.ServiceProvider
                .GetRequiredService<ISceneVideoLaneResolver>()
                .ResolveAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "The video render lane is disabled because its configuration could not be resolved. Image and text "
                + "lanes are unaffected; fix the '{Function}' function default in Model Manager.",
                AppFunction.RolePlaySceneVideo);
        }

        DurableLaneSettings SettingsFor(DurableJobLane lane) =>
            lane == DurableJobLane.VideoRender && videoLane is not null
                ? videoLane
                : new DurableLaneSettings(
                    Lane: lane,
                    MaxConcurrentJobs: analyzer.MaxConcurrentJobs,
                    LeaseSeconds: analyzer.LeaseSeconds,
                    PollIntervalMilliseconds: analyzer.PollIntervalMilliseconds,
                    RetryDelaysSeconds: analyzer.RetryDelaysSeconds,
                    ProviderTimeoutSeconds: analyzer.Model.ProviderTimeoutSeconds);

        var activeLanes = SupportedLanes
            .Where(lane => lane != DurableJobLane.VideoRender || videoLane is not null)
            .ToList();

        _logger.LogInformation(
            "Durable worker started: Lanes={Lanes}, MaxConcurrentJobsPerLane={MaxConcurrentJobs}, PollMilliseconds={PollMilliseconds}, VideoLane={VideoLane}",
            string.Join(',', activeLanes),
            string.Join(',', activeLanes.Select(lane => $"{lane}:{SettingsFor(lane).MaxConcurrentJobs}")),
            string.Join(',', activeLanes.Select(lane => $"{lane}:{SettingsFor(lane).PollIntervalMilliseconds}")),
            videoLane is null ? "disabled" : $"enabled (lease {videoLane.LeaseSeconds}s)");
        var workers = activeLanes.SelectMany(lane => Enumerable.Range(0, SettingsFor(lane).MaxConcurrentJobs)
            .Select(index => RunWorkerAsync(lane, index, SettingsFor(lane), stoppingToken)));
        await Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(
        DurableJobLane lane,
        int workerIndex,
        DurableLaneSettings laneSettings,
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
                    nextLeaseRecoveryUtc = claimedUtc.AddSeconds(Math.Max(1, laneSettings.LeaseSeconds / 2d));
                }

                var job = await _repository.TryClaimNextAsync(
                    lane,
                    leaseOwner,
                    claimedUtc,
                    claimedUtc.AddSeconds(laneSettings.LeaseSeconds),
                    stoppingToken);
                if (job is null)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(laneSettings.PollIntervalMilliseconds),
                        _timeProvider,
                        stoppingToken);
                    continue;
                }

                await using var executionScope = _scopeFactory.CreateAsyncScope();
                await executionScope.ServiceProvider
                    .GetRequiredService<TextAnalysisDurableJobExecutor>()
                    .ExecuteAsync(job, laneSettings, stoppingToken);
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
                        TimeSpan.FromMilliseconds(Math.Max(1000, laneSettings.PollIntervalMilliseconds)),
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