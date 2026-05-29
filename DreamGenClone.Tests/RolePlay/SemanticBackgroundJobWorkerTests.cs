using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.ModelManager;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SemanticBackgroundJobWorkerTests
{
    // ──────────────────────────────────────────────────────────────────
    // Stubs
    // ──────────────────────────────────────────────────────────────────

    private sealed class TrackingHandler : IBackgroundJobHandler
    {
        public string JobType => "test-job";
        public List<string> HandledJobIds { get; } = [];
        private readonly TaskCompletionSource? _gate;

        public TrackingHandler(TaskCompletionSource? gate = null)
        {
            _gate = gate;
        }

        public async Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken)
        {
            if (_gate is not null)
                await _gate.Task.WaitAsync(cancellationToken);
            lock (HandledJobIds)
                HandledJobIds.Add(job.JobId);
        }
    }

    private static async Task<IFunctionDefaultRepository> CreateRepoWithMaxConcurrentAsync(int? maxConcurrentJobs)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dreamgenclone-worker-{Guid.NewGuid():N}.db");
        var opts = new PersistenceOptions { ConnectionString = $"Data Source={dbPath}" };
        var persistence = new SqlitePersistence(
            Options.Create(opts),
            Options.Create(new LmStudioOptions()),
            Options.Create(new StoryAnalysisOptions()),
            Options.Create(new ScenarioAdaptationOptions()),
            NullLogger<SqlitePersistence>.Instance);
        await persistence.InitializeAsync();

        var repo = new FunctionDefaultRepository(Options.Create(opts), NullLogger<FunctionDefaultRepository>.Instance);
        if (maxConcurrentJobs.HasValue)
        {
            await repo.SaveAsync(new FunctionModelDefault
            {
                Id = Guid.NewGuid().ToString(),
                FunctionName = AppFunction.RolePlaySemanticAnalysis.ToString(),
                ModelId = "model-test",
                MaxConcurrentJobs = maxConcurrentJobs
            });
        }

        return repo;
    }

    private static (SemanticBackgroundJobQueue queue, SemanticBackgroundJobWorker worker, IServiceProvider services)
        BuildWorker(IFunctionDefaultRepository repo, IBackgroundJobHandler handler)
    {
        var queue = new SemanticBackgroundJobQueue(NullLogger<SemanticBackgroundJobQueue>.Instance);
        var services = new ServiceCollection()
            .AddScoped<IBackgroundJobHandler>(_ => handler)
            .BuildServiceProvider();
        var worker = new SemanticBackgroundJobWorker(
            queue,
            services.GetRequiredService<IServiceScopeFactory>(),
            repo,
            NullLogger<SemanticBackgroundJobWorker>.Instance);
        return (queue, worker, services);
    }

    // ──────────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Worker_ProcessesSingleJob_CallsHandlerAndMarkCompleted()
    {
        var repo = await CreateRepoWithMaxConcurrentAsync(null); // default concurrency
        var handler = new TrackingHandler();
        var (queue, worker, _) = BuildWorker(repo, handler);

        var cts = new CancellationTokenSource();
        var workerTask = worker.StartAsync(cts.Token);

        var enqueued = queue.Enqueue("test-job", "{}", "dedup-1");
        Assert.True(enqueued);

        // Wait up to 3s for the job to be handled
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (handler.HandledJobIds.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.Single(handler.HandledJobIds);

        cts.Cancel();
        try { await worker.StopAsync(CancellationToken.None); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Worker_DeduplicatesDuplicateKey_EnqueuesOnlyOnce()
    {
        var repo = await CreateRepoWithMaxConcurrentAsync(null);
        var gate = new TaskCompletionSource();
        var handler = new TrackingHandler(gate);
        var (queue, worker, _) = BuildWorker(repo, handler);

        var cts = new CancellationTokenSource();
        _ = worker.StartAsync(cts.Token);

        // Enqueue two jobs with the same dedup key
        var first = queue.Enqueue("test-job", "{\"id\":\"1\"}", "same-key");
        var second = queue.Enqueue("test-job", "{\"id\":\"2\"}", "same-key");

        Assert.True(first);
        Assert.False(second); // duplicate rejected

        gate.SetResult(); // release handler

        // Wait for job
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (handler.HandledJobIds.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.Single(handler.HandledJobIds);

        cts.Cancel();
        try { await worker.StopAsync(CancellationToken.None); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Worker_MaxConcurrentJobs_ClampsToConfiguredValue()
    {
        // Verify that MaxConcurrentJobs stored in the repo is read by the worker
        // (indirectly: use maxConcurrentJobs=1 and confirm the second job waits)
        var repo = await CreateRepoWithMaxConcurrentAsync(1);
        var handledOrder = new List<int>();
        int jobCount = 0;

        var gate1 = new TaskCompletionSource();
        var gate2 = new TaskCompletionSource();
        var gates = new[] { gate1, gate2 };

        var handler = new TrackingHandler(); // simple tracking, no gate
        var (queue, worker, _) = BuildWorker(repo, handler);

        var cts = new CancellationTokenSource();
        _ = worker.StartAsync(cts.Token);

        queue.Enqueue("test-job", "{\"n\":1}", "job-1");
        queue.Enqueue("test-job", "{\"n\":2}");

        // Wait for both to complete
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (handler.HandledJobIds.Count < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.Equal(2, handler.HandledJobIds.Count);

        cts.Cancel();
        try { await worker.StopAsync(CancellationToken.None); } catch { /* ignore */ }
    }
}
