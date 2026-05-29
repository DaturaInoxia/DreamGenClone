using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DreamGenClone.Web.Application.BackgroundJobs;

public sealed class SemanticBackgroundJobWorker : BackgroundService
{
    private readonly SemanticBackgroundJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFunctionDefaultRepository _functionDefaultRepository;
    private readonly ILogger<SemanticBackgroundJobWorker> _logger;

    public SemanticBackgroundJobWorker(
        SemanticBackgroundJobQueue queue,
        IServiceScopeFactory scopeFactory,
        IFunctionDefaultRepository functionDefaultRepository,
        ILogger<SemanticBackgroundJobWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _functionDefaultRepository = functionDefaultRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fd = await _functionDefaultRepository.GetByFunctionAsync(AppFunction.RolePlaySemanticAnalysis, stoppingToken);
        var maxConcurrent = fd?.MaxConcurrentJobs is int n ? Math.Clamp(n, 1, 16) : 2;

        _logger.LogInformation("SemanticBackgroundJobWorker started, MaxConcurrentJobs={MaxConcurrentJobs}", maxConcurrent);

        using var semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);

        while (!stoppingToken.IsCancellationRequested)
        {
            BackgroundJobEnvelope? job = null;
            try
            {
                job = await _queue.DequeueAsync(stoppingToken);
                _queue.MarkProcessing(job.JobId);

                await semaphore.WaitAsync(stoppingToken);

                var capturedJob = job;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var handlers = scope.ServiceProvider.GetRequiredService<IEnumerable<IBackgroundJobHandler>>();
                        var handler = handlers.FirstOrDefault(x => string.Equals(x.JobType, capturedJob.JobType, StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidOperationException($"No handler registered for semantic job type '{capturedJob.JobType}'.");

                        _logger.LogInformation("SemanticWorker: starting {JobType} ({JobId})", capturedJob.JobType, capturedJob.JobId);
                        await handler.HandleAsync(capturedJob, stoppingToken);
                        _queue.MarkCompleted(capturedJob.JobId);
                        _logger.LogInformation("SemanticWorker: completed {JobType} ({JobId})", capturedJob.JobType, capturedJob.JobId);
                    }
                    catch (Exception ex)
                    {
                        _queue.MarkFailed(capturedJob.JobId, ex.Message);
                        _logger.LogWarning(ex, "SemanticWorker: job {JobType} ({JobId}) failed: {ExceptionType}",
                            capturedJob.JobType, capturedJob.JobId, ex.GetType().Name);
                    }
                    finally
                    {
                        _queue.ReleaseDedupeKey(capturedJob.DedupeKey);
                        semaphore.Release();
                    }
                }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (job is not null)
                {
                    _queue.MarkFailed(job.JobId, ex.Message);
                    _queue.ReleaseDedupeKey(job.DedupeKey);
                }

                _logger.LogError(ex, "SemanticBackgroundJobWorker: unhandled error in dequeue loop");
            }
        }

        _logger.LogInformation("SemanticBackgroundJobWorker stopped");
    }
}
