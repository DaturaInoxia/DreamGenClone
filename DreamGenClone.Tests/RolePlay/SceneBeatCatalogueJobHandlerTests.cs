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

public sealed class SceneBeatCatalogueJobHandlerTests
{
    [Fact]
    public async Task Handle_ValidStrictOutputPromotesCurrentCatalogue()
    {
        var fixture = await CreateFixtureAsync(new CompletionClient(
            """
            {"schemaVersion":1,"beats":[{"beatId":"b1","order":1,"label":"Arrival","beatSynopsis":"Alex enters the hall.","primaryLocation":"entry hall","participants":[{"name":"Alex","involvement":"active"}],"evidenceKeys":["n0"]}]}
            """));
        try
        {
            await fixture.Handler.HandleAsync(fixture.Job);

            var persisted = await fixture.Repository.GetAsync(fixture.CatalogueId);
            Assert.NotNull(persisted);
            Assert.Equal(SceneBeatCatalogueStatus.Complete, persisted!.Status);
            var entry = Assert.Single(persisted.Entries);
            Assert.Equal("Arrival", entry.Label);
            Assert.Equal("[\"narrative-1\"]", entry.EvidenceInteractionIdsJson);
            var attempt = await fixture.Repository.GetAttemptAsync(fixture.AttemptId);
            Assert.Equal(SceneBeatAnalysisAttemptStatus.Complete, attempt!.Status);
            Assert.Equal("stop", attempt.FinishReason);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Handle_SemanticallyInvalidOutputPreservesRawResponseAndFailsAttempt()
    {
        const string raw = """
            {"schemaVersion":1,"beats":[{"beatId":"b1","order":1,"label":"Arrival","beatSynopsis":"Alex enters.","primaryLocation":"hall and stairs","participants":[{"name":"Alex","involvement":"active"}],"evidenceKeys":["n0"]}]}
            """;
        var fixture = await CreateFixtureAsync(new CompletionClient(raw));
        try
        {
            var exception = await Assert.ThrowsAsync<DurableJobFailureException>(() =>
                fixture.Handler.HandleAsync(fixture.Job));

            Assert.False(exception.IsTransient);
            Assert.Equal("scene_beat_output_invalid", exception.ErrorCode);
            var persisted = await fixture.Repository.GetAsync(fixture.CatalogueId);
            Assert.Equal(SceneBeatCatalogueStatus.Failed, persisted!.Status);
            var attempt = await fixture.Repository.GetAttemptAsync(fixture.AttemptId);
            Assert.Equal(SceneBeatAnalysisAttemptStatus.Failed, attempt!.Status);
            Assert.Equal(raw, attempt.RawModelResponse);
            Assert.Equal("scene_beat_output_invalid", attempt.ValidationCode);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Handle_TransientProviderFailureLeavesAttemptAvailableForDurableRetry()
    {
        var fixture = await CreateFixtureAsync(new CompletionClient(
            new StructuredTextCompletionException(
                "structured_text_http_503",
                "Provider unavailable.",
                true)));
        try
        {
            var exception = await Assert.ThrowsAsync<DurableJobFailureException>(() =>
                fixture.Handler.HandleAsync(fixture.Job));

            Assert.True(exception.IsTransient);
            var persisted = await fixture.Repository.GetAsync(fixture.CatalogueId);
            Assert.Equal(SceneBeatCatalogueStatus.Processing, persisted!.Status);
            var attempt = await fixture.Repository.GetAttemptAsync(fixture.AttemptId);
            Assert.Equal(SceneBeatAnalysisAttemptStatus.Processing, attempt!.Status);
            Assert.Null(attempt.CompletedUtc);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    private static async Task<TestFixture> CreateFixtureAsync(CompletionClient completionClient)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scene-beat-handler-{Guid.NewGuid():N}.db");
        var repository = new SceneBeatCatalogueRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={databasePath}"
        }));
        var now = DateTime.UtcNow;
        var catalogueId = Guid.NewGuid().ToString();
        var attemptId = Guid.NewGuid().ToString();
        var snapshot = new SceneBeatCatalogueInputSnapshot(
            1,
            "session-1",
            "turn-1",
            1,
            "UserSubmission",
            now.AddSeconds(-2),
            now,
            "membership-hash",
            [new("n0", 0, "narrative-1", "Narrative", "System", "Alex enters the hall.", now, "source-hash")],
            [new("p0", null, "Alex", "protagonist", "nonbinary", "", "", "", true, "profile-hash")]);
        var execution = new SceneBeatAnalyzerExecutionSnapshot(
            "function-default-1",
            "model-1",
            "provider-1",
            "https://provider.example",
            "/v1/chat/completions",
            30,
            false,
            "analyzer-model",
            0.2,
            0.9,
            2048,
            "Provider",
            true,
            ThinkingMode.Disabled,
            StructuredOutputMode.StrictJsonSchema,
            32768,
            4096,
            2,
            120,
            250,
            [5, 30],
            30,
            8);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var catalogue = new SceneBeatCatalogue
        {
            Id = catalogueId,
            SessionId = "session-1",
            TurnId = "turn-1",
            Version = 1,
            CurrentAttemptId = attemptId,
            SchemaVersion = 1,
            PromptContractVersion = SceneBeatCatalogueContract.ContractVersion,
            InputSnapshotJson = JsonSerializer.Serialize(snapshot, jsonOptions),
            ExecutionSettingsJson = JsonSerializer.Serialize(execution, jsonOptions),
            CreatedUtc = now,
            UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = attemptId,
            OwnerRecordId = catalogueId,
            AttemptNumber = 1,
            JobId = "job-1",
            SystemPrompt = "system prompt",
            UserPrompt = "user prompt",
            ValidationDetailsJson = "{}",
            InputCharacters = 24,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        await repository.CreateVersionAsync(catalogue, attempt);
        var job = new DurableBackgroundJob
        {
            Id = attempt.JobId,
            JobType = SceneBeatPipelineService.CatalogueJobType,
            Lane = DurableJobLane.TextAnalysis,
            PayloadJson = JsonSerializer.Serialize(new SceneBeatCatalogueJobPayload(catalogueId, attemptId), jsonOptions),
            DedupeKey = $"scene-beat-catalogue:{catalogueId}:{attemptId}",
            Status = DurableBackgroundJobStatus.Processing,
            AttemptCount = 1,
            MaxAttempts = 3,
            LeaseOwner = "test-worker",
            CreatedUtc = now,
            UpdatedUtc = now
        };
        var contract = new SceneBeatCatalogueContract(new SceneBeatCatalogueSnapshotBuilder());
        var handler = new SceneBeatCatalogueJobHandler(
            repository,
            new ProviderRepository(),
            completionClient,
            contract,
            TimeProvider.System);
        return new TestFixture(handler, repository, job, catalogueId, attemptId, databasePath);
    }

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
        SceneBeatCatalogueJobHandler Handler,
        SceneBeatCatalogueRepository Repository,
        DurableBackgroundJob Job,
        string CatalogueId,
        string AttemptId,
        string DatabasePath);

    private sealed class CompletionClient : IStructuredTextCompletionClient
    {
        private readonly string? _content;
        private readonly Exception? _failure;

        public CompletionClient(string content) => _content = content;
        public CompletionClient(Exception failure) => _failure = failure;

        public Task<StructuredTextCompletionResult> GenerateAsync(
            ResolvedSceneBeatAnalyzer analyzer,
            StructuredTextCompletionRequest request,
            CancellationToken cancellationToken = default)
            => _failure is not null
                ? Task.FromException<StructuredTextCompletionResult>(_failure)
                : Task.FromResult(new StructuredTextCompletionResult(
                    _content!, analyzer.Model.ModelIdentifier, "stop", TimeSpan.FromMilliseconds(20)));
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