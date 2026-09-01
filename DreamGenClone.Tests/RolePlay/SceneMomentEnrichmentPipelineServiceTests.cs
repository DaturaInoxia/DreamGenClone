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

public sealed class SceneMomentEnrichmentPipelineServiceTests
{
    [Fact]
    public async Task Enqueue_PersistsSplitSnapshotsAndIdsOnlyPayloadThenReusesCurrent()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var enrichment = await fixture.Service.EnqueueAsync(new(fixture.MomentSetId, fixture.MomentId));

            Assert.Equal(1, enrichment.Revision);
            Assert.DoesNotContain("interaction-0", enrichment.MomentSnapshotJson, StringComparison.Ordinal);
            Assert.Contains("interaction-0", enrichment.TurnEvidenceSnapshotJson, StringComparison.Ordinal);
            Assert.DoesNotContain("encrypted-secret", enrichment.ExecutionSettingsJson, StringComparison.Ordinal);
            var job = Assert.Single(fixture.Queue.Jobs);
            Assert.Equal(DurableJobLane.TextAnalysis, job.Lane);
            Assert.Equal(3, job.MaxAttempts);
            using var payload = JsonDocument.Parse(job.PayloadJson);
            Assert.Equal(2, payload.RootElement.EnumerateObject().Count());
            Assert.Equal(enrichment.Id, payload.RootElement.GetProperty("enrichmentId").GetString());
            Assert.Equal(enrichment.CurrentAttemptId, payload.RootElement.GetProperty("attemptId").GetString());

            var reused = await fixture.Service.EnqueueAsync(new(fixture.MomentSetId, fixture.MomentId));
            Assert.Equal(enrichment.Id, reused.Id);
            Assert.Single(fixture.Queue.Jobs);

            var attempt = await fixture.Enrichments.GetAttemptAsync(enrichment.CurrentAttemptId!);
            Assert.True(await fixture.Enrichments.TryStartAttemptAsync(
                enrichment.Id, attempt!.Id, "model", "provider", DateTime.UtcNow));
            Assert.True(await fixture.Enrichments.TryCompleteAttemptAsync(
                enrichment.Id, attempt, new("{}", "[]", "{}"), DateTime.UtcNow));
            var completedReuse = await fixture.Service.EnqueueAsync(new(fixture.MomentSetId, fixture.MomentId));
            Assert.Equal(enrichment.Id, completedReuse.Id);
            Assert.Single(fixture.Queue.Jobs);
        }
        finally { Cleanup(fixture.Path); }
    }

    [Fact]
    public async Task Replace_RequiresExplicitOperationAndRejectsActiveCurrent()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var first = await fixture.Service.EnqueueAsync(new(fixture.MomentSetId, fixture.MomentId));
            var active = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service.ReplaceAsync(new(fixture.MomentSetId, fixture.MomentId)));
            Assert.Contains("already active", active.Message, StringComparison.OrdinalIgnoreCase);

            var attempt = await fixture.Enrichments.GetAttemptAsync(first.CurrentAttemptId!);
            Assert.True(await fixture.Enrichments.TryFailAttemptAsync(
                first.Id, attempt!, "test_failure", "test failure", DateTime.UtcNow));
            var ordinary = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service.EnqueueAsync(new(fixture.MomentSetId, fixture.MomentId)));
            Assert.Contains("explicit replace", ordinary.Message, StringComparison.OrdinalIgnoreCase);

            var replacement = await fixture.Service.ReplaceAsync(new(fixture.MomentSetId, fixture.MomentId));
            Assert.Equal(2, replacement.Revision);
            Assert.Equal(SceneBeatCatalogueStatus.Superseded, (await fixture.Enrichments.GetAsync(first.Id))!.Status);
            Assert.Equal(replacement.Id, (await fixture.Enrichments.GetCurrentAsync(fixture.MomentSetId, fixture.MomentId))!.Id);
        }
        finally { Cleanup(fixture.Path); }
    }

    [Fact]
    public async Task EnqueueRecommended_UsesPersistedRecommendationAndRejectsStaleParentPlan()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var recommended = await fixture.Service.EnqueueRecommendedAsync(fixture.MomentSetId);
            Assert.Equal(fixture.MomentId, recommended.MomentId);
            Assert.Single(fixture.Queue.Jobs);

            await AddCompletedPlanAsync(fixture.Plans, "plan-new", 4);
            var stale = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service.ReplaceAsync(new(fixture.MomentSetId, fixture.MomentId)));
            Assert.Contains("no longer current", stale.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(fixture.Path); }
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scene-moment-enrichment-service-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={path}" });
        var plans = new SceneBeatProductionPlanRepository(options);
        var plan = await AddCompletedPlanAsync(plans, "plan-1", 2);
        var momentSets = new SceneMomentSetRepository(options);
        var momentSet = await AddCompletedMomentSetAsync(momentSets, plan);
        var enrichments = new SceneMomentEnrichmentRepository(options);
        var queue = new Queue();
        var service = new SceneMomentEnrichmentPipelineService(
            momentSets, plans, enrichments, new AnalyzerResolver(),
            new SceneMomentEnrichmentSnapshotBuilder(), new SceneMomentEnrichmentContract(),
            queue, TimeProvider.System);
        return new Fixture(service, plans, enrichments, queue, momentSet.Id, "m2", path);
    }

    private static async Task<SceneBeatProductionPlan> AddCompletedPlanAsync(
        SceneBeatProductionPlanRepository repository,
        string planId,
        int version)
    {
        var now = DateTime.UtcNow;
        var source = SceneBeatProductionParserTests.CreateSnapshot();
        var data = new SceneBeatProductionParser().Parse(planId, SceneBeatProductionParserTests.ValidResponse, source);
        var attemptId = $"{planId}-attempt";
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
            Id = attemptId, OwnerRecordId = planId, AttemptNumber = 1, JobId = $"{planId}-job",
            SystemPrompt = "system", UserPrompt = "user", ValidationDetailsJson = "{}",
            InputCharacters = 10, CreatedUtc = now, UpdatedUtc = now
        };
        await repository.CreateVersionAsync(plan, attempt);
        Assert.True(await repository.TryStartAttemptAsync(planId, attemptId, "model", "provider", now));
        Assert.True(await repository.TryCompleteAttemptAsync(planId, attempt, data, now));
        return (await repository.GetAsync(planId))!;
    }

    private static async Task<SceneMomentSet> AddCompletedMomentSetAsync(
        SceneMomentSetRepository repository,
        SceneBeatProductionPlan plan)
    {
        var (_, templateSet, _) = SceneMomentEnrichmentTestFixture.CreateParents();
        var now = DateTime.UtcNow;
        var source = new SceneMomentDiscoverySnapshotBuilder().Build(plan);
        var snapshotBuilder = new SceneMomentDiscoverySnapshotBuilder();
        var moment = templateSet.Moments.Single();
        moment.MomentSetId = templateSet.Id;
        var firstMoment = new SceneMoment
        {
            MomentSetId = templateSet.Id,
            MomentId = "m1",
            Order = 1,
            Label = "Threshold",
            TemporalAnchor = "the instant Becky crosses the threshold at event e1",
            FrozenState = "Becky is in the doorway while Dean remains seated.",
            VisibleAction = "crossing the threshold",
            ParticipantSummaryJson = moment.ParticipantSummaryJson,
            CompositionRationale = "The doorway preserves the starting spatial relationship.",
            ProductionRolesJson = "[\"VideoStart\"]",
            EvidenceInteractionIdsJson = moment.EvidenceInteractionIdsJson
        };
        var attemptId = "moment-set-attempt";
        var momentSet = new SceneMomentSet
        {
            Id = templateSet.Id, CatalogueId = plan.CatalogueId, BeatId = plan.BeatId,
            BeatProductionPlanId = plan.Id, BeatProductionPlanVersion = plan.Version,
            Version = templateSet.Version, CurrentAttemptId = attemptId, SchemaVersion = source.SchemaVersion,
            PromptContractVersion = SceneMomentDiscoveryContract.ContractVersion,
            BeatSnapshotJson = snapshotBuilder.SerializeBeatSnapshot(source),
            TurnEvidenceSnapshotJson = snapshotBuilder.SerializeEvidenceSnapshot(source),
            ExecutionSettingsJson = "{}", CreatedUtc = now, UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = attemptId, OwnerRecordId = momentSet.Id, AttemptNumber = 1, JobId = "moment-set-job",
            SystemPrompt = "system", UserPrompt = "user", ValidationDetailsJson = "{}",
            InputCharacters = 10, CreatedUtc = now, UpdatedUtc = now
        };
        await repository.CreateVersionAsync(momentSet, attempt);
        Assert.True(await repository.TryStartAttemptAsync(momentSet.Id, attempt.Id, "model", "provider", now));
        Assert.True(await repository.TryCompleteAttemptAsync(
            momentSet.Id, attempt, new SceneMomentSetData(moment.MomentId, [firstMoment, moment]), now));
        return (await repository.GetAsync(momentSet.Id))!;
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
        SceneMomentEnrichmentPipelineService Service,
        SceneBeatProductionPlanRepository Plans,
        SceneMomentEnrichmentRepository Enrichments,
        Queue Queue,
        string MomentSetId,
        string MomentId,
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
