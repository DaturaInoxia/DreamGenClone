using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DreamGenClone.Web.Application.BackgroundJobs;

public sealed class GenericBackgroundJobWorker : BackgroundService
{
    private readonly IBackgroundJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GenericBackgroundJobWorker> _logger;

    public GenericBackgroundJobWorker(
        IBackgroundJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<GenericBackgroundJobWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GenericBackgroundJobWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            BackgroundJobEnvelope? job = null;
            try
            {
                job = await _queue.DequeueAsync(stoppingToken);
                _queue.MarkProcessing(job.JobId);

                await using var scope = _scopeFactory.CreateAsyncScope();
                var handlers = scope.ServiceProvider.GetRequiredService<IEnumerable<IBackgroundJobHandler>>();
                var handler = handlers.FirstOrDefault(x => string.Equals(x.JobType, job.JobType, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"No handler registered for background job type '{job.JobType}'.");

                _logger.LogInformation("Running background job {JobType} ({JobId})", job.JobType, job.JobId);
                await handler.HandleAsync(job, stoppingToken);
                _queue.MarkCompleted(job.JobId);
                _logger.LogInformation("Completed background job {JobType} ({JobId})", job.JobType, job.JobId);
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
                }

                _logger.LogError(ex, "Background job failed");
            }
            finally
            {
                if (job is not null && _queue is GenericBackgroundJobQueue queue)
                {
                    queue.ReleaseDedupeKey(job.DedupeKey);
                }
            }
        }

        _logger.LogInformation("GenericBackgroundJobWorker stopped");
    }
}
