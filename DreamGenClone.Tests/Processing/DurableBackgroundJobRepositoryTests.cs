using DreamGenClone.Domain.Processing;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Processing;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.Processing;

public sealed class DurableBackgroundJobRepositoryTests
{
    [Fact]
    public async Task Enqueue_ActiveDedupeIsRejected_AndTerminalDedupeCanBeReused()
    {
        var fixture = CreateFixture();
        try
        {
            var created = Utc(10);
            Assert.True(await fixture.Repository.TryEnqueueAsync(CreateJob("job-1", "same", DurableJobLane.TextAnalysis, created)));
            Assert.False(await fixture.Repository.TryEnqueueAsync(CreateJob("job-2", "same", DurableJobLane.TextAnalysis, created.AddSeconds(1))));

            var claim = await fixture.Repository.TryClaimNextAsync(
                DurableJobLane.TextAnalysis, "worker-1", created.AddMinutes(1), created.AddMinutes(2));
            Assert.Equal("job-1", claim!.Id);
            Assert.True(await fixture.Repository.TryCompleteAsync("job-1", "worker-1", created.AddMinutes(1).AddSeconds(30)));
            Assert.True(await fixture.Repository.TryEnqueueAsync(CreateJob("job-2", "same", DurableJobLane.TextAnalysis, created.AddMinutes(3))));
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Claim_IsLaneAwareOrderedAndLeaseOwned()
    {
        var fixture = CreateFixture();
        try
        {
            var created = Utc(11);
            await fixture.Repository.TryEnqueueAsync(CreateJob("image", "image", DurableJobLane.ImageRender, created));
            await fixture.Repository.TryEnqueueAsync(CreateJob("text-2", "text-2", DurableJobLane.TextAnalysis, created.AddSeconds(2)));
            await fixture.Repository.TryEnqueueAsync(CreateJob("text-1", "text-1", DurableJobLane.TextAnalysis, created.AddSeconds(1)));

            var claimedAt = created.AddMinutes(1);
            var claimed = await fixture.Repository.TryClaimNextAsync(
                DurableJobLane.TextAnalysis, "worker-a", claimedAt, claimedAt.AddMinutes(1));

            Assert.Equal("text-1", claimed!.Id);
            Assert.Equal(1, claimed.AttemptCount);
            Assert.Equal(DurableBackgroundJobStatus.Processing, claimed.Status);
            Assert.False(await fixture.Repository.TryRenewLeaseAsync(
                claimed.Id, "worker-b", claimedAt.AddSeconds(10), claimedAt.AddMinutes(2)));
            Assert.True(await fixture.Repository.TryRenewLeaseAsync(
                claimed.Id, "worker-a", claimedAt.AddSeconds(10), claimedAt.AddMinutes(2)));
            Assert.Null(await fixture.Repository.TryClaimNextAsync(
                DurableJobLane.PromptCompilation, "worker-c", claimedAt, claimedAt.AddMinutes(1)));
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Retry_IsNotClaimableUntilDue_AndHonorsConfiguredAttemptLimit()
    {
        var fixture = CreateFixture();
        try
        {
            var created = Utc(12);
            await fixture.Repository.TryEnqueueAsync(CreateJob("job-1", "retry", DurableJobLane.TextAnalysis, created, maxAttempts: 2));
            var firstClaimAt = created.AddMinutes(1);
            Assert.NotNull(await fixture.Repository.TryClaimNextAsync(
                DurableJobLane.TextAnalysis, "worker-1", firstClaimAt, firstClaimAt.AddMinutes(1)));
            var retryAt = firstClaimAt.AddMinutes(5);
            Assert.True(await fixture.Repository.TryScheduleRetryAsync(
                "job-1", "worker-1", "provider_timeout", "Timed out", firstClaimAt.AddSeconds(10), retryAt));
            Assert.Null(await fixture.Repository.TryClaimNextAsync(
                DurableJobLane.TextAnalysis, "worker-2", retryAt.AddTicks(-1), retryAt.AddMinutes(1)));

            var second = await fixture.Repository.TryClaimNextAsync(
                DurableJobLane.TextAnalysis, "worker-2", retryAt, retryAt.AddMinutes(1));
            Assert.Equal(2, second!.AttemptCount);
            Assert.False(await fixture.Repository.TryScheduleRetryAsync(
                "job-1", "worker-2", "provider_timeout", "Timed out", retryAt.AddSeconds(10), retryAt.AddMinutes(2)));
            Assert.True(await fixture.Repository.TryFailAsync(
                "job-1", "worker-2", "provider_timeout", "Attempts exhausted", retryAt.AddSeconds(20)));
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Cancellation_PreventsStaleWorkerCompletion()
    {
        var fixture = CreateFixture();
        try
        {
            var created = Utc(13);
            await fixture.Repository.TryEnqueueAsync(CreateJob("job-1", "cancel", DurableJobLane.TextAnalysis, created));
            var claimedAt = created.AddMinutes(1);
            await fixture.Repository.TryClaimNextAsync(
                DurableJobLane.TextAnalysis, "worker-1", claimedAt, claimedAt.AddMinutes(2));

            Assert.True(await fixture.Repository.TryCancelAsync("job-1", claimedAt.AddSeconds(10)));
            Assert.False(await fixture.Repository.TryCompleteAsync("job-1", "worker-1", claimedAt.AddSeconds(20)));
            Assert.Equal(DurableBackgroundJobStatus.Cancelled, (await fixture.Repository.GetAsync("job-1"))!.Status);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Recovery_RequeuesOnlyExpiredLeasesAndPreservesAttemptCount()
    {
        var fixture = CreateFixture();
        try
        {
            var created = Utc(14);
            await fixture.Repository.TryEnqueueAsync(CreateJob("expired", "expired", DurableJobLane.TextAnalysis, created));
            await fixture.Repository.TryEnqueueAsync(CreateJob("active", "active", DurableJobLane.TextAnalysis, created.AddSeconds(1)));
            var claimedAt = created.AddMinutes(1);
            await fixture.Repository.TryClaimNextAsync(
                DurableJobLane.TextAnalysis, "worker-1", claimedAt, claimedAt.AddMinutes(1));
            await fixture.Repository.TryClaimNextAsync(
                DurableJobLane.TextAnalysis, "worker-2", claimedAt, claimedAt.AddMinutes(5));

            Assert.Equal(1, await fixture.Repository.RecoverExpiredLeasesAsync(claimedAt.AddMinutes(2)));
            var expired = await fixture.Repository.GetAsync("expired");
            var active = await fixture.Repository.GetAsync("active");
            Assert.Equal(DurableBackgroundJobStatus.Queued, expired!.Status);
            Assert.Equal(1, expired.AttemptCount);
            Assert.Null(expired.LeaseOwner);
            Assert.Equal(DurableBackgroundJobStatus.Processing, active!.Status);
            Assert.Equal("worker-2", active.LeaseOwner);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Recovery_FailsExpiredJobWhenConfiguredAttemptsAreExhausted()
    {
        var fixture = CreateFixture();
        try
        {
            var created = Utc(15);
            await fixture.Repository.TryEnqueueAsync(CreateJob(
                "job-1", "exhausted", DurableJobLane.TextAnalysis, created, maxAttempts: 1));
            var claimedAt = created.AddMinutes(1);
            await fixture.Repository.TryClaimNextAsync(
                DurableJobLane.TextAnalysis, "worker-1", claimedAt, claimedAt.AddMinutes(1));

            Assert.Equal(1, await fixture.Repository.RecoverExpiredLeasesAsync(claimedAt.AddMinutes(1)));
            var job = await fixture.Repository.GetAsync("job-1");
            Assert.Equal(DurableBackgroundJobStatus.Failed, job!.Status);
            Assert.Equal(1, job.AttemptCount);
            Assert.Equal("lease_expired_attempts_exhausted", job.ErrorCode);
            Assert.NotNull(job.CompletedUtc);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task ExpiredLeaseOwner_CannotComplete()
    {
        var fixture = CreateFixture();
        try
        {
            var created = Utc(16);
            await fixture.Repository.TryEnqueueAsync(CreateJob("job-1", "lease", DurableJobLane.TextAnalysis, created));
            var claimedAt = created.AddMinutes(1);
            await fixture.Repository.TryClaimNextAsync(
                DurableJobLane.TextAnalysis, "worker-1", claimedAt, claimedAt.AddMinutes(1));

            Assert.False(await fixture.Repository.TryCompleteAsync("job-1", "worker-1", claimedAt.AddMinutes(1)));
            Assert.Equal(DurableBackgroundJobStatus.Processing, (await fixture.Repository.GetAsync("job-1"))!.Status);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    private static TestFixture CreateFixture()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"durable-background-jobs-{Guid.NewGuid():N}.db");
        var repository = new DurableBackgroundJobRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={databasePath}"
        }));
        return new TestFixture(repository, databasePath);
    }

    private static DurableBackgroundJob CreateJob(
        string id,
        string dedupeKey,
        DurableJobLane lane,
        DateTime createdUtc,
        int maxAttempts = 3)
        => new()
        {
            Id = id,
            JobType = "test-job",
            Lane = lane,
            PayloadJson = "{\"recordId\":\"record-1\"}",
            DedupeKey = dedupeKey,
            MaxAttempts = maxAttempts,
            CreatedUtc = createdUtc,
            UpdatedUtc = createdUtc
        };

    private static DateTime Utc(int hour) => new(2026, 8, 31, hour, 0, 0, DateTimeKind.Utc);

    private static void Cleanup(string databasePath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                if (File.Exists(databasePath + suffix)) File.Delete(databasePath + suffix);
            }
            catch
            {
            }
        }
    }

    private sealed record TestFixture(DurableBackgroundJobRepository Repository, string DatabasePath);
}