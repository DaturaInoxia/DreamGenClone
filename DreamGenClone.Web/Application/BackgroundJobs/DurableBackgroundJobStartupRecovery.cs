using DreamGenClone.Application.Processing;

namespace DreamGenClone.Web.Application.BackgroundJobs;

public sealed class DurableBackgroundJobStartupRecovery : IHostedService
{
    private readonly IDurableBackgroundJobRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DurableBackgroundJobStartupRecovery> _logger;

    public DurableBackgroundJobStartupRecovery(
        IDurableBackgroundJobRepository repository,
        TimeProvider timeProvider,
        ILogger<DurableBackgroundJobStartupRecovery> logger)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var recoveredUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var recoveredCount = await _repository.RecoverExpiredLeasesAsync(recoveredUtc, cancellationToken);
        _logger.LogInformation(
            "Durable background job startup recovery processed {RecoveredCount} expired leases",
            recoveredCount);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}