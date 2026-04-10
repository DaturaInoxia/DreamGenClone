using DreamGenClone.Application.Processing;
using DreamGenClone.Application.StoryAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Processing;

public sealed class ModelProcessingWorker : BackgroundService
{
    private readonly ModelProcessingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ModelProcessingWorker> _logger;

    public ModelProcessingWorker(
        IModelProcessingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ModelProcessingWorker> logger)
    {
        _queue = (ModelProcessingQueue)queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ModelProcessingWorker started");

        await foreach (var task in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                _queue.UpdateTaskStatus(task.Id, ModelProcessingStatus.Processing);
                _logger.LogInformation("Processing {TaskType} for story {StoryId} ({Title})",
                    task.TaskType, task.ParsedStoryId, task.StoryTitle ?? "untitled");

                await using var scope = _scopeFactory.CreateAsyncScope();
                await ProcessTaskAsync(scope.ServiceProvider, task, stoppingToken);

                _queue.UpdateTaskStatus(task.Id, ModelProcessingStatus.Completed);
                _logger.LogInformation("Completed {TaskType} for story {StoryId}",
                    task.TaskType, task.ParsedStoryId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _queue.UpdateTaskStatus(task.Id, ModelProcessingStatus.Failed, "Cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed {TaskType} for story {StoryId}: {Error}",
                    task.TaskType, task.ParsedStoryId, ex.Message);
                _queue.UpdateTaskStatus(task.Id, ModelProcessingStatus.Failed, ex.Message);
            }
        }

        _logger.LogInformation("ModelProcessingWorker stopped");
    }

    private static async Task ProcessTaskAsync(IServiceProvider services, ModelProcessingTask task, CancellationToken ct)
    {
        switch (task.TaskType)
        {
            case ModelProcessingTaskType.Summarize:
                var summaryService = services.GetRequiredService<IStorySummaryService>();
                await summaryService.SummarizeAsync(task.ParsedStoryId, ct);
                break;

            case ModelProcessingTaskType.Analyze:
                var analysisService = services.GetRequiredService<IStoryAnalysisService>();
                await analysisService.AnalyzeAsync(task.ParsedStoryId, ct);
                break;

            case ModelProcessingTaskType.Rank:
                if (task.RankingProfileId is null)
                    throw new InvalidOperationException("RankingProfileId is required for Rank tasks");
                var rankingService = services.GetRequiredService<IStoryRankingService>();
                await rankingService.RankAsync(task.ParsedStoryId, task.RankingProfileId, ct);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(task.TaskType), task.TaskType, "Unknown task type");
        }
    }
}
