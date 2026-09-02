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

public sealed class SceneMomentEnrichmentJobHandlerTests
{
    internal const string ValidResponse = """
        {
          "schemaVersion": 1,
          "catalogueBeatId": "b1",
          "momentId": "m2",
          "visualDescription": "Becky and Dean hold one shared look in the entry hall.",
          "characters": [
            {
              "name": "Becky", "profileKey": "p0", "involvement": "active",
              "physicalLocation": "entry hall", "position": "just inside the doorway",
              "actionOrObservation": "holds Dean's gaze", "sightline": "toward Dean",
              "visibleCharacterNames": ["Dean"], "clothing": "blue shirt"
            },
            {
              "name": "Dean", "profileKey": "p1", "involvement": "observer",
              "physicalLocation": "entry hall", "position": "seated across the hall",
              "actionOrObservation": "looks up at Becky", "sightline": "toward Becky",
              "visibleCharacterNames": ["Becky"], "clothing": "dark lounge clothes"
            }
          ],
          "location": "entry hall",
          "timeOfDay": "evening",
          "lighting": "warm ceiling light",
          "environment": "narrow entry hall with the open door behind Becky",
          "mood": "expectant",
          "objects": ["open door"],
          "instantaneousSoundCueKeys": ["s1"],
          "videoKeyState": { "roles": ["VideoEnd"], "stateChangeAllowed": false }
        }
        """;

    [Fact]
    public async Task Handle_ValidOutputCompletesEnrichment()
    {
        var fixture = await CreateFixtureAsync(ValidResponse);
        try
        {
            await fixture.Handler.HandleAsync(fixture.Job);

            var enrichment = await fixture.Repository.GetAsync(fixture.EnrichmentId);
            Assert.Equal(SceneBeatCatalogueStatus.Complete, enrichment!.Status);
            Assert.Contains("character-becky", enrichment.FrozenStateContractJson, StringComparison.Ordinal);
            Assert.Contains("s1", enrichment.InstantaneousSoundEventsJson, StringComparison.Ordinal);
            Assert.Contains("VideoEnd", enrichment.VideoKeyStateJson, StringComparison.Ordinal);
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
            Assert.Equal("scene_moment_enrichment_output_invalid", error.ErrorCode);
            var enrichment = await fixture.Repository.GetAsync(fixture.EnrichmentId);
            Assert.Equal(SceneBeatCatalogueStatus.Failed, enrichment!.Status);
            var attempt = await fixture.Repository.GetAttemptAsync(fixture.AttemptId);
            Assert.Equal(raw, attempt!.RawModelResponse);
            Assert.Equal("scene_moment_enrichment_output_invalid", attempt.ValidationCode);
        }
        finally { Cleanup(fixture.Path); }
    }

    private static async Task<Fixture> CreateFixtureAsync(string response)
    {
        var path = Path.Combine(Path.GetTempPath(), $"scene-moment-enrichment-handler-{Guid.NewGuid():N}.db");
        var repository = new SceneMomentEnrichmentRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={path}"
        }));
        var now = DateTime.UtcNow;
        var source = SceneMomentEnrichmentTestFixture.CreateSnapshot();
        var builder = new SceneMomentEnrichmentSnapshotBuilder();
        var execution = new SceneBeatAnalyzerExecutionSnapshot(
            "default-1", "model-1", "provider-1", "https://provider.example", "/v1/chat/completions",
            30, false, "analyzer", 0.2, 0.9, 4096, "Provider", true, ThinkingMode.Disabled,
            StructuredOutputMode.StrictJsonSchema, 32768, 4096, 2, 120, 250, [5, 30], 30, 8);
        const string enrichmentId = "enrichment-1";
        const string attemptId = "attempt-1";
        var enrichment = new SceneMomentEnrichment
        {
            Id = enrichmentId, CatalogueId = source.CatalogueId, BeatId = source.BeatId,
            BeatProductionPlanId = source.BeatProductionPlanId,
            BeatProductionPlanVersion = source.BeatProductionPlanVersion,
            MomentSetId = source.Moment.MomentSetId, MomentSetVersion = source.Moment.MomentSetVersion,
            MomentId = source.Moment.MomentId, Revision = 1, CurrentAttemptId = attemptId,
            SchemaVersion = source.SchemaVersion,
            PromptContractVersion = SceneMomentEnrichmentContract.ContractVersion,
            MomentSnapshotJson = builder.SerializeMomentSnapshot(source),
            TurnEvidenceSnapshotJson = builder.SerializeEvidenceSnapshot(source),
            ExecutionSettingsJson = JsonSerializer.Serialize(execution, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CreatedUtc = now, UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = attemptId, OwnerRecordId = enrichmentId, AttemptNumber = 1, JobId = "job-1",
            SystemPrompt = "system", UserPrompt = "user", ValidationDetailsJson = "{}",
            InputCharacters = 10, CreatedUtc = now, UpdatedUtc = now
        };
        await repository.CreateRevisionAsync(enrichment, attempt);
        var job = new DurableBackgroundJob
        {
            Id = "job-1", JobType = SceneMomentEnrichmentPipelineService.JobType,
            Lane = DurableJobLane.TextAnalysis,
            PayloadJson = JsonSerializer.Serialize(new SceneMomentEnrichmentJobPayload(enrichmentId, attemptId), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            DedupeKey = "enrichment-test", Status = DurableBackgroundJobStatus.Processing,
            AttemptCount = 1, MaxAttempts = 3, LeaseOwner = "test", CreatedUtc = now, UpdatedUtc = now
        };
        var handler = new SceneMomentEnrichmentJobHandler(
            repository, new ProviderRepository(), new CompletionClient(response), builder,
            new SceneMomentEnrichmentParser(), TimeProvider.System);
        return new Fixture(handler, repository, job, enrichmentId, attemptId, path);
    }

    private static void Cleanup(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(path + suffix)) File.Delete(path + suffix); } catch { }
        }
    }

    private sealed record Fixture(
        SceneMomentEnrichmentJobHandler Handler,
        SceneMomentEnrichmentRepository Repository,
        DurableBackgroundJob Job,
        string EnrichmentId,
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
