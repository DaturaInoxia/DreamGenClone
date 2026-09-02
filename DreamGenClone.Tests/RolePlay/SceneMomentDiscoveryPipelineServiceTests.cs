using System.Text.Json;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentDiscoveryPipelineServiceTests
{
    [Fact]
    public async Task Enqueue_PersistsSplitImmutableSnapshotsAndIdsOnlyPayload()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var momentSet = await fixture.Service.EnqueueAsync(new(fixture.PlanId));

            Assert.Equal(1, momentSet.Version);
            Assert.Equal(1, momentSet.BeatProductionPlanVersion);
            Assert.DoesNotContain("interaction-0", momentSet.BeatSnapshotJson, StringComparison.Ordinal);
            Assert.Contains("interaction-0", momentSet.TurnEvidenceSnapshotJson, StringComparison.Ordinal);
            Assert.DoesNotContain("encrypted-secret", momentSet.ExecutionSettingsJson, StringComparison.Ordinal);
            var job = Assert.Single(fixture.Queue.Jobs);
            using var payload = JsonDocument.Parse(job.PayloadJson);
            Assert.Equal(2, payload.RootElement.EnumerateObject().Count());
            Assert.Equal(momentSet.Id, payload.RootElement.GetProperty("momentSetId").GetString());
            Assert.Equal(momentSet.CurrentAttemptId, payload.RootElement.GetProperty("attemptId").GetString());
            Assert.Equal(3, job.MaxAttempts);
        }
        finally { Cleanup(fixture.Path); }
    }

    [Fact]
    public async Task Replace_CreatesNewVersionAndSupersedesFailedCurrentSet()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var first = await fixture.Service.EnqueueAsync(new(fixture.PlanId));
            var attempt = await fixture.MomentSets.GetAttemptAsync(first.CurrentAttemptId!);
            Assert.True(await fixture.MomentSets.TryFailAttemptAsync(
                first.Id, attempt!, "test_failure", "test failure", DateTime.UtcNow));

            var replacement = await fixture.Service.ReplaceAsync(new(fixture.PlanId));

            Assert.Equal(2, replacement.Version);
            Assert.Equal(SceneBeatCatalogueStatus.Superseded, (await fixture.MomentSets.GetAsync(first.Id))!.Status);
            Assert.Equal(replacement.Id, (await fixture.MomentSets.GetCurrentAsync(fixture.PlanId))!.Id);
            Assert.Equal(2, fixture.Queue.Jobs.Count);
        }
        finally { Cleanup(fixture.Path); }
    }

    [Fact]
    public async Task Enqueue_RequiresExactCurrentCompletedPlanAndRejectsActiveDuplicate()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await fixture.Service.EnqueueAsync(new(fixture.PlanId));
            var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service.EnqueueAsync(new(fixture.PlanId)));
            Assert.Contains("already active", duplicate.Message, StringComparison.OrdinalIgnoreCase);

            var supersedingPlanId = await AddCompletedPlanAsync(fixture.Plans, 2);
            Assert.NotEqual(fixture.PlanId, supersedingPlanId);
            var stale = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service.ReplaceAsync(new(fixture.PlanId)));
            Assert.Contains("no longer current", stale.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(fixture.Path); }
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scene-moment-service-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={path}" });
        var plans = new SceneBeatProductionPlanRepository(options);
        var planId = await AddCompletedPlanAsync(plans, 1);
        var momentSets = new SceneMomentSetRepository(options);
        var queue = new Queue();
        var service = new SceneMomentDiscoveryPipelineService(
            plans, momentSets, new AnalyzerResolver(), new SceneMomentDiscoverySnapshotBuilder(),
            new SceneMomentDiscoveryContract(), queue, TimeProvider.System);
        return new Fixture(service, plans, momentSets, queue, planId, path);
    }

    private static async Task<string> AddCompletedPlanAsync(SceneBeatProductionPlanRepository repository, int version)
    {
        var now = DateTime.UtcNow;
        var planId = $"plan-{version}";
        var attemptId = $"attempt-{version}";
        var source = SceneBeatProductionParserTests.CreateSnapshot();
        var data = new SceneBeatProductionParser().Parse(
            planId, SceneBeatProductionParserTests.ValidResponse, source);
        var plan = new SceneBeatProductionPlan
        {
            Id = planId, CatalogueId = source.CatalogueId, BeatId = source.Beat.BeatId,
            CatalogueVersion = source.CatalogueVersion, Version = version,
            CurrentAttemptId = attemptId, SchemaVersion = source.SchemaVersion,
            PromptContractVersion = SceneBeatProductionContract.ContractVersion,
            SourceSnapshotJson = JsonSerializer.Serialize(source, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ExecutionSettingsJson = "{}", CreatedUtc = now, UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = attemptId, OwnerRecordId = planId, AttemptNumber = 1, JobId = $"job-{version}",
            SystemPrompt = "system", UserPrompt = "user", ValidationDetailsJson = "{}",
            InputCharacters = 10, CreatedUtc = now, UpdatedUtc = now
        };
        await repository.CreateVersionAsync(plan, attempt);
        Assert.True(await repository.TryStartAttemptAsync(planId, attemptId, "model", "provider", now));
        Assert.True(await repository.TryCompleteAttemptAsync(planId, attempt, data, now));
        return planId;
    }

    private static ResolvedSceneBeatAnalyzer Analyzer()
        => new(
            "default-1", "model-1", "provider-1",
            new ResolvedModel("https://provider.example", "/v1/chat/completions", 30,
                "encrypted-secret", "analyzer", 0.2, 0.9, 4096, "Provider", false)
            { SupportsThinkingControl = true, ThinkingMode = ThinkingMode.Disabled },
            StructuredOutputMode.StrictJsonSchema, 32768, 4096, 2, 120, 250, [5, 30], 30, 8);

    private static void Cleanup(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(path + suffix)) File.Delete(path + suffix); } catch { }
        }
    }

    private sealed record Fixture(
        SceneMomentDiscoveryPipelineService Service,
        SceneBeatProductionPlanRepository Plans,
        SceneMomentSetRepository MomentSets,
        Queue Queue,
        string PlanId,
        string Path);

    private sealed class AnalyzerResolver : ISceneBeatAnalyzerResolver
    {
        public Task<ResolvedSceneBeatAnalyzer> ResolveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Analyzer());
    }

    private sealed class Queue : IDurableBackgroundJobQueue
    {
        public List<DurableBackgroundJob> Jobs { get; } = [];
        public Task<bool> TryEnqueueAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default) { Jobs.Add(job); return Task.FromResult(true); }
        public Task<DurableBackgroundJob?> GetAsync(string jobId, CancellationToken cancellationToken = default) => Task.FromResult(Jobs.SingleOrDefault(item => item.Id == jobId));
        public Task<bool> TryCancelAsync(string jobId, DateTime cancelledUtc, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task WaitForWorkAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
