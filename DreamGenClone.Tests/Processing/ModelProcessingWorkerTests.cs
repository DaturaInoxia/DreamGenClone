using DreamGenClone.Application.Processing;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.Processing;

public class ModelProcessingWorkerTests
{
    [Fact]
    public async Task Worker_ProcessesTask_AndMarksCompleted()
    {
        var queue = new ModelProcessingQueue();
        var services = BuildServiceProvider(queue);
        var worker = new ModelProcessingWorker(queue, services, NullLogger<ModelProcessingWorker>.Instance);

        using var cts = new CancellationTokenSource();
        var workerTask = worker.StartAsync(cts.Token);

        queue.Enqueue(new ModelProcessingTask
        {
            ParsedStoryId = "s1",
            StoryTitle = "Test",
            TaskType = ModelProcessingTaskType.Summarize
        });

        // Give worker time to process
        await Task.Delay(200);
        cts.Cancel();
        try { await workerTask; } catch (OperationCanceledException) { }

        var tasks = queue.GetAllTasks();
        Assert.Single(tasks);
        Assert.Equal(ModelProcessingStatus.Completed, tasks[0].Status);
    }

    [Fact]
    public async Task Worker_ProcessesMultipleTasks_Sequentially()
    {
        var queue = new ModelProcessingQueue();
        var services = BuildServiceProvider(queue);
        var worker = new ModelProcessingWorker(queue, services, NullLogger<ModelProcessingWorker>.Instance);

        using var cts = new CancellationTokenSource();
        var workerTask = worker.StartAsync(cts.Token);

        queue.EnqueueStoryProcessing("s1", "Test", "profile1");

        await Task.Delay(500);
        cts.Cancel();
        try { await workerTask; } catch (OperationCanceledException) { }

        var tasks = queue.GetAllTasks();
        Assert.Equal(3, tasks.Count);
        Assert.All(tasks, t => Assert.Equal(ModelProcessingStatus.Completed, t.Status));
    }

    [Fact]
    public async Task Worker_HandlesFailing_Task_ContinuesProcessing()
    {
        var queue = new ModelProcessingQueue();
        var services = BuildServiceProvider(queue, failSummarize: true);
        var worker = new ModelProcessingWorker(queue, services, NullLogger<ModelProcessingWorker>.Instance);

        using var cts = new CancellationTokenSource();
        var workerTask = worker.StartAsync(cts.Token);

        queue.Enqueue(new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize });
        queue.Enqueue(new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Analyze });

        await Task.Delay(300);
        cts.Cancel();
        try { await workerTask; } catch (OperationCanceledException) { }

        var tasks = queue.GetAllTasks();
        var summarize = tasks.First(t => t.TaskType == ModelProcessingTaskType.Summarize);
        var analyze = tasks.First(t => t.TaskType == ModelProcessingTaskType.Analyze);

        Assert.Equal(ModelProcessingStatus.Failed, summarize.Status);
        Assert.NotNull(summarize.ErrorMessage);
        Assert.Equal(ModelProcessingStatus.Completed, analyze.Status);
    }

    [Fact]
    public async Task Worker_FiresStatusChangedEvents()
    {
        var queue = new ModelProcessingQueue();
        var services = BuildServiceProvider(queue);
        var worker = new ModelProcessingWorker(queue, services, NullLogger<ModelProcessingWorker>.Instance);

        var statusChanges = new List<(string TaskId, ModelProcessingStatus Status)>();
        queue.OnStatusChanged += t => statusChanges.Add((t.Id, t.Status));

        using var cts = new CancellationTokenSource();
        var workerTask = worker.StartAsync(cts.Token);

        var task = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize };
        queue.Enqueue(task);

        await Task.Delay(200);
        cts.Cancel();
        try { await workerTask; } catch (OperationCanceledException) { }

        // Should have: Queued (on enqueue), Processing, Completed
        Assert.Contains(statusChanges, s => s.TaskId == task.Id && s.Status == ModelProcessingStatus.Queued);
        Assert.Contains(statusChanges, s => s.TaskId == task.Id && s.Status == ModelProcessingStatus.Processing);
        Assert.Contains(statusChanges, s => s.TaskId == task.Id && s.Status == ModelProcessingStatus.Completed);
    }

    private static IServiceScopeFactory BuildServiceProvider(ModelProcessingQueue queue, bool failSummarize = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModelProcessingQueue>(queue);

        if (failSummarize)
            services.AddScoped<IStorySummaryService, FailingSummaryService>();
        else
            services.AddScoped<IStorySummaryService, FakeSummaryService>();

        services.AddScoped<IStoryAnalysisService, FakeAnalysisService>();
        services.AddScoped<IStoryRankingService, FakeRankingService>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    // ---- Test doubles ----

    private sealed class FakeSummaryService : IStorySummaryService
    {
        public Task<SummarizeResult> SummarizeAsync(string parsedStoryId, CancellationToken cancellationToken = default)
            => Task.FromResult(new SummarizeResult { Success = true, SummaryText = "fake summary" });

        public Task<StorySummary?> GetSummaryAsync(string parsedStoryId, CancellationToken cancellationToken = default)
            => Task.FromResult<StorySummary?>(null);
    }

    private sealed class FailingSummaryService : IStorySummaryService
    {
        public Task<SummarizeResult> SummarizeAsync(string parsedStoryId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("LLM unavailable");

        public Task<StorySummary?> GetSummaryAsync(string parsedStoryId, CancellationToken cancellationToken = default)
            => Task.FromResult<StorySummary?>(null);
    }

    private sealed class FakeAnalysisService : IStoryAnalysisService
    {
        public Task<AnalyzeResult> AnalyzeAsync(string parsedStoryId, CancellationToken cancellationToken = default)
            => Task.FromResult(new AnalyzeResult { Success = true });

        public Task<StoryAnalysisResult?> GetAnalysisAsync(string parsedStoryId, CancellationToken cancellationToken = default)
            => Task.FromResult<StoryAnalysisResult?>(null);
    }

    private sealed class FakeRankingService : IStoryRankingService
    {
        public Task<ThemeRankResult> RankAsync(string parsedStoryId, string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ThemeRankResult { Success = true, Score = 75.0 });

        public Task<StoryRankingResult?> GetRankingAsync(string parsedStoryId, string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult<StoryRankingResult?>(null);

        public Task<List<StoryRankingResult>> GetRankingsAsync(string parsedStoryId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<StoryRankingResult>());
    }
}
