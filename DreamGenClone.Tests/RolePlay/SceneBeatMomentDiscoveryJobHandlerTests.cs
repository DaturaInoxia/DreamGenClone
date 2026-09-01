using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
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

public sealed class SceneBeatMomentDiscoveryJobHandlerTests
{
    internal const string ValidResponse = """
        {
          "schemaVersion": 1,
          "catalogueBeatId": "b1",
          "recommendedMomentId": "m2",
          "moments": [
            {
              "momentId": "m1", "order": 1, "label": "Threshold",
              "temporalAnchor": "the instant Becky's foot lands inside",
              "frozenState": "Becky is mid-step in the doorway while Dean remains seated.",
              "visibleAction": "crossing the threshold",
              "participants": [{"profileKey":"p0","involvement":"active"},{"profileKey":"p1","involvement":"observer"}],
              "compositionRationale": "The doorway preserves the starting spatial relationship.",
              "productionRoles": ["VideoStart"], "evidenceKeys": ["n0","c1"]
            },
            {
              "momentId": "m2", "order": 2, "label": "Exchanged look",
              "temporalAnchor": "the instant their gazes meet",
              "frozenState": "Becky stands inside the hall and meets Dean's raised gaze.",
              "visibleAction": "holding eye contact",
              "participants": [{"profileKey":"p0","involvement":"active"},{"profileKey":"p1","involvement":"active"}],
              "compositionRationale": "The shared sightline creates a clear emotional center.",
              "productionRoles": ["StillCandidate","VideoEnd"], "evidenceKeys": ["n0","c1"]
            }
          ]
        }
        """;

    [Fact]
    public async Task Handle_ValidOutputCompletesSetAndPersistsRecommendation()
    {
        var fixture = await CreateFixtureAsync(ValidResponse);
        try
        {
            await fixture.Handler.HandleAsync(fixture.Job);

            var momentSet = await fixture.Repository.GetAsync(fixture.MomentSetId);
            Assert.Equal(SceneBeatCatalogueStatus.Complete, momentSet!.Status);
            Assert.Equal("m2", momentSet.RecommendedMomentId);
            Assert.Equal(2, momentSet.Moments.Count);
            var attempt = await fixture.Repository.GetAttemptAsync(fixture.AttemptId);
            Assert.Equal(SceneBeatAnalysisAttemptStatus.Complete, attempt!.Status);
            Assert.Equal("stop", attempt.FinishReason);
        }
        finally { Cleanup(fixture.Path); }
    }

    [Fact]
    public async Task Handle_InvalidOutputPreservesRawResponseAndFailsPermanently()
    {
        const string raw = "{\"schemaVersion\":1}";
        var fixture = await CreateFixtureAsync(raw);
        try
        {
            var error = await Assert.ThrowsAsync<DurableJobFailureException>(() => fixture.Handler.HandleAsync(fixture.Job));

            Assert.False(error.IsTransient);
            Assert.Equal("scene_moment_discovery_output_invalid", error.ErrorCode);
            var momentSet = await fixture.Repository.GetAsync(fixture.MomentSetId);
            Assert.Equal(SceneBeatCatalogueStatus.Failed, momentSet!.Status);
            var attempt = await fixture.Repository.GetAttemptAsync(fixture.AttemptId);
            Assert.Equal(raw, attempt!.RawModelResponse);
            Assert.Equal("scene_moment_discovery_output_invalid", attempt.ValidationCode);
        }
        finally { Cleanup(fixture.Path); }
    }

    private static async Task<Fixture> CreateFixtureAsync(string response)
    {
        var path = Path.Combine(Path.GetTempPath(), $"scene-moment-handler-{Guid.NewGuid():N}.db");
        var repository = new SceneMomentSetRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={path}"
        }));
        var now = DateTime.UtcNow;
        var source = CreateSnapshot();
        var builder = new SceneMomentDiscoverySnapshotBuilder();
        var execution = new SceneBeatAnalyzerExecutionSnapshot(
            "default-1", "model-1", "provider-1", "https://provider.example", "/v1/chat/completions",
            30, false, "analyzer", 0.2, 0.9, 4096, "Provider", true, ThinkingMode.Disabled,
            StructuredOutputMode.StrictJsonSchema, 32768, 4096, 2, 120, 250, [5, 30], 30, 8);
        const string momentSetId = "moment-set-1";
        const string attemptId = "attempt-1";
        var momentSet = new SceneMomentSet
        {
            Id = momentSetId, CatalogueId = source.CatalogueId, BeatId = source.BeatId,
            BeatProductionPlanId = source.BeatProductionPlanId,
            BeatProductionPlanVersion = source.BeatProductionPlanVersion,
            Version = 1, CurrentAttemptId = attemptId, SchemaVersion = source.SchemaVersion,
            PromptContractVersion = SceneMomentDiscoveryContract.ContractVersion,
            BeatSnapshotJson = builder.SerializeBeatSnapshot(source),
            TurnEvidenceSnapshotJson = builder.SerializeEvidenceSnapshot(source),
            ExecutionSettingsJson = JsonSerializer.Serialize(execution, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CreatedUtc = now, UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = attemptId, OwnerRecordId = momentSetId, AttemptNumber = 1, JobId = "job-1",
            SystemPrompt = "system", UserPrompt = "user", ValidationDetailsJson = "{}",
            InputCharacters = 10, CreatedUtc = now, UpdatedUtc = now
        };
        await repository.CreateVersionAsync(momentSet, attempt);
        var job = new DurableBackgroundJob
        {
            Id = "job-1", JobType = SceneMomentDiscoveryPipelineService.JobType,
            Lane = DurableJobLane.TextAnalysis,
            PayloadJson = JsonSerializer.Serialize(new SceneMomentDiscoveryJobPayload(momentSetId, attemptId), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            DedupeKey = "moment-test", Status = DurableBackgroundJobStatus.Processing,
            AttemptCount = 1, MaxAttempts = 3, LeaseOwner = "test", CreatedUtc = now, UpdatedUtc = now
        };
        var handler = new SceneBeatMomentDiscoveryJobHandler(
            repository, new ProviderRepository(), new CompletionClient(response), builder,
            new SceneMomentDiscoveryParser(), TimeProvider.System);
        return new Fixture(handler, repository, job, momentSetId, attemptId, path);
    }

    private static SceneMomentDiscoverySourceSnapshot CreateSnapshot()
        => new(
            1, "catalogue-1", 1, "b1", "plan-1", 1, "Arrival",
            "Becky enters and meets Dean's gaze.", "entry hall",
            "[{\"eventKey\":\"e1\"}]", "{\"durationIntent\":\"brief\"}", "[{\"eventKey\":\"e1\"}]",
            "{\"stateSummary\":\"outside\"}", "{\"stateSummary\":\"inside\"}",
            [new("v1", "MomentTransition", ["e1"], ["start", "end"], ["turn"])],
            [
                new("n0", 0, "interaction-0", "Narrative", "System", "Becky enters.", new string('A', 64)),
                new("c1", 1, "interaction-1", "Dean", "User", "You're still awake.", new string('B', 64))
            ],
            [new("p0", "character-becky", "Becky", "active"), new("p1", "character-dean", "Dean", "observer")]);

    private static void Cleanup(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(path + suffix)) File.Delete(path + suffix); } catch { }
        }
    }

    private sealed record Fixture(
        SceneBeatMomentDiscoveryJobHandler Handler,
        SceneMomentSetRepository Repository,
        DurableBackgroundJob Job,
        string MomentSetId,
        string AttemptId,
        string Path);

    private sealed class CompletionClient(string response) : IStructuredTextCompletionClient
    {
        public Task<StructuredTextCompletionResult> GenerateAsync(
            ResolvedSceneBeatAnalyzer analyzer,
            StructuredTextCompletionRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new StructuredTextCompletionResult(
                response, analyzer.Model.ModelIdentifier, "stop", TimeSpan.FromMilliseconds(20)));
    }

    private sealed class ProviderRepository : IProviderRepository
    {
        public Task<Provider> SaveAsync(Provider provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Provider>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
