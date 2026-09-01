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

public sealed class SceneBeatProductionPipelineServiceTests
{
    [Fact]
    public async Task Enqueue_PersistsImmutableSnapshotsAndIdsOnlyPayload()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var plan = await fixture.Service.EnqueueAsync(new("catalogue-1", "b1"));

            Assert.Equal(1, plan.Version);
            Assert.Equal(1, plan.CatalogueVersion);
            Assert.DoesNotContain("encrypted-secret", plan.ExecutionSettingsJson, StringComparison.Ordinal);
            var job = Assert.Single(fixture.Queue.Jobs);
            using var payload = JsonDocument.Parse(job.PayloadJson);
            Assert.Equal(2, payload.RootElement.EnumerateObject().Count());
            Assert.Equal(plan.Id, payload.RootElement.GetProperty("planId").GetString());
            Assert.Equal(plan.CurrentAttemptId, payload.RootElement.GetProperty("attemptId").GetString());
            Assert.Equal(3, job.MaxAttempts);
            var snapshot = JsonSerializer.Deserialize<SceneBeatProductionSourceSnapshot>(
                plan.SourceSnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(["n0"], snapshot!.Evidence.Select(item => item.Key));
            Assert.Equal("entry hall", snapshot.Beat.PrimaryLocation);
        }
        finally
        {
            Cleanup(fixture.Path);
        }
    }

    [Fact]
    public async Task Enqueue_RejectsActiveDuplicateAndSupersededCatalogue()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await fixture.Service.EnqueueAsync(new("catalogue-1", "b1"));
            var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service.EnqueueAsync(new("catalogue-1", "b1")));
            Assert.Contains("already active", duplicate.Message, StringComparison.OrdinalIgnoreCase);

            await AddCompletedCatalogueAsync(fixture.Catalogues, "catalogue-2", 2);
            var stale = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service.ReplaceAsync(new("catalogue-1", "b1")));
            Assert.Contains("no longer current", stale.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(fixture.Path);
        }
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scene-production-service-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={path}" });
        var catalogues = new SceneBeatCatalogueRepository(options);
        await AddCompletedCatalogueAsync(catalogues, "catalogue-1", 1);
        var plans = new SceneBeatProductionPlanRepository(options);
        var queue = new Queue();
        var service = new SceneBeatProductionPipelineService(
            catalogues,
            plans,
            new AnalyzerResolver(),
            new SceneBeatProductionSnapshotBuilder(),
            new SceneBeatProductionContract(),
            queue,
            TimeProvider.System);
        return new Fixture(service, catalogues, queue, path);
    }

    private static async Task AddCompletedCatalogueAsync(
        SceneBeatCatalogueRepository repository,
        string id,
        int version)
    {
        var now = DateTime.UtcNow;
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = $"attempt-{id}", OwnerRecordId = id, AttemptNumber = 1, JobId = $"job-{id}",
            SystemPrompt = "system", UserPrompt = "user", ValidationDetailsJson = "{}",
            InputCharacters = 10, CreatedUtc = now, UpdatedUtc = now
        };
        var source = new SceneBeatCatalogueInputSnapshot(
            1, "session-1", "turn-1", 1, "SubmitPrompt", now.AddSeconds(-2), now,
            new string('A', 64),
            [new("n0", 0, "interaction-1", "Narrative", "System", "Alex enters the hall.", now, new string('B', 64))],
            [new("p0", "character-alex", "Alex", "protagonist", "nonbinary", "", "", "", true, new string('C', 64))]);
        var catalogue = new SceneBeatCatalogue
        {
            Id = id, SessionId = "session-1", TurnId = "turn-1", Version = version,
            CurrentAttemptId = attempt.Id, SchemaVersion = 1,
            PromptContractVersion = SceneBeatCatalogueContract.ContractVersion,
            InputSnapshotJson = JsonSerializer.Serialize(source, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ExecutionSettingsJson = "{}", CreatedUtc = now, UpdatedUtc = now
        };
        await repository.CreateVersionAsync(catalogue, attempt);
        Assert.True(await repository.TryStartAttemptAsync(id, attempt.Id, "model", "provider", "{}", now));
        attempt.ValidationDetailsJson = "{}";
        Assert.True(await repository.TryCompleteAttemptAsync(
            id, attempt,
            [new SceneBeatCatalogueEntry
            {
                CatalogueId = id, BeatId = "b1", Order = 1, Label = "Arrival",
                BeatSynopsis = "Alex enters.", PrimaryLocation = "entry hall",
                ParticipantSummaryJson = "[{\"name\":\"Alex\",\"involvement\":\"active\"}]",
                EvidenceInteractionIdsJson = "[\"interaction-1\"]", ContentTagsJson = "[]"
            }], now));
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
        SceneBeatProductionPipelineService Service,
        SceneBeatCatalogueRepository Catalogues,
        Queue Queue,
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