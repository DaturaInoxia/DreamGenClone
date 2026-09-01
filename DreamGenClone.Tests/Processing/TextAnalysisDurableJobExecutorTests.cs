using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.Processing;

public sealed class TextAnalysisDurableJobExecutorTests
{
    [Fact]
    public async Task Execute_SuccessCompletesOwnedJob()
    {
        var repository = new RecordingRepository();
        var executor = CreateExecutor(repository, new Handler("catalogue", (_, _) => Task.CompletedTask));

        await executor.ExecuteAsync(CreateClaimedJob(), CreateAnalyzer());

        Assert.Equal(1, repository.CompleteCalls);
        Assert.Equal(0, repository.RetryCalls);
        Assert.Equal(0, repository.FailCalls);
    }

    [Fact]
    public async Task Execute_TransientFailureSchedulesConfiguredRetryForClaimedAttempt()
    {
        var repository = new RecordingRepository();
        var executor = CreateExecutor(repository, new Handler("catalogue", (_, _) =>
            throw new DurableJobFailureException("provider_busy", "Provider busy.", isTransient: true)));
        var before = DateTime.UtcNow;

        await executor.ExecuteAsync(CreateClaimedJob(attemptCount: 1, maxAttempts: 3), CreateAnalyzer());

        Assert.Equal(1, repository.RetryCalls);
        Assert.Equal("provider_busy", repository.ErrorCode);
        Assert.InRange(repository.NextAttemptUtc!.Value, before.AddSeconds(5), DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(0, repository.FailCalls);
    }

    [Fact]
    public async Task Execute_PermanentFailureFailsWithoutRetry()
    {
        var repository = new RecordingRepository();
        var executor = CreateExecutor(repository, new Handler("catalogue", (_, _) =>
            throw new DurableJobFailureException("schema_invalid", "Schema invalid.", isTransient: false)));

        await executor.ExecuteAsync(CreateClaimedJob(), CreateAnalyzer());

        Assert.Equal(0, repository.RetryCalls);
        Assert.Equal(1, repository.FailCalls);
        Assert.Equal("schema_invalid", repository.ErrorCode);
    }

    [Fact]
    public async Task Execute_TransientFailureWithExhaustedAttemptsFailsWithoutRetry()
    {
        var repository = new RecordingRepository();
        var executor = CreateExecutor(repository, new Handler("catalogue", (_, _) =>
            throw new DurableJobFailureException("provider_busy", "Provider busy.", isTransient: true)));

        await executor.ExecuteAsync(CreateClaimedJob(attemptCount: 2, maxAttempts: 2), CreateAnalyzer());

        Assert.Equal(0, repository.RetryCalls);
        Assert.Equal(1, repository.FailCalls);
        Assert.Equal("provider_busy", repository.ErrorCode);
    }

    [Fact]
    public async Task Execute_DuplicateHandlersFailPermanently()
    {
        var repository = new RecordingRepository();
        var executor = CreateExecutor(
            repository,
            new Handler("catalogue", (_, _) => Task.CompletedTask),
            new Handler("catalogue", (_, _) => Task.CompletedTask));

        await executor.ExecuteAsync(CreateClaimedJob(), CreateAnalyzer());

        Assert.Equal(1, repository.FailCalls);
        Assert.Equal("durable_handler_ambiguous", repository.ErrorCode);
    }

    [Fact]
    public async Task Execute_UnclassifiedExceptionFailsWithStableMessage()
    {
        var repository = new RecordingRepository();
        var executor = CreateExecutor(repository, new Handler("catalogue", (_, _) => throw new Exception("private detail")));

        await executor.ExecuteAsync(CreateClaimedJob(), CreateAnalyzer());

        Assert.Equal("durable_handler_unclassified_failure", repository.ErrorCode);
        Assert.Equal("The durable job handler failed permanently.", repository.ErrorMessage);
    }

    [Fact]
    public async Task Execute_LongRunningHandlerRenewsLease()
    {
        var repository = new RecordingRepository();
        var executor = CreateExecutor(repository, new Handler("catalogue", async (_, cancellationToken) =>
            await Task.Delay(TimeSpan.FromMilliseconds(650), cancellationToken)));

        await executor.ExecuteAsync(CreateClaimedJob(), CreateAnalyzer() with { LeaseSeconds = 1 });

        Assert.True(repository.RenewCalls >= 1);
        Assert.Equal(1, repository.CompleteCalls);
    }

    [Fact]
    public async Task Execute_HostCancellationLeavesJobForLeaseRecovery()
    {
        var repository = new RecordingRepository();
        var executor = CreateExecutor(repository, new Handler("catalogue", async (_, cancellationToken) =>
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await executor.ExecuteAsync(CreateClaimedJob(), CreateAnalyzer(), cancellation.Token);

        Assert.Equal(0, repository.CompleteCalls);
        Assert.Equal(0, repository.RetryCalls);
        Assert.Equal(0, repository.FailCalls);
    }

    private static TextAnalysisDurableJobExecutor CreateExecutor(
        RecordingRepository repository,
        params IDurableBackgroundJobHandler[] handlers)
        => new(repository, handlers, TimeProvider.System, NullLogger<TextAnalysisDurableJobExecutor>.Instance);

    private static DurableBackgroundJob CreateClaimedJob(int attemptCount = 1, int maxAttempts = 3) => new()
    {
        Id = "job-1",
        JobType = "catalogue",
        Lane = DurableJobLane.TextAnalysis,
        PayloadJson = "{}",
        DedupeKey = "catalogue-1",
        Status = DurableBackgroundJobStatus.Processing,
        AttemptCount = attemptCount,
        MaxAttempts = maxAttempts,
        LeaseOwner = "worker-1",
        LeaseExpiresUtc = DateTime.UtcNow.AddMinutes(1),
        CreatedUtc = DateTime.UtcNow.AddMinutes(-1),
        UpdatedUtc = DateTime.UtcNow
    };

    private static ResolvedSceneBeatAnalyzer CreateAnalyzer()
    {
        var model = new ResolvedModel(
            "https://structured.test",
            "/v1/chat/completions",
            30,
            null,
            "structured-model",
            0.2,
            0.8,
            4096,
            "Structured Provider",
            IsSessionOverride: false)
        {
            SupportsThinkingControl = true,
            ThinkingMode = ThinkingMode.Disabled
        };
        return new ResolvedSceneBeatAnalyzer(
            "function-default-1",
            "model-1",
            "provider-1",
            model,
            StructuredOutputMode.StrictJsonSchema,
            131072,
            8192,
            2,
            120,
            250,
            [5, 30],
            30,
            8);
    }

    private sealed class Handler(
        string jobType,
        Func<DurableBackgroundJob, CancellationToken, Task> handle) : IDurableBackgroundJobHandler
    {
        public string JobType { get; } = jobType;
        public Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
            => handle(job, cancellationToken);
    }

    private sealed class RecordingRepository : IDurableBackgroundJobRepository
    {
        public int RenewCalls { get; private set; }
        public int RetryCalls { get; private set; }
        public int CompleteCalls { get; private set; }
        public int FailCalls { get; private set; }
        public string? ErrorCode { get; private set; }
        public string? ErrorMessage { get; private set; }
        public DateTime? NextAttemptUtc { get; private set; }

        public Task<bool> TryRenewLeaseAsync(string jobId, string leaseOwner, DateTime renewedUtc, DateTime leaseExpiresUtc, CancellationToken cancellationToken = default)
        {
            RenewCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> TryScheduleRetryAsync(string jobId, string leaseOwner, string errorCode, string errorMessage, DateTime scheduledUtc, DateTime nextAttemptUtc, CancellationToken cancellationToken = default)
        {
            RetryCalls++;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            NextAttemptUtc = nextAttemptUtc;
            return Task.FromResult(true);
        }

        public Task<bool> TryCompleteAsync(string jobId, string leaseOwner, DateTime completedUtc, CancellationToken cancellationToken = default)
        {
            CompleteCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> TryFailAsync(string jobId, string leaseOwner, string errorCode, string errorMessage, DateTime failedUtc, CancellationToken cancellationToken = default)
        {
            FailCalls++;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            return Task.FromResult(true);
        }

        public Task<bool> TryEnqueueAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DurableBackgroundJob?> GetAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasActiveJobsAsync(DurableJobLane lane, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DurableBackgroundJob?> TryClaimNextAsync(DurableJobLane lane, string leaseOwner, DateTime claimedUtc, DateTime leaseExpiresUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCancelAsync(string jobId, DateTime cancelledUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> RecoverExpiredLeasesAsync(DateTime recoveredUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}