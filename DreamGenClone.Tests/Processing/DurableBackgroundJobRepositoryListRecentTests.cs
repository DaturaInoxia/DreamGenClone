using DreamGenClone.Domain.Processing;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Processing;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.Processing;

public sealed class DurableBackgroundJobRepositoryListRecentTests
{
    [Fact]
    public async Task ListRecent_ReturnsNewestJobsFirstAndRejectsNonPositiveLimits()
    {
        var fixture = CreateFixture();
        try
        {
            var created = Utc(10);
            await fixture.Repository.TryEnqueueAsync(CreateJob("job-1", "job-1", DurableJobLane.TextAnalysis, created));
            await fixture.Repository.TryEnqueueAsync(CreateJob("job-3", "job-3", DurableJobLane.TextAnalysis, created.AddHours(2)));
            await fixture.Repository.TryEnqueueAsync(CreateJob("job-2", "job-2", DurableJobLane.TextAnalysis, created.AddHours(1)));

            var recent = await fixture.Repository.ListRecentAsync(2);

            Assert.Equal(new[] { "job-3", "job-2" }, recent.Select(job => job.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.Repository.ListRecentAsync(0));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.Repository.ListRecentAsync(-1));
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