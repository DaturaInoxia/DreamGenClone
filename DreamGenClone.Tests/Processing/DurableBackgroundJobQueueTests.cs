using DreamGenClone.Domain.Processing;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Processing;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.Processing;

public sealed class DurableBackgroundJobQueueTests
{
    [Fact]
    public async Task Queue_UsesRepositoryAsSinglePersistencePath()
    {
        var fixture = CreateFixture();
        try
        {
            var createdUtc = Utc(10);
            var job = CreateJob("job-1", createdUtc);

            Assert.True(await fixture.Queue.TryEnqueueAsync(job));
            Assert.False(await fixture.Queue.TryEnqueueAsync(CreateJob("job-2", createdUtc.AddSeconds(1))));
            Assert.Equal("job-1", (await fixture.Queue.GetAsync("job-1"))!.Id);
            Assert.True(await fixture.Queue.TryCancelAsync("job-1", createdUtc.AddMinutes(1)));
            Assert.Equal(DurableBackgroundJobStatus.Cancelled, (await fixture.Repository.GetAsync("job-1"))!.Status);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task StartupRecovery_RecoversExpiredProcessingLeaseAtCurrentUtc()
    {
        var fixture = CreateFixture();
        try
        {
            var createdUtc = Utc(11);
            await fixture.Queue.TryEnqueueAsync(CreateJob("job-1", createdUtc));
            var claimedUtc = createdUtc.AddMinutes(1);
            await fixture.Repository.TryClaimNextAsync(
                DurableJobLane.TextAnalysis,
                "worker-1",
                claimedUtc,
                claimedUtc.AddMinutes(1));
            var recoveryUtc = claimedUtc.AddMinutes(2);
            var service = new DurableBackgroundJobStartupRecovery(
                fixture.Repository,
                new FixedTimeProvider(recoveryUtc),
                NullLogger<DurableBackgroundJobStartupRecovery>.Instance);

            await service.StartAsync(CancellationToken.None);

            var recovered = await fixture.Queue.GetAsync("job-1");
            Assert.Equal(DurableBackgroundJobStatus.Queued, recovered!.Status);
            Assert.Equal(recoveryUtc, recovered.UpdatedUtc);
            Assert.Null(recovered.LeaseOwner);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    private static TestFixture CreateFixture()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"durable-background-queue-{Guid.NewGuid():N}.db");
        var repository = new DurableBackgroundJobRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={databasePath}"
        }));
        return new TestFixture(new DurableBackgroundJobQueue(repository), repository, databasePath);
    }

    private static DurableBackgroundJob CreateJob(string id, DateTime createdUtc) => new()
    {
        Id = id,
        JobType = "test-job",
        Lane = DurableJobLane.TextAnalysis,
        PayloadJson = "{\"recordId\":\"record-1\"}",
        DedupeKey = "test-job:record-1",
        MaxAttempts = 3,
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

    private sealed record TestFixture(
        DurableBackgroundJobQueue Queue,
        DurableBackgroundJobRepository Repository,
        string DatabasePath);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}